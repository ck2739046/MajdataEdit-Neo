using Semver;
using System;
using System.IO;
using System.Reflection;

namespace MajdataEdit_Neo.Base;

public static class MajEnv
{
    public static string MajBase => AppDomain.CurrentDomain.BaseDirectory;
    public static string GetPath(string relativePath) => Path.Combine(MajBase, relativePath);

    public static string CrashFile => GetPath("crash.log");
    public static string DatabaseFile => GetPath("editor.db");

    public static readonly string MAJDATA_VERSION_STRING = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    public static readonly SemVersion MAJDATA_VERSION = SemVersion.Parse(MAJDATA_VERSION_STRING, SemVersionStyles.Any);
}