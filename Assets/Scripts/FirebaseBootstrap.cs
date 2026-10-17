using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;

/// <summary>
/// Initialises Firebase once per app run. Other scripts wait for IsReady or
/// IsFailed. Safe to have in a scene that is loaded more than once.
/// </summary>
public class FirebaseBootstrap : MonoBehaviour
{
    private const string Tag = "Firebase";

    public static FirebaseAuth Auth { get; private set; }
    public static bool IsReady { get; private set; }
    public static bool IsFailed { get; private set; }

    private static bool started;

    void Start()
    {
        if (started) return;
        started = true;

        AppLog.Info(Tag, "Initialising Firebase…");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Fail("dependency check failed: " + (task.Exception?.GetBaseException().Message ?? "cancelled"));
                return;
            }

            if (task.Result != DependencyStatus.Available)
            {
                Fail("dependencies unavailable: " + task.Result);
                return;
            }

            Auth = FirebaseAuth.DefaultInstance;
            IsReady = true;
            AppLog.Info(Tag, "Firebase ready.");
        });
    }

    private static void Fail(string reason)
    {
        IsFailed = true;
        // Let a later scene load retry.
        started = false;
        AppLog.Error(Tag, "Firebase could not start: " + reason);
        UINotifier.Show("Sign-in is unavailable right now. You can continue without signing in.", NoticeKind.Warning, 6f);
    }
}
