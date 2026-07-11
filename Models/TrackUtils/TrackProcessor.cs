using System;
using System.Diagnostics;
using System.IO;

namespace Models.TrackUtils;

public static class TrackProcessor
{
    public static void ExtractAudio(string ffmpegPath, string videoPath, string audioOutputPath)
    {
        if (!File.Exists(videoPath)) return;

        var args = $"-y -i \"{videoPath}\" -vn -ar 44100 -acodec libmp3lame -q:a 2 \"{audioOutputPath}\"";
        RunFFmpeg(ffmpegPath, args);
    }

    public static void AdjustMediaTime(string ffmpegPath, string filePath, double targetTime, double offset, bool clone = false)
    {
        if (!File.Exists(filePath)) return;

        var diff = targetTime - offset;
        if (Math.Abs(diff) < 0.01) return;

        var ext = Path.GetExtension(filePath).ToLower();
        var isAudio = ext == ".mp3" || ext == ".wav" || ext == ".ogg" || ext == ".flac";
        var audioCodec = (ext == ".mp3") ? "libmp3lame" : "aac";

        var tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"t_{Guid.NewGuid()}{ext}");
        string args;

        if (diff < 0)
        {
            var cut = Math.Abs(diff);
            if (isAudio)
                args = $"-y -i \"{filePath}\" -ss {cut} -c:a {audioCodec} \"{tempPath}\"";
            else
                args = $"-y -i \"{filePath}\" -ss {cut} -c:v libx264 -c:a {audioCodec} -preset superfast \"{tempPath}\"";
        }
        else
        {
            var delayMs = diff * 1000;
            if (isAudio)
            {
                args = $"-y -i \"{filePath}\" -af \"adelay={delayMs}:all=1\" -c:a {audioCodec} \"{tempPath}\"";
            }
            else
            {
                if (clone)
                {
                    args =
                        $"-y -i \"{filePath}\" " +
                        $"-vf \"tpad=start_duration={diff}:start_mode=clone\" " +
                        $"-an " +
                        $"-c:v libx264 -preset superfast " +
                        $"\"{tempPath}\"";
                }
                else
                {
                    args =
                        $"-y -i \"{filePath}\" " +
                        $"-vf \"tpad=start_duration={diff}:start_mode=add\" " +
                        $"-an " +
                        $"-c:v libx264 -preset superfast " +
                        $"\"{tempPath}\"";
                }
            }
        }

        try
        {
            RunFFmpeg(ffmpegPath, args);
            if (File.Exists(tempPath))
            {
                var rawPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"raw{ext}");
                if (File.Exists(rawPath))
                    File.Delete(rawPath);
                File.Move(filePath, rawPath);
                File.Move(tempPath, filePath);
            }
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    static void RunFFmpeg(string ffmpegPath, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new Exception($"FFmpeg Error: {error}");
    }
}