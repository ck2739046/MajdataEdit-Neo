using CommunityToolkit.Mvvm.ComponentModel;
using MajdataEdit_Neo.Assets.Langs;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using static MajdataEdit_Neo.Base.MajEnv;
using MajdataEdit_Neo.Utils;
using Types;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class UpdateModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    public async Task CheckUpdateAsync(bool onStart = false)
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;

        var response = await RequestGETAsync("http://api.github.com/repos/re-poem/MajdataViewX/releases/latest");

        try
        {
            if (response == "ERROR")
            {
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateRequestFail, Langs.Gui_CheckUpdate);
                return;
            }

            var resJson = JsonConvert.DeserializeObject<JObject>(response)!;

            if (resJson["tag_name"] == null || resJson["html_url"] == null)
            {
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateParseFail, Langs.Gui_CheckUpdate);
                return;
            }

            var latestVersionString = resJson["tag_name"]!.ToString();
            var releaseUrl = resJson["html_url"]!.ToString();

            var latestVersion = SemVersion.Parse(latestVersionString, SemVersionStyles.Any);
            if (latestVersion.ComparePrecedenceTo(MAJDATA_VERSION) > 0)
            {
                var msgboxText = string.Format(Langs.Msg_NewVersionDetected,
                    latestVersionString,
                    MAJDATA_VERSION_STRING);
                if (onStart) msgboxText += "\n\n" + Langs.Msg_DisablingAutoCheckUpdate;

                var result = await MessageBox.ShowWindowDialogAsync(
                    msgboxText,
                    Langs.Gui_CheckUpdate,
                    ButtonEnum.YesNo);

                switch (result)
                {
                    case ButtonResult.Yes:
                        var startInfo = new ProcessStartInfo(releaseUrl)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        break;
                    case ButtonResult.No:
                        break;
                }
            }
            else
            {
                if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_NoNewVersion, Langs.Gui_CheckUpdate);
            }
        }
        catch
        {
            if (!onStart) await MessageBox.ShowWindowDialogAsync(Langs.Msg_CheckUpdateParseFail, Langs.Gui_CheckUpdate);
            return;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public static async Task<string> RequestGETAsync(string url)
    {
        try
        {
            var executingAssembly = Assembly.GetExecutingAssembly();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", $"{executingAssembly.GetName().Name!} / {executingAssembly.GetName().Version!.ToString(3)}");
            var response = await new HttpClient().SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return "ERROR";
        }
    }
}
