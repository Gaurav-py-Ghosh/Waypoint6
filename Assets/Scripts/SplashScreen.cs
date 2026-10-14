using UnityEngine;

public class SplashScreen : MonoBehaviour
{
    public float displayTime = 2.2f;
    public UIManager uiManager;

    void Start()
    {
        Invoke(nameof(GoNext), displayTime);
    }

    void GoNext()
    {
        uiManager.GoToLogin();
    }
}