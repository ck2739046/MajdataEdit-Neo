using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Views;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Types;
using ViewModels.SubModels;
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

    public void OnWindowClosing()
    {
        Session.SaveEditRecord();
        _editDb.Dispose();
    }

    public static void AboutButtonClicked(int index)
    {
        switch (index)
        {
            case 0: OpenBrowser("https://discord.gg/AcWgZN7j6K"); break;
            case 1: OpenBrowser("https://qm.qq.com/q/GAxbFZHP6A"); break;
            case 2: OpenBrowser("https://github.com/LingFeng-bbben/MajdataEdit-Neo"); break;
            case 3: OpenBrowser("https://majdata.net/"); break;
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
        var window = new ChartInfoWindow();
        window.DataContext = new ChartInfoViewModel()
        {
            Title = Session.Doc.CurrentSimaiFile.Title,
            Artist = Session.Doc.CurrentSimaiFile.Artist,
            FinalDesigner = Session.Doc.CurrentSimaiFile.FinalDesigner,
            SimaiCommands = [.. Session.Doc.CurrentSimaiFile.Commands.Select(c => new MutSimaiCommand(c.Prefix, c.Value))],
            MaidataDir = Session.MaidataDir
        };
        await window.ShowDialog(mainWindow.MainWindow);
        var datacontext = window.DataContext as ChartInfoViewModel;
        if (datacontext is null) throw new Exception("Wtf");

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

    public void OpenBpmTapWindow()
    {
        new BpmTapWindow().Show();
    }
}

