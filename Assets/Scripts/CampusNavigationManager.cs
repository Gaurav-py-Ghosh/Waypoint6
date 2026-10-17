using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Menu-scene navigation: Main panel, destination list and the record/guide
/// prompt. Starting a route checks the camera permission, then loads the AR
/// Navigation scene. The camera never runs in this scene.
/// </summary>
public class CampusNavigationManager : MonoBehaviour
{
    private const string Tag = "CampusNavigation";

    [Header("Core Panels")]
    public GameObject mainPanel;
    public GameObject destinationPanel;

    [Header("Location Check Modal")]
    public LocationPromptModal locationPromptModal;

    private bool starting;

    void Start()
    {
        if (locationPromptModal != null) locationPromptModal.CloseModal();
    }

    void Update()
    {
        // Android back button arrives as Escape.
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;

        if (locationPromptModal != null && locationPromptModal.gameObject.activeSelf)
        {
            locationPromptModal.CloseModal();
        }
        else if (destinationPanel != null && destinationPanel.activeSelf)
        {
            ShowMainPanel();
        }
        else if (mainPanel != null && mainPanel.activeSelf)
        {
            MoveAppToBackground();
        }
    }

    public void ShowMainPanel()
    {
        HideAllViews();
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void ShowDestinationPanel()
    {
        HideAllViews();
        if (destinationPanel != null) destinationPanel.SetActive(true);
    }

    /// <summary>Called by each destination button (e.g. Library, Reception).</summary>
    public void SelectDestination(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName) || starting) return;
        AppLog.Info(Tag, $"Selected destination '{destinationName}'.");

        if (locationPromptModal != null)
        {
            locationPromptModal.Open(destinationName, StartRecordingMode, StartGuidanceMode);
        }
        else
        {
            Begin(destinationName, PathStorageManager.HasPath(destinationName) ? NavigationMode.Guide : NavigationMode.Record);
        }
    }

    public void StartRecordingMode(string destinationName) => Begin(destinationName, NavigationMode.Record);

    public void StartGuidanceMode(string destinationName) => Begin(destinationName, NavigationMode.Guide);

    private void Begin(string destinationName, NavigationMode mode)
    {
        if (starting) return;
        starting = true;

        CameraPermission.Request(result =>
        {
            switch (result)
            {
                case CameraPermissionResult.Granted:
                    UINotifier.Show("Starting AR camera…", NoticeKind.Info, 0f);
                    AppSession.StartNavigation(destinationName, mode);
                    break;
                case CameraPermissionResult.PermanentlyDenied:
                    starting = false;
                    UINotifier.Show("Camera access is turned off for this app. Turn it on in Settings to use AR navigation.",
                        NoticeKind.Warning, 8f, "SETTINGS", CameraPermission.OpenAppSettings);
                    break;
                default:
                    starting = false;
                    UINotifier.Show("AR navigation needs the camera. Please allow camera access.", NoticeKind.Warning, 5f);
                    break;
            }
        });
    }

    private void HideAllViews()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (destinationPanel != null) destinationPanel.SetActive(false);
        if (locationPromptModal != null) locationPromptModal.CloseModal();
    }

    private static void MoveAppToBackground()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call<bool>("moveTaskToBack", true);
            }
        }
        catch (System.Exception e)
        {
            AppLog.Warn(Tag, "moveTaskToBack failed: " + e.Message);
        }
#endif
    }
}
