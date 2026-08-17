using MemoryPack;

namespace MajdataEdit_Neo.Types.MajWs;

/// <summary>
/// SimaiNoteType 线格式枚举。成员顺序即线格式数值，必须与 ViewX 端一致，且与 MajSimai 值一致（Tap, Slide, Hold, Touch, TouchHold）。
/// </summary>
internal enum SimaiNoteType
{
    Tap,
    Slide,
    Hold,
    Touch,
    TouchHold
}

/// <summary>
/// SimaiFile 的线格式 DTO。属性名镜像 MajSimai.SimaiFile。
/// 线格式契约：成员声明顺序即序列化顺序，必须与 ViewX 端完全一致；Charts 固定 7 槽，未填为 null。
/// </summary>
[MemoryPackable]
internal partial class SimaiFileDto
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string FinalDesigner { get; set; } = string.Empty;
    public float Offset { get; set; }
    public string Hash { get; set; } = string.Empty;
    public SimaiCommandDto[] Commands { get; set; } = System.Array.Empty<SimaiCommandDto>();
    public SimaiChartDto?[] Charts { get; set; } = new SimaiChartDto[7];
}

/// <summary>
/// SimaiChart 的线格式 DTO。NoteTimings/CommaTimings 为数组（MajSimai 用 ReadOnlySpan，线格式只能用数组）。
/// </summary>
[MemoryPackable]
internal partial class SimaiChartDto
{
    public string Level { get; set; } = string.Empty;
    public string Designer { get; set; } = string.Empty;
    public string Fumen { get; set; } = string.Empty;
    public SimaiTimingPointDto[] NoteTimings { get; set; } = System.Array.Empty<SimaiTimingPointDto>();
    public SimaiTimingPointDto[] CommaTimings { get; set; } = System.Array.Empty<SimaiTimingPointDto>();

    [MemoryPackIgnore]
    public bool IsEmpty => NoteTimings is null || NoteTimings.Length == 0;

    public static SimaiChartDto Empty { get; } = new SimaiChartDto();
}

/// <summary>
/// SimaiTimingPoint 的线格式 DTO。
/// </summary>
[MemoryPackable]
internal partial class SimaiTimingPointDto
{
    public double Timing { get; set; }
    public float Bpm { get; set; }
    public float HSpeed { get; set; } = 1f;
    public float SVeloc { get; set; } = 1f;
    public string RawContent { get; set; } = string.Empty;
    public int RawTextPositionX { get; set; }
    public int RawTextPositionY { get; set; }
    public int RawTextPosition { get; set; }
    public int SignatureNumerator { get; set; } = 4;
    public int SignatureDenominator { get; set; } = 4;
    public SimaiNoteDto[] Notes { get; set; } = System.Array.Empty<SimaiNoteDto>();
}

/// <summary>
/// SimaiNote 的线格式 DTO。
/// </summary>
[MemoryPackable]
internal partial class SimaiNoteDto
{
    public SimaiNoteType Type { get; set; }
    public int StartPosition { get; set; } = 1;
    public double HoldTime { get; set; }
    public double SlideTime { get; set; }
    public double SlideStartTime { get; set; }
    public bool IsBreak { get; set; }
    public bool IsEx { get; set; }
    public bool IsFakeRotate { get; set; }
    public bool IsForceStar { get; set; }
    public bool IsHanabi { get; set; }
    public bool IsSlideBreak { get; set; }
    public bool IsSlideNoHead { get; set; }
    public bool IsTapHeadSlide { get; set; }
    public bool IsMine { get; set; }
    public bool IsMineSlide { get; set; }
    public bool UsingSV { get; set; }
    public string RawContent { get; set; } = string.Empty;
    public char TouchArea { get; set; } = ' ';
}

/// <summary>
/// SimaiCommand 的线格式 DTO。
/// </summary>
[MemoryPackable]
internal partial class SimaiCommandDto
{
    public string Prefix { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
