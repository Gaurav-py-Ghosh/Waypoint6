using System.Collections;
using UnityEngine;

/// <summary>
/// Onboarding flow: Splash -> (already signed in? Main : Login) -> Main.
/// Returning from the AR scene skips straight to Main.
/// After onboarding, CampusNavigationManager runs the menu screens.
/// </summary>
public class UIManager : MonoBehaviour
{
    private const float MaxAuthWaitSeconds = 4f;

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
        ShowOnly(splashScreen);
    }

    /// <summary>Called by SplashScreen when its display time is over.</summary>
    public void OnSplashFinished()
    {
        if (AppSession.OnboardingComplete) return;
        StartCoroutine(ContinueAfterSplash());
    }

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

    /// <summary>Called after successful login or skip. Hands off to CampusNavigationManager.</summary>
    public void GoToMain()
    {
        // Skip Login and any other path that reaches Main without signing in means guest.
        if (!UserSession.IsSignedIn && !UserSession.IsGuest) UserSession.SetGuest();

        AppSession.OnboardingComplete = true;
        ShowOnly(null);

        if (campusNavigationManager != null) campusNavigationManager.ShowMainPanel();
        else AppLog.Error("UIManager", "CampusNavigationManager is not assigned.");
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
