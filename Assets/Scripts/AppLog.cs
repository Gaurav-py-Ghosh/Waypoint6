using System.Diagnostics;
using Debug = UnityEngine.Debug;

/// <summary>
/// App-wide logging. Info logs are compiled out of release builds, so they never
/// reach logcat on users' phones. Warnings and errors are always kept.
/// Never pass personal data (emails, user IDs, tokens) to any of these methods.
/// </summary>
public static class AppLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Info(string tag, string message)
    {
        Debug.Log($"[{tag}] {message}");
    }

    public static void Warn(string tag, string message)
    {
        Debug.LogWarning($"[{tag}] {message}");
    }

    public static void Error(string tag, string message)
    {
        Debug.LogError($"[{tag}] {message}");
    }
}
