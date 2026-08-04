using MajdataEdit_Neo.Types.SimaiAnalyzer;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MajdataEdit_Neo.Models.SimaiAnalyzer;

/// <summary>
/// tokenizer：把原始谱面文本清洗并切分为可校验的结构。
/// 包含三部分：
///  1) 预处理：定位 E 结束符、剥离 <c>||</c> 注释与格式空白、记录换行并校验换行位置；
///  2) 分段：按逗号 / 空白把清洗后的谱面切成 <see cref="ChartSegment"/>；
///  3) span 文本工具：供本文件及 note/definition 校验共用。
/// </summary>
internal static class SimaiTokenizer
{
    // ---- 预处理 ----

    internal static int FindEndMarkerIndex(ReadOnlySpan<char> fumen)
    {
        var previousSignificantIndex = -1;
        var lastSignificantIndex = -1;
        var inComment = false;

        for (var i = 0; i < fumen.Length; i++)
        {
            var c = fumen[i];
            if (inComment)
            {
                if (c == '\n') inComment = false;
                continue;
            }

            if (c == '|' && i + 1 < fumen.Length && fumen[i + 1] == '|')
            {
                inComment = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c)) continue;

            previousSignificantIndex = lastSignificantIndex;
            lastSignificantIndex = i;
        }

        if (lastSignificantIndex < 0 || fumen[lastSignificantIndex] != 'E')
            return -1;

        if (previousSignificantIndex >= 0 && fumen[previousSignificantIndex] != ',')
            return -1;

        return lastSignificantIndex;
    }

    public static (string CleanedFumen, List<TextPosition> PositionMap, List<(int Index, TextPosition OriginalPos)> Newlines)
        PreprocessNewlines(string fumen, CheckerContext context)
    {
        var cleanedChars = new List<char>();
        var positionMap = new List<TextPosition>();
        var newlines = new List<(int Index, TextPosition OriginalPos)>();

        var endMarkerIndex = FindEndMarkerIndex(fumen);
        var originalPos = TextPosition.Start;
        var inComment = false;

        for (var i = 0; i < fumen.Length; i++)
        {
            var c = fumen[i];

            if (i == endMarkerIndex)
            {
                originalPos = originalPos.Advance(c);
                continue;
            }

            if (inComment)
            {
                if (c == '\n')
                {
                    // 遇到换行符时自动结束注释
                    inComment = false;
                    newlines.Add((i, originalPos));
                    originalPos = originalPos.Advance(c);
                }
                else
                {
                    originalPos = originalPos.Advance(c);
                }
                continue;
            }

            if (c == '|' && i + 1 < fumen.Length && fumen[i + 1] == '|')
            {
                inComment = true;
                i++;
                originalPos = originalPos.Advance('|').Advance('|');
                continue;
            }

            if (c == '\n')
            {
                newlines.Add((i, originalPos));
                originalPos = originalPos.Advance(c);
                continue;
            }

            if (c == '\r')
            {
                originalPos = originalPos.Advance(c);
                continue;
            }

            // simai ignores formatting whitespace anywhere in the chart.
            if (char.IsWhiteSpace(c))
            {
                originalPos = originalPos.Advance(c);
                continue;
            }

            cleanedChars.Add(c);
            positionMap.Add(originalPos);
            originalPos = originalPos.Advance(c);
        }

        var cleanedFumen = new string(cleanedChars.ToArray());

        return (cleanedFumen, positionMap, newlines);
    }

    public static void CheckNewlinePositions(
        string originalFumen,
        List<(int Index, TextPosition OriginalPos)> newlines,
        CheckerContext context)
    {
        foreach (var (newlineIndex, originalPos) in newlines)
        {
            var isValidPosition = IsNewlineAtValidPosition(originalFumen.AsSpan(), newlineIndex);

            if (!isValidPosition)
            {
                context.AddWarning(
                    "Newline inside definition or note",
                    "Newlines should not appear inside BPM, HSpeed, Beat definitions, or note content. The newline will be ignored during parsing.",
                    originalPos,
                    2
                );
            }
        }
    }

    private static bool IsNewlineAtValidPosition(ReadOnlySpan<char> fumen, int newlineIndex)
    {
        var beforeContext = GetContextBefore(fumen, newlineIndex);
        var afterContext = GetContextAfter(fumen, newlineIndex);

        if (IsInsideBpmDefinition(beforeContext, afterContext))
            return false;

        if (IsInsideHsDefinition(beforeContext, afterContext))
            return false;

        if (IsInsideSvDefinition(beforeContext, afterContext))
            return false;

        if (IsInsideBeatDefinition(beforeContext, afterContext))
            return false;

        if (IsInsideNoteContent(beforeContext, afterContext))
            return false;

        return true;
    }

    private static ReadOnlySpan<char> GetContextBefore(ReadOnlySpan<char> fumen, int index)
    {
        var start = Math.Max(0, index - 100);
        return fumen[start..index];
    }

    private static ReadOnlySpan<char> GetContextAfter(ReadOnlySpan<char> fumen, int index)
    {
        var end = Math.Min(fumen.Length, index + 100);
        return fumen[(index + 1)..end];
    }

    private static bool IsInsideBpmDefinition(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var lastOpenParen = before.LastIndexOf('(');
        if (lastOpenParen == -1) return false;

        var lastCloseParen = before.LastIndexOf(')');
        if (lastCloseParen != -1 && lastCloseParen > lastOpenParen) return false;

        var closeParenAfter = after.IndexOf(')');
        if (closeParenAfter == -1) return true;

        var openParenAfter = after.IndexOf('(');
        if (openParenAfter != -1 && openParenAfter < closeParenAfter) return false;

        return true;
    }

    private static bool IsInsideHsDefinition(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var lastHsStart = before.LastIndexOf("<HS*".AsSpan());
        if (lastHsStart == -1) return false;

        var afterHsStart = before[lastHsStart..];
        var lastCloseAngle = afterHsStart.LastIndexOf('>');
        if (lastCloseAngle != -1) return false;

        var closeAngleAfter = after.IndexOf('>');
        if (closeAngleAfter == -1) return true;

        return true;
    }

    private static bool IsInsideSvDefinition(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var lastSvStart = before.LastIndexOf("<SV*".AsSpan());
        if (lastSvStart == -1) return false;

        var afterSvStart = before[lastSvStart..];
        var lastCloseAngle = afterSvStart.LastIndexOf('>');
        if (lastCloseAngle != -1) return false;

        var closeAngleAfter = after.IndexOf('>');
        if (closeAngleAfter == -1) return true;

        return true;
    }

    private static bool IsInsideBeatDefinition(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var lastOpenBrace = before.LastIndexOf('{');
        if (lastOpenBrace == -1) return false;

        var lastCloseBrace = before.LastIndexOf('}');
        if (lastCloseBrace != -1 && lastCloseBrace > lastOpenBrace) return false;

        var closeBraceAfter = after.IndexOf('}');
        if (closeBraceAfter == -1) return true;

        var openBraceAfter = after.IndexOf('{');
        if (openBraceAfter != -1 && openBraceAfter < closeBraceAfter) return false;

        return true;
    }

    private static bool IsInsideNoteContent(ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        var lastComma = before.LastIndexOf(',');
        var lastCommaAfter = after.IndexOf(',');

        var afterTrimmed = after.TrimStart();
        var beforeTrimmed = before.TrimEnd();

        if (beforeTrimmed.IsEmpty || afterTrimmed.IsEmpty)
            return false;

        if (afterTrimmed.StartsWith("(".AsSpan()) ||
            afterTrimmed.StartsWith("{".AsSpan()) ||
            afterTrimmed.StartsWith("<HS*".AsSpan()) ||
            afterTrimmed.StartsWith("<SV*".AsSpan()) ||
            afterTrimmed.StartsWith("E".AsSpan()) ||
            afterTrimmed.StartsWith("||".AsSpan()))
            return false;

        var lastCharBefore = beforeTrimmed[^1];
        var firstCharAfter = afterTrimmed[0];

        if (lastCharBefore == ',')
            return false;

        if (char.IsDigit(lastCharBefore) || SimaiSymbols.IsTouchSensorType(lastCharBefore))
        {
            if (char.IsDigit(firstCharAfter) ||
                SimaiSymbols.IsTouchSensorType(firstCharAfter) ||
                SimaiSymbols.IsNoteModifier(firstCharAfter) ||
                SimaiSymbols.IsSlideChar(firstCharAfter))
                return true;
        }

        if (SimaiSymbols.IsNoteModifier(lastCharBefore) || lastCharBefore == ']' || lastCharBefore == ')')
        {
            if (char.IsDigit(firstCharAfter) || SimaiSymbols.IsTouchSensorType(firstCharAfter))
                return true;
        }

        if (lastCharBefore == '[')
            return true;

        if (afterTrimmed[0] == ']')
            return true;

        return false;
    }

    // ---- 分段 ----

    private static readonly ReadOnlyMemory<char> s_commaMemory = ",".AsMemory();

    public static List<ChartSegment> SplitIntoSegments(string fumen, List<TextPosition> positionMap, CheckerContext context)
    {
        var segments = new List<ChartSegment>();
        var currentStart = 0;
        var fumenMemory = fumen.AsMemory();

        for (var i = 0; i < fumen.Length; i++)
        {
            var c = fumen[i];

            if (c == ',')
            {
                if (i > currentStart)
                {
                    var startPos = GetOriginalPosition(positionMap, currentStart);
                    segments.Add(new ChartSegment(fumenMemory[currentStart..i], startPos, i - currentStart));
                }
                var commaPos = GetOriginalPosition(positionMap, i);
                segments.Add(new ChartSegment(s_commaMemory, commaPos, 1));
                currentStart = i + 1;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (i > currentStart)
                {
                    var startPos = GetOriginalPosition(positionMap, currentStart);
                    segments.Add(new ChartSegment(fumenMemory[currentStart..i], startPos, i - currentStart));
                }
                currentStart = i + 1;
                continue;
            }
        }

        if (currentStart < fumen.Length)
        {
            var startPos = GetOriginalPosition(positionMap, currentStart);
            segments.Add(new ChartSegment(fumenMemory[currentStart..], startPos, fumen.Length - currentStart));
        }

        return segments;
    }

    private static TextPosition GetOriginalPosition(List<TextPosition> positionMap, int cleanedIndex)
    {
        if (positionMap == null || positionMap.Count == 0)
            return TextPosition.Start;

        if (cleanedIndex >= positionMap.Count)
            cleanedIndex = positionMap.Count - 1;
        if (cleanedIndex < 0)
            cleanedIndex = 0;

        return positionMap[cleanedIndex];
    }

    // ---- span 文本工具 ----

    /// <summary>
    /// 解析一个有限十进制数。拒绝空串、单独符号、无整数部分等情况，
    /// 并通过 <see cref="double.IsFinite"/> 过滤 NaN/Infinity。
    /// </summary>
    public static bool TryParseFiniteDecimal(ReadOnlySpan<char> value, bool allowSign, out double result)
    {
        result = 0;
        if (value.IsEmpty) return false;

        var index = 0;
        if (value[0] is '+' or '-')
        {
            if (!allowSign) return false;
            index++;
            if (index == value.Length) return false;
        }

        var digitsBeforeDecimal = 0;
        while (index < value.Length && value[index] is >= '0' and <= '9')
        {
            digitsBeforeDecimal++;
            index++;
        }

        if (digitsBeforeDecimal == 0) return false;

        if (index < value.Length && value[index] == '.')
        {
            index++;
            var digitsAfterDecimal = 0;
            while (index < value.Length && value[index] is >= '0' and <= '9')
            {
                digitsAfterDecimal++;
                index++;
            }

            if (digitsAfterDecimal == 0) return false;
        }

        return index == value.Length &&
               double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out result) &&
               double.IsFinite(result);
    }

    public static int CountChar(ReadOnlySpan<char> s, char c)
    {
        var count = 0;
        foreach (var ch in s)
        {
            if (ch == c) count++;
        }
        return count;
    }

    public static List<Range> SplitByChar(ReadOnlySpan<char> s, char c)
    {
        var result = new List<Range>();
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == c)
            {
                result.Add(start..i);
                start = i + 1;
            }
        }
        result.Add(start..s.Length);
        return result;
    }
}
