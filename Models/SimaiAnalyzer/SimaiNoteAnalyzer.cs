using MajdataEdit_Neo.Types.SimaiAnalyzer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MajdataEdit_Neo.Models.SimaiAnalyzer;

/// <summary>
/// 单个 note token 解析后的结构化信息（button note 专用）。
/// 由 <see cref="SimaiNoteAnalyzer.ParseNoteInfo"/> 填充，由同类的 Validate* 方法校验。
/// </summary>
internal class NoteInfo
{
    public int StartPosition { get; set; }
    public bool IsHold { get; set; }
    public bool IsBreak { get; set; }
    public int BreakModifierCount { get; set; }
    public bool IsEx { get; set; }
    public bool IsMine { get; set; }
    public bool HasStar { get; set; }
    public bool HasDoubleStar { get; set; }
    public bool NoStar { get; set; }
    public bool FadeSlide { get; set; }
    public bool NoFadeSlide { get; set; }
    public bool HasSameStartPointSlides { get; set; }
    public bool NextSlideIsSameHeadChainStart { get; set; }
    public bool HasInvalidSameHeadSeparator { get; set; }
    public ReadOnlyMemory<char>? Duration { get; set; }
    public int DurationStart { get; set; }
    public int DurationEnd { get; set; }
    public List<SlideInfo> Slides { get; set; } = new();
    public List<(char C, int Index)> UnknownChars { get; set; } = new();
    public HashSet<char> ExtraModifiers { get; set; } = new();
}

/// <summary>
/// 一条 slide 路径的解析结果。一个 note 可包含多条 slide（含同起点 / 连接链）。
/// </summary>
internal class SlideInfo
{
    public string? SlideType { get; set; }
    public int StartPosition { get; set; }
    public int? EndPosition { get; set; }
    public int? FlexionPoint { get; set; }
    public ReadOnlyMemory<char>? Duration { get; set; }
    public int DurationStart { get; set; }
    public int DurationEnd { get; set; }
    public bool IsBreak { get; set; }
    public int BreakModifierCount { get; set; }
    public bool IsMine { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public bool IsSameHeadChainStart { get; set; }
}

/// <summary>
/// note analyzer：note token 的解析与校验合并在此，避免解析/检查分处两文件造成阅读歧义。
/// 流程：note 组按 <c>/</c> <c>`</c> 分隔 -> 单 note 判定（touch / button）
/// -> 解析为 <see cref="NoteInfo"/> -> 校验修饰符、时长、slide 链与路径。
/// 合法标记集合与冲突规则见 <see cref="SimaiSymbols"/>，文本工具见 <see cref="SimaiTokenizer"/>。
/// </summary>
internal static class SimaiNoteAnalyzer
{
    // 冲突规则按字符查回 <see cref="SimaiSymbols.ModifierConflicts"/> 中的声明，
    // 让校验文案与符号表保持单一来源。详见 ValidateNoteInfo。
    private static readonly SimaiSymbols.ModifierConflict s_starConflict = FindConflict('$', '@');
    private static readonly SimaiSymbols.ModifierConflict s_headFlagsConflict = FindConflict('!', '?', '@');

    private static SimaiSymbols.ModifierConflict FindConflict(params char[] mods)
    {
        foreach (var c in SimaiSymbols.ModifierConflicts)
        {
            if (c.Modifiers.Length != mods.Length) continue;
            var ok = true;
            for (var i = 0; i < mods.Length; i++)
            {
                if (!c.Modifiers.Contains(mods[i])) { ok = false; break; }
            }
            if (ok) return c;
        }
        return new SimaiSymbols.ModifierConflict(mods, "Conflicting modifiers: '{0}'", "These modifiers are mutually exclusive; use at most one");
    }

    // ---- note 组分隔 ----

    public static void CheckDefinitionPrefix(
        CheckerContext context,
        ReadOnlySpan<char> prefix,
        TextPosition startPos)
    {
        if (prefix.IsEmpty) return;
        CheckNoteGroup(context, prefix.ToString().AsMemory(), startPos);
    }

    public static void CheckNoteGroup(CheckerContext context, ReadOnlyMemory<char> content, TextPosition startPos)
    {
        var contentSpan = content.Span;
        var hasEachSeparator = contentSpan.IndexOf('/') >= 0;
        var currentStart = 0;

        for (var i = 0; i <= contentSpan.Length; i++)
        {
            if (i == contentSpan.Length || contentSpan[i] == '/' || contentSpan[i] == '`')
            {
                if (i > currentStart)
                {
                    var noteSpan = content[currentStart..i];
                    if (hasEachSeparator && IsCompactEach(noteSpan.Span))
                    {
                        context.AddError(
                            $"Compact EACH shorthand '{noteSpan.ToString()}' cannot be used inside an EACH group",
                            "When using '/' to separate notes, each side must be a single note; write e.g. '1/2/3/4' instead of '12/34'",
                            startPos.Advance(contentSpan[..currentStart]),
                            noteSpan.Length
                        );
                    }
                    CheckSingleNote(context, noteSpan, startPos.Advance(contentSpan[..currentStart]));
                }
                else
                {
                    var separatorIndex = i == contentSpan.Length ? Math.Max(0, i - 1) : i;
                    context.AddError(
                        "Missing note between separators",
                        "EACH '/' and pseudo-EACH '`' separators must have a note on both sides",
                        startPos.Advance(contentSpan[..separatorIndex]),
                        1
                    );
                }
                currentStart = i + 1;
            }
        }
    }

    private static bool IsCompactEach(ReadOnlySpan<char> span)
        => span.Length == 2 && span[0] is >= '1' and <= '8' && span[1] is >= '1' and <= '8';

    private static void CheckSingleNote(CheckerContext context, ReadOnlyMemory<char> content, TextPosition startPos)
    {
        if (content.IsEmpty) return;
        var span = content.Span;

        if (IsTouchNote(span, out var sensorType, out var sensorIndex))
        {
            CheckTouchNote(context, content, startPos, sensorType, sensorIndex);
            return;
        }

        if (span[0] is >= '0' and <= '9')
        {
            CheckButtonNote(context, content, startPos);
            return;
        }

        context.AddError(
            $"Invalid note: '{span.ToString()}'",
            "Note must start with a button number (1-8) or sensor type (A-E)",
            startPos,
            content.Length
        );
    }

    private static bool IsTouchNote(ReadOnlySpan<char> content, out char sensorType, out int? sensorIndex)
    {
        sensorType = '\0';
        sensorIndex = null;

        if (content.IsEmpty) return false;

        var c = content[0];
        if (!SimaiSymbols.IsTouchSensorType(c)) return false;

        sensorType = c;

        if (content.Length == 1)
        {
            return sensorType == 'C';
        }

        var idx = 1;
        if (content.Length > idx && char.IsDigit(content[idx]))
        {
            sensorIndex = content[idx] - '0';
            idx++;
        }

        return true;
    }

    private static void CheckTouchNote(CheckerContext context, ReadOnlyMemory<char> content, TextPosition startPos, char sensorType, int? sensorIndex)
    {
        var span = content.Span;

        if (sensorType == 'C')
        {
            if (sensorIndex.HasValue && sensorIndex.Value != 1 && sensorIndex.Value != 2)
            {
                context.AddError(
                    $"Invalid C sensor index: {sensorIndex.Value}",
                    "C sensor can only have index 1 or 2 (or no index)",
                    startPos,
                    2
                );
            }
        }
        else
        {
            if (!sensorIndex.HasValue || sensorIndex.Value < 1 || sensorIndex.Value > 8)
            {
                context.AddError(
                    $"Invalid sensor index for {sensorType}",
                    "Sensor index must be between 1 and 8",
                    startPos,
                    1
                );
            }
        }

        var idx = 1;
        if (sensorIndex.HasValue) idx++;

        var isHold = false;
        var modifierCounts = new Dictionary<char, int>();
        var durationStart = -1;
        var durationEnd = -1;

        for (var i = idx; i < span.Length; i++)
        {
            var c = span[i];

            if (c == '[')
            {
                if (durationStart != -1)
                {
                    context.AddError(
                        "Duplicate duration bracket",
                        "Touch note can only have one duration specification",
                        startPos.Advance(span[..i]),
                        1
                    );
                }
                durationStart = i;
                var relCloseIdx = span[i..].IndexOf(']');
                var closeIdx = relCloseIdx != -1 ? relCloseIdx + i : -1;
                if (closeIdx == -1)
                {
                    context.AddError(
                        "Duration not closed for touch hold",
                        "Duration must be enclosed in brackets, e.g., Ch[4:3]",
                        startPos.Advance(span[..i]),
                        1
                    );
                    return;
                }
                durationEnd = closeIdx;
                i = closeIdx;
                continue;
            }

            switch (c)
            {
                case 'h':
                    isHold = true;
                    modifierCounts.TryGetValue(c, out var holdModifierCount);
                    modifierCounts[c] = holdModifierCount + 1;
                    break;
                case 'f':
                case 'x':
                case 'b':
                case 'm':
                    modifierCounts.TryGetValue(c, out var modifierCount);
                    modifierCounts[c] = modifierCount + 1;
                    break;
                default:
                    if (SimaiSymbols.IsTouchModifier(c))
                    {
                        modifierCounts.TryGetValue(c, out var extraCount);
                        modifierCounts[c] = extraCount + 1;
                    }
                    else
                    {
                        context.AddError(
                            $"Invalid character in touch note: '{span[i]}'",
                            "Touch notes can only contain registered note modifiers (see SimaiSymbols.NoteModifiers)",
                            startPos,
                            content.Length
                        );
                    }
                    break;
            }
        }

        foreach (var (modifier, count) in modifierCounts)
        {
            if (count <= 1) continue;
            context.AddError(
                $"Duplicate touch modifier: '{modifier}'",
                "Each touch-note modifier may appear at most once",
                startPos,
                content.Length
            );
        }

        if (durationEnd >= 0 && durationEnd != span.Length - 1)
        {
            context.AddError(
                "Modifier after TOUCH HOLD duration",
                "TOUCH modifiers must be written before the duration bracket",
                startPos.Advance(span[..(durationEnd + 1)]),
                span.Length - durationEnd - 1
            );
        }

        if (isHold && durationStart != -1)
        {
            var duration = content[(durationStart + 1)..durationEnd];
            ValidateDuration(context, span, startPos, duration.Span, durationStart, "TOUCH HOLD", allowSlideFormat: false);
        }
        else if (durationStart != -1 && !isHold)
        {
            context.AddError(
                "Duration specified for non-hold touch note",
                "Only a TOUCH HOLD may have a duration",
                startPos.Advance(span[..durationStart]),
                durationEnd - durationStart + 1
            );
        }
    }

    // ---- button note ----

    private static void CheckButtonNote(CheckerContext context, ReadOnlyMemory<char> content, TextPosition startPos)
    {
        var span = content.Span;
        var firstDigit = span[0] - '0';
        if (firstDigit < 1 || firstDigit > 8)
        {
            context.AddError(
                $"Invalid button position: {firstDigit}",
                "Button position must be between 1 and 8",
                startPos,
                1
            );
            return;
        }

        if (span.Length == 1) return;

        if (span[1] is >= '0' and <= '9')
        {
            var secondDigit = span[1] - '0';
            if (secondDigit < 1 || secondDigit > 8)
            {
                context.AddError(
                    $"Invalid button position: {secondDigit}",
                    "Button position must be between 1 and 8",
                    startPos.Advance(stackalloc char[] { span[0] }),
                    1
                );
            }

            if (span.Length > 2)
            {
                context.AddError(
                    $"Invalid shorthand EACH note: '{span.ToString()}'",
                    "The compact EACH form contains exactly two unmodified TAP button numbers. Use '/' for three or more notes or when modifiers are present",
                    startPos,
                    content.Length
                );
            }
            return;
        }

        ValidateDurationBrackets(context, span, startPos);
        ValidateButtonModifierOccurrences(context, span, startPos);
        var noteInfo = ParseNoteInfo(content);
        ValidateNoteInfo(context, span, startPos, noteInfo);
    }

    private static void ValidateButtonModifierOccurrences(
        CheckerContext context,
        ReadOnlySpan<char> content,
        TextPosition startPos)
    {
        foreach (var modifier in SimaiSymbols.TapHoldModifiers)
        {
            if (SimaiTokenizer.CountChar(content, modifier) <= 1) continue;
            context.AddError(
                $"Duplicate note modifier: '{modifier}'",
                "Each note modifier may appear at most once",
                startPos,
                content.Length
            );
        }

        var firstStar = content.IndexOf('$');
        if (firstStar < 0) return;

        var starCount = SimaiTokenizer.CountChar(content, '$');
        if (starCount > 2 ||
            (starCount == 2 && (firstStar + 1 >= content.Length || content[firstStar + 1] != '$')))
        {
            context.AddError(
                "Invalid star modifier",
                "Use '$' for a star TAP or one consecutive '$$' pair for a rotating star TAP",
                startPos.Advance(content[..firstStar]),
                starCount
            );
        }
    }

    private static void ValidateDurationBrackets(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos)
    {
        var openIndex = -1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '[')
            {
                if (openIndex != -1)
                {
                    context.AddError(
                        "Nested duration bracket",
                        "Duration brackets cannot be nested",
                        startPos.Advance(content[..i]),
                        1
                    );
                }
                openIndex = i;
            }
            else if (content[i] == ']')
            {
                if (openIndex == -1)
                {
                    context.AddError(
                        "Unexpected closing duration bracket",
                        "A closing ']' must have a matching '['",
                        startPos.Advance(content[..i]),
                        1
                    );
                }
                else
                {
                    openIndex = -1;
                }
            }
        }

        if (openIndex != -1)
        {
            context.AddError(
                "Duration not closed",
                "Duration must be enclosed in brackets, e.g., [4:1]",
                startPos.Advance(content[..openIndex]),
                1
            );
        }
    }

    // ---- 解析：note token -> NoteInfo ----

    public static NoteInfo ParseNoteInfo(ReadOnlyMemory<char> content)
    {
        var span = content.Span;
        var info = new NoteInfo
        {
            StartPosition = span[0] - '0'
        };

        var idx = 1;
        var lastSlideEndPosition = info.StartPosition;

        while (idx < span.Length)
        {
            var c = span[idx];

            // Check for slidecode pattern (uppercase A/B/C/K/P/Q followed by digit(s) ending with K+digit)
            if (SimaiSymbols.IsSlideCodeCommand(c))
            {
                var slideCodeMatch = TryMatchSlideCode(content, idx);
                if (slideCodeMatch != null)
                {
                    if (info.NextSlideIsSameHeadChainStart)
                    {
                        slideCodeMatch.IsSameHeadChainStart = true;
                        info.NextSlideIsSameHeadChainStart = false;
                    }
                    info.Slides.Add(slideCodeMatch);
                    idx = slideCodeMatch.EndIndex;
                    if (slideCodeMatch.EndPosition.HasValue)
                    {
                        lastSlideEndPosition = slideCodeMatch.EndPosition.Value;
                    }
                    continue;
                }
            }

            switch (c)
            {
                case 'h':
                    info.IsHold = true;
                    idx++;
                    break;
                case 'b':
                    if (info.Slides.Count > 0)
                    {
                        var lastSlide = info.Slides[^1];
                        if (idx + 1 < span.Length && span[idx + 1] == '[')
                        {
                            lastSlide.IsBreak = true;
                            lastSlide.BreakModifierCount++;
                        }
                        else if (idx == span.Length - 1)
                        {
                            lastSlide.IsBreak = true;
                            lastSlide.BreakModifierCount++;
                        }
                        else
                        {
                            info.IsBreak = true;
                            info.BreakModifierCount++;
                        }
                    }
                    else
                    {
                        info.IsBreak = true;
                        info.BreakModifierCount++;
                    }
                    idx++;
                    break;
                case 'x':
                    info.IsEx = true;
                    idx++;
                    break;
                case 'm':
                    if (info.Slides.Count > 0)
                    {
                        var lastSlide = info.Slides[^1];
                        if (idx + 1 < span.Length && span[idx + 1] == '[')
                        {
                            lastSlide.IsMine = true;
                        }
                        else if (idx == span.Length - 1)
                        {
                            lastSlide.IsMine = true;
                        }
                        else
                        {
                            info.IsMine = true;
                        }
                    }
                    else
                    {
                        info.IsMine = true;
                    }
                    idx++;
                    break;
                case '$':
                    info.HasStar = true;
                    if (idx + 1 < span.Length && span[idx + 1] == '$')
                    {
                        info.HasDoubleStar = true;
                        idx += 2;
                    }
                    else
                    {
                        idx++;
                    }
                    break;
                case '@':
                    info.NoStar = true;
                    idx++;
                    break;
                case '?':
                    info.FadeSlide = true;
                    idx++;
                    break;
                case '!':
                    info.NoFadeSlide = true;
                    idx++;
                    break;
                case '[':
                    var relClose = span[idx..].IndexOf(']');
                    var closeIdx = relClose != -1 ? relClose + idx : -1;
                    if (closeIdx != -1)
                    {
                        if (info.Slides.Count > 0)
                        {
                            var lastSlide = info.Slides[^1];
                            lastSlide.Duration = content[(idx + 1)..closeIdx];
                            lastSlide.DurationStart = idx;
                            lastSlide.DurationEnd = closeIdx;
                        }
                        else
                        {
                            info.Duration = content[(idx + 1)..closeIdx];
                            info.DurationStart = idx;
                            info.DurationEnd = closeIdx;
                        }
                        idx = closeIdx + 1;
                    }
                    else
                    {
                        if (info.Slides.Count > 0)
                        {
                            var lastSlide = info.Slides[^1];
                            lastSlide.Duration = content[(idx + 1)..];
                            lastSlide.DurationStart = idx;
                            info.DurationEnd = span.Length - 1;
                        }
                        else
                        {
                            info.Duration = content[(idx + 1)..];
                            info.DurationStart = idx;
                            info.DurationEnd = span.Length - 1;
                        }
                        idx = span.Length;
                    }
                    break;
                case '*':
                    info.HasSameStartPointSlides = true;
                    if (info.NextSlideIsSameHeadChainStart)
                    {
                        info.HasInvalidSameHeadSeparator = true;
                    }
                    idx++;
                    lastSlideEndPosition = info.StartPosition;
                    info.NextSlideIsSameHeadChainStart = true;
                    break;
                default:
                    var slideMatch = TryMatchSlide(content, idx, lastSlideEndPosition);
                    if (slideMatch != null)
                    {
                        if (info.NextSlideIsSameHeadChainStart)
                        {
                            slideMatch.IsSameHeadChainStart = true;
                            info.NextSlideIsSameHeadChainStart = false;
                        }
                        info.Slides.Add(slideMatch);
                        idx = slideMatch.EndIndex;
                        if (slideMatch.EndPosition.HasValue)
                        {
                            lastSlideEndPosition = slideMatch.EndPosition.Value;
                        }
                    }
                    else if (SimaiSymbols.IsButtonModifier(c))
                    {
                        info.ExtraModifiers.Add(c);
                        idx++;
                    }
                    else
                    {
                        info.UnknownChars.Add((c, idx));
                        idx++;
                    }
                    break;
            }
        }

        return info;
    }

    private static SlideInfo? TryMatchSlide(ReadOnlyMemory<char> content, int startIdx, int noteStartPosition)
    {
        var span = content.Span;
        var idx = startIdx;
        var slide = new SlideInfo { StartIndex = idx, StartPosition = noteStartPosition };

        foreach (var doubleChar in SimaiSymbols.SlideTypeDoubleChars)
        {
            if (idx + 2 <= span.Length && span.Slice(idx, 2).SequenceEqual(doubleChar.AsSpan()))
            {
                slide.SlideType = doubleChar;
                idx += 2;
                break;
            }
        }

        if (slide.SlideType == null)
        {
            foreach (var slideChar in SimaiSymbols.SlideTypeChars)
            {
                if (idx < span.Length && span[idx] == slideChar)
                {
                    slide.SlideType = slideChar.ToString();
                    idx++;
                    break;
                }
            }
        }

        if (slide.SlideType == null) return null;

        if (slide.SlideType == "V")
        {
            if (idx < span.Length && char.IsDigit(span[idx]))
            {
                slide.FlexionPoint = span[idx] - '0';
                idx++;
            }
        }

        if (idx < span.Length && char.IsDigit(span[idx]))
        {
            slide.EndPosition = span[idx] - '0';
            idx++;
        }

        if (idx < span.Length && span[idx] == '[')
        {
            var relClose = span[idx..].IndexOf(']');
            var closeIdx = relClose != -1 ? relClose + idx : -1;
            if (closeIdx != -1)
            {
                slide.Duration = content[(idx + 1)..closeIdx];
                slide.DurationStart = idx;
                slide.DurationEnd = closeIdx;
                idx = closeIdx + 1;
            }
        }

        if (idx < span.Length && span[idx] == 'b')
        {
            slide.IsBreak = true;
            slide.BreakModifierCount++;
            idx++;
        }

        if (idx < span.Length && span[idx] == 'm')
        {
            slide.IsMine = true;
            idx++;
        }

        slide.EndIndex = idx;
        return slide;
    }

    private static SlideInfo? TryMatchSlideCode(ReadOnlyMemory<char> content, int startIdx)
    {
        var span = content.Span;
        var idx = startIdx;

        if (idx >= span.Length || !SimaiSymbols.IsSlideCodeCommand(span[idx]))
            return null;

        // Check if this is followed by digit(s) and eventually K+digit
        // A slidecode pattern: command letter(s) and digit(s) ending with K[1-8]
        var tempIdx = idx;
        var foundK = false;

        // Scan forward to find if there's a K followed by a digit
        while (tempIdx < span.Length)
        {
            var tc = span[tempIdx];
            if (tc == 'K' && tempIdx + 1 < span.Length && span[tempIdx + 1] >= '1' && span[tempIdx + 1] <= '8')
            {
                // Check that K is not followed by another command letter or digit (would mean it's not the end)
                if (tempIdx + 2 < span.Length && SimaiSymbols.IsSlideCodeCommand(span[tempIdx + 2]))
                    break; // K is followed by a command letter, not the end
                if (tempIdx + 2 < span.Length && char.IsDigit(span[tempIdx + 2]))
                    break; // K is followed by a digit that's not part of K's parameter
                foundK = true;
                break;
            }
            // Stop if we hit a duration bracket, break/mine flags, or end
            if (tc == '[' || tc == 'b' || tc == 'm' || tc == '/' || tc == '`' || tc == ',')
                break;
            tempIdx++;
        }

        if (!foundK) return null;

        // Now parse the slidecode
        var slide = new SlideInfo { StartIndex = idx, SlideType = "SC" };

        // Special case: first char is K - directly parse K+digit+duration+flags
        if (span[idx] == 'K')
        {
            idx++;
            if (idx >= span.Length || span[idx] < '1' || span[idx] > '8')
                return null;
            slide.EndPosition = span[idx] - '0';
            idx++;

            // Parse duration
            if (idx < span.Length && span[idx] == '[')
            {
                var relClose = span[idx..].IndexOf(']');
                var closeIdx = relClose != -1 ? relClose + idx : -1;
                if (closeIdx != -1)
                {
                    slide.Duration = content[(idx + 1)..closeIdx];
                    slide.DurationStart = idx;
                    slide.DurationEnd = closeIdx;
                    idx = closeIdx + 1;
                }
            }

            // Parse break/mine flags
            if (idx < span.Length && span[idx] == 'b')
            {
                slide.IsBreak = true;
                slide.BreakModifierCount++;
                idx++;
            }
            if (idx < span.Length && span[idx] == 'm')
            {
                slide.IsMine = true;
                idx++;
            }

            slide.EndIndex = idx;
            return slide;
        }

        idx++; // skip first command letter

        while (idx < span.Length)
        {
            var c = span[idx];

            if (c == 'K')
            {
                idx++;
                if (idx >= span.Length || span[idx] < '1' || span[idx] > '8')
                    return null;
                slide.EndPosition = span[idx] - '0';
                idx++;

                // Parse duration
                if (idx < span.Length && span[idx] == '[')
                {
                    var relClose = span[idx..].IndexOf(']');
                    var closeIdx = relClose != -1 ? relClose + idx : -1;
                    if (closeIdx != -1)
                    {
                        slide.Duration = content[(idx + 1)..closeIdx];
                        slide.DurationStart = idx;
                        slide.DurationEnd = closeIdx;
                        idx = closeIdx + 1;
                    }
                }

                // Parse break/mine flags
                if (idx < span.Length && span[idx] == 'b')
                {
                    slide.IsBreak = true;
                    slide.BreakModifierCount++;
                    idx++;
                }
                if (idx < span.Length && span[idx] == 'm')
                {
                    slide.IsMine = true;
                    idx++;
                }

                slide.EndIndex = idx;
                return slide;
            }

            if (SimaiSymbols.IsSlideCodeCommand(c) || char.IsDigit(c))
            {
                idx++;
                continue;
            }

            // Invalid character in slidecode
            return null;
        }

        return null;
    }

    // ---- 校验：NoteInfo ----

    private static void ValidateNoteInfo(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos, NoteInfo info)
    {
        foreach (var (c, idx) in info.UnknownChars)
        {
            context.AddError(
                $"Unknown character in note: '{c}'",
                $"Character '{c}' is not a valid note modifier or slide type",
                startPos.Advance(content[..idx]),
                content.Length
            );
        }

        if (info.IsHold && info.Slides.Count > 0)
        {
            context.AddError(
                "Note cannot be both HOLD and SLIDE",
                "A note can only be one type: TAP, HOLD, or SLIDE",
                startPos,
                content.Length
            );
        }

        // $ 与 @ 互斥（force-star vs no-star），文案取自 SimaiSymbols.ModifierConflicts
        if (info.HasStar && info.NoStar)
        {
            context.AddError(
                s_starConflict.Message,
                s_starConflict.Detail,
                startPos,
                content.Length
            );
        }

        if (info.BreakModifierCount > 1 || info.Slides.Any(slide => slide.BreakModifierCount > 1))
        {
            context.AddError(
                "Duplicate BREAK modifier",
                "The TAP/HOLD head and each SLIDE chain may each contain at most one 'b' modifier",
                startPos,
                content.Length
            );
        }

        if (info.HasInvalidSameHeadSeparator || info.NextSlideIsSameHeadChainStart)
        {
            context.AddError(
                "Invalid same-start SLIDE separator",
                "Each '*' must be followed by another SLIDE path from the same starting button",
                startPos,
                content.Length
            );
        }

        if (info.HasSameStartPointSlides && info.Slides.Count < 2)
        {
            context.AddError(
                "Same-start SLIDE needs at least two paths",
                "The '*' notation joins two or more SLIDEs that share a starting button",
                startPos,
                content.Length
            );
        }

        if (info.Slides.Count == 0 && SimaiTokenizer.CountChar(content, '[') > 1)
        {
            context.AddError(
                "Duplicate duration bracket",
                "A HOLD can only have one duration specification",
                startPos,
                content.Length
            );
        }

        // '!', '?', '@' 互斥（slide head flags），文案取自 SimaiSymbols.ModifierConflicts
        var headStyleFlags = new List<char>();
        if (info.NoFadeSlide) headStyleFlags.Add('!');
        if (info.FadeSlide) headStyleFlags.Add('?');
        if (info.NoStar) headStyleFlags.Add('@');
        if (headStyleFlags.Count > 1)
        {
            var message = string.Format(
                CultureInfo.InvariantCulture,
                s_headFlagsConflict.Message,
                string.Join("', '", headStyleFlags));
            context.AddError(
                message,
                s_headFlagsConflict.Detail,
                startPos,
                content.Length
            );
        }

        if (info.HasStar && info.Slides.Count > 0)
        {
            context.AddWarning(
                "Redundant star modifier '$' on SLIDE",
                "SLIDE notes automatically have a star shape; '$' is redundant here",
                startPos,
                content.Length
            );
        }

        if (info.NoStar && info.Slides.Count == 0)
        {
            context.AddError(
                "Invalid '@' modifier on non-SLIDE note",
                "The '@' modifier (no star) is only meaningful for SLIDE notes",
                startPos,
                content.Length
            );
        }

        if (info.FadeSlide && info.Slides.Count == 0)
        {
            context.AddError(
                "Invalid '?' modifier on non-SLIDE note",
                "The '?' modifier (fade slide) is only meaningful for SLIDE notes",
                startPos,
                content.Length
            );
        }

        if (info.NoFadeSlide && info.Slides.Count == 0)
        {
            context.AddError(
                "Invalid '!' modifier on non-SLIDE note",
                "The '!' modifier (no fade slide) is only meaningful for SLIDE notes",
                startPos,
                content.Length
            );
        }

        if (info.IsHold && info.Duration.HasValue)
        {
            if (info.DurationEnd != content.Length - 1)
            {
                context.AddError(
                    "Modifier after HOLD duration",
                    "HOLD modifiers must be written before the duration bracket",
                    startPos.Advance(content[..(info.DurationEnd + 1)]),
                    content.Length - info.DurationEnd - 1
                );
            }
            ValidateDuration(context, content, startPos, info.Duration.Value.Span, info.DurationStart, "HOLD", allowSlideFormat: false);
        }

        ValidateSlidesDuration(context, content, startPos, info);

        if (!info.IsHold && info.Slides.Count == 0 && info.Duration.HasValue)
        {
            context.AddError(
                "Duration specified for non-HOLD/SLIDE note",
                "Only HOLD and SLIDE notes may have a duration",
                startPos.Advance(content[..info.DurationStart]),
                info.Duration.Value.Length
            );
        }
    }

    private static void ValidateSlidesDuration(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos, NoteInfo info)
    {
        if (info.Slides.Count == 0) return;

        if (info.HasSameStartPointSlides)
        {
            var chains = SplitIntoSlideChains(info.Slides);
            foreach (var chain in chains)
            {
                ValidateSlideChain(context, content, startPos, chain);
            }
        }
        else
        {
            ValidateSlideChain(context, content, startPos, info.Slides);
        }
    }

    private static List<List<SlideInfo>> SplitIntoSlideChains(List<SlideInfo> slides)
    {
        var chains = new List<List<SlideInfo>>();
        var currentChain = new List<SlideInfo>();

        foreach (var slide in slides)
        {
            if (slide.IsSameHeadChainStart && currentChain.Count > 0)
            {
                chains.Add(currentChain);
                currentChain = new List<SlideInfo>();
            }
            currentChain.Add(slide);
        }

        if (currentChain.Count > 0)
        {
            chains.Add(currentChain);
        }

        return chains;
    }

    private static void ValidateSlideChain(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos, List<SlideInfo> chain)
    {
        if (chain.Count == 0) return;

        var slidesWithDuration = chain.Count(s => s.Duration.HasValue);
        var lastSlide = chain[^1];

        for (var i = 0; i < chain.Count; i++)
        {
            var slide = chain[i];
            ValidateSlide(context, content, startPos, slide, checkDuration: false);
            if (i < chain.Count - 1 && (slide.IsBreak || slide.IsMine))
            {
                context.AddError(
                    "Modifier inside connected SLIDE",
                    "BREAK/mine modifiers apply to the complete connected SLIDE and may only appear after its final duration",
                    startPos.Advance(content[..Math.Max(slide.StartIndex, slide.EndIndex - 1)]),
                    1
                );
            }
        }

        if (slidesWithDuration == 0)
        {
            context.AddError(
                "Slide missing duration",
                "Slide must have a duration specified, e.g., [8:1] or [#1.5]",
                startPos.Advance(content[..lastSlide.EndIndex]),
                1
            );
            return;
        }

        if (slidesWithDuration == chain.Count)
        {
            foreach (var slide in chain)
            {
                if (slide.Duration.HasValue)
                {
                    ValidateDuration(context, content, startPos, slide.Duration.Value.Span, slide.DurationStart, "SLIDE", allowSlideFormat: true);
                }
            }
            return;
        }

        if (slidesWithDuration == 1 && lastSlide.Duration.HasValue)
        {
            ValidateDuration(context, content, startPos, lastSlide.Duration.Value.Span, lastSlide.DurationStart, "SLIDE", allowSlideFormat: true);
            return;
        }

        context.AddError(
            "Invalid slide duration specification",
            "For connected slides, either all slides must have individual durations, or only the last slide can have a duration (applied to entire chain)",
            startPos,
            content.Length
        );
    }

    // ---- 时长校验 ----

    private static void ValidateDuration(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos,
        ReadOnlySpan<char> duration, int durationStart, string noteType, bool allowSlideFormat,
        bool allowBareNumber = false)
    {
        if (duration.IsEmpty)
        {
            context.AddError(
                $"Empty duration for {noteType}",
                "Duration cannot be empty",
                startPos.Advance(content[..durationStart]),
                2
            );
            return;
        }

        var hashCount = SimaiTokenizer.CountChar(duration, '#');
        var colonCount = SimaiTokenizer.CountChar(duration, ':');

        if (allowSlideFormat && hashCount >= 2)
        {
            ValidateSlideDuration(context, content, startPos, duration, durationStart);
            return;
        }

        if (hashCount == 0 && colonCount == 0)
        {
            if (!allowBareNumber ||
                !SimaiTokenizer.TryParseFiniteDecimal(duration, allowSign: false, out var value) ||
                value < 0)
            {
                context.AddError(
                    $"Invalid duration format: '{duration.ToString()}'",
                    allowBareNumber
                        ? "Duration must be a non-negative number"
                        : "A duration cannot be a bare number. Use a ratio such as '8:1', absolute seconds such as '#1.5', or a supported SLIDE duration format",
                    startPos.Advance(content[..(durationStart + 1)]),
                    duration.Length
                );
            }
        }
        else if (hashCount == 0 && colonCount == 1)
        {
            ValidateRatioDuration(context, content, startPos, duration, durationStart);
        }
        else if (hashCount == 1 && duration[0] == '#')
        {
            var timeValue = duration[1..];
            if (!SimaiTokenizer.TryParseFiniteDecimal(timeValue, allowSign: false, out var time) || time < 0)
            {
                context.AddError(
                    $"Invalid absolute time: '{timeValue.ToString()}'",
                    "Absolute time must be a non-negative number (in seconds)",
                    startPos.Advance(content[..(durationStart + 2)]),
                    timeValue.Length
                );
            }
        }
        else if (hashCount == 1 && duration[0] != '#')
        {
            ValidateCustomBpmDuration(context, content, startPos, duration, durationStart);
        }
        else
        {
            context.AddError(
                $"Invalid duration format: '{duration.ToString()}'",
                "Duration format is invalid. Use 'division:beats', '#seconds', or 'BPM#division:beats'",
                startPos.Advance(content[..(durationStart + 1)]),
                duration.Length
            );
        }
    }

    private static void ValidateSlideDuration(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos,
        ReadOnlySpan<char> duration, int durationStart)
    {
        var separatorIndex = duration.IndexOf("##".AsSpan());
        if (separatorIndex < 0 || duration[(separatorIndex + 2)..].IndexOf("##".AsSpan()) >= 0)
        {
            context.AddError(
                $"Invalid slide duration format: '{duration.ToString()}'",
                "Slide duration must contain exactly one '##' separator: 'startTime##moveTime'",
                startPos.Advance(content[..(durationStart + 1)]),
                duration.Length
            );
            return;
        }

        var startTimeStr = duration[..separatorIndex];
        if (startTimeStr.IsEmpty ||
            !SimaiTokenizer.TryParseFiniteDecimal(startTimeStr, allowSign: false, out var startTime) ||
            startTime < 0)
        {
            context.AddError(
                $"Invalid slide start time: '{startTimeStr.ToString()}'",
                "Slide start time before '##' must be a non-negative number (in seconds)",
                startPos.Advance(content[..(durationStart + 1)]),
                Math.Max(1, startTimeStr.Length)
            );
        }

        var moveTimeStr = duration[(separatorIndex + 2)..];
        var moveTimeOffset = durationStart + 1 + separatorIndex + 2;

        if (moveTimeStr.IsEmpty)
        {
            context.AddError(
                "Empty slide move time",
                "Slide move time cannot be empty",
                startPos.Advance(content[..moveTimeOffset]),
                1
            );
            return;
        }

        if (moveTimeStr[0] == '#')
        {
            context.AddError(
                $"Invalid slide move time: '{moveTimeStr.ToString()}'",
                "Move time after '##' must be seconds, a ratio, or a custom-BPM duration; it cannot start with '#'",
                startPos.Advance(content[..moveTimeOffset]),
                moveTimeStr.Length
            );
            return;
        }

        ValidateDuration(
            context,
            content,
            startPos,
            moveTimeStr,
            moveTimeOffset - 1,
            "SLIDE move time",
            allowSlideFormat: false,
            allowBareNumber: true
        );
    }

    private static void ValidateRatioDuration(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos,
        ReadOnlySpan<char> duration, int durationStart)
    {
        var colonIdx = duration.IndexOf(':');
        if (colonIdx <= 0 || colonIdx == duration.Length - 1)
        {
            context.AddError(
                $"Invalid duration format: '{duration.ToString()}'",
                "Duration format should be 'division:beats', e.g., '4:2' means 2 beats at quarter note division",
                startPos.Advance(content[..(durationStart + 1)]),
                duration.Length
            );
            return;
        }

        var divisionStr = duration[..colonIdx];
        var beatsStr = duration[(colonIdx + 1)..];

        if (!int.TryParse(divisionStr, out var division) || division <= 0)
        {
            context.AddError(
                $"Invalid division: '{divisionStr.ToString()}'",
                "Division must be a positive integer (e.g., 4 for quarter note, 8 for eighth note)",
                startPos.Advance(content[..(durationStart + 1)]),
                divisionStr.Length
            );
        }

        if (!int.TryParse(beatsStr, out var beats) || beats < 0)
        {
            context.AddError(
                $"Invalid beat count: '{beatsStr.ToString()}'",
                "Beat count must be a non-negative integer",
                startPos.Advance(content[..(durationStart + 1 + colonIdx + 1)]),
                beatsStr.Length
            );
        }
    }

    private static void ValidateCustomBpmDuration(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos,
        ReadOnlySpan<char> duration, int durationStart)
    {
        var hashIdx = duration.IndexOf('#');
        var bpmStr = duration[..hashIdx];
        var restStr = duration[(hashIdx + 1)..];

        if (bpmStr.IsEmpty)
        {
            context.AddError(
                "Empty BPM in duration",
                "Custom BPM cannot be empty",
                startPos.Advance(content[..(durationStart + 1)]),
                1
            );
            return;
        }

        if (!SimaiTokenizer.TryParseFiniteDecimal(bpmStr, allowSign: false, out var bpm) || bpm <= 0)
        {
            context.AddError(
                $"Invalid BPM: '{bpmStr.ToString()}'",
                "BPM must be a positive number",
                startPos.Advance(content[..(durationStart + 1)]),
                bpmStr.Length
            );
            return;
        }

        if (restStr.IsEmpty)
        {
            context.AddError(
                "Empty duration after BPM",
                "Duration must be specified after BPM",
                startPos.Advance(content[..(durationStart + 1 + hashIdx + 1)]),
                1
            );
            return;
        }

        if (restStr.Contains(':'))
        {
            ValidateRatioDuration(context, content, startPos, restStr, durationStart + 1 + hashIdx);
        }
        else if (!SimaiTokenizer.TryParseFiniteDecimal(restStr, allowSign: false, out var durationValue) || durationValue < 0)
        {
            context.AddError(
                $"Invalid duration: '{restStr.ToString()}'",
                "Duration must be a non-negative number or ratio format like '8:1'",
                startPos.Advance(content[..(durationStart + 1 + hashIdx + 1)]),
                restStr.Length
            );
        }
    }

    // ---- slide 路径校验 ----

    private static void ValidateSlide(CheckerContext context, ReadOnlySpan<char> content, TextPosition startPos,
        SlideInfo slide, bool checkDuration)
    {
        // SlideCode has its own validation in TryMatchSlideCode
        if (slide.SlideType == "SC")
        {
            if (checkDuration && slide.Duration.HasValue)
            {
                ValidateDuration(context, content, startPos, slide.Duration.Value.Span, slide.DurationStart, "SLIDE", allowSlideFormat: true);
            }
            return;
        }

        if (slide.EndPosition == null)
        {
            context.AddError(
                $"Slide missing end position",
                $"Slide type '{slide.SlideType}' requires an end position (button 1-8)",
                startPos.Advance(content[..slide.StartIndex]),
                content.Length
            );
            return;
        }

        if (slide.EndPosition < 1 || slide.EndPosition > 8)
        {
            context.AddError(
                $"Invalid slide end position: {slide.EndPosition}",
                "End position must be between 1 and 8",
                startPos.Advance(content[..(slide.StartIndex + slide.SlideType!.Length)]),
                content.Length - slide.SlideType!.Length
            );
            return;
        }

        if (slide.SlideType == "V" &&
            (!slide.FlexionPoint.HasValue || slide.FlexionPoint.Value < 1 || slide.FlexionPoint.Value > 8))
        {
            context.AddError(
                "Invalid V-shaped SLIDE flexion point",
                "V-shaped SLIDE requires a flexion button between 1 and 8, e.g., 1V35",
                startPos.Advance(content[..slide.StartIndex]),
                Math.Max(1, slide.EndIndex - slide.StartIndex)
            );
            return;
        }

        if (slide.SlideType == "v" && slide.StartPosition == slide.EndPosition)
        {
            context.AddWarning(
                "Same-button 'v' SLIDE",
                "A same-button 'v' SLIDE is supported, but its use is not recommended",
                startPos.Advance(content[..slide.StartIndex]),
                Math.Max(1, slide.EndIndex - slide.StartIndex)
            );
        }

        if (!IsValidSlidePath(slide.SlideType!, slide.StartPosition, slide.EndPosition.Value, slide.FlexionPoint))
        {
            var detail = GetSlidePathErrorDetail(slide.SlideType!, slide.StartPosition, slide.EndPosition.Value, slide.FlexionPoint);
            context.AddError(
                $"Invalid slide path: {slide.StartPosition}{slide.SlideType}{slide.FlexionPoint}{slide.EndPosition}",
                detail,
                startPos.Advance(content[..slide.StartIndex]),
                content.Length
            );
        }

        if (checkDuration && slide.Duration.HasValue)
        {
            ValidateDuration(context, content, startPos, slide.Duration.Value.Span, slide.DurationStart, "SLIDE", allowSlideFormat: true);
        }
    }

    private static bool IsValidSlidePath(string slideType, int start, int end, int? flexionPoint)
    {
        var interval = GetPointInterval(start, end);

        return slideType switch
        {
            "-" => interval >= 2,
            "^" => interval is 1 or 2 or 3,
            "v" => interval != 4,
            "<" or ">" => true,
            "V" => flexionPoint.HasValue &&
                   GetPointInterval(start, flexionPoint.Value) == 2 &&
                   GetPointInterval(flexionPoint.Value, end) >= 2 &&
                   start != end,
            "p" or "q" or "pp" or "qq" => true,
            "s" or "z" or "w" => interval == 4,
            _ => true
        };
    }

    private static string GetSlidePathErrorDetail(string slideType, int start, int end, int? flexionPoint)
    {
        return slideType switch
        {
            "-" => "Straight slide requires start and end positions to be at least 2 buttons apart",
            "^" => "The '^' arc cannot connect the same button or the opposite button",
            "v" => "The 'v' slide cannot connect opposite buttons",
            "V" => flexionPoint == null
                ? "V-shaped slide requires a flexion point, e.g., 1V35"
                : "V-shaped slide requires flexion point to be exactly 2 buttons from start, and end to be at least 2 buttons from flexion point",
            "s" or "z" or "w" => "This slide type requires start and end positions to be opposite (diagonally across)",
            _ => "Invalid slide path"
        };
    }

    private static int GetPointInterval(int a, int b)
    {
        var angleA = GetButtonAngle(a);
        var angleB = GetButtonAngle(b);
        var diff = Math.Abs(angleA - angleB);
        return Math.Min(diff / 45, 8 - diff / 45);
    }

    private static int GetButtonAngle(int button)
    {
        return button switch
        {
            8 => 0,
            1 => 45,
            2 => 90,
            3 => 135,
            4 => 180,
            5 => 225,
            6 => 270,
            7 => 315,
            _ => 0
        };
    }
}
