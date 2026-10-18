using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Greets the user on the main screen and offers Log out (or Sign in for guests).
/// </summary>
public class MainPanelHeader : MonoBehaviour
{
    public TMP_Text welcomeText;
    public TMP_Text roleText;
    public GameObject roleBadge;
    public Button accountButton;
    public TMP_Text accountButtonText;

    public UIManager uiManager;
    public GoogleAuthManager googleAuthManager;

    void Awake()
    {
        if (accountButton != null) accountButton.onClick.AddListener(OnAccountClicked);
    }

    void OnEnable()
    {
        UserSession.OnChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        UserSession.OnChanged -= Refresh;
    }

    private void Refresh()
    {
        string name = UserSession.FirstName;

        if (welcomeText != null)
        {
            welcomeText.text = UserSession.IsSignedIn && !string.IsNullOrEmpty(name)
                ? $"Welcome, {name}!"
                : "Welcome, explorer!";
        }

        bool showBadge = UserSession.IsAdmin;
        if (roleBadge != null) roleBadge.SetActive(showBadge);
        if (roleText != null) roleText.text = "ADMIN";

        if (accountButtonText != null) accountButtonText.text = UserSession.IsSignedIn ? "Log out" : "Sign in";
    }

    private void OnAccountClicked()
    {
        if (UserSession.IsSignedIn)
        {
            if (googleAuthManager != null) googleAuthManager.SignOut();
            return;
        }

        // Guest: go back to the login screen to sign in.
        UserSession.Clear();
        AppSession.OnboardingComplete = false;
        if (uiManager != null) uiManager.GoToLogin();
    }
}
