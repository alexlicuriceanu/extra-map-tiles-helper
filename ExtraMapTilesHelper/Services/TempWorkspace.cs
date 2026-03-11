using System;
using System.IO;

namespace ExtraMapTilesHelper.Services;

public static class TempWorkspace
{
    public static string RootPath { get; private set; } = string.Empty;

    public static void Initialize()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "extramaptileshelper_temp");
        Cleanup(); // Wipe any leftovers from a previous crash
        Directory.CreateDirectory(RootPath);
    }

    public static string GetDictionaryFolder(string dictionaryName)
    {
        string path = Path.Combine(RootPath, dictionaryName);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    public static void Cleanup()
    {
        if (Directory.Exists(RootPath))
        {
            try { Directory.Delete(RootPath, true); } catch { /* Ignore locked files */ }
        }
    }
}