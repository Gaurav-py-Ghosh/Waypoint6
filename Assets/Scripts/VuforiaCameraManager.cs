using UnityEngine;
using Vuforia;

/// <summary>
/// Manages the Vuforia camera lifecycle:
/// Ensures the camera runs during AR Recording and AR Guidance,
/// while pausing during 2D UI panels (Splash, Login, Main Menu, Destination Menu).
/// </summary>
public class VuforiaCameraManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Vuforia ARCamera's VuforiaBehaviour component.")]
    public VuforiaBehaviour vuforiaBehaviour;

    [Tooltip("The CampusNavigationManager to check current screen state.")]
    public CampusNavigationManager navigationManager;

    [Header("Active Panels")]
    public GameObject recordingHUD;
    public GameObject guidanceHUD;

    void Start()
    {
        if (vuforiaBehaviour == null)
        {
            vuforiaBehaviour = FindFirstObjectByType<VuforiaBehaviour>();
        }

        if (navigationManager == null)
        {
            navigationManager = FindFirstObjectByType<CampusNavigationManager>();
        }

        // Start with Vuforia paused on splash/login
        if (vuforiaBehaviour != null)
        {
            vuforiaBehaviour.enabled = false;
        }
    }

    void Update()
    {
        if (vuforiaBehaviour == null) return;

        bool shouldBeActive = false;

        if (navigationManager != null)
        {
            shouldBeActive =
                (navigationManager.recordingHUD != null && navigationManager.recordingHUD.activeInHierarchy) ||
                (navigationManager.guidanceHUD != null && navigationManager.guidanceHUD.activeInHierarchy);
        }

        if (!shouldBeActive)
        {
            shouldBeActive =
                (recordingHUD != null && recordingHUD.activeInHierarchy) ||
                (guidanceHUD != null && guidanceHUD.activeInHierarchy);
        }

        if (shouldBeActive && !vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = true;
            Debug.Log("VuforiaCameraManager: Vuforia CAMERA STARTED for AR view.");
        }
        else if (!shouldBeActive && vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = false;
            Debug.Log("VuforiaCameraManager: Vuforia CAMERA PAUSED for UI view.");
        }
    }
}
