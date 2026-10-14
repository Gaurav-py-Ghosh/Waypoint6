using System.IO;
using UnityEngine;

/// <summary>
/// Static utility to save, load, and check paths recorded for campus destinations.
/// Persists data as JSON files in Application.persistentDataPath and backs up to PlayerPrefs.
/// </summary>
public static class PathStorageManager
{
    private const string FOLDER_NAME = "CampusPaths";
    private const string PREFS_PREFIX = "BMU_PATH_";

    private static string GetDirectoryPath()
    {
        string dir = Path.Combine(Application.persistentDataPath, FOLDER_NAME);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static string GetFilePath(string destinationName)
    {
        string safeName = SanitizeFileName(destinationName);
        return Path.Combine(GetDirectoryPath(), safeName + ".json");
    }

    private static string SanitizeFileName(string name)
    {
        string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        foreach (char c in invalid)
        {
            name = name.Replace(c.ToString(), "");
        }
        return name.Trim().Replace(" ", "_");
    }

    /// <summary>
    /// Checks if a recorded path already exists for this destination.
    /// </summary>
    public static bool HasPath(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName)) return false;

        string path = GetFilePath(destinationName);
        if (File.Exists(path)) return true;

        return PlayerPrefs.HasKey(PREFS_PREFIX + SanitizeFileName(destinationName));
    }

    /// <summary>
    /// Saves the given path data to disk and PlayerPrefs.
    /// </summary>
    public static void SavePath(PathData pathData)
    {
        if (pathData == null || string.IsNullOrEmpty(pathData.destinationName)) return;

        string json = pathData.ToJson();
        string safeName = SanitizeFileName(pathData.destinationName);

        try
        {
            string filePath = GetFilePath(pathData.destinationName);
            File.WriteAllText(filePath, json);
            Debug.Log($"PathStorageManager: Saved path for '{pathData.destinationName}' to {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"PathStorageManager: Could not write file: {e.Message}");
        }

        // Backup in PlayerPrefs
        PlayerPrefs.SetString(PREFS_PREFIX + safeName, json);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Loads the path data for a given destination. Returns null if not found.
    /// </summary>
    public static PathData LoadPath(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName)) return null;

        string safeName = SanitizeFileName(destinationName);
        string filePath = GetFilePath(destinationName);

        // Try reading from file first
        if (File.Exists(filePath))
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return PathData.FromJson(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"PathStorageManager: Failed to read file {filePath}: {e.Message}");
            }
        }

        // Fallback to PlayerPrefs
        string key = PREFS_PREFIX + safeName;
        if (PlayerPrefs.HasKey(key))
        {
            string json = PlayerPrefs.GetString(key);
            return PathData.FromJson(json);
        }

        return null;
    }

    /// <summary>
    /// Deletes a recorded path for a destination.
    /// </summary>
    public static void DeletePath(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName)) return;

        string safeName = SanitizeFileName(destinationName);
        string filePath = GetFilePath(destinationName);

        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }

        string key = PREFS_PREFIX + safeName;
        if (PlayerPrefs.HasKey(key))
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        Debug.Log($"PathStorageManager: Deleted path for '{destinationName}'.");
    }
}
