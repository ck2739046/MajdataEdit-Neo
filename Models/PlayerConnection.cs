using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;
using System.Diagnostics;
using MajdataEdit_Neo.Utils;
using Avalonia.Threading;
using System.Collections.Concurrent;
using MsBox.Avalonia.Enums;
using MajdataEdit_Neo.Types.MajWs;
using MajdataEdit_Neo.Types.MajSetting;
using MajSimai;
using System.Collections.Generic;

namespace MajdataEdit_Neo.Models;

internal class PlayerConnection : IDisposable, IAsyncDisposable
{
    public bool IsConnected => _client?.IsAlive ?? false;
    public ViewSummary ViewSummary
    {
        get
        {
            lock (_stateSync)
                return _viewSummary;
        }
    }
    private ViewSummary _viewSummary;

    public delegate void NotifyViewStateChangedEventHandler(object sender, MajWsResponseType e);
    public event NotifyViewStateChangedEventHandler? OnPlayStarted;
    public event NotifyViewStateChangedEventHandler? OnPlayStopped;
    public event EventHandler<ViewStatus>? OnViewStateChanged;

    public event EventHandler? OnLoadRequired;
    public event EventHandler? OnStopRequired;
    public event EventHandler? OnLoadFinished;
    public event EventHandler? OnDisconnected;

    readonly object _stateSync = new();
    readonly object _connectionSync = new();
    readonly CancellationTokenSource _lifetimeCts = new();
    readonly SemaphoreSlim _connectGate = new(1, 1);
    readonly SemaphoreSlim _messageSignal = new(0);
    readonly SemaphoreSlim _stateChangedSignal = new(0, 1);
    readonly Task _listenerTask;
    bool _lastState;
    bool _disposed;
    WebSocket? _client;
    readonly ConcurrentQueue<MessageEventArgs> _playerMessages = new();

    readonly static JsonSerializerOptions JSON_READER_OPTIONS = new()
    {
        Converters =
        {
            new JsonStringEnumConverter()
        },
        TypeInfoResolver = MajWsJsonContext.Default
    };

    public PlayerConnection()
    {
        _listenerTask = Task.Run(() => StartToListenWebSocket(_lifetimeCts.Token));
    }

    public async Task<bool> ConnectAsync(string url = "ws://127.0.0.1:8083/majdata")
    {
        if (IsConnected)
            return true;

        return await ConnectToPlayer(url);
    }

    private async Task<bool> ConnectToPlayer(string url)
    {
        await _connectGate.WaitAsync();
        try
        {
            if (IsConnected)
                return true;

            WebSocket client;
            lock (_connectionSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_client is not null)
                    CloseClient(_client);

                client = new WebSocket(url)
                {
                    WaitTime = TimeSpan.FromSeconds(2)
                };
                client.OnClose += OnClose;
                client.OnOpen += OnOpen;
                client.OnMessage += OnMessage;
                client.OnError += OnError;
                _client = client;
            }

            await Task.Run(client.Connect);
            if (!client.IsAlive)
            {
                DiscardClient(client);
                return false;
            }

            Debug.WriteLine($"Connected to player: {url}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to connect to player: {ex}");
            var failedClient = _client;
            if (failedClient is not null && !failedClient.IsAlive)
                DiscardClient(failedClient);
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }
    void OnOpen(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _client))
            return;

        _lastState = true;
    }
    void OnClose(object? sender, CloseEventArgs args)
    {
        if (!ReferenceEquals(sender, _client))
            return;

        if (!_lastState)
            return;
        OnDisconnected?.Invoke(this, new EventArgs());
        _lastState = false;
        Signal(_stateChangedSignal);
    }
    void OnMessage(object? sender, MessageEventArgs args)
    {
        if (!ReferenceEquals(sender, _client))
            return;

        _playerMessages.Enqueue(args);
        Signal(_messageSignal);
    }
    void OnError(object? sender, ErrorEventArgs args)
    {
        Debug.WriteLine(args);
    }
    public async Task LoadAsync(string trackPath,
                                       string coverPath,
                                       string mvPath)
    {
        if (ViewSummary.State == ViewStatus.Error) await StopAsync();

        if (ViewSummary.State != ViewStatus.Loaded)
        {
            if (ViewSummary.State is ViewStatus.Paused or ViewStatus.Playing)
            {
                OnStopRequired?.Invoke(this, new EventArgs());
            }

            //if busy, wait
            await WaitUntilNotBusyAsync();
        }
        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Load,
            requestData = new MajWsRequestLoad()
            {
                ImagePath = coverPath,
                TrackPath = trackPath,
                VideoPath = mvPath
            }
        };
        await SendAsync(req);
    }
    public async Task SettingAsync(MajViewSetting viewSetting, MajVolumeSetting volumeSetting)
    {
        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Setting,
            requestData = new MajWsRequestSetting()
            {
                ViewSetting = viewSetting,
                VolumeSetting = volumeSetting
            }
        };
        await SendAsync(req);
    }
    public async Task PauseAsync()
    {
        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Pause,
            requestData = null
        };
        await SendAsync(req);
    }
    public async Task StopAsync()
    {
        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Stop,
            requestData = null
        };
        await SendAsync(req);
    }
    public async Task ParseAndPlayAsync(PlaybackMode mode,
        double startAt, float speed,
        string title, string artist, float offset,
        string designer, string level, string fumen,
        IList<SimaiCommand> commands, int difficulty, string? maidataPath = null)
    {
        if (ViewSummary.State == ViewStatus.Error) await StopAsync();

        if (ViewSummary.State != ViewStatus.Loaded)
        {
            if (ViewSummary.State is ViewStatus.Paused or ViewStatus.Playing)
            {
                OnStopRequired?.Invoke(this, new EventArgs());
                await Task.Delay(114); //wait for stop
            }
            else
            {
                OnLoadRequired?.Invoke(this, new EventArgs());
            }

            //if busy, wait
            await WaitUntilNotBusyAsync();
        }

        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Play,
            requestData = new MajWsRequestPlay()
            {
                Mode = mode,
                StartAt = startAt,
                Speed = speed,
                Title = title,
                Artist = artist,
                Offset = offset,
                Designer = designer,
                Level = level,
                Fumen = fumen,
                Commands = commands,
                Difficulty = difficulty,
                MaidataPath = maidataPath
            }
        };
        await SendAsync(req);
    }
    public async Task ResumeAsync()
    {
        var req = new MajWsRequestBase()
        {
            requestType = MajWsRequestType.Resume,
            requestData = null
        };
        await SendAsync(req);
    }
    async Task SendAsync(MajWsRequestBase req)
    {
        var client = _client;
        if (client is null || !client.IsAlive)
            throw new PlayerNotConnectedException();

        var json = JsonSerializer.Serialize(req, JSON_READER_OPTIONS);
        await Task.Run(() => client.Send(json));
        Debug.WriteLine($"Player request sent: {req.requestType}");
    }
    private async Task WaitUntilNotBusyAsync()
    {
        while (ViewSummary.State == ViewStatus.Busy)
            await _stateChangedSignal.WaitAsync(_lifetimeCts.Token);
    }

    async Task StartToListenWebSocket(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _messageSignal.WaitAsync(cancellationToken);
                while (_playerMessages.TryDequeue(out var args))
                {
                    try
                    {
                        await ProcessMessageAsync(args);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to process player message: {ex}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Player message listener failed: {ex}");
        }
    }

    private async Task ProcessMessageAsync(MessageEventArgs args)
    {
        var resp = JsonSerializer.Deserialize<MajWsResponseBase>(args.Data, JSON_READER_OPTIONS);
        switch (resp.responseType)
        {
            case MajWsResponseType.PlayPaused:
            case MajWsResponseType.Heartbeat:
            case MajWsResponseType.Ok:
                UpdateViewSummary(DeserializeViewSummary(resp));
                break;
            case MajWsResponseType.LoadOk:
                UpdateViewSummary(DeserializeViewSummary(resp));
                OnLoadFinished?.Invoke(this, EventArgs.Empty);
                break;
            case MajWsResponseType.PlayResumed:
            case MajWsResponseType.PlayStarted:
                UpdateViewSummary(DeserializeViewSummary(resp));
                OnPlayStarted?.Invoke(this, resp.responseType);
                break;
            case MajWsResponseType.PlayStopped:
                UpdateViewSummary(DeserializeViewSummary(resp));
                OnPlayStopped?.Invoke(this, resp.responseType);
                break;
            case MajWsResponseType.Error:
                OnViewStateChanged?.Invoke(this, ViewSummary.State);
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MessageBox.ShowAsync(
                        resp.responseData?.ToString() ?? "Unknown Error",
                        "Error",
                        icon: Icon.Error);
                });
                break;
        }
    }

    private static ViewSummary DeserializeViewSummary(MajWsResponseBase response)
    {
        return JsonSerializer.Deserialize<ViewSummary>(
            response.responseData?.ToString() ?? string.Empty,
            JSON_READER_OPTIONS);
    }

    private void UpdateViewSummary(ViewSummary summary)
    {
        ViewStatus oldState;
        lock (_stateSync)
        {
            oldState = _viewSummary.State;
            _viewSummary = summary;
        }

        Signal(_stateChangedSignal);
        if (oldState != summary.State)
            OnViewStateChanged?.Invoke(this, summary.State);
    }

    private static void Signal(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private void CloseClient(WebSocket client)
    {
        client.OnClose -= OnClose;
        client.OnOpen -= OnOpen;
        client.OnMessage -= OnMessage;
        client.OnError -= OnError;
        try
        {
            client.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to close player connection: {ex}");
        }
    }

    private void DiscardClient(WebSocket client)
    {
        lock (_connectionSync)
        {
            if (ReferenceEquals(_client, client))
                _client = null;
        }
        CloseClient(client);
    }

    public void Dispose()
    {
        lock (_connectionSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetimeCts.Cancel();
            if (_client is not null)
            {
                CloseClient(_client);
                _client = null;
            }
        }

        _lifetimeCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _listenerTask;
        }
        catch (OperationCanceledException)
        {
        }
        _messageSignal.Dispose();
        _stateChangedSignal.Dispose();
        _connectGate.Dispose();
    }
}
internal class PlayerNotConnectedException : Exception
{
    public PlayerNotConnectedException() : base() { }
}

