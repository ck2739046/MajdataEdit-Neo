using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using DiscordRPC;
using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Extensions;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Modules.AutoSave;
using MajdataEdit_Neo.Modules.AutoSave.Contexts;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.MajSetting;
using MajdataEdit_Neo.Types.MajWs;
using MajdataEdit_Neo.Types.SimaiAnalyzer;
using MajdataEdit_Neo.Utils;
using MajdataEdit_Neo.Views;
using MajSimai;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MajdataEdit_Neo.Base;
using static MajdataEdit_Neo.Base.MajEnv;
using static MajdataEdit_Neo.Utils.FFmpegChecker;

namespace MajdataEdit_Neo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public static MainWindowViewModel Ins { get; private set; }

    public static readonly string MAJDATA_VERSION_STRING = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    public static readonly SemVersion MAJDATA_VERSION = SemVersion.Parse(MAJDATA_VERSION_STRING, SemVersionStyles.Any);

    //------control panel
    public string DisplayTime
    {
        get
        {
            var minute = (int)TrackTime / 60;
            double second = (int)(TrackTime - 60 * minute);
            return string.Format("{0}:{1:00}", minute, second);
        }
    }
    public string DisplayLineComboText =>
        $"L {CaretLine}  Cb {CaretCombo}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Level))]
    [NotifyPropertyChangedFor(nameof(Designer))]
    public partial int SelectedDifficulty { get; set; } = 0;

    [ObservableProperty]
    public partial float PlaybackSpeed { get; set; } = 1;

    public float Offset
    {
        get
        {
            if (CurrentSimaiFile is null) return _offset;
            _offset = CurrentSimaiFile.Offset;
            return _offset;
        }
        set
        {
            if (CurrentSimaiFile is null) return;
            CurrentSimaiFile.Offset = value;
            SetProperty(ref _offset, value);
            OnPropertyChanged(nameof(CurrentSimaiFile));
        }
    }
    public string Level
    {
        get
        {
            if (CurrentSimaiFile is null || CurrentChartMetadata[SelectedDifficulty] is null) return "";
            _level[SelectedDifficulty] = CurrentChartMetadata[SelectedDifficulty].Level;
            return _level[SelectedDifficulty];
        }
        set
        {
            if (CurrentSimaiFile is null || CurrentChartMetadata[SelectedDifficulty] is null) return;
            CurrentChartMetadata[SelectedDifficulty].Level = value;
            Debug.WriteLine(SelectedDifficulty);
            SetProperty(ref _level[SelectedDifficulty], value);
            OnPropertyChanged(nameof(CurrentSimaiFile));
        }
    }
    public string Designer
    {
        get
        {
            if (CurrentSimaiFile is null || CurrentChartMetadata[SelectedDifficulty] is null) return "";
            var text = CurrentChartMetadata[SelectedDifficulty].Designer;
            if (text is null) return "";
            return text;
        }
        set
        {
            if (CurrentSimaiFile is null || CurrentChartMetadata[SelectedDifficulty] is null) return;
            var text = CurrentChartMetadata[SelectedDifficulty].Designer;
            if (text is null) return;
            SetProperty(ref text, value);
            CurrentChartMetadata[SelectedDifficulty].Designer = text;
            OnPropertyChanged(nameof(CurrentSimaiFile));
        }
    }
    public int SimaiDiagnosticsCount =>
        SimaiDiagnostics?.Count(o => o.Severity == Severity.Error) ?? 0;

    //------window state
    public bool IsLoaded
    {
        get
        {
            return CurrentSimaiFile is not null;
        }
    }
    public bool IsPointerPressedSimaiVisual { get; set; }
    public string WindowTitle
    {
        get
        {
            if (CurrentSimaiFile is null) return $"MajdataEdit Neo {MAJDATA_VERSION_STRING}";
            return $"MajdataEdit Neo {MAJDATA_VERSION_STRING} - {CurrentSimaiFile.Title}" + (IsSaved ? "" : "*");
        }
    }
    public bool IsFumenContextChanged
    {
        get
        {
            _autoSaveManager.IsFileChanged = !IsSaved;
            return _autoSaveManager.IsFileChanged;
        }
        set
        {
            IsSaved = !value;
            _autoSaveManager.IsFileChanged = value;
        }
    }
    //------simai
    private readonly TextDocument _fumenDocument = new();
    private Lock _updatingFumenDocumentLock = new();

    public TextDocument FumenDocument => _fumenDocument;
    public string CurrentFumen
    {
        get
        {
            if (CurrentSimaiFile is null)
                return string.Empty;

            return CurrentChartMetadata[SelectedDifficulty].Fumen;
        }
    }
    public string OriginFumen { get; set; } = string.Empty;

    // Provide file metadata
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Level))]
    [NotifyPropertyChangedFor(nameof(Designer))]
    [NotifyPropertyChangedFor(nameof(Offset))]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    public partial SimaiFile? CurrentSimaiFile { get; set; } = null;

    // Provide chart metadatas only, apply on save
    [ObservableProperty]
    private partial MutSimaiChartMetadata[] CurrentChartMetadata { get; set; } = new MutSimaiChartMetadata[7];

    // Provide timings only
    [ObservableProperty]
    public partial SimaiChart CurrentChartData { get; set; }

    private async Task UpdateFumenDocument()
    {
        lock (_updatingFumenDocumentLock)
            try
            {
                if (CurrentSimaiFile is null)
                {
                    _fumenDocument.Text = string.Empty;
                    OriginFumen = string.Empty;
                    return;
                }

                var fumenContent = CurrentChartMetadata[SelectedDifficulty].Fumen ?? string.Empty;
                OriginFumen = fumenContent;

                _fumenDocument.Text = OriginFumen;

                CurrentChartData = SimaiParser.ParseChartAsync(string.Empty, string.Empty, fumenContent).Result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
    }
    public async Task SetFumenContent(string content)
    {
        if (CurrentSimaiFile is null || _updatingFumenDocumentLock.IsHeldByCurrentThread) return;
        content ??= string.Empty;

        CurrentChartMetadata[SelectedDifficulty].Fumen = content;
        OnPropertyChanged(nameof(CurrentSimaiFile));
    }
    //------connection
    public bool IsConnected
    {
        get
        {
            return _playerConnection.IsConnected;
        }
    }

    //------window
    [ObservableProperty]
    MajSetting settings;
    [ObservableProperty]
    double caretTime = 0f;
    [ObservableProperty]
    float trackZoomLevel = 4f;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    bool isSaved = true;
    [ObservableProperty]
    TrackInfo? songTrackInfo = null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    double trackTime = 0f;
    [ObservableProperty]
    bool isFollowCursor;
    [ObservableProperty]
    bool isPlayControlEnabled = true;
    [ObservableProperty]
    bool isAnimated = true;
    [ObservableProperty]
    double fontSize;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLineComboText))]
    int caretLine = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLineComboText))]
    int caretCombo = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimaiDiagnosticsCount))]
    IReadOnlyList<SimaiDiagnostic> simaiDiagnostics;
    [ObservableProperty]
    List<(double, int, int)> signatures = [(0, 4, 4)];
    [ObservableProperty]
    bool isCheckingUpdate;
    [ObservableProperty]
    Bitmap backgroundImage;

    // 状态栏：当前View状态（响应式更新）
    [ObservableProperty]
    public partial ViewStatus CurrentViewState { get; set; } = ViewStatus.Idle;

    // 状态栏：临时消息（优先显示，为null时显示状态）
    [ObservableProperty]
    public partial string? StatusBarMessage { get; set; } = null;

    bool _isBackToStartOnPlayStop = false;
    bool _isUpdatingAutoSaveContext = false;

    bool _isStopping = false;

    float _offset = 0;
    double playStartTime = 0d;
    DateTime _lastUpdateAutoSaveContextTime = DateTime.UnixEpoch;

    string _maidataDir = "";
    public string MaidataDir => _maidataDir;

    readonly string[] _level = new string[7];
    readonly Lock _syncLock = new();
    readonly DiscordRpcClient _dcRPCClient = new("1068882546932326481");
    readonly RichPresence _dcRichPresence = new()
    {
        Details = "Nothing to do",
        State = "",
        Assets = new DiscordRPC.Assets
        {
            LargeImageKey = "salt",
            LargeImageText = "Majdata",
            SmallImageKey = "None"
        }
    };
    readonly Lock _fumenContentChangedSyncLock = new();

    TextEditor _textEditor;

    ChartEditDatabase _editDb = new(DatabaseFile);
    EditTimer _editTimer = new();

    PlayerConnection _playerConnection = new PlayerConnection();
    TrackReader _trackReader = new TrackReader();
    InternalAutoSaveContext _internalLocalAutoSaveContext = new();
    InternalAutoSaveContext _internalGlobalAutoSaveContext = new();
    InternalAutoSaveContentProvider _internalAutoSaveContentProvider = new();
    AutoSaveManager _autoSaveManager;
    IAutoSaveRecoverer _autoSaveRecoverer;

    //Bitmap太性情了 都不给一张Bitmap.Empty滚木
    private static readonly WriteableBitmap emptyBitmap = new(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888);

    const int AUTOSAVE_CONTEXT_UPDATE_INTERVAL_MS = 5000;

    public static string SettingsFile => GetPath("Settings.json");
    public static string CrashFile => GetPath("crash.log");
    public static string DatabaseFile => GetPath("editor.db");

    public MainWindowViewModel()
    {
        Ins = this;
        for (var i = 0; i < 7; i++) CurrentChartMetadata[i] = MutSimaiChartMetadata.Empty;

        PropertyChanged += MainWindowViewModel_PropertyChanged;
        _playerConnection.OnPlayStarted += _playerConnection_OnPlayStarted;
        _playerConnection.OnPlayStopped += _playerConnection_OnPlayStopped;
        _playerConnection.OnLoadRequired += _playerConnection_OnLoadRequired;
        _playerConnection.OnStopRequired += _playerConnection_OnStopRequired;
        _playerConnection.OnLoadFinished += _playerConnection_OnLoadFinished;
        _playerConnection.OnDisconnected += _playerConnection_OnDisconnected;
        _playerConnection.OnViewStateChanged += _playerConnection_OnViewStateChanged;
        _internalLocalAutoSaveContext = new(_internalAutoSaveContentProvider);
        _internalGlobalAutoSaveContext = new InternalAutoSaveContext(_internalAutoSaveContentProvider);
        AutoSaveManager.Initialize(_internalLocalAutoSaveContext, _internalGlobalAutoSaveContext);
        _autoSaveManager = AutoSaveManager.Instance;
        _autoSaveRecoverer = _autoSaveManager.Recoverer;

        ReadSettings();

        // 设计时初始化
        if (Design.IsDesignMode)
        {
            CurrentSimaiFile = SimaiFile.Empty("", "");
        }

        //_autoSaveManager.OnAutoSaveExecuted += OnAutoSaveExecuted;
        _dcRPCClient.SetPresence(_dcRichPresence);
    }

    private void _playerConnection_OnViewStateChanged(object? sender, ViewStatus e)
    {
        CurrentViewState = e;
    }

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
    public void SlideZoomLevel(float delta)
    {
        var level = TrackZoomLevel + delta;
        if (level <= 0.1f) level = 0.1f;
        if (level > 10f) level = 10f;
        TrackZoomLevel = level;
    }

    /// <summary>
    /// returns raw position in chart
    /// </summary>
    /// <param name="delta"></param>
    /// <returns></returns>
    public Point SlideTrackTime(double delta)
    {
        if (SongTrackInfo is null) return new Point();
        var time = TrackTime - delta * 0.2 * TrackZoomLevel;
        if (time < 0) time = 0;
        else if (time > SongTrackInfo.Length) time = SongTrackInfo.Length;
        if (_playerConnection.ViewSummary.State is ViewStatus.Playing or ViewStatus.Paused)
        {
            Stop(false);
        }
        TrackTime = time;
        if (CurrentChartData is null) return new Point();
        var timings = CurrentChartData.CommaTimings.Where(o => o.Timing + Offset - time < 0);
        if (timings.Length == 0) return new Point();
        var nearestNote = timings.MinBy(o => Math.Abs(o.Timing + Offset - time));
        if (nearestNote is null) return new Point();
        return new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY - 1);
    }
    public async void SetCaretTime(int rawPostion, bool setTrackTime)
    {
        if (CurrentChartData is null) return;

        //timings
        CaretTime = GetNearestCommaTimingFromPos(rawPostion)?.Timing ?? 0;

        //notes (combo)
        var currentCombo = 0;
        foreach (var note in CurrentChartData.NoteTimings)
        {
            if (note.RawTextPosition >= rawPostion)
            {
                break;
            }
            else
            {
                currentCombo += note.Notes.Length;
            }
        }
        CaretCombo = currentCombo;

        //track time
        if (setTrackTime)
        {
            //By pass Ctrl+Click if it's playing
            if (_playerConnection.ViewSummary.State == ViewStatus.Playing) return;
            Stop(false);
            TrackTime = CaretTime + Offset;
        }
    }

    public async Task NewFile()
    {
        if (await AskSave()) return;
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Track);
            if (file is null) return;
            var maidataPath = file.TryGetLocalPath();
            if (maidataPath is null) return;
            var fileInfo = new FileInfo(maidataPath);
            var directory = fileInfo.Directory.FullName;
            if (File.Exists(Path.Combine(directory, "maidata.txt")))
            {
                await Utils.MessageBox.ShowWindowDialogAsync(
                    Langs.Msg_MaidataAlreadyExist,
                    Langs.Gui_Error,
                    ButtonEnum.Ok, Icon.Error);
                await LoadChart(maidataPath);
                return;
            }
            await NewChart(directory);
            OpenChartInfoWindow();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }
    public async Task OpenFile()
    {
        if (await AskSave()) return;
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Maidata);
            if (file is null) return;
            var maidataPath = file.TryGetLocalPath();
            if (maidataPath is null) return;

            await LoadChart(maidataPath);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }

    private async Task EditorLoad()
    {
        try
        {
            IsPlayControlEnabled = false;
            var useOgg = File.Exists(_maidataDir + "/track.ogg");
            var trackPath = _maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3");

            var bgPath = _maidataDir + "/bg.jpg";
            if (!File.Exists(bgPath)) bgPath = _maidataDir + "/bg.png";
            if (!File.Exists(bgPath)) bgPath = "";

            var pvPath = _maidataDir + "/pv.mp4";
            if (!File.Exists(pvPath)) pvPath = _maidataDir + "/bg.mp4";
            if (!File.Exists(pvPath)) pvPath = "";

            if (!await CheckPlayerConnectionAndReconnect())
            {
                return;
            }
            await _playerConnection.LoadAsync(trackPath, bgPath, pvPath);
        }
        catch
        {
        }
    }
    private void _playerConnection_OnLoadFinished(object? sender, EventArgs e)
    {
        IsPlayControlEnabled = true;
    }

    //return: isCancel
    public async Task<bool> AskSave()
    {
        if (!IsSaved)
        {
            var result = await Utils.MessageBox.ShowWindowDialogAsync(
                Langs.Msg_ChartNotSaved,
                Langs.Gui_Warning,
                ButtonEnum.YesNoCancel,
                Icon.Warning);

            switch (result)
            {
                case ButtonResult.Yes:
                    SaveFile();
                    return false;
                case ButtonResult.No:
                    return false;
                default:
                    return true;

            }
        }
        return false;
    }
    public async void SaveFile()
    {
        if (CurrentSimaiFile is null)
            return;
        lock (_fumenContentChangedSyncLock)
        {
            IsFumenContextChanged = false;
            OriginFumen = CurrentFumen;
        }
        for (var i = 0; i < 7; i++)
        {
            CurrentSimaiFile.Charts[i] = new SimaiChart(
                CurrentChartMetadata[i].Level,
                CurrentChartMetadata[i].Designer,
                CurrentChartMetadata[i].Fumen,
                ReadOnlySpan<SimaiTimingPoint>.Empty);
        }
        await SimaiParser.DeparseAsync(CurrentSimaiFile,
            new FileStream(_maidataDir + "/maidata.txt", FileMode.Create, FileAccess.Write));
    }

    public async Task ReloadFile()
    {
        if (string.IsNullOrEmpty(_maidataDir)) return;
        var maidataPath = Path.Combine(_maidataDir, "maidata.txt");
        if (!File.Exists(maidataPath)) return;

        await LoadChart(maidataPath);
    }

    /// <summary>
    /// 初始化新谱面
    /// </summary>
    public async Task NewChart(string directory)
    {
        // 保存当前编辑记录
        SaveEditRecord();

        _maidataDir = directory;
        File.Create(Path.Combine(_maidataDir, "maidata.txt"));
        var levels = new SimaiChart[7];
        for (var i = 0; i < 7; i++)
            levels[i] = new SimaiChart(string.Empty, string.Empty, string.Empty, []);
        CurrentSimaiFile = new SimaiFile("Set Title", "Set Artist", 0, string.Empty, levels, null);
        SongTrackInfo = _trackReader.ReadTrack(_maidataDir);
        IsSaved = false;
        _autoSaveManager.Enabled = true;
        _internalAutoSaveContentProvider.Content = "";
        UpdateAutoSaveContext();

        // 初始化编辑记录
        _editTimer.Reset();
        _editTimer.Start();

        await EditorLoad();
    }

    /// <summary>
    /// 加载已有谱面
    /// </summary>
    private async Task LoadChart(string maidataPath)
    {
        // 保存当前编辑记录
        SaveEditRecord();

        var simaiFile = await SimaiParser.ParseAsync(new FileStream(maidataPath, FileMode.Open, FileAccess.Read));
        for (var i = 0; i < 7; i++)
        {
            var chart = simaiFile.Charts[i];
            CurrentChartMetadata[i] = chart.IsEmpty
                ? new()
                : new MutSimaiChartMetadata
                {
                    Level = chart.Level,
                    Designer = chart.Designer,
                    Fumen = chart.Fumen
                };
        }
        CurrentSimaiFile = simaiFile;
        IsSaved = true;
        var fileInfo = new FileInfo(maidataPath);
        _maidataDir = fileInfo.Directory.FullName;
        SongTrackInfo = _trackReader.ReadTrack(_maidataDir);
        _autoSaveManager.Enabled = true;
        _internalAutoSaveContentProvider.Content = await File.ReadAllTextAsync(maidataPath);
        UpdateAutoSaveContext();

        // 加载编辑记录
        LoadEditRecord();

        await EditorLoad();
    }

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }
    public async void OpenChartInfoWindow()
    {
        if (CurrentSimaiFile is null) return;
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;
        var window = new ChartInfoWindow();
        window.DataContext = new ChartInfoViewModel()
        {
            Title = CurrentSimaiFile.Title,
            Artist = CurrentSimaiFile.Artist,
            FinalDesigner = CurrentSimaiFile.FinalDesigner,
            SimaiCommands = [with(CurrentSimaiFile.Commands.Select(c => new MutSimaiCommand(c.Prefix, c.Value)))],
            MaidataDir = _maidataDir
        };
        await window.ShowDialog(mainWindow.MainWindow);
        var datacontext = window.DataContext as ChartInfoViewModel;
        if (datacontext is null) throw new Exception("Wtf");
        CurrentSimaiFile.Title = datacontext.Title;
        CurrentSimaiFile.Artist = datacontext.Artist;
        CurrentSimaiFile.FinalDesigner = datacontext.FinalDesigner;
        CurrentSimaiFile.Commands.Clear();
        foreach (var item in datacontext.SimaiCommands)
            CurrentSimaiFile.Commands.Add(item);
        await Task.Delay(100);
        OnPropertyChanged(nameof(CurrentSimaiFile));
        await EditorLoad();
    }
    public async void OpenSettingsWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;

        var settingsViewModel = new SettingsViewModel();
        settingsViewModel.LoadSettings(Settings);
        var window = new SettingsWindow
        {
            DataContext = settingsViewModel
        };
        await window.ShowDialog(mainWindow.MainWindow);
        SaveSettings();
        await Task.Delay(1);
    }
    public void MirrorHorizontal(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.LRMirror);
    }
    public void MirrorVertical(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.UDMirror);
    }
    public void Mirror180(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.HalfRotation);
    }
    public void Turn45(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.Rotation45);
    }
    public void TurnNegative45(TextEditor editor)
    {
        editor.SelectedText = SimaiMirror.HandleMirror(editor.SelectedText, SimaiMirror.HandleType.CcwRotation45);
    }
    public void Subdivide1p5x(TextEditor editor)
    {
        editor.SelectedText = SimaiSubdivide.Subdivide(editor.SelectedText, 1.5f);
    }
    public void Subdivide2x(TextEditor editor)
    {
        editor.SelectedText = SimaiSubdivide.Subdivide(editor.SelectedText, 2f);
    }

    public void IncreasePlaybackSpeed() => PlaybackSpeed += 0.1f;
    public void DecreasePlaybackSpeed() => PlaybackSpeed -= 0.1f;

    public async void PlayPause(TextEditor textEditor)
    {
        bool shouldRecoverPlayControl = true;
        try
        {
            IsPlayControlEnabled = false;
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
                    playStartTime = TrackTime;
                    return;
            }
            shouldRecoverPlayControl = false;
            playStartTime = TrackTime;
            _textEditor = textEditor;
            await _playerConnection.SettingAsync(Settings.ViewSetting, Settings.VolumeSetting);
            await _playerConnection.ParseAndPlayAsync(PlaybackMode.Normal, playStartTime, PlaybackSpeed,
                CurrentSimaiFile!.Title, CurrentSimaiFile!.Artist, Offset,
                Designer, Level, CurrentChartMetadata[SelectedDifficulty].Fumen,
                CurrentSimaiFile.Commands, SelectedDifficulty);
        }
        finally
        {
            if (shouldRecoverPlayControl)
                IsPlayControlEnabled = true;
        }
    }

    public async void PlayStop(TextEditor textEditor)
    {
        bool shouldRecoverPlayControl = true;
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckPlayerConnectionAndReconnect(true))
            {
                TrackTime = playStartTime;
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
                    playStartTime = TrackTime;
                    return;
            }
            shouldRecoverPlayControl = false;
            playStartTime = TrackTime;
            _textEditor = textEditor;
            await _playerConnection.SettingAsync(Settings.ViewSetting, Settings.VolumeSetting);
            await _playerConnection.ParseAndPlayAsync(PlaybackMode.Normal, playStartTime, PlaybackSpeed,
                CurrentSimaiFile!.Title, CurrentSimaiFile!.Artist, Offset,
                Designer, Level, CurrentChartMetadata[SelectedDifficulty].Fumen,
                CurrentSimaiFile.Commands, SelectedDifficulty);
        }
        finally
        {
            if (shouldRecoverPlayControl)
                IsPlayControlEnabled = true;
        }
    }

    public async void PlayIncludeOp(TextEditor textEditor)
    {
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckPlayerConnectionAndReconnect(true))
            {
                return;
            }
            TrackTime = 0;
            playStartTime = TrackTime;
            _textEditor = textEditor;
            await _playerConnection.SettingAsync(Settings.ViewSetting, Settings.VolumeSetting);
            await _playerConnection.ParseAndPlayAsync(PlaybackMode.IncludeOp, playStartTime, PlaybackSpeed,
                CurrentSimaiFile!.Title, CurrentSimaiFile!.Artist, Offset,
                Designer, Level, CurrentChartMetadata[SelectedDifficulty].Fumen,
                CurrentSimaiFile.Commands, SelectedDifficulty);
        }
        finally
        {
            IsPlayControlEnabled = true;
        }
    }

    public async void PlayRecord(TextEditor textEditor)
    {
        try
        {
            IsPlayControlEnabled = false;
            if (!await CheckPlayerConnectionAndReconnect(true))
            {
                return;
            }
            //TrackTime = 0;
            playStartTime = TrackTime;
            _textEditor = textEditor;
            await _playerConnection.SettingAsync(Settings.ViewSetting, Settings.VolumeSetting);
            await _playerConnection.ParseAndPlayAsync(PlaybackMode.Record, playStartTime, PlaybackSpeed,
                CurrentSimaiFile!.Title, CurrentSimaiFile!.Artist, Offset,
                Designer, Level, CurrentChartMetadata[SelectedDifficulty].Fumen,
                CurrentSimaiFile.Commands, SelectedDifficulty, _maidataDir);
        }
        finally
        {
            IsPlayControlEnabled = true;
        }
    }

    private async void _playerConnection_OnPlayStarted(object sender, MajWsResponseType e)
    {
        IsPlayControlEnabled = true;
        await Task.Run(async () =>
        {
            var recoverIsAnimated = IsAnimated;
            IsAnimated = false;

#if DEBUG
            string mmfAudioTimePath = string.Empty;
            if (OperatingSystem.IsWindows())
                mmfAudioTimePath = Path.Combine("D:\\repos\\re-poem\\MajdataViewX", "majdata_time.dat");
            else if (OperatingSystem.IsMacOS())
                mmfAudioTimePath = Path.Combine("/Users/re_poem/repos/MajdataViewX", "majdata_time.dat");
#else
            var mmfAudioTimePath = Path.Combine(MajEnv.MajBase, "majdata_time.dat");
#endif
            var mmfAudioTimeFileStream = new FileStream(
                mmfAudioTimePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            using var mmfAudioTime = MemoryMappedFile.CreateFromFile(
                mmfAudioTimeFileStream,
                null,
                sizeof(float),
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                false);
            using var mmvAudioTime = mmfAudioTime.CreateViewAccessor(0, sizeof(float), MemoryMappedFileAccess.Read);

            while (_playerConnection.ViewSummary.State == ViewStatus.Playing &&
                   _playerConnection.IsConnected)
            {
                TrackTime = mmvAudioTime.ReadSingle(0);
                if (IsFollowCursor)
                {
                    var nearestNote = CurrentChartData.CommaTimings.LastOrDefault(o => TrackTime - (o.Timing + Offset) > 0);
                    if (nearestNote is null) continue;

                    var point = new Point(nearestNote.RawTextPositionX, nearestNote.RawTextPositionY - 1);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SeekToDocPos(point, _textEditor);
                    });
                }
            }
            if (recoverIsAnimated)
                IsAnimated = true;
        });
    }
    public async void Stop(bool isBackToStart = true)
    {
        if (_isStopping) return;
        _isBackToStartOnPlayStop = isBackToStart;
        try
        {
            _isStopping = true;
            IsPlayControlEnabled = false;
            if (!await CheckPlayerConnectionAndReconnect())
            {
                if (isBackToStart)
                    TrackTime = playStartTime;
                return;
            }
            switch (_playerConnection.ViewSummary.State)
            {
                case ViewStatus.Loaded:
                    if (isBackToStart)
                        break;
                    else
                        return;
                case ViewStatus.Playing:
                case ViewStatus.Paused:
                    break;
                default:
                    return;
            }
            await _playerConnection.StopAsync();
        }
        finally
        {
            _isStopping = false;
            IsPlayControlEnabled = true;
        }

    }

    private async void _playerConnection_OnPlayStopped(object sender, MajWsResponseType e)
    {
        await Task.Delay(32); // Wait the OnPlayStarted Loop to end
        if (_isBackToStartOnPlayStop)
            TrackTime = playStartTime;
        IsPlayControlEnabled = true;
    }

    private async void _playerConnection_OnLoadRequired(object? sender, EventArgs e)
    {
        await EditorLoad();
    }

    private async void _playerConnection_OnStopRequired(object? sender, EventArgs e)
    {
        Stop();
    }
    private void _playerConnection_OnDisconnected(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsConnected));
    }

    async Task<bool> CheckPlayerConnectionAndReconnect(bool showMessageBox = false)
    {
        //TODO: 改成弱提示，比如状态指示灯

        if (!_playerConnection.IsConnected)
        {
            if (!await _playerConnection.ConnectAsync())
            {
                OnPropertyChanged(nameof(IsConnected));
                return false;
            }
            OnPropertyChanged(nameof(IsConnected));
            return false;
        }
        OnPropertyChanged(nameof(IsConnected));
        return true;
    }
    public void SeekToDocPos(Point position, TextEditor editor)
    {
        if (position.Y + 1 > editor.Document.LineCount) return;
        var offset = editor.Document.GetOffset((int)position.Y + 1, (int)position.X);
        editor.Select(offset, 0);
        editor.ScrollTo((int)position.Y + 1, (int)position.X);
        editor.Focus();
    }
    void UpdateAutoSaveContext()
    {
        _internalLocalAutoSaveContext.RawFilePath = Path.Combine(_maidataDir, "maidata.txt");
        _internalLocalAutoSaveContext.WorkingPath = Path.Combine(_maidataDir, ".autosave");
        _internalGlobalAutoSaveContext.RawFilePath = Path.Combine(_maidataDir, "maidata.txt");
    }

    void LoadEditRecord()
    {
        if (string.IsNullOrEmpty(_maidataDir)) return;

        var record = _editDb.GetRecord(_maidataDir);
        if (record is not null)
        {
            TrackTime = record.TrackTime;
            SelectedDifficulty = record.SelectedDifficulty;
            _editTimer.LoadAccumulated(record.TotalEditDuration);
        }
        else
        {
            _editTimer.Reset();
        }
        _editTimer.Start();
    }

    void SaveEditRecord()
    {
        if (string.IsNullOrEmpty(_maidataDir)) return;

        _editTimer.Pause();
        var record = new ChartEditRecord
        {
            ChartPath = _maidataDir,
            SelectedDifficulty = SelectedDifficulty,
            TrackTime = TrackTime,
            TotalEditDuration = _editTimer.Elapsed
        };
        _editDb.UpsertRecord(record);
    }
    private async void MainWindowViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        //Debug.WriteLine(e.PropertyName);
        if (e.PropertyName == nameof(SelectedDifficulty))
        {
            SaveEditRecord();
            await UpdateFumenDocument();
        }
        else if (e.PropertyName == nameof(CurrentSimaiFile))
        {
            Debug.WriteLine("SimaiFileChanged");
            Stop(false);
            lock (_fumenContentChangedSyncLock)
            {
                if (OriginFumen == CurrentFumen)
                    IsFumenContextChanged = false;
                else
                    IsFumenContextChanged = true;
            }
            lock (_syncLock)
            {
                if ((DateTime.Now - _lastUpdateAutoSaveContextTime).TotalMilliseconds < AUTOSAVE_CONTEXT_UPDATE_INTERVAL_MS)
                    return;
                else if (_isUpdatingAutoSaveContext)
                    return;
                _isUpdatingAutoSaveContext = true;
                _lastUpdateAutoSaveContextTime = DateTime.Now;
            }

            try
            {
                if (CurrentSimaiFile is null)
                    return;
                var maidata = await SimaiParser.DeparseAsync(CurrentSimaiFile);
                _internalAutoSaveContentProvider.Content = maidata;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                _isUpdatingAutoSaveContext = false;
            }
            await UpdateFumenDocument();
        }
    }
    //void OnAutoSaveExecuted(object? sender)
    //{
    //    if (!_fumenContentChangedSyncLock.TryEnter())
    //        return;
    //    try
    //    {
    //        IsFumenContextChanged = false;
    //        OriginFumen = CurrentFumen;
    //    }
    //    finally
    //    {
    //        _fumenContentChangedSyncLock.Exit();
    //    }
    //}
    public void AboutButtonClicked(int index)
    {
        switch (index)
        {
            case 0:
                OpenBrowser("https://discord.gg/AcWgZN7j6K");
                break;
            case 1:
                OpenBrowser("https://qm.qq.com/q/GAxbFZHP6A");
                break;
            case 2:
                OpenBrowser("https://github.com/LingFeng-bbben/MajdataEdit-Neo");
                break;
            case 3:
                OpenBrowser("https://majdata.net/");
                break;
        }
    }

    private void OpenBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // Works ok on windows
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);  // Works ok on linux
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url); // Not tested
        }
    }

    private void CreateSettings()
    {
        Settings = new MajSetting();
        File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));

        OpenSettingsWindow();
    }

    private void ReadSettings()
    {
        if (!File.Exists(SettingsFile)) CreateSettings();
        var json = File.ReadAllText(SettingsFile);
        Settings = JsonConvert.DeserializeObject<MajSetting>(json)!;

        ReloadSettings();

        SaveSettings(); // 覆盖旧版本setting
    }

    public void ReloadSettings()
    {
        I18N.Ins.Culture = new CultureInfo(Settings.EditSetting.Language);
        FontSize = Settings.EditSetting.FontSize;
        IsAnimated = Settings.EditSetting.WaveAnimated;
        var bgPath = GetPath(Settings.EditSetting.BackgroundImagePath);
        BackgroundImage = File.Exists(bgPath) ? new Bitmap(bgPath) : emptyBitmap;
    }

    public void SetWindowLastState(Window window)
    {
        Settings.WindowSetting = new MajWindowSetting
        {
            Width = window.Bounds.Width,
            Height = window.Bounds.Height,
            PosX = window.Position.X,
            PosY = window.Position.Y
        };
        SaveSettings();
    }

    private void SaveSettings()
    {
        File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
    }

    public async Task CheckUpdateAsync(bool onStart = false)
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;

        var response = await RequestGETAsync("http://api.github.com/repos/re-poem/MajdataViewX/releases/latest");

        try
        {
            if (response == "ERROR")
            {
                // 网络请求失败
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateRequestFail, Langs.Gui_CheckUpdate);
                return;
            }

            var resJson = JsonConvert.DeserializeObject<JObject>(response)!;

            if (resJson["tag_name"] == null || resJson["html_url"] == null)
            {
                // 解析失败
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateParseFail, Langs.Gui_CheckUpdate);
                return;
            }

            var latestVersionString = resJson["tag_name"]!.ToString();
            var releaseUrl = resJson["html_url"]!.ToString();

            var latestVersion = SemVersion.Parse(latestVersionString, SemVersionStyles.Any);
            if (latestVersion.ComparePrecedenceTo(MAJDATA_VERSION) > 0)
            {
                // 版本不同，需要更新
                var msgboxText = string.Format(Langs.Msg_NewVersionDetected,
                    latestVersionString,
                    MAJDATA_VERSION_STRING);
                if (onStart) msgboxText += "\n\n" + Langs.Msg_DisablingAutoCheckUpdate;

                var result = await MessageBox.ShowWindowDialogAsync(
                    msgboxText,
                    Langs.Gui_CheckUpdate,
                    ButtonEnum.YesNo);

                switch (result)
                {
                    case ButtonResult.Yes:
                        var startInfo = new ProcessStartInfo(releaseUrl)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        break;
                    case ButtonResult.No:
                        break;
                }
            }
            else
            {
                // 没有新版本
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_NoNewVersion, Langs.Gui_CheckUpdate);
            }
        }
        catch
        {
            // 解析失败
            if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateParseFail, Langs.Gui_CheckUpdate);
            return;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    //helpers
    public SimaiTimingPoint? GetNearestCommaTimingFromPos(int rawPosition)
    {
        var timings = CurrentChartData.CommaTimings;
        var nearestTiming = timings.FirstOrDefault();
        foreach (var timing in timings)
        {
            if (timing.RawTextPosition >= rawPosition)
            {
                nearestTiming = timing;
                break;
            }
        }
        return nearestTiming;
    }

    /// <summary>
    /// 显示临时状态栏消息
    /// </summary>
    public void ShowStatusMessage(string message) => StatusBarMessage = message;

    /// <summary>
    /// 重置状态栏，恢复显示 ViewState
    /// </summary>
    public void ResetStatusMessage() => StatusBarMessage = null;

    /// <summary>
    /// 窗口关闭时调用，保存编辑记录
    /// </summary>
    public void OnWindowClosing()
    {
        SaveEditRecord();
        _editDb.Dispose();
    }

    /// <summary>
    /// 压缩bg.mp4或pv.mp4
    /// </summary>
    public async Task CompressBgVideo()
    {
        // 优先 bg.mp4，其次 pv.mp4
        var bgVideoPath = Path.Combine(_maidataDir, "bg.mp4");
        if (!File.Exists(bgVideoPath))
        {
            bgVideoPath = Path.Combine(_maidataDir, "pv.mp4");
        }
        if (!File.Exists(bgVideoPath))
        {
            await MessageBox.ShowWindowDialogAsync(Assets.Langs.Langs.Status_NoBgVideo, "Error", icon: Icon.Error);
            return;
        }

        if (!await EnsureFFmpeg()) return;

        var videoFileName = Path.GetFileName(bgVideoPath); // "bg.mp4" 或 "pv.mp4"
        var videoBaseName = Path.GetFileNameWithoutExtension(bgVideoPath); // "bg" 或 "pv"

        var outputPath = Path.Combine(_maidataDir, $"{videoBaseName}_compressed.mp4");

        ShowStatusMessage(Assets.Langs.Langs.Status_Compressing);

        try
        {
            var success = await Task.Run(() => RunFfmpegCompress("ffmpeg", bgVideoPath, outputPath));

            if (success && File.Exists(outputPath))
            {
                // 备份原文件并替换
                var backupPath = Path.Combine(_maidataDir, $"{videoBaseName}_original.mp4");
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(bgVideoPath, backupPath);
                File.Move(outputPath, bgVideoPath);

                var originalSize = new FileInfo(backupPath).Length / 1024.0 / 1024.0;
                var newSize = new FileInfo(bgVideoPath).Length / 1024.0 / 1024.0;

                await MessageBox.ShowWindowDialogAsync(
                    $"{Assets.Langs.Langs.Status_CompressComplete}\n{originalSize:F2}MB → {newSize:F2}MB",
                    "Success", icon: Icon.Success);
                await ReloadFile();
            }
            else
            {
                await MessageBox.ShowWindowDialogAsync(Assets.Langs.Langs.Status_CompressFailed, "Error", icon: Icon.Error);
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowWindowDialogAsync($"Error: {ex.Message}", "Error", icon: Icon.Error);
        }
        finally
        {
            ResetStatusMessage();
        }
    }

    private bool RunFfmpegCompress(string ffmpegPath, string inputPath, string outputPath)
    {
        try
        {
            // 目标：5分钟视频压到20MB以内
            // 无音频，20MB/5min ≈ 540kbps 全给视频，540p
            var args = $"-y -i \"{inputPath}\" -vf \"scale=-2:540,fps=30\" -c:v libx264 -preset veryfast -b:v 540k -an \"{outputPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // For Update Check
    public static async Task<string> RequestGETAsync(string url)
    {
        try
        {
            var executingAssembly = Assembly.GetExecutingAssembly();

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Add("User-Agent", $"{executingAssembly.GetName().Name!} / {executingAssembly.GetName().Version!.ToString(3)}");

            var response = await new HttpClient().SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return "ERROR";
        }
    }

    class InternalAutoSaveContext : IAutoSaveContext, IAutoSaveContentProvider<string>
    {
        public string WorkingPath { get; set; } = Path.Combine(Environment.CurrentDirectory, ".autosave");
        public string RawFilePath { get; set; } = string.Empty;
        public string Content => _contentProvider?.Content ?? string.Empty;

        IAutoSaveContentProvider<string>? _contentProvider;

        public InternalAutoSaveContext(IAutoSaveContentProvider<string>? contentProvider)
        {
            _contentProvider = contentProvider;
        }
        public InternalAutoSaveContext()
        {

        }
    }
    class InternalAutoSaveContentProvider : IAutoSaveContentProvider<string>
    {
        public string Content { get; set; } = string.Empty;
    }
}