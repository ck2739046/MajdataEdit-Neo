using MajdataEdit_Neo.Types;
using MajdataEdit_Neo.Types.Plugin;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MajdataEdit_Neo.ViewModels;

/// <summary>
/// 插件注册与菜单项
/// </summary>
public partial class MainWindowViewModel
{
    public ObservableCollection<object> PluginItems { get; } = new();

    public void Register<T>() where T : IMajPlugin, new()
    {
        if (PluginItems.Count > 0)
        {
            PluginItems.Add(new PluginMenuSeparator());
        }

        var plugin = new T();
        foreach (var action in plugin.GetActions())
        {
            PluginItems.Add(action);
        }
    }

    private void RegisterAll()
    {
        var pluginTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IMajPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        foreach (var type in pluginTypes)
        {
            var method = this.GetType().GetMethod(nameof(Register))!.MakeGenericMethod(type);
            method.Invoke(this, null);
        }
    }

    public void InitializePlugins() => RegisterAll();
}
