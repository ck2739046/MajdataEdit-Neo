using MajdataEdit_Neo.Assets.Langs;
using MemoryPack;
using System.ComponentModel.DataAnnotations;

namespace MajdataEdit_Neo.Types.MajSetting;

// 线格式契约：成员声明顺序即序列化顺序，必须与 ViewX 端 MajVolumeSetting 完全一致
[MemoryPackable]
public partial class MajVolumeSetting
{
    [Display(Name = nameof(SampleType.Track))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Track { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Answer))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Answer { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Tap))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Tap { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Slide))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Slide { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Break))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Break { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.BreakSlide))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float BreakSlide { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Ex))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Ex { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Touch))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Touch { get; set; } = 0.9f;
    [Display(Name = nameof(SampleType.Hanabi))]
    [SettingControl(SettingControlType.Slider, Max = 1, Min = 0, Step = 0.01)]
    public float Hanabi { get; set; } = 0.9f;
}
public enum SampleType
{
    Track,
    Answer,
    Tap,
    Slide,
    Break,
    BreakSlide,
    Ex,
    Touch,
    Hanabi
}