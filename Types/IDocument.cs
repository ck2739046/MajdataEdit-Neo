using AvaloniaEdit.Document;
using MajSimai;
using System.Collections.Generic;
using System.Threading.Tasks;
using MajdataEdit_Neo.Types.SimaiAnalyzer;

namespace MajdataEdit_Neo.Types;

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
}

public interface IMutableDocument : IReadOnlyDocument
{
    Task SetFumenContent(string content);
    void RefreshFumenDocument();
    new float Offset { get; set; }
    new string Level { get; set; }
    new string Designer { get; set; }
}
