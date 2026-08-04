using System;
using System.Collections.Generic;
using static MajdataEdit_Neo.Models.SimaiAnalyzer.SimaiSymbols.NoteModifierScope;

namespace MajdataEdit_Neo.Models.SimaiAnalyzer;

/// <summary>
/// simai 记谱的全部符号定义集中在此。
/// <see cref="NoteModifiers"/> 是"合法修饰符"的唯一注册基准：
/// 只有出现在该表里、且 <see cref="NoteModifierScope"/> 覆盖当前 note 类型的字符，
/// 解析器 default 分支才会接受；不在表里、或作用域不匹配的字符一律报"未知字符"。
/// 新增一个可用标记只需：
///   1. 在 <see cref="NoteModifiers"/> 表里加一条 <see cref="NoteModifier"/>（带 <see cref="NoteModifierScope"/>），解析器即会自动接受该字符；
///   2. 若该标记需要特殊语义（影响时长/slide/星形等），在 <c>SimaiNoteAnalyzer</c> 的解析 switch 里补一个 case；
///   3. 若需限制出现次数，把字符加进 <see cref="TapHoldModifiers"/>（至多一次）；
///   4. 若它与其他标记互斥，在 <see cref="ModifierConflicts"/> 里声明冲突。
/// 所有修饰符大小写敏感：<c>b</c> 是 break，<c>B</c> 属于 SlideCode 命令，二者不可混同。
/// </summary>
internal static class SimaiSymbols
{
    // ---- slide 形状 ----

    /// <summary>单字符 slide 类型（直线、弧线等）。</summary>
    public static readonly char[] SlideTypeChars = ['-', '^', 'v', '<', '>', 'V', 'p', 'q', 's', 'z', 'w'];

    /// <summary>双字符 slide 类型。</summary>
    public static readonly string[] SlideTypeDoubleChars = ["pp", "qq"];

    /// <summary>SlideCode 指令字母（形如 <c>A...K8</c> 的连接 slide）。</summary>
    public static readonly char[] SlideCodeCommands = ['A', 'B', 'C', 'K', 'P', 'Q'];

    // ---- touch 传感器 ----

    /// <summary>touch 传感器类型（A-E，C 为中心）。大小写敏感，仅大写。</summary>
    public static readonly char[] TouchSensorTypes = ['A', 'B', 'C', 'D', 'E'];

    // ---- note 修饰符元数据（注册列表）----

    /// <summary>修饰符作用域：可出现在 button note、touch note 或两者。</summary>
    [Flags]
    public enum NoteModifierScope { None = 0, Button = 1, Touch = 2 }

    /// <summary>单个 note 修饰符的描述。<paramref name="Scope"/> 决定它在哪类 note 里被接受。</summary>
    public sealed record NoteModifier(char Char, string Name, string Description, NoteModifierScope Scope);

    /// <summary>
    /// note 可附带的全部修饰符--这是"合法标记"的唯一注册基准。
    /// <see cref="IsButtonModifier"/> / <see cref="IsTouchModifier"/> 均由本表的 <see cref="NoteModifierScope"/> 派生，
    /// 解析器 default 分支据此自动接受已注册且作用域匹配的字符。
    /// </summary>
    public static readonly NoteModifier[] NoteModifiers =
    [
        new('b', "BREAK",      "绝赞",                 Button | Touch),
        new('x', "EX",         "保护套",               Button | Touch),
        new('m', "MINE",       "地雷",                 Button | Touch),
        new('c', "CONSTANT",   "不受SV影响",           Button | Touch),

        new('h', "HOLD",       "长按",                 Button | Touch),

        new('f', "FIREWORK",   "touch 烟花",           Touch),

        new('$', "STAR",       "星形 tap；$$ 为旋转星", Button),
        new('@', "NO_STAR",    "tap 头 slide",         Button),
        new('?', "FADE",       "无头渐入 slide",       Button),
        new('!', "NO_FADE",    "无头不渐入 slide",     Button),
        new('*', "SAME_START", "同起点 slide 分隔符",   Button),
    ];

    /// <summary>button note 中"至多出现一次"的修饰符（大小写敏感判定）。</summary>
    public static readonly char[] TapHoldModifiers = ['h', 'x', '@', '?', '!', 'c'];

    // ---- 冲突标记列表 ----

    /// <summary>
    /// 互斥修饰符组：组内最多出现一个，多于一个即报错。
    /// <see cref="ModifierConflict.Message"/> 中的 <c>{0}</c> 会被替换为实际出现的字符列表。
    /// </summary>
    public sealed record ModifierConflict(char[] Modifiers, string Message, string Detail);

    /// <summary>
    /// 全部冲突规则。新增冲突只需在此追加一条 <see cref="ModifierConflict"/>，
    /// 并在 <c>SimaiNoteAnalyzer</c> 的 <c>FindConflict</c> 里为涉及字符补一个探针。
    /// </summary>
    public static readonly ModifierConflict[] ModifierConflicts =
    [
        new(['$', '@'],
            "Conflicting star modifiers: '$' and '@'",
            "Using both '$' (force star) and '@' (no star) is contradictory"),
        new(['!', '?', '@'],
            "Conflicting slide head modifiers: '{0}'",
            "The slide head flags '!', '?', and '@' are mutually exclusive; use at most one"),
    ];

    // ---- 由上表派生的查找结构 ----

    private static readonly HashSet<char> s_noteModifierOrSeparatorChars = BuildNoteModifierOrSeparatorChars();
    private static readonly Dictionary<char, NoteModifierScope> s_modifierScopes = BuildModifierScopes();

    private static HashSet<char> BuildNoteModifierOrSeparatorChars()
    {
        var set = new HashSet<char>();
        foreach (var m in NoteModifiers)
            set.Add(m.Char);
        // 分隔符 '/' 与 '`' 同样属于 note 内容字符，参与换行位置判定。
        set.Add('/');
        set.Add('`');
        return set;
    }

    private static Dictionary<char, NoteModifierScope> BuildModifierScopes()
    {
        var d = new Dictionary<char, NoteModifierScope>();
        foreach (var m in NoteModifiers)
            d[m.Char] = m.Scope;
        return d;
    }

    // ---- 谓词（大小写敏感）----

    /// <summary>
    /// 该字符是否属于 note 内容字符（修饰符或分隔符），用于换行位置判定。
    /// </summary>
    public static bool IsNoteModifier(char c) => s_noteModifierOrSeparatorChars.Contains(c);

    /// <summary>该字符是否是已注册的 button note 修饰符（作用域含 Button）。</summary>
    public static bool IsButtonModifier(char c) => IsInScope(c, Button);

    /// <summary>该字符是否是已注册的 touch note 修饰符（作用域含 Touch）。</summary>
    public static bool IsTouchModifier(char c) => IsInScope(c, Touch);

    private static bool IsInScope(char c, NoteModifierScope scope) =>
        s_modifierScopes.TryGetValue(c, out var s) && (s & scope) != None;

    public static bool IsSlideChar(char c)
    {
        foreach (var slideChar in SlideTypeChars)
        {
            if (c == slideChar) return true;
        }
        return false;
    }

    public static bool IsTouchSensorType(char c)
    {
        foreach (var t in TouchSensorTypes)
        {
            if (c == t) return true;
        }
        return false;
    }

    public static bool IsSlideCodeCommand(char c) => c is 'A' or 'B' or 'C' or 'K' or 'P' or 'Q';
}
