using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Modules.AutoSave;
using MajdataEdit_Neo.ViewModels.SubModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Types;

namespace MajdataEdit_Neo.ViewModels;

public sealed partial class RecoverViewModel : ViewModelBase
{
    private static readonly string[] DifficultyNames =
    [
        "EASY",
        "BASIC",
        "ADVANCED",
        "EXPERT",
        "MASTER",
        "Re:MASTER",
        "ORIGINAL"
    ];

    private readonly AutoSaveModel _autoSave;
    private RecoverAutoSaveItem? _selectedAutoSave;
    private string _content = string.Empty;
    private IReadOnlyList<RecoverDifficultyItem> _difficulties = [];

    public IReadOnlyList<RecoverAutoSaveItem> AutoSaves { get; }

    public RecoverAutoSaveItem? SelectedAutoSave
    {
        get => _selectedAutoSave;
        set
        {
            if (!SetProperty(ref _selectedAutoSave, value))
                return;

            Content = value?.Content ?? string.Empty;
            Difficulties = FindDifficulties(Content);
            OnPropertyChanged(nameof(HasAutoSave));
            OnPropertyChanged(nameof(CanLoadSelectedChart));
        }
    }

    public string Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public IReadOnlyList<RecoverDifficultyItem> Difficulties
    {
        get => _difficulties;
        private set => SetProperty(ref _difficulties, value);
    }

    public bool HasAutoSave => SelectedAutoSave is not null;
    public bool CanLoadSelectedChart =>
        SelectedAutoSave is not null && File.Exists(SelectedAutoSave.MaidataPath);
    public string? RecoveredMaidataPath => SelectedAutoSave?.MaidataPath;

    private RecoverViewModel(
        AutoSaveModel autoSave,
        IReadOnlyList<RecoverAutoSaveItem> autoSaves)
    {
        _autoSave = autoSave;
        AutoSaves = autoSaves;
        SelectedAutoSave = autoSaves.FirstOrDefault();
    }

    public static async Task<RecoverViewModel> CreateAsync(
        AutoSaveModel autoSave,
        string? maidataDirectory)
    {
        var candidates = new List<(AutoSaveFileInfo File, RecoverAutoSaveSource Source)>();
        candidates.AddRange(autoSave.GetGlobalAutoSaves()
            .Select(file => (file, RecoverAutoSaveSource.Global)));

        if (!string.IsNullOrWhiteSpace(maidataDirectory))
        {
            candidates.AddRange(autoSave.GetLocalAutoSaves()
                .Select(file => (file, RecoverAutoSaveSource.Local)));
        }

        var autoSaves = new List<RecoverAutoSaveItem>(candidates.Count);
        foreach (var candidate in candidates.OrderByDescending(item => item.File.SavedTime))
        {
            if (string.IsNullOrWhiteSpace(candidate.File.FileName) ||
                !File.Exists(candidate.File.FileName) ||
                !TryResolveMaidataPath(candidate.File, out var maidataPath))
                continue;

            try
            {
                var content = await File.ReadAllTextAsync(candidate.File.FileName);
                autoSaves.Add(CreateAutoSaveItem(
                    candidate.File,
                    candidate.Source,
                    maidataPath,
                    content));
            }
            catch (IOException)
            {
                // The autosave may have been rotated out while the window was loading.
            }
        }

        return new RecoverViewModel(autoSave, autoSaves);
    }

    public bool Recover()
    {
        return SelectedAutoSave is not null &&
               _autoSave.RecoverFile(SelectedAutoSave.FileInfo);
    }

    private static RecoverAutoSaveItem CreateAutoSaveItem(
        AutoSaveFileInfo file,
        RecoverAutoSaveSource source,
        string maidataPath,
        string content)
    {
        var titleMatch = TitleRegex().Match(content);
        var chartName = titleMatch.Success
            ? titleMatch.Groups["title"].Value.Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(chartName))
        {
            var directory = Path.GetDirectoryName(maidataPath);
            chartName = string.IsNullOrWhiteSpace(directory)
                ? Path.GetFileNameWithoutExtension(maidataPath)
                : Path.GetFileName(directory);
        }

        var sourceName = source == RecoverAutoSaveSource.Global
            ? Langs.Gui_Global
            : Langs.Gui_Local;
        var savedAt = DateTimeOffset.FromUnixTimeSeconds(file.SavedTime).ToLocalTime();
        if (savedAt > DateTimeOffset.Now.AddMinutes(1))
            savedAt = savedAt.AddHours(-8);
        var savedTime = savedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var displayName = $"[{sourceName}] {chartName} - {savedTime}";

        return new RecoverAutoSaveItem(
            file,
            source,
            maidataPath,
            content,
            displayName);
    }

    private static bool TryResolveMaidataPath(
        AutoSaveFileInfo file,
        out string maidataPath)
    {
        maidataPath = string.Empty;
        if (string.IsNullOrWhiteSpace(file.RawPath))
            return false;

        var rawPath = Path.GetFullPath(file.RawPath);
        maidataPath = Directory.Exists(rawPath)
            ? Path.Combine(rawPath, "maidata.txt")
            : rawPath;
        return !string.IsNullOrWhiteSpace(Path.GetDirectoryName(maidataPath));
    }

    [GeneratedRegex(@"^[\t ]*&title[\t ]*=(?<title>[^\r\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^[\t ]*&inote_(?<index>\d+)[\t ]*=", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex InoteRegex();

    private static List<RecoverDifficultyItem> FindDifficulties(string content)
    {
        var inoteMatches = InoteRegex().Matches(content);

        var results = new List<RecoverDifficultyItem>();
        var seenIndexes = new HashSet<int>();
        foreach (Match match in inoteMatches)
        {
            var index = int.Parse(match.Groups["index"].Value);
            if (!seenIndexes.Add(index))
                continue;

            var levelMatch = Regex.Match(
                content,
                $@"^[\t ]*&lv_{index}[\t ]*=(?<level>[^\r\n]*)",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            var level = levelMatch.Success ? levelMatch.Groups["level"].Value.Trim() : string.Empty;
            var name = index >= 1 && index <= DifficultyNames.Length
                ? DifficultyNames[index - 1]
                : $"DIFFICULTY {index}";
            var displayName = string.IsNullOrWhiteSpace(level) ? name : $"{name}  {level}";
            var lineNumber = content.AsSpan(0, match.Index).Count('\n') + 1;

            results.Add(new RecoverDifficultyItem(index, displayName, lineNumber));
        }

        return results;
    }
}

public enum RecoverAutoSaveSource
{
    Global,
    Local
}

public sealed record RecoverAutoSaveItem(
    AutoSaveFileInfo FileInfo,
    RecoverAutoSaveSource Source,
    string MaidataPath,
    string Content,
    string DisplayName);

public sealed record RecoverDifficultyItem(
    int Index, 
    string DisplayName, 
    int LineNumber);

public enum RecoverDialogAction
{
    Load,
    Recover
}

public sealed record RecoverDialogResult(
    RecoverDialogAction Action,
    string MaidataPath);
