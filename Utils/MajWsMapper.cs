using MajdataEdit_Neo.Types.MajWs;
using MajSimai;
using System.Linq;

namespace MajdataEdit_Neo.Utils;

/// <summary>
/// MajSimai 对象 → 线格式 DTO 的映射（仅 Edit 需要：服务器直接消费 DTO，不再做反向转换）。
/// 设置类不需要映射——两端各自的 MajViewSetting/MajVolumeSetting 已直接 [MemoryPackable]。
/// </summary>
internal static class MajWsMapper
{
    public static SimaiFileDto ToDto(SimaiFile file)
    {
        var dto = new SimaiFileDto
        {
            Title = file.Title ?? string.Empty,
            Artist = file.Artist ?? string.Empty,
            FinalDesigner = file.FinalDesigner ?? string.Empty,
            Offset = file.Offset,
            Hash = file.Hash ?? string.Empty,
            Commands = file.Commands
                .Select(c => new SimaiCommandDto { Prefix = c.Prefix, Value = c.Value })
                .ToArray(),
            Charts = new SimaiChartDto[7]
        };

        var charts = file.Charts;
        for (var i = 0; i < 7 && i < charts.Length; i++)
        {
            var chart = charts[i];
            if (chart is null || chart.IsEmpty)
                continue;

            dto.Charts[i] = new SimaiChartDto
            {
                Level = chart.Level ?? string.Empty,
                Designer = chart.Designer ?? string.Empty,
                Fumen = chart.Fumen ?? string.Empty,
                NoteTimings = chart.NoteTimings.ToArray().Select(ToDto).ToArray(),
                CommaTimings = chart.CommaTimings.ToArray().Select(ToDto).ToArray()
            };
        }

        return dto;
    }

    public static SimaiTimingPointDto ToDto(SimaiTimingPoint timing) => new()
    {
        Timing = timing.Timing,
        Bpm = timing.Bpm,
        HSpeed = timing.HSpeed,
        SVeloc = timing.SVeloc,
        RawContent = timing.RawContent ?? string.Empty,
        RawTextPositionX = timing.RawTextPositionX,
        RawTextPositionY = timing.RawTextPositionY,
        RawTextPosition = timing.RawTextPosition,
        SignatureNumerator = timing.SignatureNumerator,
        SignatureDenominator = timing.SignatureDenominator,
        Notes = (timing.Notes ?? System.Array.Empty<SimaiNote>()).Select(ToDto).ToArray()
    };

    public static SimaiNoteDto ToDto(SimaiNote note) => new()
    {
        // 两端 SimaiNoteType 成员顺序一致，直接枚举转换（本端线格式枚举在 MajdataEdit_Neo.Types.MajWs）
        Type = (MajdataEdit_Neo.Types.MajWs.SimaiNoteType)note.Type,
        StartPosition = note.StartPosition,
        HoldTime = note.HoldTime,
        SlideTime = note.SlideTime,
        SlideStartTime = note.SlideStartTime,
        IsBreak = note.IsBreak,
        IsEx = note.IsEx,
        IsFakeRotate = note.IsFakeRotate,
        IsForceStar = note.IsForceStar,
        IsHanabi = note.IsHanabi,
        IsSlideBreak = note.IsSlideBreak,
        IsSlideNoHead = note.IsSlideNoHead,
        IsTapHeadSlide = note.IsTapHeadSlide,
        IsMine = note.IsMine,
        IsMineSlide = note.IsMineSlide,
        UsingSV = note.UsingSV,
        RawContent = note.RawContent ?? string.Empty,
        TouchArea = note.TouchArea
    };
}
