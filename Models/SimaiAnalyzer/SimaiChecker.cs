using System;
using System.Collections.Generic;
using MajdataEdit_Neo.Types.SimaiAnalyzer;

namespace MajdataEdit_Neo.Models.SimaiAnalyzer;

/// <summary>
/// simai 谱面语法校验的公开入口。本身只做流水线编排，各阶段实现见：
/// <list type="bullet">
/// <item><see cref="SimaiTokenizer"/>：清洗文本、切 segment、span 文本工具</item>
/// <item><see cref="SimaiDefinitionChecker"/>：BPM/Beat/HSpeed/SVeloc 定义语法 + segment 分发</item>
/// <item><see cref="SimaiNoteAnalyzer"/>：note 的解析与校验（含 slide 路径）</item>
/// <item><see cref="SimaiSymbols"/>：全部符号常量与冲突规则（加新标记改这里）</item>
/// </list>
/// </summary>
public static class SimaiChecker
{
    public static IReadOnlyList<SimaiDiagnostic> Check(string fumen)
    {
        var context = new CheckerContext(fumen);

        var (cleanedFumen, positionMap, newlines) = SimaiTokenizer.PreprocessNewlines(fumen, context);
        SimaiTokenizer.CheckNewlinePositions(fumen, newlines, context);

        var segments = SimaiTokenizer.SplitIntoSegments(cleanedFumen, positionMap, context);

        for (var i = 0; i < segments.Count; i++)
        {
            SimaiDefinitionChecker.CheckSegment(context, segments[i]);
        }
        return context.Diagnostics;
    }
}

/// <summary>
/// 一次 Check 调用的共享上下文：累积诊断、记录是否已见过 BPM 定义。
/// </summary>
internal class CheckerContext
{
    public string Source { get; }
    public List<SimaiDiagnostic> Diagnostics { get; } = new();
    public bool HasBpmDefinition { get; set; }

    public CheckerContext(string source)
    {
        Source = source;
    }

    public void AddError(string message, string detail, TextPosition start, int length)
    {
        Diagnostics.Add(new SimaiDiagnostic(Severity.Error, message, detail, start, length));
    }

    public void AddWarning(string message, string detail, TextPosition start, int length)
    {
        Diagnostics.Add(new SimaiDiagnostic(Severity.Warning, message, detail, start, length));
    }

    public void AddInfo(string message, string detail, TextPosition start, int length)
    {
        Diagnostics.Add(new SimaiDiagnostic(Severity.Info, message, detail, start, length));
    }
}

/// <summary>
/// 清洗后的谱面按逗号/空白切出的一个片段。
/// <see cref="Content"/> 指向清洗后字符串的内存切片，<see cref="StartPosition"/> 映射回原文位置。
/// </summary>
internal record struct ChartSegment(ReadOnlyMemory<char> Content, TextPosition StartPosition, int Length);
