using Avalonia;
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
using System.Threading.Tasks;
using Types;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class PlaybackModel : ViewModelBase
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

        var mmfAudioTimeFileStream = new FileStream(
            //这个文件在库里包含并在发布时也包含，避免第一次打开crash
            Path.Combine(MajEnv.MajBase, "majdata_time.dat"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        mmfAudioTime = MemoryMappedFile.CreateFromFile(
            mmfAudioTimeFileStream,
            null,
            sizeof(float),
            MemoryMappedFileAccess.Read,
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
    private int _currentCombo = 0;

    internal readonly PlayerConnection _playerConnection = new();
    internal double _playStartTime = 0d;
    internal bool _isBackToStartOnPlayStop = false;
    internal bool _isStopping = false;
    internal bool _isLastPlayIncludeOp = false;

    public event EventHandler? LoadRequired;
    public event Action<Point>? RequestSeekToDocPos;


    public bool IsConnected => _playerConnection.IsConnected;

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

        SimaiTimingPoint? nearestNote = null;
        double minDiff = double.MaxValue;

        foreach (var o in chartData.CommaTimings)
        {
            double diff = Math.Abs(o.Timing + offset - time);
            if (diff < minDiff)
            {
                minDiff = diff;
                nearestNote = o;
            }
        }

        if (nearestNote is null) return new Point();
        return new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY - 1);
    }

    public void IncreasePlaybackSpeed() => PlaybackSpeed += 0.1f;
    public void DecreasePlaybackSpeed() => PlaybackSpeed -= 0.1f;

    public void SetCaretTime(double caretTime, float offset, bool setTrackTime)
    {
        if (_doc.CurrentChartData is null) return;
        CaretTime = caretTime + offset;

        var notes = _doc.CurrentChartData.NoteTimings;
        var currentCombo = 0;
        foreach (var note in notes)
        {
            if (note.Timing >= caretTime) break;
            currentCombo++;
        }
        CurrentCombo = currentCombo;

        if (setTrackTime)
            TrackTime = CaretTime;
    }

    public async void SetCaretTime(int rawPosition, bool setTrackTime)
    {
        var chartData = _doc.CurrentChartData;
        if (chartData is null) return;

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
            currentCombo++;
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
        CurrentViewState = ViewStatus.Playing;

        await Task.Run(async () =>
        {
            try
            {
                while (_playerConnection.ViewSummary.State == ViewStatus.Playing &&
                       _playerConnection.IsConnected)
                {
                    TrackTime = mmvAudioTime.ReadSingle(0);
                    if (IsFollowCursor)
                    {
                        var chartData = _doc.CurrentChartData;
                        if (chartData is not null)
                        {
                            SimaiTimingPoint? nearestNote = null;
                            foreach (var o in chartData.CommaTimings)
                            {
                                if (TrackTime - (o.Timing + _doc.Offset) > 0)
                                {
                                    nearestNote = o;
                                }
                            }
                            if (nearestNote != null)
                            {
                                var point = new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY - 1);
                                RequestSeekToDocPos?.Invoke(point);
                            }
                        }
                    }
                    await Task.Delay(16);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Start Read Play Time MMV Err:{ex}");
            }
        });
    }

    private async void OnPlayStopped(object sender, MajWsResponseType e)
    {
        await Task.Delay(32); // Wait the OnPlayStarted Loop to end
        CurrentViewState = ViewStatus.Idle;
        if (_isBackToStartOnPlayStop) TrackTime = _playStartTime;
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
        OnPropertyChanged(nameof(IsConnected));
    }

    private void OnViewStateChanged(object? sender, ViewStatus e)
    {
        CurrentViewState = e;
    }
}
