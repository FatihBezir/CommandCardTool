using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LauncherWinUI.Services;

/// <summary>
/// Zero Hour install folder. Auto-detects Steam paths; optional override via ZH_GAME_DIR env var.
/// </summary>
internal static class GameDirectory
{
    public const string EnvVarName = "ZH_GAME_DIR";
    private const string GameFolderName = "Command & Conquer Generals - Zero Hour";

    public static string Get()
    {
        string? env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            string full = Path.GetFullPath(env.Trim());
            if (Directory.Exists(full)) return full;
        }

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        if (LooksLikeGameDir(exeDir)) return exeDir;

        foreach (string candidate in DiscoverInstallCandidates())
        {
            if (LooksLikeGameDir(candidate)) return candidate;
        }

        return exeDir;
    }

    public static bool HasBigArchives(string? dir = null)
    {
        dir ??= Get();
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.big").Any();
    }

    public static string Combine(params string[] paths)
        => Path.Combine(Get(), Path.Combine(paths));

    private static bool LooksLikeGameDir(string dir)
        => Directory.Exists(dir)
        && File.Exists(Path.Combine(dir, "INIZH.big"))
        && File.Exists(Path.Combine(dir, "EnglishZH.big"));

    private static IEnumerable<string> DiscoverInstallCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Track(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path.Trim());
                if (seen.Add(full)) list.Add(full);
            }
            catch { /* ignore bad paths */ }
        }

        Track(@"d:\SteamLibrary\steamapps\common\" + GameFolderName);
        Track(@"c:\SteamLibrary\steamapps\common\" + GameFolderName);
        Track(@"e:\SteamLibrary\steamapps\common\" + GameFolderName);

        foreach (string steamRoot in GetSteamRoots())
        {
            Track(Path.Combine(steamRoot, "steamapps", "common", GameFolderName));
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                string text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    Track(Path.Combine(lib, "steamapps", "common", GameFolderName));
                }
            }
            catch { /* skip unreadable vdf */ }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        Track(Path.Combine(programFiles, "Steam", "steamapps", "common", GameFolderName));
        Track(Path.Combine(programFiles, "EA Games", GameFolderName));

        return list;
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(@"Software\Valve\Steam");
                string? steamPath = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(steamPath))
                    roots.Add(steamPath.Replace('/', '\\'));
            }
        }
        catch { /* registry unavailable */ }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return roots;
    }
}
