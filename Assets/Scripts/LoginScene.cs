using UnityEngine;
using UnityEngine.UI;

/// <summary>First login screen: its one button opens the Google sign-in screen. Signing in is required.</summary>
public class LoginScreen : MonoBehaviour
{
    public Button loginButton;

    public UIManager uiManager;

    void Start()
    {
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(() => uiManager?.GoToMailVerify());
        }
    }
}
