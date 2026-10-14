using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Central navigation manager for BMUARR:
/// - Main Screen -> Destination List -> Location Check Modal
/// - If no path: Record Mode (Scan Floor Compass -> Lock Origin -> Drop Anchor at turns -> Finish at Destination)
/// - If path exists: Guide Me Mode (Smooth glowing emerald ribbon line on floor + Destination Stop Marker)
/// </summary>
public class CampusNavigationManager : MonoBehaviour
{
    [Header("Core Panels")]
    public GameObject mainPanel;
    public GameObject destinationPanel;

    [Header("Location Check Modal")]
    public LocationPromptModal locationPromptModal;

    [Header("Recording HUD")]
    public GameObject recordingHUD;
    public TMP_Text recordingTitleText;
    public TMP_Text anchorCountText;
    public Button dropAnchorButton;
    public Button finishRecordingButton;
    public Button cancelRecordingButton;

    [Header("Guidance HUD")]
    public GameObject guidanceHUD;
    public TMP_Text guidanceDestinationText;
    public TMP_Text guidanceDistanceText;
    public Button endNavigationButton;

    [Header("AR Subsystems")]
    public AnchorTracker anchorTracker;
    public ARPathRecorder pathRecorder;
    public ARPathRenderer pathRenderer;
    public VuforiaImageTargetHandler imageTargetHandler;

    private string currentDestination = "";
    private int anchorCount = 0;
    private bool isGuidancePathRendered = false;

    void Awake()
    {
        // Wire Recording HUD buttons
        if (dropAnchorButton != null)
        {
            dropAnchorButton.onClick.AddListener(OnDropAnchorClicked);
        }

        if (finishRecordingButton != null)
        {
            finishRecordingButton.onClick.AddListener(OnFinishRecordingClicked);
        }

        if (cancelRecordingButton != null)
        {
            cancelRecordingButton.onClick.AddListener(OnCancelRecordingClicked);
        }

        // Wire Guidance HUD buttons
        if (endNavigationButton != null)
        {
            endNavigationButton.onClick.AddListener(ShowMainPanel);
        }

        if (anchorTracker == null)
        {
            anchorTracker = FindFirstObjectByType<AnchorTracker>();
        }
    }

    void Start()
    {
        if (anchorTracker != null)
        {
            anchorTracker.OnOriginCalibrated += HandleOriginCalibrated;
            anchorTracker.OnOriginRecalibrated += HandleOriginCalibrated;
            anchorTracker.OnVisualVisibilityChanged += HandleVisualVisibilityChanged;
        }

        if (recordingHUD != null) recordingHUD.SetActive(false);
        if (guidanceHUD != null) guidanceHUD.SetActive(false);
        if (locationPromptModal != null) locationPromptModal.CloseModal();
    }

    void OnDestroy()
    {
        if (anchorTracker != null)
        {
            anchorTracker.OnOriginCalibrated -= HandleOriginCalibrated;
            anchorTracker.OnOriginRecalibrated -= HandleOriginCalibrated;
            anchorTracker.OnVisualVisibilityChanged -= HandleVisualVisibilityChanged;
        }
    }

    private void HandleVisualVisibilityChanged(bool isVisible)
    {
        if (isVisible && guidanceHUD != null && guidanceHUD.activeSelf && !isGuidancePathRendered)
        {
            RenderGuidancePath();
        }
    }

    private void HandleOriginCalibrated()
    {
        if (recordingHUD != null && recordingHUD.activeSelf)
        {
            SetRecordingHUDState(true);
        }
        else if (guidanceHUD != null && guidanceHUD.activeSelf && !isGuidancePathRendered)
        {
            RenderGuidancePath();
        }
    }

    // ─────────────────────────────────────────────
    // Screen Navigation Transitions
    // ─────────────────────────────────────────────

    public void ShowMainPanel()
    {
        HideAllViews();
        if (pathRecorder != null && pathRecorder.isRecording)
        {
            pathRecorder.CancelRecording();
        }
        if (pathRenderer != null)
        {
            pathRenderer.ClearAll();
        }

        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void ShowDestinationPanel()
    {
        HideAllViews();
        if (pathRecorder != null && pathRecorder.isRecording)
        {
            pathRecorder.CancelRecording();
        }
        if (pathRenderer != null)
        {
            pathRenderer.ClearAll();
        }

        if (destinationPanel != null) destinationPanel.SetActive(true);
    }

    /// <summary>
    /// Called when the user taps any campus landmark button (e.g. Library, Reception).
    /// </summary>
    public void SelectDestination(string destinationName)
    {
        currentDestination = destinationName;
        Debug.Log($"CampusNavigationManager: Selected destination '{destinationName}'");

        if (locationPromptModal != null)
        {
            locationPromptModal.Open(
                destinationName,
                StartRecordingMode,
                StartGuidanceMode
            );
        }
        else
        {
            if (PathStorageManager.HasPath(destinationName))
            {
                StartGuidanceMode(destinationName);
            }
            else
            {
                StartRecordingMode(destinationName);
            }
        }
    }

    // ─────────────────────────────────────────────
    // Mode 1: Path Recording
    // ─────────────────────────────────────────────

    public void StartRecordingMode(string destName)
    {
        currentDestination = destName;
        anchorCount = 0;
        HideAllViews();

        if (recordingHUD != null)
        {
            recordingHUD.SetActive(true);
        }

        if (recordingTitleText != null)
        {
            recordingTitleText.text = $"RECORDING: {destName.ToUpper()}";
        }

        bool isAlreadyCalibrated = anchorTracker != null && anchorTracker.IsCalibrated;
        SetRecordingHUDState(isAlreadyCalibrated);

        if (pathRecorder != null)
        {
            pathRecorder.StartRecording(destName);
        }
    }

    private void SetRecordingHUDState(bool originLocked)
    {
        if (originLocked)
        {
            if (anchorCountText != null)
            {
                if (anchorCount == 0)
                {
                    anchorCountText.text = "[OK] TARGET CALIBRATED! Stand at start & tap 'DROP START ANCHOR'";
                    anchorCountText.color = new Color(0.2f, 1f, 0.4f); // Emerald
                    SetDropButtonText("+ DROP START ANCHOR");
                }
                else
                {
                    anchorCountText.text = $"Anchors Dropped: {anchorCount}  •  Walk forward to next turn";
                    anchorCountText.color = Color.white;
                    SetDropButtonText("+ DROP ANCHOR (TURN)");
                }
            }

            if (dropAnchorButton != null) dropAnchorButton.interactable = true;
            if (finishRecordingButton != null) finishRecordingButton.interactable = (anchorCount > 0);
        }
        else
        {
            if (anchorCountText != null)
            {
                anchorCountText.text = "POINT CAMERA AT TARGET (WALL OR FLOOR) TO START";
                anchorCountText.color = new Color(1f, 0.85f, 0.2f); // Amber
                SetDropButtonText("+ DROP ANCHOR");
            }

            if (dropAnchorButton != null) dropAnchorButton.interactable = false;
            if (finishRecordingButton != null) finishRecordingButton.interactable = false;
        }
    }

    private void SetDropButtonText(string text)
    {
        if (dropAnchorButton != null)
        {
            TMP_Text btnText = dropAnchorButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null) btnText.text = text;
        }
    }

    private void OnDropAnchorClicked()
    {
        if (pathRecorder != null && pathRecorder.isRecording && pathRecorder.isCalibrated)
        {
            pathRecorder.DropAnchor();
            anchorCount = pathRecorder.WaypointCount;
            UpdateAnchorCountDisplay();
            if (finishRecordingButton != null) finishRecordingButton.interactable = (anchorCount > 0);
        }
    }

    private void UpdateAnchorCountDisplay()
    {
        if (anchorCountText != null && pathRecorder != null)
        {
            if (anchorCount == 1)
            {
                anchorCountText.text = "Start Anchor Placed! Walk route & tap at turns.";
                anchorCountText.color = new Color(0.2f, 1f, 0.4f);
                SetDropButtonText("+ DROP ANCHOR (TURN)");
            }
            else
            {
                anchorCountText.text = $"Anchors: {anchorCount - 1} turns | Distance: ~{pathRecorder.CurrentDistance:F1}m";
                anchorCountText.color = Color.white;
                SetDropButtonText("+ DROP ANCHOR (TURN)");
            }
        }
    }

    private void OnFinishRecordingClicked()
    {
        if (pathRecorder != null && pathRecorder.isRecording && pathRecorder.isCalibrated)
        {
            PathData saved = pathRecorder.FinishRecording();

            // Auto-switch to Guide Me mode so user sees their new path immediately!
            if (saved != null)
            {
                StartGuidanceMode(saved.destinationName);
            }
            else
            {
                ShowDestinationPanel();
            }
        }
    }

    private void OnCancelRecordingClicked()
    {
        if (pathRecorder != null)
        {
            pathRecorder.CancelRecording();
        }
        ShowDestinationPanel();
    }

    // ─────────────────────────────────────────────
    // Mode 2: Guided AR Navigation
    // ─────────────────────────────────────────────

    public void StartGuidanceMode(string destName)
    {
        currentDestination = destName;
        isGuidancePathRendered = false;
        HideAllViews();

        PathData pathData = PathStorageManager.LoadPath(destName);
        if (pathData == null)
        {
            Debug.LogWarning($"CampusNavigationManager: No path found for '{destName}'. Opening recording mode.");
            StartRecordingMode(destName);
            return;
        }

        if (guidanceHUD != null)
        {
            guidanceHUD.SetActive(true);
        }

        if (guidanceDestinationText != null)
        {
            guidanceDestinationText.text = $"TO: {destName.ToUpper()}";
        }

        // Initially clear path visuals until image target is scanned
        if (pathRenderer != null)
        {
            pathRenderer.ClearAll();
        }

        // If target is directly visible in camera right now, render immediately; otherwise wait for scan
        if (anchorTracker != null && anchorTracker.IsDirectlyVisible)
        {
            RenderGuidancePath();
        }
        else
        {
            if (guidanceDistanceText != null)
            {
                guidanceDistanceText.text = $"Distance: ~{pathData.totalDistanceMeters:F1}m • Aim camera at Compass Target to align";
                guidanceDistanceText.color = new Color(1f, 0.85f, 0.2f);
            }
            Debug.Log($"CampusNavigationManager: Started guidance for '{destName}'. Waiting for compass scan to render path.");
        }
    }

    private void RenderGuidancePath()
    {
        if (isGuidancePathRendered || string.IsNullOrEmpty(currentDestination)) return;

        PathData pathData = PathStorageManager.LoadPath(currentDestination);
        if (pathData == null) return;

        isGuidancePathRendered = true;

        if (pathRenderer != null && anchorTracker != null)
        {
            pathRenderer.SetOriginPose(anchorTracker.StabilizedOrigin.position, anchorTracker.StabilizedOrigin.rotation);
            pathRenderer.RenderGuidedPath(pathData);
        }

        if (guidanceDistanceText != null)
        {
            guidanceDistanceText.text = $"Aligned with Compass! Follow the glowing path (~{pathData.totalDistanceMeters:F1}m)";
            guidanceDistanceText.color = new Color(0.2f, 1f, 0.4f);
        }

        Debug.Log($"CampusNavigationManager: Image target scanned! Guidance path rendered for '{currentDestination}'.");
    }

    // ─────────────────────────────────────────────
    // Vuforia Target Event Handler
    // ─────────────────────────────────────────────

    public void OnImageTargetTracked(Transform targetTransform)
    {
        HandleOriginCalibrated();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void HideAllViews()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (destinationPanel != null) destinationPanel.SetActive(false);
        if (recordingHUD != null) recordingHUD.SetActive(false);
        if (guidanceHUD != null) guidanceHUD.SetActive(false);
        if (locationPromptModal != null) locationPromptModal.CloseModal();

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            string[] legacyPanels = { "QRMainPanel", "QRScanningPanel", "ARScreenPanel", "DestARScreenPanel" };
            foreach (string panelName in legacyPanels)
            {
                Transform t = canvas.transform.Find(panelName);
                if (t != null && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                }
            }
        }
    }
}
