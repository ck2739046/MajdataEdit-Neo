using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataEdit_Neo.Types.Plugin;

public class PluginAction : IPluginItem
{
    public required string Name { get; set; }
    public string? IconKey { get; set; }
    public required Func<string, string> Transform { get; set; }
}

public class MenuSeparator : IPluginItem
{
    public string Name => "---";
}