using MajdataEdit_Neo.Types.MajSetting;
using MemoryPack;

namespace MajdataEdit_Neo.Types.MajWs;

/// <summary>
/// 请求信封（线格式）。union tag 即请求类型，tag 与成员顺序必须与 ViewX 端一致：
/// 0=Setting, 1=Load, 2=Update, 3=Play, 4=Pause, 5=Stop, 6=State, 7=Reset。
/// </summary>
[MemoryPackable]
[MemoryPackUnion(0, typeof(MajWsSettingRequest))]
[MemoryPackUnion(1, typeof(MajWsLoadRequest))]
[MemoryPackUnion(2, typeof(MajWsUpdateRequest))]
[MemoryPackUnion(3, typeof(MajWsPlayRequest))]
[MemoryPackUnion(4, typeof(MajWsPauseRequest))]
[MemoryPackUnion(5, typeof(MajWsStopRequest))]
[MemoryPackUnion(6, typeof(MajWsStateRequest))]
[MemoryPackUnion(7, typeof(MajWsResetRequest))]
internal abstract partial class MajWsRequest
{
}

[MemoryPackable]
internal partial class MajWsSettingRequest : MajWsRequest
{
    public MajViewSetting ViewSetting { get; set; } = new();
    public MajVolumeSetting VolumeSetting { get; set; } = new();
}

/// <summary>Load 只传路径（媒体文件不走线格式）。</summary>
[MemoryPackable]
internal partial class MajWsLoadRequest : MajWsRequest
{
    public string TrackPath { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
}

/// <summary>Update 携带 FileLength/ChartLength（共享内存中两段 MemoryPack 字节的长度），服务器不再重新解析。</summary>
[MemoryPackable]
internal partial class MajWsUpdateRequest : MajWsRequest
{
    public long FileLength { get; set; }
    public long ChartLength { get; set; }
    public int SelectedDifficulty { get; set; }
}

/// <summary>Play 只带播放参数，图数据由 Update 提供。</summary>
[MemoryPackable]
internal partial class MajWsPlayRequest : MajWsRequest
{
    public PlaybackMode Mode { get; set; }
    public double StartAt { get; set; }
    public float Speed { get; set; } = 1f;
    public string? MaidataPath { get; set; }
}

[MemoryPackable]
internal partial class MajWsPauseRequest : MajWsRequest
{
}

[MemoryPackable]
internal partial class MajWsStopRequest : MajWsRequest
{
}

[MemoryPackable]
internal partial class MajWsStateRequest : MajWsRequest
{
}

[MemoryPackable]
internal partial class MajWsResetRequest : MajWsRequest
{
}
