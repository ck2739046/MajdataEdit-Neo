using Avalonia;
using Avalonia.Threading;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.ComponentModel;
using MajdataEdit_Neo.Base;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.MajSetting;
using MajdataEdit_Neo.Types.MajWs;
using MajSimai;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Types;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class PlaybackModel : ViewModelBase, IAsyncDisposable
{
    private readonly IReadOnlyDocument _doc;
    private readonly Func<string> _getMaidataDir;

    private readonly MemoryMappedFile mmfAudioTime = null!;
    private readonly MemoryMappedViewAccessor mmvAudioTime = null!;
    public PlaybackModel(IReadOnlyDocument doc, Func<string> getMaidataDir)
    {
        _doc = doc;
        _getMaidataDir = getMaidataDir;
        _playerConnection.OnPlayStarted += OnPlayStarted;
        _playerConnection.OnPlayStopped += OnPlayStopped;
        _playerConnection.OnLoadRequired += OnLoadRequired;
        _playerConnection.OnStopRequired += OnStopRequired;
        _playerConnection.OnDisconnected += OnDisconnected;
        _playerConnection.OnViewStateChanged += OnViewStateChanged;

        Directory.CreateDirectory(MajEnv.MajdataViewPersistentDataPath);
        var mmfAudioTimeFileStream = new FileStream(
            MajEnv.MajdataViewTimeFile,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite
        );
        if (mmfAudioTimeFileStream.Length < sizeof(float))
            mmfAudioTimeFileStream.SetLength(sizeof(float));
        mmfAudioTime = MemoryMappedFile.CreateFromFile(
            mmfAudioTimeFileStream,
            null,
            sizeof(float),
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            false
        );
        mmvAudioTime = mmfAudioTime.CreateViewAccessor(0, sizeof(float), MemoryMappedFileAccess.Read);
    }

    [ObservableProperty]
    private float _playbackSpeed = 1f;

    [ObservableProperty]
    private double _trackTime = 0d;

    partial void OnTrackTimeChanged(double value) => OnPropertyChanged(nameof(DisplayTime));

    [ObservableProperty]
    private float _trackZoomLevel = 4f;

    [ObservableProperty]
    private double _caretTime = 0d;

    [ObservableProperty]
    private bool _isFollowCursor;

    [ObservableProperty]
    private ViewStatus _currentViewState = ViewStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLineComboText))]
    private int _currentCombo = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLineComboText))]
    private int _caretLine = 1;

    internal readonly PlayerConnection _playerConnection = new();
    internal double _playStartTime = 0d;
    internal bool _isBackToStartOnPlayStop = false;
    internal bool _isStopping = false;
    internal bool _isLastPlayIncludeOp = false;
    private readonly Lock _playbackTrackingLock = new();
    private CancellationTokenSource? _playbackTrackingCts;
    private Task _playbackTrackingTask = Task.CompletedTask;
    private SimaiChart? _followChart;
    private int _followTimingIndex = -1;
    private int _lastReportedFollowTimingIndex = -1;
    private double _lastFollowChartTime = double.NegativeInfinity;
    private bool _disposed;

    public event Action<Point>? RequestSeekToDocPos;


    public bool IsConnected => _playerConnection.IsConnected;

    public string DisplayLineComboText => $"L {CaretLine}  Cb {CurrentCombo}";

    public string DisplayTime
    {
        get
        {
            var minute = (int)TrackTime / 60;
            double second = (int)(TrackTime - 60 * minute);
            return string.Format("{0}:{1:00}", minute, second);
        }
    }

    public record PlayContext(
        string Title,
        string Artist,
        float Offset,
        string Designer,
        string Level,
        string Fumen,
        IList<SimaiCommand> Commands,
        int SelectedDifficulty
    );

    public async Task<bool> ConnectToPlayerAsync()
    {
        if (!await _playerConnection.ConnectAsync())
        {
            OnPropertyChanged(nameof(IsConnected));
            return false;
        }
        OnPropertyChanged(nameof(IsConnected));
        return true;
    }

    public async Task<bool> CheckPlayerConnectionAndReconnect(bool showMessageBox = false)
    {
        if (!_playerConnection.IsConnected)
        {
            if (!await _playerConnection.ConnectAsync())
            {
                OnPropertyChanged(nameof(IsConnected));
                return false;
            }
        }
        OnPropertyChanged(nameof(IsConnected));
        return true;
    }

    public void SlideZoomLevel(float delta)
    {
        var level = TrackZoomLevel + delta;
        if (level <= 0.1f) level = 0.1f;
        if (level > 10f) level = 10f;
        TrackZoomLevel = level;
    }

    public Point SlideTrackTime(double delta, TrackInfo? songTrackInfo, SimaiChart? chartData, float offset)
    {
        if (songTrackInfo is null) return new Point();
        var time = TrackTime - delta * 0.2 * TrackZoomLevel;
        if (time < 0) time = 0;
        else if (time > songTrackInfo.Length) time = songTrackInfo.Length;
        if (_playerConnection.ViewSummary.State is ViewStatus.Playing or ViewStatus.Paused)
        {
            Stop(false);
        }
        TrackTime = time;
        if (chartData is null) return new Point();

        var timings = chartData.CommaTimings;
        if (timings.Length == 0) return new Point();
        var chartTime = time - offset;
        var index = FindTimingIndexAtOrBefore(timings, chartTime);
        if (index + 1 < timings.Length &&
            (index < 0 ||
             Math.Abs(timings[index + 1].Timing - chartTime) <
             Math.Abs(timings[index].Timing - chartTime)))
        {
            index++;
        }
        if (index < 0) index = 0;
        var nearestNote = timings[index];

        return new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY - 1);
    }

    public void IncreasePlaybackSpeed() => PlaybackSpeed += 0.1f;
    public void DecreasePlaybackSpeed() => PlaybackSpeed -= 0.1f;

    public void SetCaretPosition(int rawPosition, int line, bool setTrackTime)
    {
        CaretLine = line;

        var chartData = _doc.CurrentChartData;

        var timings = chartData.CommaTimings;
        var nearestTiming = timings.Length > 0 ? timings[0] : default;
        foreach (var timing in timings)
        {
            if (timing.RawTextPosition >= rawPosition)
            {
                nearestTiming = timing;
                break;
            }
        }
        CaretTime = nearestTiming?.Timing ?? 0;

        var notes = chartData.NoteTimings;
        var currentCombo = 0;
        foreach (var note in notes)
        {
            if (note.RawTextPosition >= rawPosition) break;
            currentCombo += note.Notes.Length;
        }
        CurrentCombo = currentCombo;

        if (setTrackTime)
        {
            TrackTime = CaretTime + _doc.Offset;
        }
    }

    public async Task EditorLoad(string maidataDir)
    {
        try
        {
            var useOgg = System.IO.File.Exists(maidataDir + "/track.ogg");
            var trackPath = maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3");

            var bgPath = maidataDir + "/bg.jpg";
            if (!System.IO.File.Exists(bgPath)) bgPath = maidataDir + "/bg.png";
            if (!System.IO.File.Exists(bgPath)) bgPath = "";

            var pvPath = maidataDir + "/pv.mp4";
            if (!System.IO.File.Exists(pvPath)) pvPath = maidataDir + "/bg.mp4";
            if (!System.IO.File.Exists(pvPath)) pvPath = "";

            if (!await CheckPlayerConnectionAndReconnect())
            {
                return;
            }
            await _playerConnection.LoadAsync(trackPath, bgPath, pvPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load editor: {ex}");
        }
    }

    public async Task PlayPause(PlayContext ctx, MajSetting settings)
    {
        if (!await CheckPlayerConnectionAndReconnect(true))
        {
            return;
        }

        switch (_playerConnection.ViewSummary.State)
        {
            case ViewStatus.Playing:
                await _playerConnection.PauseAsync();
                return;
            case ViewStatus.Paused:
                await _playerConnection.ResumeAsync();
                _playStartTime = TrackTime;
                _isLastPlayIncludeOp = false;
                return;
        }
        _playStartTime = TrackTime;
        await _playerConnection.SettingAsync(settings.ViewSetting, settings.VolumeSetting);
        await _playerConnection.ParseAndPlayAsync(PlaybackMode.Normal, _playStartTime, PlaybackSpeed,
            ctx.Title, ctx.Artist, ctx.Offset,
            ctx.Designer, ctx.Level, ctx.Fumen,
            ctx.Commands, ctx.SelectedDifficulty);
        _isLastPlayIncludeOp = false;
    }

    public async void Stop(bool toStart = true)
    {
        try
        {
            _isStopping = true;
            _isBackToStartOnPlayStop = toStart;

            if (!await CheckPlayerConnectionAndReconnect())
            {
                if (toStart)
                    TrackTime = _playStartTime;
                return;
            }

            await _playerConnection.StopAsync();
        }
        finally
        {
            _isStopping = false;
        }
    }

    public async Task PlayStop(PlayContext ctx, MajSetting settings)
    {
        if (!await CheckPlayerConnectionAndReconnect(true))
        {
            TrackTime = _playStartTime;
            return;
        }

        switch (_playerConnection.ViewSummary.State)
        {
            case ViewStatus.Playing:
                _isBackToStartOnPlayStop = true;
                await _playerConnection.StopAsync();
                return;
            case ViewStatus.Paused:
                await _playerConnection.ResumeAsync();
                _isLastPlayIncludeOp = false;
                _playStartTime = TrackTime;
                return;
        }
        _playStartTime = TrackTime;
        await _playerConnection.SettingAsync(settings.ViewSetting, settings.VolumeSetting);
        await _playerConnection.ParseAndPlayAsync(PlaybackMode.Normal, _playStartTime, PlaybackSpeed,
            ctx.Title, ctx.Artist, ctx.Offset,
            ctx.Designer, ctx.Level, ctx.Fumen,
            ctx.Commands, ctx.SelectedDifficulty);
        _isLastPlayIncludeOp = false;
    }

    public async Task PlayIncludeOp(PlayContext ctx, MajSetting settings)
    {
        if (!await CheckPlayerConnectionAndReconnect(true))
        {
            return;
        }
        _playStartTime = TrackTime;
        await _playerConnection.SettingAsync(settings.ViewSetting, settings.VolumeSetting);
        await _playerConnection.ParseAndPlayAsync(PlaybackMode.IncludeOp, _playStartTime, PlaybackSpeed,
            ctx.Title, ctx.Artist, ctx.Offset,
            ctx.Designer, ctx.Level, ctx.Fumen,
            ctx.Commands, ctx.SelectedDifficulty);
        _isLastPlayIncludeOp = true;
    }

    public async Task PlayRecord(PlayContext ctx, MajSetting settings, string maidataDir)
    {
        if (!await CheckPlayerConnectionAndReconnect(true))
        {
            return;
        }

        _playStartTime = TrackTime;
        await _playerConnection.SettingAsync(settings.ViewSetting, settings.VolumeSetting);
        await _playerConnection.ParseAndPlayAsync(PlaybackMode.Record, _playStartTime, PlaybackSpeed,
            ctx.Title, ctx.Artist, ctx.Offset,
            ctx.Designer, ctx.Level, ctx.Fumen,
            ctx.Commands, ctx.SelectedDifficulty, maidataDir);
        _isLastPlayIncludeOp = false;
    }


    private async void OnPlayStarted(object sender, MajWsResponseType e)
    {
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        Task trackingTask;
        lock (_playbackTrackingLock)
        {
            var previousCts = _playbackTrackingCts;
            _playbackTrackingCts = cts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                CurrentViewState = ViewStatus.Playing;
                ResetFollowCursorIndex();
            });

            trackingTask = TrackPlaybackAsync(cancellationToken);
            _playbackTrackingTask = trackingTask;
        }

        try
        {
            await trackingTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Start Read Play Time MMV Err:{ex}");
        }
    }

    private async void OnPlayStopped(object sender, MajWsResponseType e)
    {
        var trackingTask = CancelPlaybackTracking();
        try
        {
            await trackingTask;
        }
        catch (OperationCanceledException)
        {
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentViewState = ViewStatus.Idle;
            if (_isBackToStartOnPlayStop) TrackTime = _playStartTime;
        });
    }

    private async void OnLoadRequired(object? sender, EventArgs e)
    {
        await EditorLoad(_getMaidataDir());
    }

    private void OnStopRequired(object? sender, EventArgs e)
    {
        Stop();
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        CancelPlaybackTracking();
        Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsConnected)));
    }

    private void OnViewStateChanged(object? sender, ViewStatus e)
    {
        Dispatcher.UIThread.Post(() => CurrentViewState = e);
    }

    private async Task TrackPlaybackAsync(CancellationToken cancellationToken)
    {
        while (_playerConnection.ViewSummary.State == ViewStatus.Playing &&
               _playerConnection.IsConnected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trackTime = mmvAudioTime.ReadSingle(0);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                TrackTime = trackTime;
                if (!IsFollowCursor)
                    return;

                var point = GetFollowCursorPoint(trackTime);
                if (point is not null)
                    RequestSeekToDocPos?.Invoke(point.Value);
            });

            await Task.Delay(16, cancellationToken);
        }
    }

    private Point? GetFollowCursorPoint(double trackTime)
    {
        var chart = _doc.CurrentChartData;
        var timings = chart.CommaTimings;
        if (timings.Length == 0)
            return null;

        var chartTime = trackTime - _doc.Offset;
        if (!ReferenceEquals(chart, _followChart) ||
            chartTime < _lastFollowChartTime ||
            chartTime - _lastFollowChartTime > 0.5 ||
            _followTimingIndex >= timings.Length)
        {
            if (!ReferenceEquals(chart, _followChart))
                _lastReportedFollowTimingIndex = -1;
            _followChart = chart;
            _followTimingIndex = FindTimingIndexAtOrBefore(timings, chartTime);
        }
        else
        {
            while (_followTimingIndex + 1 < timings.Length &&
                   timings[_followTimingIndex + 1].Timing <= chartTime)
            {
                _followTimingIndex++;
            }
        }

        _lastFollowChartTime = chartTime;
        if (_followTimingIndex < 0 ||
            _followTimingIndex == _lastReportedFollowTimingIndex)
            return null;

        _lastReportedFollowTimingIndex = _followTimingIndex;
        var timing = timings[_followTimingIndex];
        return new Point(timing.RawTextPositionX, timing.RawTextPositionY - 1);
    }

    private void ResetFollowCursorIndex()
    {
        _followChart = null;
        _followTimingIndex = -1;
        _lastReportedFollowTimingIndex = -1;
        _lastFollowChartTime = double.NegativeInfinity;
    }

    private Task CancelPlaybackTracking()
    {
        lock (_playbackTrackingLock)
        {
            var cts = _playbackTrackingCts;
            _playbackTrackingCts = null;
            cts?.Cancel();
            cts?.Dispose();
            return _playbackTrackingTask;
        }
    }

    private static int FindTimingIndexAtOrBefore(ReadOnlySpan<SimaiTimingPoint> timings, double chartTime)
    {
        var low = 0;
        var high = timings.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (timings[middle].Timing <= chartTime)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        var trackingTask = CancelPlaybackTracking();
        try
        {
            await trackingTask;
        }
        catch (OperationCanceledException)
        {
        }
        _playerConnection.OnPlayStarted -= OnPlayStarted;
        _playerConnection.OnPlayStopped -= OnPlayStopped;
        _playerConnection.OnLoadRequired -= OnLoadRequired;
        _playerConnection.OnStopRequired -= OnStopRequired;
        _playerConnection.OnDisconnected -= OnDisconnected;
        _playerConnection.OnViewStateChanged -= OnViewStateChanged;
        try
        {
            await _playerConnection.DisposeAsync();
        }
        finally
        {
            mmvAudioTime.Dispose();
            mmfAudioTime.Dispose();
        }
    }
}
