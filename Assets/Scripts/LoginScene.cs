using UnityEngine;
using UnityEngine.UI;

public class LoginScreen : MonoBehaviour
{
    public Button loginButton;
    public Button skipLoginButton;

    public UIManager uiManager;

    void Start()
    {
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(() => uiManager?.GoToMailVerify());
        }

        if (skipLoginButton != null)
        {
            skipLoginButton.onClick.AddListener(() => uiManager?.GoToMain());
        }
    }
}