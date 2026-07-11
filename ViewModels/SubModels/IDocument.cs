using AvaloniaEdit.Document;
using MajSimai;
using System.Collections.Generic;
using System.Threading.Tasks;
using MajdataEdit_Neo.Types.SimaiAnalyzer;

namespace MajdataEdit_Neo.ViewModels;

public interface IReadOnlyDocument
{
    SimaiFile? CurrentSimaiFile { get; }
    int SelectedDifficulty { get; }
    string CurrentFumen { get; }
    IReadOnlyList<SimaiDiagnostic> SimaiDiagnostics { get; }
    List<(double, int, int)> Signatures { get; }
    bool IsLoaded { get; }
    float Offset { get; }
    SimaiChart CurrentChartData { get; }
    string Level { get; }
    string Designer { get; }
    string OriginFumen { get; }
    bool IsFumenContextChanged { get; }
    int CaretLine { get; }
    int CaretCombo { get; }
}

public interface IMutableDocument : IReadOnlyDocument
{
    Task SetFumenContent(string content);
    void RefreshFumenDocument();
    void SetCaretInfo(int rawPosition);
    new float Offset { get; set; }
    new string Level { get; set; }
    new string Designer { get; set; }
}
