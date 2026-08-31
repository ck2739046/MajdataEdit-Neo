using MajdataEdit_Neo.Assets.Langs;
using MemoryPack;
using System.ComponentModel.DataAnnotations;

namespace MajdataEdit_Neo.Types.MajSetting;

// 线格式契约：成员声明顺序即序列化顺序，必须与 ViewX 端 MajViewSetting 完全一致
[MemoryPackable]
public partial class MajViewSetting
{
    [Display(Name = nameof(Langs.Set_TapSpeed))]
    [SettingControl(SettingControlType.Numeric, Max = 20, Min = 0, Step = 0.25)]
    public float TapSpeed { get; set; } = 7.5f;

    [Display(Name = nameof(Langs.Set_TouchSpeed))]
    [SettingControl(SettingControlType.Numeric, Max = 20, Min = 0, Step = 0.25)]
    public float TouchSpeed { get; set; } = 7.5f;

    [Display(Name = nameof(Langs.Set_SmoothSlideAnime))]
    [SettingControl(SettingControlType.Toggle)]
    public bool SmoothSlideAnime { get; set; } = true;

    [Display(Name = nameof(Langs.Set_BackgroundDim))]
    [SettingControl(SettingControlType.Numeric, Max = 1, Min = 0, Step = 0.1)]
    public float BackgroundDim { get; set; } = 0.7f;

    [Display(Name = nameof(Langs.Set_BackgroundOutsideDim))]
    [SettingControl(SettingControlType.Numeric, Max = 1, Min = 0, Step = 0.1)]
    public float BackgroundOutsideDim { get; set; } = 0.3f;

    [Display(Name = nameof(Langs.Set_ComboStatusType))]
    [SettingControl(SettingControlType.Selection,
        Values = new object[] { BgInfoDisplay.None,
                                BgInfoDisplay.Combo,
                                BgInfoDisplay.Achievement,
                                BgInfoDisplay.Achievement_100,
                                BgInfoDisplay.Achievement_101,
                                BgInfoDisplay.AchievementClassical,
                                BgInfoDisplay.AchievementClassical_100,
                                BgInfoDisplay.DXScore,
                                BgInfoDisplay.DXScore_Dec,
                                BgInfoDisplay.S_Border,
                                BgInfoDisplay.SS_Border,
                                BgInfoDisplay.SSS_Border},
        Labels = new[] {        "None",
                                "Combo",
                                "Achievement + (Deluxe)",
                                "Achievement - (Deluxe, 100)",
                                "Achievement - (Deluxe, 101)",
                                "Achievement + (Classic)",
                                "Achievement - (Classic, 100)",
                                "Deluxe Score +",
                                "Deluxe Score -",
                                "S Border",
                                "SS Border",
                                "SSS Border"})]
    public BgInfoDisplay ComboStatusType { get; set; } = BgInfoDisplay.Combo;


    [Display(Name = nameof(Langs.Set_AutoMode))]
    [SettingControl(SettingControlType.Selection,
        Values = new object[] { AutoPlayMode.Enable,
                                AutoPlayMode.DJAutoButton,
                                AutoPlayMode.DJAutoSensor,
                                AutoPlayMode.Random,
                                AutoPlayMode.Disable },
        Labels = new[] {        "Enable",
                                "DJAuto (Btn)",
                                "DJAuto",
                                "Random",
                                "Disable" })]
    public AutoPlayMode AutoMode { get; set; } = AutoPlayMode.Enable;


    [Display(Name = nameof(Langs.Set_ShowHand))]
    [SettingControl(SettingControlType.Toggle)]
    public bool ShowHand { get; set; } = false;


    [Display(Name = nameof(Langs.Set_OutputFps))]
    [SettingControl(SettingControlType.Numeric, Max = 1000, Min = 0, Step = 30)]
    public int OutputFps { get; set; } = 60;

    [Display(Name = nameof(Langs.Set_ExportQuality))]
    [SettingControl(SettingControlType.Selection,
    Values = new object[] {     ExportQuality.Low,
                                ExportQuality.Medium,
                                ExportQuality.High,
                                ExportQuality.Ultra },
    Labels = new[] {            "Low",
                                "Medium",
                                "High",
                                "Ultra" })]
    public ExportQuality ExportQuality { get; set; } = ExportQuality.High;

    /*
    [Display(Name = nameof(Langs.Set_ResizeBg))]
    [SettingControl(SettingControlType.Toggle)]
    */
    [SettingUnbrowsable]
    public bool ResizeBg { get; set; } = false;

    [Display(Name = nameof(Langs.Set_UIType))]
    [SettingControl(SettingControlType.Selection,
        Values = new object[] { UIType.Legacy,
                                UIType.TrgUI },
        Labels = new[] {        "Legacy",
                                "TrgUI" })]
    public UIType UIType { get; set; } = UIType.Legacy;

    [Display(Name = nameof(Langs.Set_GlobalAudioOffset))]
    [SettingControl(SettingControlType.Numeric, Max = 1000, Min = -1000, Step = 0.01)]
    public double GlobalAudioOffset { get; set; } = 0;

    [Display(Name = nameof(Langs.Set_LegacySlideLayer))]
    [SettingControl(SettingControlType.Toggle)]
    public bool LegacySlideLayer { get; set; } = false;
    [Display(Name = nameof(Langs.Set_MineAutoSlide))]
    [SettingControl(SettingControlType.Toggle)]
    public bool MineAutoSlide { get; set; } = true;
}

public enum BgInfoDisplay
{
    None,
    Combo,
    Achievement_101,
    Achievement_100,
    Achievement,
    AchievementClassical,
    AchievementClassical_100,
    DXScore,
    DXScore_Dec,
    S_Border,
    SS_Border,
    SSS_Border,
}

public enum UIType
{
    Legacy,
    TrgUI
}