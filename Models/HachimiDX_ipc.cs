using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataEdit_Neo.Models;

/// <summary>
/// HachimiDX 双向 UDP IPC：监听 8015 收指令（load/reset/exit，seq 去重 + ack），向 8014 广播播放同步事件。
/// </summary>
internal sealed class HachimiDX_ipc : IDisposable
{
    public const int EditListenPort = 8015;
    public const int HachimiListenPort = 8014;

    public sealed class LoadCommandEventArgs : EventArgs
    {
        public string Folder { get; init; } = string.Empty;
        public string Maidata { get; init; } = string.Empty;
        public string Track { get; init; } = string.Empty;
        public string? Pv { get; init; }
    }

    public event EventHandler<LoadCommandEventArgs>? LoadRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? ExitRequested;

    private readonly CancellationTokenSource _cts = new();
    private readonly object _seqLock = new();
    private long _lastSeq = -1;
    private UdpClient? _listener;
    private UdpClient? _sender;
    private Task? _receiveLoop;

    public void Start()
    {
        if (_listener is not null)
            return;

        _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, EditListenPort));
        _sender = new UdpClient();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void SendEvent(JObject payload)
    {
        payload["v"] = 1;
        var bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
        try
        {
            _sender?.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, HachimiListenPort));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HachimiDX_ipc send failed: {ex}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(ct);
                HandleDatagram(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HachimiDX_ipc receive failed: {ex}");
            }
        }
    }

    private void HandleDatagram(byte[] buffer, IPEndPoint remote)
    {
        JObject? msg;
        try
        {
            msg = JObject.Parse(Encoding.UTF8.GetString(buffer));
        }
        catch
        {
            return;
        }

        var type = (string?)msg["type"];
        var seq = (long?)msg["seq"] ?? -1;

        if (seq >= 0)
        {
            lock (_seqLock)
            {
                if (seq <= _lastSeq)
                {
                    SendAck(remote, seq, "ok");
                    return;
                }
                _lastSeq = seq;
            }
        }

        switch (type)
        {
            case "load":
            {
                var folder = (string?)msg["folder"];
                var maidata = (string?)msg["maidata"];
                var track = (string?)msg["track"];
                var pv = (string?)msg["pv"];
                if (string.IsNullOrWhiteSpace(folder) ||
                    string.IsNullOrWhiteSpace(maidata) ||
                    string.IsNullOrWhiteSpace(track))
                {
                    SendAck(remote, seq, "error", "missing folder/maidata/track");
                    return;
                }
                LoadRequested?.Invoke(this, new LoadCommandEventArgs
                {
                    Folder = folder,
                    Maidata = maidata,
                    Track = track,
                    Pv = string.IsNullOrWhiteSpace(pv) ? null : pv
                });
                SendAck(remote, seq, "ok");
                break;
            }
            case "reset":
                ResetRequested?.Invoke(this, EventArgs.Empty);
                SendAck(remote, seq, "ok");
                break;
            case "exit":
                ExitRequested?.Invoke(this, EventArgs.Empty);
                SendAck(remote, seq, "ok");
                break;
            default:
                SendAck(remote, seq, "error", $"unknown type: {type}");
                break;
        }
    }

    private void SendAck(IPEndPoint remote, long seq, string status, string? error = null)
    {
        var ack = new JObject
        {
            ["v"] = 1,
            ["type"] = "ack",
            ["seq"] = seq,
            ["status"] = status
        };
        if (error is not null)
            ack["error"] = error;

        var bytes = Encoding.UTF8.GetBytes(ack.ToString(Formatting.None));
        try
        {
            _listener?.Send(bytes, bytes.Length, remote);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HachimiDX_ipc ack failed: {ex}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Dispose();
        _sender?.Dispose();
        _cts.Dispose();
    }
}
