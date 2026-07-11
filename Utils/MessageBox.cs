using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace MajdataEdit_Neo.Utils;

internal static class MessageBox
{
    public static Window? MainWindow
    {
        get
        {
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
    }

    static IMsBox<ButtonResult> GetStandardMsgBoxInternal(string content,
                                                  string title,
                                                  ButtonEnum button,
                                                  Icon icon,
                                                  WindowStartupLocation startupLocation)
    {
        return MessageBoxManager.GetMessageBoxStandard(title, content, button, icon, startupLocation);
    }
    static IMsBox<ButtonResult> GetStandardMsgBoxInternal(MessageBoxStandardParams msgBoxParams)
    {
        return MessageBoxManager.GetMessageBoxStandard(msgBoxParams);
    }
    internal static async Task<ButtonResult> ShowAsync(string content,
                                                       string title = "Message",
                                                       ButtonEnum button = ButtonEnum.Ok,
                                                       Icon icon = Icon.None,
                                                       WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowAsync();
    }
    internal static async Task<ButtonResult> ShowAsync(MessageBoxStandardParams msgBoxParams)
    {
        return await GetStandardMsgBoxInternal(msgBoxParams).ShowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowAsync(string content,
                                                             string title = "Message",
                                                             ButtonEnum button = ButtonEnum.Ok,
                                                             Icon icon = Icon.None,
                                                             WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowWindowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowAsync(MessageBoxStandardParams msgBoxParams)
    {
        return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowDialogAsync(string content,
                                                                   string title = "Message",
                                                                   ButtonEnum button = ButtonEnum.Ok,
                                                                   Icon icon = Icon.None,
                                                                   WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen,
                                                                   Window? context = null)
    {
        var win = context ?? MainWindow;
        if (win is null)
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowWindowDialogAsync(win);
        }
    }
    internal static async Task<ButtonResult> ShowWindowDialogAsync(MessageBoxStandardParams msgBoxParams, Window? context = null)
    {
        var win = context ?? MainWindow;
        if (win is null)
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowDialogAsync(win);
        }
    }
    internal static async Task<ButtonResult> ShowWindowDialogAsync(string content,
                                                                   Window owner,
                                                                   string title = "Message",
                                                                   ButtonEnum button = ButtonEnum.Ok,
                                                                   Icon icon = Icon.None,
                                                                   WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowWindowDialogAsync(owner);
    }
    internal static async Task<ButtonResult> ShowAsPopupAsync(string content,
                                                              string title = "Message",
                                                              ButtonEnum button = ButtonEnum.Ok,
                                                              Icon icon = Icon.None,
                                                              WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen,
                                                              ContentControl? context = null)
    {
        var win = context ?? MainWindow;
        if (win is null)
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowAsPopupAsync(win);
        }
    }
    internal static async Task<ButtonResult> ShowAsPopupAsync(MessageBoxStandardParams msgBoxParams, ContentControl? context = null)
    {
        var win = context ?? MainWindow;
        if (win is null)
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowAsPopupAsync(win);
        }
    }
    internal static async Task<ButtonResult> ShowAsPopupAsync(string content,
                                                              ContentControl owner,
                                                              string title = "Message",
                                                              ButtonEnum button = ButtonEnum.Ok,
                                                              Icon icon = Icon.None,
                                                              WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, startupLocation).ShowAsPopupAsync(owner);
    }
}
