using UnityEngine;

/// <summary>
/// Manages the auth/onboarding UI flow:
///   Splash -> Login -> Mail Verify -> Hand off to CampusNavigationManager
///
/// This script only controls the splash, login, and verification screens.
/// Once the user is authenticated (or skips login), it delegates to
/// CampusNavigationManager for all campus navigation screens.
/// </summary>
public class UIManager : MonoBehaviour
{
    public GameObject splashScreen;
    public GameObject loginScreen;
    public GameObject mailVerify;

    [Header("Navigation")]
    [Tooltip("The CampusNavigationManager that takes over after login.")]
    public CampusNavigationManager campusNavigationManager;

    void Start()
    {
        ShowOnly(splashScreen);
    }

    void ShowOnly(GameObject screenToShow)
    {
        splashScreen.SetActive(false);
        loginScreen.SetActive(false);
        mailVerify.SetActive(false);

        screenToShow.SetActive(true);
    }

    public void GoToLogin()
    {
        ShowOnly(loginScreen);
    }

    public void GoToMailVerify()
    {
        ShowOnly(mailVerify);
    }

    /// <summary>
    /// Called after successful login or skip.
    /// Hides all auth screens and hands off to CampusNavigationManager.
    /// </summary>
    public void GoToMain()
    {
        // Hide all auth screens
        splashScreen.SetActive(false);
        loginScreen.SetActive(false);
        mailVerify.SetActive(false);

        // Hand off to the campus navigation manager
        if (campusNavigationManager != null)
        {
            campusNavigationManager.ShowMainPanel();
        }
        else
        {
            Debug.LogError(
                "UIManager: CampusNavigationManager is not assigned!"
            );
        }
    }
}