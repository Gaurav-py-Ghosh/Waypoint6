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

    [Header("Campus Map")]
    public GameObject campusMapPanel;
    public CampusMapController campusMap;

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
        else if (campusMapPanel != null && campusMapPanel.activeSelf)
        {
            CloseCampusMap();
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
        RouteSync.SyncIfStale(60f);
    }

    /// <summary>Hides all menu screens (used when going back to the login screen).</summary>
    public void HideMenu() => HideAllViews();

    /// <summary>Explore Campus: the 2D campus map with walking directions.</summary>
    public void ShowCampusMap()
    {
        HideAllViews();
        if (campusMapPanel != null) campusMapPanel.SetActive(true);
        else UINotifier.Show("The campus map is not available.", NoticeKind.Warning);
    }

    public void CloseCampusMap() => ShowMainPanel();

    /// <summary>
    /// Called by each destination button (e.g. Library, Reception).
    /// Admins choose between recording and guiding; everyone else goes straight to guidance.
    /// </summary>
    public void SelectDestination(string destinationName)
    {
        if (string.IsNullOrEmpty(destinationName) || starting) return;
        AppLog.Info(Tag, $"Selected destination '{destinationName}'.");

        bool hasRoute = PathStorageManager.HasPath(destinationName);

        if (UserSession.IsAdmin)
        {
            if (locationPromptModal != null) locationPromptModal.Open(destinationName, StartRecordingMode, StartGuidanceMode, canRecord: true);
            else Begin(destinationName, hasRoute ? NavigationMode.Guide : NavigationMode.Record);
            return;
        }

        if (hasRoute)
        {
            StartGuidanceMode(destinationName);
        }
        else if (locationPromptModal != null)
        {
            // Shows "no route yet" with only a Back button.
            locationPromptModal.Open(destinationName, null, StartGuidanceMode, canRecord: false);
        }
        else
        {
            UINotifier.Show($"No route to {destinationName} yet. Ask an admin to record it.", NoticeKind.Info);
        }
    }

    public void StartRecordingMode(string destinationName)
    {
        if (!UserSession.IsAdmin)
        {
            UINotifier.Show("Only admins can record routes.", NoticeKind.Warning);
            return;
        }
        Begin(destinationName, NavigationMode.Record);
    }

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
        if (campusMapPanel != null) campusMapPanel.SetActive(false);
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
