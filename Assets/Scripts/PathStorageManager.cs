using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Saves and loads recorded routes as JSON files in Application.persistentDataPath.
/// Writes are atomic (temp file + replace), so a crash mid-save never corrupts a route.
/// </summary>
public static class PathStorageManager
{
    private const string Tag = "PathStorage";
    private const string FolderName = "CampusPaths";
    private const string LegacyPrefsPrefix = "BMU_PATH_";

    private static string DirectoryPath
    {
        get
        {
            string dir = Path.Combine(Application.persistentDataPath, FolderName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private const string PendingUploadsKey = "BMU_PENDING_ROUTE_UPLOADS";

    private static string GetFilePath(string destinationName)
    {
        return Path.Combine(DirectoryPath, SanitizeFileName(destinationName) + ".json");
    }

    /// <summary>Stable id for a destination, used for the local file and the shared cloud copy.</summary>
    public static string RouteKey(string destinationName) => SanitizeFileName(destinationName);

    /// <summary>All valid routes stored on this phone.</summary>
    public static List<PathData> LoadAll()
    {
        var routes = new List<PathData>();
        try
        {
            foreach (string file in Directory.GetFiles(DirectoryPath, "*.json"))
            {
                PathData data = PathData.FromJson(File.ReadAllText(file));
                if (data != null && data.IsValid) routes.Add(data);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not list routes: " + e.Message);
        }
        return routes;
    }

    // ───────── Unfinished recording, kept so a crash does not lose the walk ─────────

    private static string DraftPath => Path.Combine(DirectoryPath, "draft.recording");

    public static void SaveDraft(PathData draft)
    {
        try
        {
            string tempPath = DraftPath + ".tmp";
            File.WriteAllText(tempPath, draft.ToJson());
            if (File.Exists(DraftPath)) File.Delete(DraftPath);
            File.Move(tempPath, DraftPath);
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not save recording draft: " + e.Message);
        }
    }

    /// <summary>The unfinished recording for this destination, or null.</summary>
    public static PathData LoadDraft(string destinationName)
    {
        try
        {
            if (!File.Exists(DraftPath)) return null;
            PathData draft = PathData.FromJson(File.ReadAllText(DraftPath));
            if (draft == null || draft.version != PathData.CurrentVersion || draft.markerId != PathData.CurrentMarkerId) return null;
            return RouteKey(draft.destinationName) == RouteKey(destinationName) ? draft : null;
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not read recording draft: " + e.Message);
            return null;
        }
    }

    public static void ClearDraft()
    {
        try { if (File.Exists(DraftPath)) File.Delete(DraftPath); }
        catch (Exception e) { AppLog.Warn(Tag, "Could not delete recording draft: " + e.Message); }
    }

    // ───────── Routes recorded on this phone that still need uploading ─────────

    public static List<string> GetPendingUploads()
    {
        string stored = PlayerPrefs.GetString(PendingUploadsKey, "");
        var list = new List<string>();
        foreach (string name in stored.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name)) list.Add(name);
        }
        return list;
    }

    public static void MarkPendingUpload(string destinationName)
    {
        List<string> list = GetPendingUploads();
        if (!list.Contains(destinationName)) list.Add(destinationName);
        PlayerPrefs.SetString(PendingUploadsKey, string.Join("\n", list));
        PlayerPrefs.Save();
    }

    public static void ClearPendingUpload(string destinationName)
    {
        List<string> list = GetPendingUploads();
        if (list.Remove(destinationName))
        {
            PlayerPrefs.SetString(PendingUploadsKey, string.Join("\n", list));
            PlayerPrefs.Save();
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c.ToString(), "");
        }
        return name.Trim().Replace(" ", "_").ToLowerInvariant();
    }

    /// <summary>True when a route compatible with this app version exists.</summary>
    public static bool HasPath(string destinationName)
    {
        return LoadPath(destinationName) != null;
    }

    /// <summary>Returns the saved route, or null if none exists or it is from an incompatible version.</summary>
    public static PathData LoadPath(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName)) return null;

        string filePath = GetFilePath(destinationName);
        if (!File.Exists(filePath)) return null;

        try
        {
            PathData data = PathData.FromJson(File.ReadAllText(filePath));
            if (data == null || !data.IsValid)
            {
                AppLog.Warn(Tag, $"Route for '{destinationName}' is from an older app version or marker, or damaged; it must be recorded again.");
                return null;
            }
            return data;
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, $"Could not read route for '{destinationName}': {e.Message}");
            return null;
        }
    }

    /// <returns>True when the route was written to disk.</returns>
    public static bool SavePath(PathData pathData)
    {
        if (pathData == null || !pathData.IsValid)
        {
            AppLog.Warn(Tag, "Refusing to save an invalid route.");
            return false;
        }

        string filePath = GetFilePath(pathData.destinationName);
        string tempPath = filePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, pathData.ToJson());
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);

            RemoveLegacyCopy(pathData.destinationName);
            AppLog.Info(Tag, $"Saved route '{pathData.destinationName}' ({pathData.totalDistanceMeters:F1} m, {pathData.points.Count} points).");
            return true;
        }
        catch (Exception e)
        {
            AppLog.Error(Tag, $"Could not save route '{pathData.destinationName}': {e.Message}");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            return false;
        }
    }

    public static void DeletePath(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName)) return;

        try
        {
            string filePath = GetFilePath(destinationName);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, $"Could not delete route '{destinationName}': {e.Message}");
        }
        RemoveLegacyCopy(destinationName);
    }

    /// <summary>Version 1 also kept a PlayerPrefs copy; clear it so it cannot shadow new data.</summary>
    private static void RemoveLegacyCopy(string destinationName)
    {
        string legacyKey = LegacyPrefsPrefix + destinationName.Trim().Replace(" ", "_");
        if (PlayerPrefs.HasKey(legacyKey))
        {
            PlayerPrefs.DeleteKey(legacyKey);
            PlayerPrefs.Save();
        }
    }
}
