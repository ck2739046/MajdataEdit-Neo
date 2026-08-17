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
using MajdataEdit_Neo.ViewModels.SubModels;
using MajdataEdit_Neo.Models.Plugins;
using MajdataEdit_Neo.Base;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class FileSessionModel : ViewModelBase, IAsyncDisposable
{
    //------sub-models (hierarchical ownership)
    public DocumentModel Doc { get; }
    public PlaybackModel Playback { get; }
    public AutoSaveModel AutoSave { get; }
    public ToolsModel Tools { get; }
    public PluginModel Plugins { get; }
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

        AutoSave = new AutoSaveModel(MajEnv.MajBase);
        DiscordRpc = new DiscordRpcModel();

        // Inject mutable interface to ToolsModel
        Tools = new ToolsModel(_mainWindow, this, Doc);

        Plugins = new PluginModel();
        Plugins.RegisterAll();

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

        Doc.FumenContentChanged += async (s, e) =>
        {
            await AutoSave.OnSimaiFileChangedAsync(Doc.CurrentSimaiFile);
            // 文本变更（已防抖 + 解析完成）-> 推送 Update
            _ = Playback.PushUpdateAsync();
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

        File.Create(Path.Combine(directory, "maidata.txt")).Dispose();
        var levels = new SimaiChart[7];
        var metadata = new MutSimaiChartMetadata[7];
        for (var i = 0; i < 7; i++)
        {
            levels[i] = new SimaiChart(string.Empty, string.Empty, string.Empty, []);
            metadata[i] = new MutSimaiChartMetadata();
        }
        var songTrackInfo = _trackReader.ReadTrack(directory);

        _editTimer.Reset();
        _editTimer.Start();

        MaidataDir = directory;
        SongTrackInfo = songTrackInfo;
        Doc.CurrentChartMetadata = metadata;
        Doc.CurrentSimaiFile = new SimaiFile("Set Title", "Set Artist", 0, string.Empty, levels, null);
        Doc.SelectedDifficulty = 0;
        Doc.IsSaved = false;
        AutoSave.UpdateContext(MaidataDir);
        AutoSave.SetContent("");
        AutoSave.Enabled = true;
        await Playback.EditorLoad(MaidataDir);
    }

    public async Task NewFile()
    {
        if (await AskSave()) return;
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Track);
            if (file is null) return;
            var trackPath = file.TryGetLocalPath();
            if (trackPath is null) return;
            var fileInfo = new FileInfo(trackPath);
            var directory = fileInfo.Directory?.FullName;
            if (directory is null) return;
            if (File.Exists(Path.Combine(directory, "maidata.txt")))
            {
                await MessageBox.ShowWindowDialogAsync(
                    Langs.Msg_MaidataAlreadyExist,
                    Langs.Gui_Error,
                    ButtonEnum.Ok, Icon.Error);
                await LoadChart(Path.Combine(directory, "maidata.txt"));
                return;
            }
            await NewChartFromDir(directory);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
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
            Debug.WriteLine(e);
        }
    }
    public async Task OpenFile(string maidataPath)
    {
        if (await AskSave()) return;
        try
        {
            await LoadChart(maidataPath);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    private async Task LoadChart(string maidataPath)
    {
        SaveEditRecord();

        await using var maidataStream = new FileStream(maidataPath, FileMode.Open, FileAccess.Read);
        var simaiFile = await SimaiParser.ParseAsync(maidataStream);
        var metadata = new MutSimaiChartMetadata[7];
        for (var i = 0; i < 7; i++)
        {
            var chart = simaiFile.Charts[i];
            metadata[i] = new MutSimaiChartMetadata
            {
                Level = chart.Level,
                Designer = chart.Designer,
                Fumen = chart.Fumen
            };
        }
        var fileInfo = new FileInfo(maidataPath);
        var directory = fileInfo.Directory?.FullName;
        if (directory is null) return;
        var songTrackInfo = _trackReader.ReadTrack(directory);
        var content = await File.ReadAllTextAsync(maidataPath);

        MaidataDir = directory;
        SongTrackInfo = songTrackInfo;
        Doc.CurrentChartMetadata = metadata;
        Doc.CurrentSimaiFile = simaiFile;
        AutoSave.UpdateContext(MaidataDir);
        AutoSave.SetContent(content);
        AutoSave.Enabled = true;

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

    public async Task SaveFile()
    {
        if (Doc.CurrentSimaiFile is null) return;

        for (var i = 0; i < 7; i++)
        {
            var parsedChart = Doc.CurrentSimaiFile.Charts[i];
            Doc.CurrentSimaiFile.Charts[i] = new SimaiChart(
                Doc.CurrentChartMetadata[i].Level,
                Doc.CurrentChartMetadata[i].Designer,
                Doc.CurrentChartMetadata[i].Fumen,
                parsedChart.NoteTimings,
                parsedChart.CommaTimings);
        }
        var maidataPath = Path.Combine(MaidataDir, "maidata.txt");
        var tempPath = Path.Combine(MaidataDir, $".maidata.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await SimaiParser.DeparseAsync(Doc.CurrentSimaiFile, stream);
            }

            File.Move(tempPath, maidataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        Doc.MarkAsSaved();
        AutoSave.IsFileChanged = false;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Playback.DisposeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose playback resources: {ex}");
        }

        try
        {
            _trackReader.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose audio resources: {ex}");
        }

        try
        {
            DiscordRpc.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose Discord RPC: {ex}");
        }
    }
}






