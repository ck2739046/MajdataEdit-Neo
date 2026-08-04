using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Utils;
using MajdataEdit_Neo.Base;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MajdataEdit_Neo.Types.SimaiAnalyzer;

public class SimaiCompletionData(string text) : ICompletionData
{
    public IImage Image { get; } = null!;
    public string Text { get; } = text;
    public object Content => Text;
    public object? Description { get; } = null;
    public double Priority { get; } = 0;

    public void Complete(TextArea textArea, ISegment completionSegment,
                          EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }

    public static readonly Dictionary<char, SimaiCompletionData[]> SIMAI_COMPLETIONS = new();
    static SimaiCompletionData()
    {
        var file = File.ReadAllText(MajEnv.CompletionFile);
        var json = JsonSerializer.Deserialize<Dictionary<char, string[]>>(file);
        if (json == null) return;
        foreach (var pair in json)
            SIMAI_COMPLETIONS.Add(pair.Key, pair.Value.Select(s => new SimaiCompletionData(s)).ToArray());
    }
}

