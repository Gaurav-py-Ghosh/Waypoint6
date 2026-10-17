using UnityEngine;
using UnityEngine.SceneManagement;

public enum NavigationMode
{
    Record,
    Guide
}

/// <summary>
/// State shared between the menu scene and the AR navigation scene, plus the
/// scene transitions themselves. The AR camera only runs while the
/// Navigation scene is loaded.
/// </summary>
public static class AppSession
{
    public const string MenuSceneName = "MainMenu";
    public const string NavigationSceneName = "Navigation";

    /// <summary>True once the user has passed splash/login, so returning from AR skips them.</summary>
    public static bool OnboardingComplete { get; set; }

    public static string Destination { get; private set; }
    public static NavigationMode Mode { get; private set; }
    public static bool HasPendingNavigation => !string.IsNullOrEmpty(Destination);

    private static bool isLoading;

    public static void StartNavigation(string destination, NavigationMode mode)
    {
        if (isLoading || string.IsNullOrEmpty(destination)) return;

        Destination = destination;
        Mode = mode;
        LoadScene(NavigationSceneName);
    }

    public static void ReturnToMenu()
    {
        if (isLoading) return;

        Destination = null;
        LoadScene(MenuSceneName);
    }

    private static void LoadScene(string sceneName)
    {
        isLoading = true;
        AppLog.Info("AppSession", $"Loading scene '{sceneName}'.");

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (op == null)
        {
            isLoading = false;
            AppLog.Error("AppSession", $"Scene '{sceneName}' is not in Build Settings.");
            return;
        }

        op.completed += _ => isLoading = false;
    }
}
