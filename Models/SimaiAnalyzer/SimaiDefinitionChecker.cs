using MajdataEdit_Neo.Types.SimaiAnalyzer;
using System;

namespace MajdataEdit_Neo.Models.SimaiAnalyzer;

/// <summary>
/// segment 前缀部分的校验：BPM / Beat / HSpeed / SVeloc 定义语法，
/// 以及 segment 内部"定义前缀"的切分与分发。剩余的 note 内容交给 <see cref="SimaiNoteAnalyzer"/>。
/// </summary>
internal static class SimaiDefinitionChecker
{
    public static void CheckSegment(CheckerContext context, ChartSegment segment)
    {
        var contentSpan = segment.Content.Span;
        if (contentSpan.IsEmpty || contentSpan.IsWhiteSpace()) return;
        if (contentSpan.Length == 1 && contentSpan[0] == ',') return;

        var startPos = segment.StartPosition;
        var contentOffset = 0;
        var remaining = segment.Content;

        while (!remaining.IsEmpty)
        {
            var span = remaining.Span;
            var checkingStartPos = startPos.Advance(segment.Content.Span[..contentOffset]);

            if (span.StartsWith("<HS*".AsSpan()))
            {
                var consumed = CheckHSpeedSyntax(context, span, checkingStartPos);
                if (consumed == 0) return;
                contentOffset += consumed;
                remaining = segment.Content[contentOffset..];
                continue;
            }

            if (span.StartsWith("<SV*".AsSpan()))
            {
                var consumed = CheckSVelocSyntax(context, span, checkingStartPos);
                if (consumed == 0) return;
                contentOffset += consumed;
                remaining = segment.Content[contentOffset..];
                continue;
            }

            if (span[0] == '(')
            {
                var bpmEnd = span.IndexOf(')');
                CheckBpmDefinition(context, span, checkingStartPos);
                context.HasBpmDefinition = true;
                if (bpmEnd == -1) return;
                contentOffset += bpmEnd + 1;
                remaining = segment.Content[contentOffset..];
                continue;
            }

            if (span[0] == '{')
            {
                var beatEnd = span.IndexOf('}');
                if (!context.HasBpmDefinition && !span.StartsWith("{#".AsSpan()))
                {
                    context.AddError(
                        "Beat definition without prior BPM",
                        "A BPM definition must appear before a beat-division definition",
                        checkingStartPos,
                        beatEnd != -1 ? beatEnd + 1 : 1
                    );
                }
                CheckBeatDefinition(context, span, checkingStartPos);
                if (beatEnd == -1) return;
                contentOffset += beatEnd + 1;
                remaining = segment.Content[contentOffset..];
                continue;
            }

            var definitionIndex = FindNextDefinitionIndex(span);
            if (definitionIndex > 0)
            {
                SimaiNoteAnalyzer.CheckDefinitionPrefix(context, span[..definitionIndex], checkingStartPos);
                contentOffset += definitionIndex;
                remaining = segment.Content[contentOffset..];
                continue;
            }

            break;
        }

        if (remaining.IsEmpty) return;
        var noteStartPos = startPos.Advance(segment.Content.Span[..contentOffset]);
        SimaiNoteAnalyzer.CheckNoteGroup(context, remaining, noteStartPos);
    }

    private static int FindNextDefinitionIndex(ReadOnlySpan<char> content)
    {
        var result = -1;
        foreach (var marker in new[] { "<HS*", "<SV*", "(", "{" })
        {
            var index = content.IndexOf(marker.AsSpan());
            if (index > 0 && (result == -1 || index < result))
            {
                result = index;
            }
        }
        return result;
    }

    private static int CheckHSpeedSyntax(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos)
    {
        var hspeedEnd = content.IndexOf('>');
        if (hspeedEnd == -1)
        {
            context.AddError(
                "HSpeed definition not closed",
                "HSpeed must be enclosed in angle brackets, e.g., <HS*1.5>",
                startPos,
                1
            );
            return 0;
        }

        var hspeedContent = content[4..hspeedEnd];
        if (hspeedContent.IsEmpty)
        {
            context.AddError(
                "Empty HSpeed value",
                "HSpeed value cannot be empty",
                startPos,
                4
            );
            return hspeedEnd + 1;
        }

        if (!SimaiTokenizer.TryParseFiniteDecimal(hspeedContent, allowSign: true, out _))
        {
            context.AddError(
                $"Invalid HSpeed value: '{hspeedContent.ToString()}'",
                "HSpeed must be a number",
                startPos.Advance("<HS*".AsSpan()),
                hspeedContent.Length
            );
        }

        return hspeedEnd + 1;
    }

    private static int CheckSVelocSyntax(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos)
    {
        var svelocEnd = content.IndexOf('>');
        if (svelocEnd == -1)
        {
            context.AddError(
                "SVeloc definition not closed",
                "SVeloc must be enclosed in angle brackets, e.g., <SV*1.5>",
                startPos,
                1
            );
            return 0;
        }

        var svelocContent = content[4..svelocEnd];
        if (svelocContent.IsEmpty)
        {
            context.AddError(
                "Empty SVeloc value",
                "SVeloc value cannot be empty",
                startPos,
                4
            );
            return svelocEnd + 1;
        }

        if (!SimaiTokenizer.TryParseFiniteDecimal(svelocContent, allowSign: true, out _))
        {
            context.AddError(
                $"Invalid SVeloc value: '{svelocContent.ToString()}'",
                "SVeloc must be a number",
                startPos.Advance("<SV*".AsSpan()),
                svelocContent.Length
            );
        }

        return svelocEnd + 1;
    }

    private static void CheckBpmDefinition(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos)
    {
        var closeIndex = content.IndexOf(')');
        if (closeIndex == -1)
        {
            context.AddError(
                "BPM definition not closed",
                "BPM must be enclosed in parentheses, e.g., (120)",
                startPos,
                1
            );
            return;
        }

        var bpmContent = content[1..closeIndex];
        if (bpmContent.IsEmpty)
        {
            context.AddError(
                "Empty BPM definition",
                "BPM value cannot be empty",
                startPos,
                2
            );
            return;
        }

        if (!SimaiTokenizer.TryParseFiniteDecimal(bpmContent, allowSign: false, out var bpm) || bpm <= 0)
        {
            context.AddError(
                $"Invalid BPM value: '{bpmContent.ToString()}'",
                "BPM must be a positive number",
                startPos.Advance("(".AsSpan()),
                bpmContent.Length
            );
        }
    }

    private static void CheckBeatDefinition(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos)
    {
        var closeIndex = content.IndexOf('}');
        if (closeIndex == -1)
        {
            context.AddError(
                "Beat definition not closed",
                "Beat must be enclosed in braces, e.g., {4} or {#0.5}",
                startPos,
                1
            );
            return;
        }

        var beatContent = content[1..closeIndex];
        if (beatContent.IsEmpty)
        {
            context.AddError(
                "Empty beat definition",
                "Beat value cannot be empty",
                startPos,
                2
            );
            return;
        }

        if (beatContent[0] == '#')
        {
            var timeValue = beatContent[1..];
            if (!SimaiTokenizer.TryParseFiniteDecimal(timeValue, allowSign: false, out var time) || time < 0)
            {
                context.AddError(
                    $"Invalid absolute time value: '{timeValue.ToString()}'",
                    "Absolute time must be a non-negative number (in seconds)",
                    startPos.Advance("{#".AsSpan()),
                    timeValue.Length
                );
            }
        }
        else
        {
            if (!SimaiTokenizer.TryParseFiniteDecimal(beatContent, allowSign: false, out var beat) || beat <= 0)
            {
                context.AddError(
                    $"Invalid beat value: '{beatContent.ToString()}'",
                    "Beat must be a positive number, e.g., {4}, {8}, {16}, or {4.5}",
                    startPos.Advance("{".AsSpan()),
                    beatContent.Length
                );
            }
        }
    }
}
