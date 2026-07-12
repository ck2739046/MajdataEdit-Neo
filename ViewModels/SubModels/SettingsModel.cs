using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MajdataEdit_Neo.Types.MajSetting;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;
using Avalonia.Platform;
using static MajdataEdit_Neo.Base.MajEnv;
using Types;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class SettingsModel : ViewModelBase
{
    [ObservableProperty]
    public partial MajSetting Settings { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial bool IsAnimated { get; set; } = true;

    [ObservableProperty]
    public partial Bitmap BackgroundImage { get; set; }

    private static readonly WriteableBitmap emptyBitmap = new(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888);

    public static string SettingsFile => GetPath("Settings.json");

    public bool Initialize()
    {
        if (!File.Exists(SettingsFile))
        {
            CreateSettings();
            return true;
        }
        ReadSettings();
        return false;
    }

    private void CreateSettings()
    {
        Settings = new MajSetting();
        File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
    }

    private void ReadSettings()
    {
        var json = File.ReadAllText(SettingsFile);
        Settings = JsonConvert.DeserializeObject<MajSetting>(json)!;
        ReloadSettings();
        SaveSettings();
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

    public void SaveSettings()
    {
        File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
    }
}
