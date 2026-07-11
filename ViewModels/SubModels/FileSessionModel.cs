using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Utils;
using MajSimai;
using MajdataEdit_Neo.ViewModels;
using MsBox.Avalonia.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Types;

namespace ViewModels.SubModels;

public partial class FileSessionModel : ViewModelBase
{
    //------sub-models (hierarchical ownership)
    public DocumentModel Doc { get; }
    public PlaybackModel Playback { get; }
    public AutoSaveModel AutoSave { get; }
    public ToolsModel Tools { get; }
    public DiscordRpcModel DiscordRpc { get; }

    //------file state
    readonly ChartEditDatabase _editDb;
    readonly TrackReader _trackReader = new();
    readonly EditTimer _editTimer = new();

    [ObservableProperty]
    private string _maidataDir = "";

    [ObservableProperty]
    private TrackInfo? _songTrackInfo = null;

    private readonly MainWindowViewModel _mainWindow;

    public FileSessionModel(MainWindowViewModel mainWindow, ChartEditDatabase editDb)
    {
        _mainWindow = mainWindow;
        _editDb = editDb;

        // Instantiate Document first
        Doc = new DocumentModel();

        // Inject readonly interfaces to sub-models
        Playback = new PlaybackModel(this.Doc, () => this.MaidataDir);
        // Note: Currently PlaybackModel uses global MainWindowViewModel.Ins.Session.Doc or maybe it doesn't? 
        // We will refactor PlaybackModel to accept IReadOnlyDocument soon.

        AutoSave = new AutoSaveModel();
        DiscordRpc = new DiscordRpcModel();

        // Inject mutable interface to ToolsModel
        Tools = new ToolsModel(_mainWindow, this, Doc);

        WireEvents();
    }

    private void WireEvents()
    {
        // Doc changes -> update WindowTitle, auto-save, stop playback
        Doc.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DocumentModel.CurrentSimaiFile))
            {
                _mainWindow.NotifyWindowTitleChanged();
                Playback.Stop(false);
                Doc.UpdateFumenContextChanged();
                AutoSave.IsFileChanged = !Doc.IsSaved;
                _ = AutoSave.OnSimaiFileChangedAsync(Doc.CurrentSimaiFile);
            }
            else if (e.PropertyName == nameof(DocumentModel.SelectedDifficulty))
            {
                SaveEditRecord();
            }
            else if (e.PropertyName == nameof(DocumentModel.IsSaved))
            {
                _mainWindow.NotifyWindowTitleChanged();
                AutoSave.IsFileChanged = !Doc.IsSaved;
            }
        };

        Playback.LoadRequired += async (s, e) =>
        {
            await Playback.EditorLoad(MaidataDir);
        };
    }

    public async Task<bool> AskSave()
    {
        if (!Doc.IsSaved)
        {
            var result = await MessageBox.ShowWindowDialogAsync(
                Langs.Msg_ChartNotSaved,
                Langs.Gui_Warning,
                ButtonEnum.YesNoCancel,
                Icon.Warning);

            switch (result)
            {
                case ButtonResult.Yes:
                    await SaveFile();
                    return false;
                case ButtonResult.No:
                    return false;
                default:
                    return true; // cancel
            }
        }
        return false;
    }

    public async Task ReloadFile()
    {
        if (string.IsNullOrEmpty(MaidataDir)) return;
        var maidataPath = Path.Combine(MaidataDir, "maidata.txt");
        if (!File.Exists(maidataPath)) return;
        await LoadChart(maidataPath);
    }

    public async Task NewChartFromDir(string directory)
    {
        SaveEditRecord();

        MaidataDir = directory;
        File.Create(Path.Combine(MaidataDir, "maidata.txt")).Dispose();
        var levels = new SimaiChart[7];
        for (var i = 0; i < 7; i++)
            levels[i] = new SimaiChart(string.Empty, string.Empty, string.Empty, []);
        SongTrackInfo = _trackReader.ReadTrack(MaidataDir);

        _editTimer.Reset();
        _editTimer.Start();

        Doc.CurrentSimaiFile = new SimaiFile("Set Title", "Set Artist", 0, string.Empty, levels, null);
        Doc.IsSaved = false;
        AutoSave.Enabled = true;
        AutoSave.SetContent("");
        AutoSave.UpdateContext(MaidataDir);
        await Playback.EditorLoad(MaidataDir);
    }

    [RelayCommand]
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
            var directory = fileInfo.Directory?.FullName;
            if (directory is null) return;
            if (File.Exists(Path.Combine(directory, "maidata.txt")))
            {
                await MessageBox.ShowWindowDialogAsync(
                    Langs.Msg_MaidataAlreadyExist,
                    Langs.Gui_Error,
                    ButtonEnum.Ok, Icon.Error);
                await LoadChart(maidataPath);
                return;
            }
            await NewChartFromDir(directory);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }

    [RelayCommand]
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

    private async Task LoadChart(string maidataPath)
    {
        SaveEditRecord();

        var simaiFile = await SimaiParser.ParseAsync(new FileStream(maidataPath, FileMode.Open, FileAccess.Read));
        for (var i = 0; i < 7; i++)
        {
            var chart = simaiFile.Charts[i];
            Doc.CurrentChartMetadata[i] = new MutSimaiChartMetadata
            {
                Level = chart.Level,
                Designer = chart.Designer,
                Fumen = chart.Fumen
            };
        }
        var fileInfo = new FileInfo(maidataPath);
        var directory = fileInfo.Directory?.FullName;
        if (directory is null) return;
        MaidataDir = directory;
        SongTrackInfo = _trackReader.ReadTrack(MaidataDir);
        var content = await File.ReadAllTextAsync(maidataPath);

        Doc.CurrentSimaiFile = simaiFile;
        AutoSave.Enabled = true;
        AutoSave.SetContent(content);
        AutoSave.UpdateContext(MaidataDir);

        LoadEditRecord();
        await Playback.EditorLoad(MaidataDir);
    }

    public void LoadEditRecord()
    {
        if (string.IsNullOrEmpty(MaidataDir)) return;

        var record = _editDb.GetRecord(MaidataDir);
        if (record is not null)
        {
            Playback.TrackTime = record.TrackTime;
            Doc.SelectedDifficulty = record.SelectedDifficulty;
            _editTimer.LoadAccumulated(record.TotalEditDuration);
        }
        else
        {
            _editTimer.Reset();
        }
        _editTimer.Start();
    }

    public void SaveEditRecord()
    {
        if (string.IsNullOrEmpty(MaidataDir)) return;

        _editTimer.Pause();
        var record = new ChartEditRecord
        {
            ChartPath = MaidataDir,
            SelectedDifficulty = Doc.SelectedDifficulty,
            TrackTime = Playback.TrackTime,
            TotalEditDuration = _editTimer.Elapsed
        };
        _editDb.UpsertRecord(record);
    }

    [RelayCommand]
    public async Task SaveFile()
    {
        if (Doc.CurrentSimaiFile is null) return;

        for (var i = 0; i < 7; i++)
        {
            Doc.CurrentSimaiFile.Charts[i] = new SimaiChart(
                Doc.CurrentChartMetadata[i].Level,
                Doc.CurrentChartMetadata[i].Designer,
                Doc.CurrentChartMetadata[i].Fumen,
                ReadOnlySpan<SimaiTimingPoint>.Empty);
        }
        await SimaiParser.DeparseAsync(Doc.CurrentSimaiFile,
            new FileStream(Path.Combine(MaidataDir, "maidata.txt"), FileMode.Create, FileAccess.Write));

        Doc.MarkAsSaved();
        AutoSave.IsFileChanged = false;
    }
}




