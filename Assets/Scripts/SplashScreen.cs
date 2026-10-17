using UnityEngine;

public class SplashScreen : MonoBehaviour
{
    public float displayTime = 2.2f;
    public UIManager uiManager;

    void OnEnable()
    {
        CancelInvoke(nameof(GoNext));
        Invoke(nameof(GoNext), displayTime);
    }

    void OnDisable()
    {
        CancelInvoke(nameof(GoNext));
    }

    void GoNext()
    {
        if (uiManager != null) uiManager.OnSplashFinished();
    }
}
