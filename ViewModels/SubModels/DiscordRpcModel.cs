using DiscordRPC;

namespace ViewModels.SubModels;

public class DiscordRpcModel
{
    readonly DiscordRpcClient _client = new("1068882546932326481");
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

    public void Initialize()
    {
        _client.SetPresence(_presence);
    }

    public void UpdatePresence(string? details = null, string? state = null)
    {
        if (details != null) _presence.Details = details;
        if (state != null) _presence.State = state;
        _client.SetPresence(_presence);
    }
}
