/*
using DiscordRPC;
using System;

namespace MajdataEdit_Neo.ViewModels;

/// <summary>
/// Discord RPC 状态展示
/// </summary>
public partial class MainWindowViewModel
{
    readonly DiscordRpcClient _drpcClient = new("1068882546932326481");
    readonly RichPresence _presence = new()
    {
        Details = "Nothing to do",
        State = "",
        Assets = new()
        {
            LargeImageKey = "salt",
            LargeImageText = "Majdata",
            SmallImageKey = "None"
        }
    };

    private void InitializeDiscordRpc()
    {
        _drpcClient.SetPresence(_presence);
    }

    public void UpdatePresence(string? details = null, string? state = null)
    {
        if (details != null) _presence.Details = details;
        if (state != null) _presence.State = state;
        _drpcClient.SetPresence(_presence);
    }

    private void DisposeDiscordRpc()
    {
        _drpcClient.Dispose();
    }
}
*/
