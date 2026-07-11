using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MajdataEdit_Neo.Utils;

public static class StorageUtils
{
    public static IStorageProvider? GetStorageProvider()
    {
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return mainWindow != null ? TopLevel.GetTopLevel(mainWindow)?.StorageProvider : null;
    }

    public static async Task<IReadOnlyList<IStorageFile>?> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        var provider = GetStorageProvider();
        if (provider == null) return null;
        return await provider.OpenFilePickerAsync(options);
    }
}
