using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.Plugin;
using MajdataEdit_Neo.ViewModels.SubModels;
using MajdataEdit_Neo.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Types;
using static MajdataEdit_Neo.Base.MajEnv;

namespace MajdataEdit_Neo.ViewModels;

/// <summary>
/// Composition root: holds and coordinates all sub-models, provides window-level UI state
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    //------sub-models

    public FileSessionModel Session { get; }
    private readonly ChartEditDatabase _editDb = new(DatabaseFile); //owned by mainwindow
    public SettingsModel Settings { get; }
    public UpdateModel Update { get; }

    public static MainWindowViewModel Ins { get; private set; } = null!;


    //------status bar (window-level)

    [ObservableProperty]
    private string? _statusBarMessage = null;

    //------derived properties that span multiple models

    public string WindowTitle
    {
        get
        {
            var baseTitle = $"MajdataEdit Neo {MAJDATA_VERSION_STRING}";
            if (Session?.Doc is null) return baseTitle;
            return baseTitle + Session.Doc.WindowTitleSuffix;
        }
    }

    public bool IsPointerPressedSimaiVisual { get; set; }

    //------constructor

    public MainWindowViewModel()
    {
        Ins = this;

        // Create sub-models (order matters: dependencies first)
        Session = new FileSessionModel(this, _editDb);
        Settings = new SettingsModel();
        Update = new UpdateModel();

        // Initialize settings (may signal to open settings window)
        var needsSettingsWindow = Settings.Initialize();
        if (needsSettingsWindow)
        {
            OpenSettingsWindow();
        }

        Session.DiscordRpc.Initialize();



        // Design-time support
        if (Design.IsDesignMode)
        {
            Session.Doc.CurrentSimaiFile = MajSimai.SimaiFile.Empty("", "");
        }
    }

    public void NotifyWindowTitleChanged()
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    //------window-level methods

    public void ShowStatusMessage(string message) => StatusBarMessage = message;
    public void ResetStatusMessage() => StatusBarMessage = null;

    public async Task OnWindowClosingAsync()
    {
        try
        {
            Session.SaveEditRecord();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save edit record while closing: {ex}");
        }

        await Session.DisposeAsync();
        try
        {
            Settings.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to dispose settings resources: {ex}");
        }
        _editDb.Dispose();
    }

    [RelayCommand]
    public void AboutButtonClicked(string? index)
    {
        switch (index)
        {
            case "0": OpenBrowser("https://discord.gg/AcWgZN7j6K"); break;
            case "1": OpenBrowser("https://qm.qq.com/q/GAxbFZHP6A"); break;
            case "2": OpenBrowser("https://github.com/LingFeng-bbben/MajdataEdit-Neo"); break;
            case "3": OpenBrowser("https://majdata.net/"); break;
        }
        static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }

    public async void OpenSettingsWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;

        var settingsViewModel = new SettingsViewModel();
        settingsViewModel.LoadSettings(Settings.Settings);
        var window = new SettingsWindow
        {
            DataContext = settingsViewModel
        };
        await window.ShowDialog(mainWindow.MainWindow);
        Settings.SaveSettings();
        await Task.Delay(1);
    }

    public async void OpenChartInfoWindow()
    {
        if (Session.Doc.CurrentSimaiFile is null) return;
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;
        using var chartInfo = new ChartInfoViewModel()
        {
            Title = Session.Doc.CurrentSimaiFile.Title,
            Artist = Session.Doc.CurrentSimaiFile.Artist,
            FinalDesigner = Session.Doc.CurrentSimaiFile.FinalDesigner,
            SimaiCommands = [.. Session.Doc.CurrentSimaiFile.Commands.Select(c => new MutSimaiCommand(c.Prefix, c.Value))],
            MaidataDir = Session.MaidataDir
        };
        var window = new ChartInfoWindow
        {
            DataContext = chartInfo
        };
        await window.ShowDialog(mainWindow.MainWindow);
        var datacontext = window.DataContext as ChartInfoViewModel;
        if (datacontext is null)
            throw new InvalidOperationException("Chart info window has an unexpected data context.");

        Session.Doc.CurrentSimaiFile.Title = datacontext.Title ?? string.Empty;
        Session.Doc.CurrentSimaiFile.Artist = datacontext.Artist ?? string.Empty;
        Session.Doc.CurrentSimaiFile.FinalDesigner = datacontext.FinalDesigner ?? string.Empty;
        Session.Doc.CurrentSimaiFile.Commands.Clear();
        foreach (var item in datacontext.SimaiCommands)
            Session.Doc.CurrentSimaiFile.Commands.Add(item);

        await Task.Delay(100);
        Session.Doc.NotifySimaiFileChanged();
        await Session.Playback.EditorLoad(Session.MaidataDir);
    }

    public async void OpenRecoverWindow()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is null) return;

        var maidataDirectory = Session.Doc.IsLoaded ? Session.MaidataDir : null;
        var recoverViewModel = await RecoverViewModel.CreateAsync(Session.AutoSave, maidataDirectory);
        var window = new RecoverWindow(recoverViewModel);
        var result = await window.ShowDialog<RecoverDialogResult?>(desktop.MainWindow);
        if (result is null) return;

        if (result.Action == RecoverDialogAction.Load)
        {
            await Session.OpenFile(result.MaidataPath);
            return;
        }

        if (!Session.Doc.IsLoaded) return;
        var currentMaidataPath = Path.GetFullPath(Path.Combine(Session.MaidataDir, "maidata.txt"));
        var recoveredMaidataPath = Path.GetFullPath(result.MaidataPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(
            currentMaidataPath,
            recoveredMaidataPath,
            pathComparison))
        {
            await Session.ReloadFile();
        }
    }

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }

    [RelayCommand]
    public Task NewFile() => Session.NewFile();

    [RelayCommand]
    public Task OpenFile() => Session.OpenFile();

    [RelayCommand]
    public Task SaveFile() => Session.SaveFile();

    [RelayCommand]
    public Task CompressBgVideo() => Session.Tools.CompressBgVideoAsync();

    [RelayCommand]
    public Task MediaQuickProcess() => Session.Tools.MediaQuickProcessAsync();

    [RelayCommand]
    public Task NewChartFromVideo() => Session.Tools.NewChartFromVideoAsync();

    [RelayCommand]
    public void IncreasePlaybackSpeed() => Session.Playback.IncreasePlaybackSpeed();

    [RelayCommand]
    public void DecreasePlaybackSpeed() => Session.Playback.DecreasePlaybackSpeed();

    [RelayCommand]
    public void PlayRecord()
    {
        var simai = Session.Doc.CurrentSimaiFile;
        if (simai == null) return;
        var ctx = new PlaybackModel.PlayContext(
            simai.Title ?? "",
            simai.Artist ?? "",
            Session.Doc.Offset,
            Session.Doc.Designer,
            Session.Doc.Level,
            Session.Doc.CurrentFumen,
            simai.Commands,
            Session.Doc.SelectedDifficulty
        );
        _ = Session.Playback.PlayRecord(ctx, Settings.Settings, Session.MaidataDir);
    }

    [RelayCommand]
    public void PlayIncludeOp()
    {
        var simai = Session.Doc.CurrentSimaiFile;
        if (simai == null) return;
        var ctx = new PlaybackModel.PlayContext(
            simai.Title ?? "",
            simai.Artist ?? "",
            Session.Doc.Offset,
            Session.Doc.Designer,
            Session.Doc.Level,
            Session.Doc.CurrentFumen,
            simai.Commands,
            Session.Doc.SelectedDifficulty
        );
        _ = Session.Playback.PlayIncludeOp(ctx, Settings.Settings);
    }

    [RelayCommand]
    public void PlayStop()
    {
        var simai = Session.Doc.CurrentSimaiFile;
        if (simai == null) return;
        var ctx = new PlaybackModel.PlayContext(
            simai.Title ?? "",
            simai.Artist ?? "",
            Session.Doc.Offset,
            Session.Doc.Designer,
            Session.Doc.Level,
            Session.Doc.CurrentFumen,
            simai.Commands,
            Session.Doc.SelectedDifficulty
        );
        _ = Session.Playback.PlayStop(ctx, Settings.Settings);
    }

    [RelayCommand]
    public void PlayPause()
    {
        var simai = Session.Doc.CurrentSimaiFile;
        if (simai == null) return;
        var ctx = new PlaybackModel.PlayContext(
            simai.Title ?? "",
            simai.Artist ?? "",
            Session.Doc.Offset,
            Session.Doc.Designer,
            Session.Doc.Level,
            Session.Doc.CurrentFumen,
            simai.Commands,
            Session.Doc.SelectedDifficulty
        );
        _ = Session.Playback.PlayPause(ctx, Settings.Settings);
    }

    [RelayCommand]
    public void Stop() => Session.Playback.Stop();


    public event Action<PluginAction>? RequestPluginActionExecution;
    [RelayCommand]
    public void ExecutePluginAction(PluginAction action)
    {
        RequestPluginActionExecution?.Invoke(action);
    }
}





