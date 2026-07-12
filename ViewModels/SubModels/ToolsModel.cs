using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MajdataEdit_Neo.Assets.Langs;
using MajdataEdit_Neo.Utils;
using MajdataEdit_Neo.ViewModels;
using MsBox.Avalonia.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Types;
using static MajdataEdit_Neo.Utils.FFmpegChecker;
using Avalonia.Platform.Storage;
using MajdataEdit_Neo.Models.TrackUtils;
using MajdataEdit_Neo.Types;

namespace MajdataEdit_Neo.ViewModels.SubModels;

public partial class ToolsModel(
        MainWindowViewModel _mainWindow,
        FileSessionModel _session,
        IMutableDocument _doc
    ) : ViewModelBase
{
    [ObservableProperty]
    public partial int MediaQuickProcessBeatsCount { get; set; } = 4;

    [ObservableProperty]
    public partial bool MediaQuickProcessFreezeFrame { get; set; } = false;

    private static readonly string[] VideoFilter = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.flv", "*.wmv"];
    private static readonly string[] AllFilter = ["*.*"];

    public async Task CompressBgVideoAsync()
    {
        var maidataDir = _session.MaidataDir;
        var bgVideoPath = Path.Combine(maidataDir, "bg.mp4");
        if (!File.Exists(bgVideoPath))
        {
            bgVideoPath = Path.Combine(maidataDir, "pv.mp4");
        }
        if (!File.Exists(bgVideoPath))
        {
            await MessageBox.ShowWindowDialogAsync(Langs.Status_NoBgVideo, "Error", icon: MsBox.Avalonia.Enums.Icon.Error);
            return;
        }

        if (!await EnsureFFmpeg()) return;

        var videoBaseName = Path.GetFileNameWithoutExtension(bgVideoPath);
        var outputPath = Path.Combine(maidataDir, $"{videoBaseName}_compressed.mp4");

        _mainWindow.ShowStatusMessage(Langs.Status_Compressing);

        try
        {
            var success = await Task.Run(() => RunFfmpegCompress("ffmpeg", bgVideoPath, outputPath));

            if (success && File.Exists(outputPath))
            {
                var backupPath = Path.Combine(maidataDir, $"{videoBaseName}_original.mp4");
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(bgVideoPath, backupPath);
                File.Move(outputPath, bgVideoPath);

                var originalSize = new FileInfo(backupPath).Length / 1024.0 / 1024.0;
                var newSize = new FileInfo(bgVideoPath).Length / 1024.0 / 1024.0;

                await MessageBox.ShowWindowDialogAsync(
                    $"{Langs.Status_CompressComplete}\n{originalSize:F2}MB �?{newSize:F2}MB",
                    "Success", icon: MsBox.Avalonia.Enums.Icon.Success);
                await _session.ReloadFile();
            }
            else
            {
                await MessageBox.ShowWindowDialogAsync(Langs.Status_CompressFailed, "Error", icon: MsBox.Avalonia.Enums.Icon.Error);
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowWindowDialogAsync($"Error: {ex.Message}", "Error", icon: MsBox.Avalonia.Enums.Icon.Error);
        }
        finally
        {
            _mainWindow.ResetStatusMessage();
        }
    }

    public async Task MediaQuickProcessAsync()
    {
        try
        {
            if (_doc.CurrentChartData is null || _doc.CurrentChartData.CommaTimings.Length == 0)
            {
                await MessageBox.ShowWindowDialogAsync(
                    Langs.Msg_NoBpmInChart,
                    Langs.Gui_Error,
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                return;
            }
            var firstTiming = _doc.CurrentChartData.CommaTimings[0];
            var bpm = firstTiming.Bpm; var offset = _doc.Offset;
            var beatsCount = MediaQuickProcessBeatsCount; var freezeFrame = MediaQuickProcessFreezeFrame;
            if (!await EnsureFFmpeg()) return;
            var maidataDir = _session.MaidataDir;
            var audioPath = Path.Combine(maidataDir, "track.mp3");
            if (!File.Exists(audioPath)) audioPath = Path.Combine(maidataDir, "track.ogg");
            _mainWindow.ShowStatusMessage(Langs.Status_Processing);
            await Task.Run(() =>
            {
                TrackProcessor.AdjustMediaTime("ffmpeg", audioPath, 60.0 / bpm * beatsCount, offset);
                string? videoPath = null;
                foreach (var name in new[] { "pv.mp4", "mv.mp4", "bg.mp4" })
                {
                    var dir = Path.Combine(maidataDir, name);
                    if (File.Exists(dir))
                    {
                        videoPath = dir;
                        break;
                    }
                }
                if (videoPath != null) TrackProcessor.AdjustMediaTime("ffmpeg", videoPath, 60.0 / bpm * beatsCount, offset, freezeFrame);
            });
            _doc.Offset = 0;
            await _session.SaveFile();
            await Task.Delay(30);
            await _session.ReloadFile();
            _mainWindow.ResetStatusMessage();
            await MessageBox.ShowWindowDialogAsync(Langs.Msg_MediaProcessComplete, Langs.Gui_Success, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Success);
        }
        catch (Exception ex)
        {
            _mainWindow.ResetStatusMessage();
            await MessageBox.ShowWindowDialogAsync(string.Format(Langs.Msg_MediaProcessFailed, ex.Message), Langs.Gui_Error, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    public async Task NewChartFromVideoAsync()
    {
        try
        {
            var files = await StorageUtils.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Video File",
                FileTypeFilter = [
                    new FilePickerFileType("Video Files") { Patterns = VideoFilter },
                    new FilePickerFileType("All Files") { Patterns = AllFilter }
                ],
                AllowMultiple = false
            });
            if (files == null || files.Count == 0) return;
            var file = files[0].TryGetLocalPath();
            if (file is null) return;
            var parent = Path.GetDirectoryName(file)!;
            var newFile = Path.Combine(parent, "pv.mp4");
            if (file != newFile)
            {
                if (File.Exists(newFile))
                    File.Delete(newFile); File.Move(file, newFile);
            }
            if (!await EnsureFFmpeg()) return;
            _mainWindow.ShowStatusMessage(Langs.Status_ExtractingAudio);
            var audioPath = Path.Combine(parent, "track.mp3");
            await Task.Run(() => TrackProcessor.ExtractAudio("ffmpeg", newFile, audioPath));
            _mainWindow.ResetStatusMessage();
            await _session.NewChartFromDir(parent);
            _mainWindow.OpenChartInfoWindow();
        }
        catch (Exception ex)
        {
            _mainWindow.ResetStatusMessage();
            await MessageBox.ShowWindowDialogAsync(string.Format(Langs.Msg_ExtractAudioFailed, ex.Message), Langs.Gui_Error, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    private static bool RunFfmpegCompress(string ffmpegPath, string inputPath, string outputPath)
    {
        try
        {
            var args = $"-y -i \"{inputPath}\" -vf \"scale=-2:540,fps=30\" -c:v libx264 -preset veryfast -b:v 540k -an \"{outputPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

