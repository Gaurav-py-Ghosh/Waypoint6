using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// After the app was closed by a crash, freeze or low memory, offers to share a short
/// report (Android's exit reason plus the previous SessionLog) through the share sheet,
/// so testers without a computer can send it over WhatsApp, email, etc.
/// Uses ApplicationExitInfo (Android 11+, which is this app's minimum).
/// </summary>
public static class CrashReporter
{
    private const string Tag = "CrashReporter";
    private const string LastHandledKey = "BMU_LAST_EXIT_TIMESTAMP";
    private const int MaxLogChars = 40000;

    private static bool checkedThisRun;

    /// <summary>Call once the main menu is showing.</summary>
    public static void CheckAndPrompt()
    {
        if (checkedThisRun) return;
        checkedThisRun = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            ExitRecord exit = ReadLastExit();
            if (exit == null || !IsAbnormal(exit.reason)) return;

            // Prompt once per abnormal exit.
            string stamp = exit.timestamp.ToString();
            if (PlayerPrefs.GetString(LastHandledKey, "") == stamp) return;
            PlayerPrefs.SetString(LastHandledKey, stamp);
            PlayerPrefs.Save();

            string report = BuildReport(exit);
            SaveReport(report);
            UINotifier.Show($"Waypoint closed unexpectedly last time ({ReasonName(exit.reason)}). Send a report to help fix it?",
                NoticeKind.Warning, 15f, "SEND REPORT", () => Share(report));
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not read the last exit reason: " + e.Message);
        }
#endif
    }

    private class ExitRecord
    {
        public int reason;
        public string description;
        public long timestamp;
        public long pssKb;
        public long rssKb;
        public int importance;
    }

    private static ExitRecord ReadLastExit()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaObject activityManager = activity.Call<AndroidJavaObject>("getSystemService", "activity"))
        using (AndroidJavaObject list = activityManager.Call<AndroidJavaObject>("getHistoricalProcessExitReasons", Application.identifier, 0, 1))
        {
            if (list == null || list.Call<int>("size") == 0) return null;
            using (AndroidJavaObject info = list.Call<AndroidJavaObject>("get", 0))
            {
                return new ExitRecord
                {
                    reason = info.Call<int>("getReason"),
                    description = info.Call<string>("getDescription"),
                    timestamp = info.Call<long>("getTimestamp"),
                    pssKb = info.Call<long>("getPss"),
                    rssKb = info.Call<long>("getRss"),
                    importance = info.Call<int>("getImportance")
                };
            }
        }
    }

    // ApplicationExitInfo.REASON_* values.
    private static bool IsAbnormal(int reason) =>
        reason == 2 || reason == 3 || reason == 4 || reason == 5 || reason == 6 || reason == 9;

    private static string ReasonName(int reason)
    {
        switch (reason)
        {
            case 1: return "exited";
            case 2: return "killed by the system";
            case 3: return "low memory";
            case 4: return "crash";
            case 5: return "native crash";
            case 6: return "app froze";
            case 7: return "startup failure";
            case 9: return "too much battery or CPU use";
            case 10: return "closed by user";
            default: return "reason " + reason;
        }
    }

    private static string BuildReport(ExitRecord exit)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Waypoint crash report");
        sb.AppendLine($"App {Application.version} · {SystemInfo.deviceModel} · {SystemInfo.operatingSystem}");
        sb.AppendLine($"RAM {SystemInfo.systemMemorySize} MB");
        sb.AppendLine($"Exit: {ReasonName(exit.reason)} (code {exit.reason})");
        sb.AppendLine($"When: {DateTimeOffset.FromUnixTimeMilliseconds(exit.timestamp).ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Detail: {exit.description}");
        sb.AppendLine($"Memory at exit: PSS {exit.pssKb / 1024} MB, RSS {exit.rssKb / 1024} MB, importance {exit.importance}");
        sb.AppendLine();
        sb.AppendLine("--- Log before the exit ---");

        string log = ReadPreviousLog();
        if (log.Length > MaxLogChars) log = "…" + log.Substring(log.Length - MaxLogChars);
        sb.Append(log);
        return sb.ToString();
    }

    private static string ReadPreviousLog()
    {
        try
        {
            string file = Path.Combine(Application.persistentDataPath, "logs", "previous_session.log");
            return File.Exists(file) ? File.ReadAllText(file) : "(no log)";
        }
        catch (Exception e)
        {
            return "(log unreadable: " + e.Message + ")";
        }
    }

    /// <summary>Keeps a copy, since the next launch rotates previous_session.log away.</summary>
    private static void SaveReport(string report)
    {
        try
        {
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "logs", "crash_report.txt"), report);
        }
        catch (Exception)
        {
            // Sharing still works without the copy.
        }
    }

    private static void Share(string report)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND"))
            {
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.SUBJECT", "Waypoint crash report");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", report);
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Send report"))
                {
                    activity.Call("startActivity", chooser);
                }
            }
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not open the share sheet: " + e.Message);
            UINotifier.Show("Could not open sharing on this phone.", NoticeKind.Warning);
        }
#endif
    }
}
