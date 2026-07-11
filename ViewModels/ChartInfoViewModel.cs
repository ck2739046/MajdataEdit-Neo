using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Utils;
using MajdataEdit_Neo.Views;
using MajSimai;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Types;

namespace MajdataEdit_Neo.ViewModels;

partial class ChartInfoViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? title;
    [ObservableProperty]
    private string? artist;
    [ObservableProperty]
    private string? finalDesigner;

    [ObservableProperty]
    private ObservableCollection<MutSimaiCommand> simaiCommands = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cover))]
    private string? maidataDir;

    public Bitmap Cover
    {
        get
        {
            var coverPath = MaidataDir + "/bg.png";
            if (File.Exists(coverPath)) return new Bitmap(coverPath);
            coverPath = MaidataDir + "/bg.jpg";
            if (File.Exists(coverPath)) return new Bitmap(coverPath);
            return new Bitmap(AssetLoader.Open(new Uri("avares://MajdataEdit-Neo/Assets/dummy.png")));
        }
    }

    public void AddNewCommand()
    {
        SimaiCommands.Add(new MutSimaiCommand("prefix", "value"));
    }
    public void DelCommand(MutSimaiCommand command)
    {
        if (SimaiCommands is null) throw new InvalidOperationException();
        SimaiCommands.Remove(command);
    }

    public async Task OpenBgCover()
    {
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Image);
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (path is null || MaidataDir is null) return;
            File.Delete(MaidataDir + "/bg.jpg");
            File.Delete(MaidataDir + "/bg.png");
            if (path.EndsWith("jpg"))
                File.Copy(path, MaidataDir + "/bg.jpg", true);
            if (path.EndsWith("png"))
                File.Copy(path, MaidataDir + "/bg.png", true);
            OnPropertyChanged(nameof(Cover));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }

    public async Task OpenBgVideo()
    {
        try
        {
            var file = await FileIOManager.DoOpenFilePickerAsync(FileIOManager.FileOpenerType.Video);
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (path is null || MaidataDir is null) return;
            File.Delete(MaidataDir + "/bg.mp4");
            File.Delete(MaidataDir + "/pv.mp4");
            File.Copy(path, MaidataDir + "/bg.mp4", true);
            if (new FileInfo(path).Length > 20971520)
            {
                var result = await MessageBox.ShowWindowDialogAsync(Langs.Msg_BgTooLarge, Langs.Gui_Warning,
                    MsBox.Avalonia.Enums.ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Warning);
                if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
                {
                    await MainWindowViewModel.Ins.Session.Tools.CompressBgVideoAsync();
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }
}
