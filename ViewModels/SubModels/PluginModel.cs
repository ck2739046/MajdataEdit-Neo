using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.Plugin;
using System.Collections.ObjectModel;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public class PluginModel()
{
    public ObservableCollection<IPluginItem> PluginItems { get; } = new();

    public void Register<T>() where T : IMajPlugin, new()
    {
        if (PluginItems.Count > 0)
        {
            PluginItems.Add(new MenuSeparator());
        }

        var plugin = new T();
        foreach (var action in plugin.GetActions())
        {
            PluginItems.Add(action);
        }
    }
}
