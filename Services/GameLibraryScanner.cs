using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace GameOptimizer.Services;

public static class GameLibraryScanner
{
    public static List<string> ScanAll()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ScanSteam())   paths.Add(p);
        foreach (var p in ScanEpic())    paths.Add(p);
        foreach (var p in ScanGog())     paths.Add(p);
        foreach (var p in ScanUbisoft()) paths.Add(p);
        foreach (var p in ScanEa())      paths.Add(p);
        return [.. paths.Where(Directory.Exists).OrderBy(x => x)];
    }

    public static IEnumerable<string> ScanSteam()
    {
        var steamPath = GetRegValue(@"SOFTWARE\Valve\Steam", "SteamPath")
                     ?? GetRegValue(@"SOFTWARE\WOW6432Node\Valve\Steam", "SteamPath");
        if (steamPath is null) yield break;

        steamPath = steamPath.Replace('/', '\\');
        var defaultLib = Path.Combine(steamPath, "steamapps", "common");
        if (Directory.Exists(defaultLib)) yield return defaultLib;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        foreach (var line in File.ReadLines(vdf))
        {
            var t = line.Trim();
            if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = t.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var lib = Path.Combine(parts[1].Replace(@"\\", @"\"), "steamapps", "common");
                if (Directory.Exists(lib)) yield return lib;
            }
        }
    }

    public static IEnumerable<string> ScanEpic()
    {
        var manifestsDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestsDir)) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            string json;
            try { json = File.ReadAllText(item); } catch { continue; }
            var m = Regex.Match(json, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success) continue;
            var installPath = m.Groups[1].Value.Replace(@"\\", @"\");
            var parent = Path.GetDirectoryName(installPath);
            if (parent is not null && seen.Add(parent)) yield return parent;
        }
    }

    public static IEnumerable<string> ScanGog()
    {
        string[] keys = [@"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games"];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var sk = key.OpenSubKey(sub);
                var path = sk?.GetValue("path") as string ?? sk?.GetValue("exe") as string;
                if (path is null) continue;
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (dir is null) continue;
                var parent = Path.GetDirectoryName(dir) ?? dir;
                if (seen.Add(parent)) yield return parent;
            }
        }
    }

    public static IEnumerable<string> ScanUbisoft()
    {
        string[] keys =
        [
            @"SOFTWARE\Ubisoft\Launcher\Installs",
            @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs"
        ];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var sk = key.OpenSubKey(sub);
                var dir = sk?.GetValue("InstallDir") as string;
                if (dir is null) continue;
                dir = dir.TrimEnd('\\', '/');
                var parent = Path.GetDirectoryName(dir) ?? dir;
                if (Directory.Exists(dir) && seen.Add(parent)) yield return parent;
            }
        }
    }

    public static IEnumerable<string> ScanEa()
    {
        string[] keys =
        [
            @"SOFTWARE\Electronic Arts",
            @"SOFTWARE\WOW6432Node\Electronic Arts"
        ];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var sk = key.OpenSubKey(sub);
                var dir = (sk?.GetValue("Install Dir") ?? sk?.GetValue("InstallDir")) as string;
                if (dir is null) continue;
                dir = dir.TrimEnd('\\', '/');
                var parent = Path.GetDirectoryName(dir) ?? dir;
                if (Directory.Exists(dir) && seen.Add(parent)) yield return parent;
            }
        }

        foreach (var defaultDir in new[]
        {
            @"C:\Program Files\EA Games",
            @"C:\Program Files (x86)\EA Games",
        })
        {
            if (Directory.Exists(defaultDir) && seen.Add(defaultDir))
                yield return defaultDir;
        }
    }

    private static string? GetRegValue(string keyPath, string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(keyPath)
                       ?? Registry.CurrentUser.OpenSubKey(keyPath);
            return k?.GetValue(name) as string;
        }
        catch { return null; }
    }
}
