using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CustomRadioPanel.Core.Sim;

/// <summary>
/// Finds the native <c>SimConnect.dll</c> at runtime so the app can ship without redistributing it.
/// SimConnect.dll is a proprietary Microsoft library that installs with MSFS; we locate the copy the
/// user already has (env override, app folder, Steam library, or MSFS SDK).
/// </summary>
public static class SimConnectLocator
{
    private const string FileName = "SimConnect.dll";

    /// <summary>Returns the full path to a usable SimConnect.dll, or null if none was found.</summary>
    public static string? Find()
    {
        foreach (var path in Candidates())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
            catch
            {
                // ignore malformed paths / access errors and keep looking
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        // 1) Explicit override: SIMCONNECT_DLL may be a full file path or a directory.
        var env = Environment.GetEnvironmentVariable("SIMCONNECT_DLL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
            yield return Path.Combine(env, FileName);
        }

        // 2) Next to the executable (also where a dev-time libs copy lands).
        yield return Path.Combine(AppContext.BaseDirectory, FileName);

        // 3) Steam installs of MSFS.
        foreach (var lib in SteamLibraryRoots())
            yield return Path.Combine(lib, "steamapps", "common", "MicrosoftFlightSimulator", FileName);

        // 4) MSFS SDK default location.
        yield return @"C:\MSFS SDK\SimConnect SDK\lib\" + FileName;
    }

    private static IEnumerable<string> SteamLibraryRoots()
    {
        string? steam = ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                        ?? ReadRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(steam))
            yield break;

        steam = steam.Replace('/', '\\');
        yield return steam; // the base Steam folder is itself a library

        // Parse libraryfolders.vdf for additional library roots.
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        string? text = null;
        try { if (File.Exists(vdf)) text = File.ReadAllText(vdf); }
        catch { /* ignore */ }

        if (text is null)
            yield break;

        foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace("\\\\", "\\");
    }

    private static string? ReadRegistry(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
