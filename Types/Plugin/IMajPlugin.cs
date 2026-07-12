using System;
using System.Collections.Generic;

namespace MajdataEdit_Neo.Types.Plugin;

public interface IMajPlugin
{
    string Name { get; }
    IEnumerable<PluginAction> GetActions();
}

