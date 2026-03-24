using System;
using System.IO;

namespace ExtraMapTilesHelper.Services;

public static class TempWorkspace
{
    public static string RootPath { get; private set; } = string.Empty;

    public static void Initialize()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "ExtraMapTilesHelper");
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
            try
            {
                Directory.Delete(RootPath, true);
            }
            catch (Exception ex)
            {
                /* Ignore locked files */
                System.Diagnostics.Debug.WriteLine($"Error in Cleanup: {ex.Message}");
            }
        }
    }
}