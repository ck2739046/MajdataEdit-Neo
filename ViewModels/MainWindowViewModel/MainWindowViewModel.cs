using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.Plugin;
using MajdataEdit_Neo.Views;
using Newtonsoft.Json.Linq;
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
/// Composition root: holds and coordinates all editor state, provides window-level UI state.
/// Split into partial files by function: Document.cs, FileSession.cs, Playback.cs, Tools.cs,
/// AutoSave.cs, DiscordRpc.cs, Plugin.cs, Settings.cs, Update.cs.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public static MainWindowViewModel Ins { get; private set; } = null!;
    private ShortcutWindow? _shortcutWindow;
    private readonly HachimiDX_ipc _hachimiDxIpc = new();

    //------status bar (window-level)

    [ObservableProperty]
    private string? _statusBarMessage = null;

    //------derived properties that span multiple models

    public string WindowTitle
    {
        get
        {
            var baseTitle = $"MajdataEdit Neo {MAJDATA_VERSION_STRING}";
            if (CurrentSimaiFile is null) return baseTitle;
            return baseTitle + WindowTitleSuffix;
        }
    }

    public bool IsPointerPressedSimaiVisual { get; set; }

    //------constructor

    public MainWindowViewModel()
    {
        Ins = this;

        InitializeDocument();
        InitializePlayback();
        InitializeAutoSave();
        InitializePlugins();
        InitializeHachimiDxIpc();

        // Wire document -> window title / auto-save / playback events
        WireEvents();

        // Initialize settings (may signal to open settings window)
        BackgroundImage = _emptyBitmap;
        var needsSettingsWindow = InitializeSettings();
        if (needsSettingsWindow)
        {
            OpenSettingsWindow();
        }

        // InitializeDiscordRpc();

        // Design-time support
        if (Design.IsDesignMode)
        {
            CurrentSimaiFile = MajSimai.SimaiFile.Empty("", "");
        }
    }

    public void NotifyWindowTitleChanged()
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    //------HachimiDX IPC

    private void InitializeHachimiDxIpc()
    {
        if (Design.IsDesignMode)
            return;

        _hachimiDxIpc.LoadRequested += OnHachimiLoad;
        _hachimiDxIpc.ResetRequested += OnHachimiReset;
        _hachimiDxIpc.ExitRequested += OnHachimiExit;
        try
        {
            _hachimiDxIpc.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HachimiDX_ipc start failed: {ex}");
        }
    }

    public void BroadcastToHachimi(JObject payload) => _hachimiDxIpc.SendEvent(payload);

    private void OnHachimiLoad(object? sender, HachimiDX_ipc.LoadCommandEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
            await LoadChartFromHachimiAsync(e.Folder, e.Maidata, e.Track, e.Pv));
    }

    private void OnHachimiReset(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () => await ResetToInitialStateAsync());
    }

    private void OnHachimiExit(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(CloseMainWindow);
    }

    private void CloseMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow?.Close();
    }

    //------window-level methods

    public void ShowStatusMessage(string message) => StatusBarMessage = message;
    public void ResetStatusMessage() => StatusBarMessage = null;

    public async Task OnWindowClosingAsync()
    {
        _shortcutWindow?.Close();
        _shortcutWindow = null;

        try
        {
            SaveEditRecord();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save edit record while closing: {ex}");
        }

        await DisposeAsync();
        _hachimiDxIpc.Dispose();
        try
        {
            DisposeSettings();
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
            case "3": OpenBrowser("https://github.com/re-poem/MajdataViewX"); break;
            case "4": OpenBrowser("https://majdata.net/"); break;
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

    [RelayCommand]
    public void OpenShortcutWindow()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is null) return;

        if (_shortcutWindow is not null)
        {
            _shortcutWindow.Activate();
            return;
        }

        var window = new ShortcutWindow
        {
            DataContext = new ShortcutWindowViewModel()
        };
        _shortcutWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shortcutWindow, window))
                _shortcutWindow = null;
        };
        window.Show(desktop.MainWindow);
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

    public async void OpenChartInfoWindow()
    {
        if (CurrentSimaiFile is null) return;
        var mainWindow = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (mainWindow is null || mainWindow.MainWindow is null) return;
        using var chartInfo = new ChartInfoViewModel()
        {
            Title = CurrentSimaiFile.Title,
            Artist = CurrentSimaiFile.Artist,
            FinalDesigner = CurrentSimaiFile.FinalDesigner,
            SimaiCommands = [.. CurrentSimaiFile.Commands.Select(c => new MutSimaiCommand(c.Prefix, c.Value))],
            MaidataDir = MaidataDir
        };
        var window = new ChartInfoWindow
        {
            DataContext = chartInfo
        };
        await window.ShowDialog(mainWindow.MainWindow);
        var datacontext = window.DataContext as ChartInfoViewModel;
        if (datacontext is null)
            throw new InvalidOperationException("Chart info window has an unexpected data context.");

        CurrentSimaiFile.Title = datacontext.Title ?? string.Empty;
        CurrentSimaiFile.Artist = datacontext.Artist ?? string.Empty;
        CurrentSimaiFile.FinalDesigner = datacontext.FinalDesigner ?? string.Empty;
        CurrentSimaiFile.Commands.Clear();
        foreach (var item in datacontext.SimaiCommands)
            CurrentSimaiFile.Commands.Add(item);

        await Task.Delay(100);
        NotifySimaiFileChanged();
        await EditorLoad(MaidataDir);
    }

    public async void OpenRecoverWindow()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is null) return;

        var maidataDirectory = IsLoaded ? MaidataDir : null;
        var recoverViewModel = await RecoverViewModel.CreateAsync(this, maidataDirectory);
        var window = new RecoverWindow(recoverViewModel);
        var result = await window.ShowDialog<RecoverDialogResult?>(desktop.MainWindow);
        if (result is null) return;

        if (result.Action == RecoverDialogAction.Load)
        {
            await OpenFile(result.MaidataPath);
            return;
        }

        if (!IsLoaded) return;
        var currentMaidataPath = Path.GetFullPath(Path.Combine(MaidataDir, "maidata.txt"));
        var recoveredMaidataPath = Path.GetFullPath(result.MaidataPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(
            currentMaidataPath,
            recoveredMaidataPath,
            pathComparison))
        {
            await ReloadFile();
        }
    }

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }

    public event Action<PluginAction>? RequestPluginActionExecution;
    [RelayCommand]
    public void ExecutePluginAction(PluginAction action)
    {
        RequestPluginActionExecution?.Invoke(action);
    }
}
