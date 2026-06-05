using System;
using System.IO;
using System.Linq;

namespace LauncherWinUI.Services;

/// <summary>
/// Oyun veri klasörü: varsayılan olarak EXE'nin bulunduğu dizin.
/// Test için kopyalamadan Steam klasörüne bakmak istersen:
///   - ortam değişkeni: ZH_GAME_DIR
///   - veya EXE yanında tek satırlık game_path.txt
/// </summary>
internal static class GameDirectory
{
    public const string EnvVarName = "ZH_GAME_DIR";
    public const string PathFileName = "game_path.txt";

    /// <summary>Resolved game folder (always returns a path; may lack .big files).</summary>
    public static string Get()
    {
        string? env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            string full = Path.GetFullPath(env.Trim());
            if (Directory.Exists(full)) return full;
        }

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string pathFile = Path.Combine(exeDir, PathFileName);
        if (File.Exists(pathFile))
        {
            string? line = File.ReadLines(pathFile).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (!string.IsNullOrWhiteSpace(line))
            {
                string full = Path.GetFullPath(line.Trim());
                if (Directory.Exists(full)) return full;
            }
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
}
