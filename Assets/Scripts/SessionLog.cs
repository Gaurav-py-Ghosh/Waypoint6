using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Keeps warnings, errors and exceptions in a small file on the phone, so the cause
/// of a crash can be read after the app restarts (release builds strip Info logs).
/// The previous run's file is kept as previous_session.log.
/// Path: Android/data/&lt;package&gt;/files/logs/.
/// </summary>
public static class SessionLog
{
    private const int MaxBytes = 256 * 1024;
    private static readonly object Gate = new object();
    private static string path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "logs");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "session.log");

            string previous = Path.Combine(dir, "previous_session.log");
            if (File.Exists(path))
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }

            Write($"Session start: {Application.version}, {SystemInfo.deviceModel}, Android {SystemInfo.operatingSystem}");
            Application.logMessageReceivedThreaded += OnLog;
            Application.lowMemory += () => Write("LOW MEMORY warning from the OS.");
        }
        catch (Exception)
        {
            path = null; // Logging must never break the app.
        }
    }

    /// <summary>Adds a breadcrumb line (no personal data).</summary>
    public static void Mark(string message) => Write(message);

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log) return;
        Write(type == LogType.Exception || type == LogType.Error
            ? $"{type}: {condition}\n{stackTrace}"
            : $"{type}: {condition}");
    }

    private static void Write(string line)
    {
        if (path == null) return;
        lock (Gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
            }
            catch (Exception)
            {
                // Ignore: disk full or file busy.
            }
        }
    }
}
