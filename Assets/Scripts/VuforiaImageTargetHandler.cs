using UnityEngine;
using Vuforia;

/// <summary>
/// Handles Vuforia Image Target tracking events for BMU campus navigation.
/// When the target image (floor compass) is detected, it notifies CampusNavigationManager
/// with its world Transform so the origin anchor can be locked for recording or guidance.
/// </summary>
public class VuforiaImageTargetHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The CampusNavigationManager that controls screen flow.")]
    public CampusNavigationManager navigationManager;

    [Header("3D Content")]
    [Tooltip("Parent GameObject containing all 3D AR content to show when the target is found.")]
    public GameObject arContentRoot;

    [Header("UI Feedback")]
    [Tooltip("Optional: a UI text element to display tracking status.")]
    public TMPro.TMP_Text statusText;

    private ObserverBehaviour observerBehaviour;
    private bool hasNotifiedFound = false;

    void Start()
    {
        observerBehaviour = GetComponent<ObserverBehaviour>();

        if (observerBehaviour == null)
        {
            Debug.LogError("VuforiaImageTargetHandler: No ObserverBehaviour found on " + gameObject.name);
            return;
        }

        observerBehaviour.OnTargetStatusChanged += OnTargetStatusChanged;

        if (navigationManager == null)
        {
            navigationManager = FindFirstObjectByType<CampusNavigationManager>();
        }
    }

    void OnDestroy()
    {
        if (observerBehaviour != null)
        {
            observerBehaviour.OnTargetStatusChanged -= OnTargetStatusChanged;
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus targetStatus)
    {
        bool isTracked = targetStatus.Status == Status.TRACKED ||
                         targetStatus.Status == Status.EXTENDED_TRACKED;

        if (isTracked)
        {
            OnTargetFound();
        }
        else
        {
            OnTargetLost();
        }
    }

    private void OnTargetFound()
    {
        Debug.Log("VuforiaImageTargetHandler: Compass Target TRACKED at " + transform.position);

        if (arContentRoot != null)
        {
            arContentRoot.SetActive(true);
        }

        if (statusText != null)
        {
            statusText.text = "Compass Target Detected!";
        }

        if (navigationManager != null)
        {
            navigationManager.OnImageTargetTracked(transform);
        }
    }

    private void OnTargetLost()
    {
        Debug.Log("VuforiaImageTargetHandler: Target LOST from direct view");

        // Note: Do not hide world navigation path when target leaves view!
        // The user is actively walking through the campus corridors.
        if (statusText != null)
        {
            statusText.text = "Navigating campus...";
        }
    }

    public void ResetScanState()
    {
        hasNotifiedFound = false;
    }
}
