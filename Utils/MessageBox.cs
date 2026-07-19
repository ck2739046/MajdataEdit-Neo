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
                                                  object? context,
                                                  WindowStartupLocation startupLocation)
    {
        return MessageBoxManager.GetMessageBoxStandard(title, content, button, icon, context, startupLocation);
    }
    static IMsBox<ButtonResult> GetStandardMsgBoxInternal(MessageBoxStandardParams msgBoxParams)
    {
        return MessageBoxManager.GetMessageBoxStandard(msgBoxParams);
    }
    internal static async Task<ButtonResult> ShowAsync(string content,
                                                       string title = "Message",
                                                       ButtonEnum button = ButtonEnum.Ok,
                                                       Icon icon = Icon.None,
                                                       object? context = null,
                                                       WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowAsync();
    }
    internal static async Task<ButtonResult> ShowAsync(MessageBoxStandardParams msgBoxParams)
    {
        return await GetStandardMsgBoxInternal(msgBoxParams).ShowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowAsync(string content,
                                                             string title = "Message",
                                                             ButtonEnum button = ButtonEnum.Ok,
                                                             Icon icon = Icon.None,
                                                             object? context = null,
                                                             WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowWindowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowAsync(MessageBoxStandardParams msgBoxParams)
    {
        return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
    }
    internal static async Task<ButtonResult> ShowWindowDialogAsync(string content,
                                                                   string title = "Message",
                                                                   ButtonEnum button = ButtonEnum.Ok,
                                                                   Icon icon = Icon.None,
                                                                   object? context = null,
                                                                   WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen,
                                                                   Window? owner = null)
    {
        var window = owner ?? MainWindow;
        if (window is null)
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowWindowDialogAsync(window);
        }
    }

    internal static async Task<ButtonResult> ShowWindowDialogAsync(MessageBoxStandardParams msgBoxParams, Window? owner = null)
    {
        var window = owner ?? MainWindow;
        if (window is null)
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowDialogAsync(window);
        }
    }

    internal static async Task<ButtonResult> ShowWindowDialogAsync(string content,
                                                                   Window owner,
                                                                   string title = "Message",
                                                                   ButtonEnum button = ButtonEnum.Ok,
                                                                   Icon icon = Icon.None,
                                                                   object? context = null,
                                                                   WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowWindowDialogAsync(owner);
    }
    internal static async Task<ButtonResult> ShowAsPopupAsync(string content,
                                                              string title = "Message",
                                                              ButtonEnum button = ButtonEnum.Ok,
                                                              Icon icon = Icon.None,
                                                              object? context = null,
                                                              WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen,
                                                              ContentControl? owner = null)
    {
        var control = owner ?? MainWindow;
        if (control is null)
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowAsPopupAsync(control);
        }
    }

    internal static async Task<ButtonResult> ShowAsPopupAsync(MessageBoxStandardParams msgBoxParams, ContentControl? owner = null)
    {
        var control = owner ?? MainWindow;
        if (control is null)
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowWindowAsync();
        }
        else
        {
            return await GetStandardMsgBoxInternal(msgBoxParams).ShowAsPopupAsync(control);
        }
    }

    internal static async Task<ButtonResult> ShowAsPopupAsync(string content,
                                                              ContentControl owner,
                                                              string title = "Message",
                                                              ButtonEnum button = ButtonEnum.Ok,
                                                              Icon icon = Icon.None,
                                                              object? context = null,
                                                              WindowStartupLocation startupLocation = WindowStartupLocation.CenterScreen)
    {
        return await GetStandardMsgBoxInternal(content, title, button, icon, context, startupLocation).ShowAsPopupAsync(owner);
    }
}
