using Semver;
using System;
using System.IO;
using System.Reflection;

namespace MajdataEdit_Neo.Base;

public static class MajEnv
{
    private const string ViewCompanyName = "bbben";
    private const string ViewProductName = "MajdataViewX";

    public static string MajBase => AppDomain.CurrentDomain.BaseDirectory;
    public static string GetPath(string relativePath) => Path.Combine(MajBase, relativePath);

    public static string MajdataViewPersistentDataPath
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                var appData = Directory.GetParent(localAppData)?.FullName
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(
                    appData,
                    "LocalLow",
                    ViewCompanyName,
                    ViewProductName);
            }

            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    ViewCompanyName,
                    ViewProductName);
            }

            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                configHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config");
            }
            return Path.Combine(
                configHome,
                "unity3d",
                ViewCompanyName,
                ViewProductName);
        }
    }

    public static string MmfAudioTimePath =>
        Path.Combine(MajdataViewPersistentDataPath, "majdata_time.dat");
    public const long MmfChartDataCapacity = 64 * 1024 * 1024; //64mb
    public static string MmfChartDataPath =>
        Path.Combine(MajdataViewPersistentDataPath, "majdata_chart.dat");

    public static string MajdataViewBassDllFile
    {
        get
        {
#if DEBUG
            if (OperatingSystem.IsWindows())
            {
                return GetPath("..\\..\\..\\runtimes\\win-x64\\native\\bass.dll");
            }
            else if (OperatingSystem.IsMacOS())
            {
                return GetPath("..\\..\\..\\runtimes\\osx\\native\\libbass.dylib");
            }
            else if (OperatingSystem.IsLinux())
            {
                return GetPath("..\\..\\..\\runtimes\\linux-x64\\native\\libbass.so");
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform for MajdataViewBassDllFile.");
            }
#else
            if (OperatingSystem.IsWindows())
            {
                return GetPath("MajdataViewX_Data\\Plugins\\x86_64\\bass.dll");
            }
            else if (OperatingSystem.IsMacOS())
            {
                return GetPath("MajdataViewX_Data/Plugins/x86_64/libbass.dylib");
            }
            else if (OperatingSystem.IsLinux())
            {
                return GetPath("MajdataViewX_Data/Plugins/x86_64/libbass.so");
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform for MajdataViewBassDllFile.");
            }
#endif
        }
    }

    public static string SettingsFile => GetPath("Settings.json");
    public static string CrashFile => GetPath("crash.log");
    public static string DatabaseFile => GetPath("editor.db");
    public static string CompletionFile => GetPath("completions.json");

    public static readonly string MAJDATA_VERSION_STRING = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    public static readonly SemVersion MAJDATA_VERSION = SemVersion.Parse(MAJDATA_VERSION_STRING, SemVersionStyles.Any);
}
