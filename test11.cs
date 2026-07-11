using MsBox.Avalonia.Dto;
using Avalonia.Controls;
namespace MajdataEdit_Neo.Utils;

class Test11
{
    void F()
    {
        TopLevel t = null;
        var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams());
        box.ShowAsPopupAsync(t);
    }
}
