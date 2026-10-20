using System.Collections;
using UnityEngine;

/// <summary>
/// Onboarding flow: Splash -> (already signed in? Main : Login) -> Main.
/// Returning from the AR scene skips straight to Main; reopening the app after
/// 30 s or more in the background shows the splash again.
/// After onboarding, CampusNavigationManager runs the menu screens.
/// </summary>
public class UIManager : MonoBehaviour
{
    private const float MaxAuthWaitSeconds = 4f;
    private const float SplashReplayAfterSeconds = 30f;

    private bool replayingSplash;
    private float pausedAt = -1f;

    public GameObject splashScreen;
    public GameObject loginScreen;
    public GameObject mailVerify;

    [Header("Navigation")]
    [Tooltip("The CampusNavigationManager that takes over after login.")]
    public CampusNavigationManager campusNavigationManager;

    [Tooltip("Used to skip the login screen when a user is already signed in.")]
    public GoogleAuthManager googleAuthManager;

    void Awake()
    {
        if (googleAuthManager == null) googleAuthManager = GetComponent<GoogleAuthManager>();
    }

    void Start()
    {
        if (AppSession.OnboardingComplete)
        {
            GoToMain();
            return;
        }

        // The menu screens sit above the splash in the canvas; hide them so the splash shows.
        if (campusNavigationManager != null) campusNavigationManager.HideMenu();
        ShowOnly(splashScreen);
    }

    /// <summary>Called by SplashScreen when its display time is over.</summary>
    public void OnSplashFinished()
    {
        if (replayingSplash)
        {
            replayingSplash = false;
            GoToMain();
            return;
        }
        if (AppSession.OnboardingComplete) return;
        StartCoroutine(ContinueAfterSplash());
    }

    // Android keeps the app alive in the background (Back on the main menu does not quit it),
    // so reopening it later would skip the splash. Replay it after a longer absence.
    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            pausedAt = Time.realtimeSinceStartup;
            return;
        }

        bool longAbsence = pausedAt >= 0f && Time.realtimeSinceStartup - pausedAt >= SplashReplayAfterSeconds;
        pausedAt = -1f;
        if (!longAbsence || !AppSession.OnboardingComplete) return;

        // Not while signing in: the Google account picker also pauses the app.
        if (IsActive(splashScreen) || IsActive(loginScreen) || IsActive(mailVerify)) return;

        replayingSplash = true;
        if (campusNavigationManager != null) campusNavigationManager.HideMenu();
        ShowOnly(splashScreen);
    }

    private static bool IsActive(GameObject go) => go != null && go.activeInHierarchy;

    private IEnumerator ContinueAfterSplash()
    {
        // Give Firebase a moment to restore a previous sign-in.
        float deadline = Time.realtimeSinceStartup + MaxAuthWaitSeconds;
        while (!FirebaseBootstrap.IsReady && !FirebaseBootstrap.IsFailed && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
        yield return null; // Let GoogleAuthManager pick up the Auth instance.

        if (googleAuthManager != null && googleAuthManager.RestoreSession()) GoToMain();
        else GoToLogin();
    }

    public void GoToLogin()
    {
        if (campusNavigationManager != null) campusNavigationManager.HideMenu();
        ShowOnly(loginScreen);
    }

    public void GoToMailVerify()
    {
        ShowOnly(mailVerify);
    }

    /// <summary>Called after a successful login. Hands off to CampusNavigationManager.</summary>
    public void GoToMain()
    {
        // Signing in is required on phones. The Editor cannot run the Google bridge, so it continues as a guest.
#if UNITY_EDITOR
        if (!UserSession.IsSignedIn && !UserSession.IsGuest) UserSession.SetGuest();
#else
        if (!UserSession.IsSignedIn)
        {
            GoToLogin();
            return;
        }
#endif

        AppSession.OnboardingComplete = true;
        ShowOnly(null);

        // Fetch routes recorded by admins (and push this admin's offline recordings).
        RouteSync.SyncIfStale(30f);

        if (campusNavigationManager != null) campusNavigationManager.ShowMainPanel();
        else AppLog.Error("UIManager", "CampusNavigationManager is not assigned.");

        // If the last run ended in a crash, offer to share a report.
        CrashReporter.CheckAndPrompt();
    }

    private void ShowOnly(GameObject screenToShow)
    {
        SetActive(splashScreen, false);
        SetActive(loginScreen, false);
        SetActive(mailVerify, false);
        SetActive(screenToShow, true);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
