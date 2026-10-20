using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Vuforia;

/// <summary>
/// Runs the AR Navigation scene: record a new route or guide along a saved one,
/// based on what the menu scene asked for (AppSession). Leaving returns to the
/// menu scene, which shuts the camera down.
/// </summary>
// Runs before VuforiaBehaviour so the init-result subscription is in place in time.
[DefaultExecutionOrder(-1000)]
public class NavigationSceneController : MonoBehaviour
{
    private const string Tag = "Navigation";
    private const float MaxTapMovePixels = 40f;
    private const float MaxTapSeconds = 0.6f;

    [Header("References")]
    public NavigationHUD hud;
    public AnchorTracker anchorTracker;
    public ARPathRecorder recorder;
    public GuidanceController guidance;
    public TrackingQualityMonitor trackingMonitor;
    public ARPathRenderer pathRenderer;

#pragma warning disable 0414 // Only read in the Editor.
    [Header("Editor Testing")]
    [Tooltip("Used when this scene is played directly in the Editor without going through the menu.")]
    [SerializeField] private string editorTestDestination = "Library";
    [SerializeField] private NavigationMode editorTestMode = NavigationMode.Record;
#pragma warning restore 0414

    [Tooltip("HUD refresh rate (per second) for status text.")]
    [SerializeField] private float hudRefreshRate = 5f;

    private NavigationMode mode;
    private string destination;
    private bool leaving;
    private bool finished;
    private int previousFrameRate = -1;
    private float nextHudRefresh;

    private bool pointerDown;
    private Vector2 pointerDownPosition;
    private float pointerDownTime;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    void Awake()
    {
        // Subscribe early: Vuforia may report its init result right after scene load.
        VuforiaApplication.Instance.OnVuforiaInitialized += HandleVuforiaInitialized;
        VuforiaApplication.Instance.OnVuforiaError += HandleVuforiaError;

        // The HUD must be visible even if its canvas was left disabled in the scene.
        if (hud != null)
        {
            Canvas canvas = hud.GetComponentInParent<Canvas>(true);
            if (canvas != null && !canvas.gameObject.activeSelf)
            {
                AppLog.Warn(Tag, "HUD canvas was disabled in the scene; enabling it.");
                canvas.gameObject.SetActive(true);
            }
        }
    }

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // The camera delivers 30 fps; rendering faster only adds heat and CPU load.
        previousFrameRate = Application.targetFrameRate;
        Application.targetFrameRate = 30;

        if (!ResolveSession()) return;
        SessionLog.Mark($"AR scene started ({mode}).");

        hud.OnPrimary += HandlePrimary;
        hud.OnSecondary += HandleBack;
        hud.OnUndo += HandleUndo;
        hud.OnFinish += FinishRecording;
        if (recorder != null) recorder.OnStateChanged += RefreshRecordingHud;
        if (guidance != null)
        {
            guidance.OnGuidanceUpdated += HandleGuidanceUpdate;
            guidance.OnArrived += HandleArrived;
        }
        if (trackingMonitor != null) trackingMonitor.OnQualityChanged += HandleTrackingChanged;
        if (anchorTracker != null) anchorTracker.OnOriginCorrected += HandleOriginCorrected;

        if (mode == NavigationMode.Record) BeginRecording();
        else BeginGuidance();

        RefreshTrackingBanner();
    }

    void OnDestroy()
    {
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        Application.targetFrameRate = previousFrameRate;

        if (VuforiaApplication.Instance != null)
        {
            VuforiaApplication.Instance.OnVuforiaInitialized -= HandleVuforiaInitialized;
            VuforiaApplication.Instance.OnVuforiaError -= HandleVuforiaError;
        }
        if (hud != null)
        {
            hud.OnPrimary -= HandlePrimary;
            hud.OnSecondary -= HandleBack;
            hud.OnUndo -= HandleUndo;
            hud.OnFinish -= FinishRecording;
        }
        if (recorder != null) recorder.OnStateChanged -= RefreshRecordingHud;
        if (guidance != null)
        {
            guidance.OnGuidanceUpdated -= HandleGuidanceUpdate;
            guidance.OnArrived -= HandleArrived;
        }
        if (trackingMonitor != null) trackingMonitor.OnQualityChanged -= HandleTrackingChanged;
        if (anchorTracker != null) anchorTracker.OnOriginCorrected -= HandleOriginCorrected;
    }

    void Update()
    {
        if (destination == null || leaving) return;

        // Android back button arrives as Escape.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (!hud.CloseTopDialog()) HandleBack();
        }

        if (mode == NavigationMode.Record && !finished) HandleFloorTap();

        if (Time.time >= nextHudRefresh)
        {
            nextHudRefresh = Time.time + 1f / Mathf.Max(1f, hudRefreshRate);
            if (mode == NavigationMode.Record) RefreshRecordingHud();
            else RefreshAlignmentStatus();
        }
    }

    private bool ResolveSession()
    {
        if (AppSession.HasPendingNavigation)
        {
            destination = AppSession.Destination;
            mode = AppSession.Mode;
            if (mode == NavigationMode.Record && !UserSession.IsAdmin)
            {
                AppLog.Warn(Tag, "Non-admin tried to record; switching to guidance.");
                mode = NavigationMode.Guide;
            }
            return true;
        }

#if UNITY_EDITOR
        destination = editorTestDestination;
        mode = editorTestMode;
        AppLog.Warn(Tag, $"No session from the menu; testing '{destination}' in {mode} mode.");
        return true;
#else
        AppLog.Error(Tag, "Navigation scene opened without a destination.");
        Leave();
        return false;
#endif
    }

    // ───────────────────────── Recording ─────────────────────────

    private void BeginRecording()
    {
        hud.SetTitle($"RECORDING: {destination.ToUpperInvariant()}");
        hud.SetGuidanceVisible(false);
        hud.SetArrowVisible(false);
        hud.SetSecondary("CANCEL");
        recorder.Begin(destination);
        if (recorder.RestoredPointCount > 0)
        {
            UINotifier.Show($"Resumed your unfinished recording ({recorder.RestoredPointCount} points). Scan the marker to continue.",
                NoticeKind.Info, 6f);
        }
        RefreshRecordingHud();
    }

    private void RefreshRecordingHud()
    {
        if (mode != NavigationMode.Record || recorder == null || finished) return;

        bool reliable = trackingMonitor == null || trackingMonitor.IsReliable;
        bool placing = recorder.State == RecorderState.Ready || recorder.State == RecorderState.Recording;
        int count = recorder.PointCount;

        hud.SetPrimary("+ DROP POINT", true, placing && reliable && recorder.HasAim);
        hud.SetRecordingControls(placing, count > 0, count >= 2);

        switch (recorder.State)
        {
            case RecorderState.WaitingForMarker:
                hud.SetStatus(AlignmentMessage());
                break;
            case RecorderState.Ready:
                hud.SetStatus(recorder.HasAim
                    ? "Marker found. Aim the ring at the floor next to the marker, then tap DROP POINT (or tap the floor)."
                    : "Marker found. Point the camera down at the floor to place the first point.");
                break;
            case RecorderState.Recording:
                string aimHint = recorder.HasAim ? "" : "  Aim lower, at the floor.";
                hud.SetStatus($"{count} point{(count == 1 ? "" : "s")} · {recorder.RecordedDistance:F1} m. " +
                              $"Walk to the next turn and drop a point. FINISH at {destination}.{aimHint}");
                break;
        }
    }

    private void HandlePrimary()
    {
        if (mode != NavigationMode.Record || finished) return;
        if (!recorder.DropAtAim(out string error)) UINotifier.Show(error, NoticeKind.Warning, 2.5f);
        RefreshRecordingHud();
    }

    private void HandleUndo()
    {
        if (mode != NavigationMode.Record || finished) return;
        recorder.Undo();
        RefreshRecordingHud();
    }

    /// <summary>A short tap on the camera view (not on a button) places a point where it meets the floor.</summary>
    private void HandleFloorTap()
    {
        Pointer pointer = Pointer.current;
        if (pointer == null || hud.IsDialogOpen) return;

        if (pointer.press.wasPressedThisFrame)
        {
            pointerDownPosition = pointer.position.ReadValue();
            pointerDownTime = Time.unscaledTime;
            pointerDown = !IsOverUI(pointerDownPosition);
        }
        else if (pointer.press.wasReleasedThisFrame && pointerDown)
        {
            pointerDown = false;
            Vector2 position = pointer.position.ReadValue();
            bool isTap = Vector2.Distance(position, pointerDownPosition) <= MaxTapMovePixels &&
                         Time.unscaledTime - pointerDownTime <= MaxTapSeconds;
            if (!isTap) return;

            if (recorder.State == RecorderState.Ready || recorder.State == RecorderState.Recording)
            {
                if (!recorder.DropAtScreenPoint(position, out string error)) UINotifier.Show(error, NoticeKind.Warning, 2.5f);
                RefreshRecordingHud();
            }
        }
    }

    private bool IsOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        uiHits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPosition }, uiHits);
        return uiHits.Count > 0;
    }

    private void FinishRecording()
    {
        if (mode != NavigationMode.Record || finished) return;

        PathData saved = recorder.Finish(out string error);
        if (saved == null)
        {
            UINotifier.Show(error, NoticeKind.Warning);
            return;
        }

        finished = true;
        hud.SetPrimary("", false, false);
        hud.SetRecordingControls(false, false, false);
        hud.SetStatus("Route saved. Sharing…");

        // Share with everyone via Firebase; the result shows even after leaving this screen.
        RouteSync.Upload(saved, (shared, message) =>
        {
            UINotifier.Show(message, shared ? NoticeKind.Success : NoticeKind.Warning, 5f);
            if (!leaving && hud != null) hud.SetStatus(shared ? "Route saved and shared." : "Route saved on this phone.");
        });

        // Show the whole saved route so the user can check it.
        if (pathRenderer != null)
        {
            var points = saved.GetPoints2D();
            pathRenderer.RenderGuide(points, points[points.Count - 1], saved.destinationName, saved.floorHeight);
        }

        hud.ShowResult("Route saved",
            $"{saved.totalDistanceMeters:F0} m route to {saved.destinationName} with {saved.points.Count} points. " +
            "Choose Guide Me from the marker to follow it.",
            Leave);
    }

    // ───────────────────────── Guidance ─────────────────────────

    private void BeginGuidance()
    {
        PathData path = PathStorageManager.LoadPath(destination);
        hud.SetTitle($"TO: {destination.ToUpperInvariant()}");
        hud.SetSecondary("EXIT");
        hud.SetPrimary("", false, false);
        hud.SetRecordingControls(false, false, false);

        if (path == null)
        {
            hud.ShowError($"No saved route to {destination}. Record it first.", null, null, Leave);
            return;
        }

        hud.SetGuidanceVisible(true);
        hud.SetInstruction("Scan the marker to start", $"{path.totalDistanceMeters:F0} m");
        guidance.Begin(path);
        RefreshAlignmentStatus();
    }

    private void RefreshAlignmentStatus()
    {
        if (anchorTracker != null && anchorTracker.IsCalibrated) return;
        hud.SetStatus(AlignmentMessage());
    }

    private void HandleGuidanceUpdate(GuidanceUpdate update)
    {
        if (finished) return;

        string distance = update.status == GuidanceStatus.Arrived ? "" : $"{Mathf.CeilToInt(update.remainingMeters)} m left";
        hud.SetInstruction(update.instruction, distance);

        bool showArrow = update.status == GuidanceStatus.OnRoute || update.status == GuidanceStatus.OffRoute;
        hud.SetArrowVisible(showArrow);
        if (showArrow) hud.SetArrowAngle(update.directionAngle);

        if (update.status != GuidanceStatus.WaitingForMarker)
        {
            hud.SetStatus(update.status == GuidanceStatus.OffRoute
                ? "Walk back towards the green path."
                : "Follow the green path on the floor.");
        }
    }

    private void HandleArrived()
    {
        finished = true;
        hud.SetArrowVisible(false);
        hud.ShowResult("You have arrived", destination, Leave);
    }

    // ───────────────────────── Shared ─────────────────────────

    private void HandleBack()
    {
        if (leaving) return;

        bool unsavedPoints = mode == NavigationMode.Record && !finished && recorder.PointCount > 0;
        if (unsavedPoints) hud.ShowConfirm("Discard this recording? The route will not be saved.", Leave);
        else Leave();
    }

    private void Leave()
    {
        if (leaving) return;
        leaving = true;

        if (recorder != null) recorder.Cancel();
        if (guidance != null) guidance.Stop();
        SessionLog.Mark("AR scene left normally.");
        AppSession.ReturnToMenu();
    }

    private string AlignmentMessage()
    {
        if (anchorTracker != null && anchorTracker.IsDirectlyVisible)
        {
            return $"Marker found. Hold steady… {Mathf.RoundToInt(anchorTracker.CalibrationProgress * 100f)}%";
        }
        return "Point the camera at the marker.";
    }

    private void HandleTrackingChanged()
    {
        RefreshTrackingBanner();
        if (mode == NavigationMode.Record) RefreshRecordingHud();
    }

    private void RefreshTrackingBanner()
    {
        if (trackingMonitor == null || hud == null) return;

        string message = trackingMonitor.Hint;
        if (string.IsNullOrEmpty(message) && trackingMonitor.NeedsRealignment && anchorTracker.IsCalibrated)
        {
            message = "Tracking was interrupted. If the path looks wrong, go back and re-scan the marker.";
        }
        hud.SetTrackingBanner(message);
    }

    private void HandleOriginCorrected(float meters)
    {
        if (meters >= 0.3f) UINotifier.Show($"Route re-aligned to the marker ({meters:F1} m).", NoticeKind.Info, 2f);
    }

    private void HandleVuforiaInitialized(VuforiaInitError error)
    {
        if (error == VuforiaInitError.NONE) return;

        AppLog.Error(Tag, "Vuforia failed to start: " + error);
        string message;
        string actionLabel = null;
        System.Action action = null;

        switch (error)
        {
            case VuforiaInitError.PERMISSION_ERROR:
                message = "Camera access is needed for AR navigation. Allow the camera permission in Settings.";
                actionLabel = "OPEN SETTINGS";
                action = CameraPermission.OpenAppSettings;
                break;
            case VuforiaInitError.DEVICE_NOT_SUPPORTED:
                message = "This phone does not support AR navigation.";
                break;
            case VuforiaInitError.LICENSE_CONFIG_NO_NETWORK_PERMANENT:
            case VuforiaInitError.LICENSE_CONFIG_NO_NETWORK_TRANSIENT:
                message = "AR needs an internet connection to start. Connect and try again.";
                break;
            case VuforiaInitError.LICENSE_ERROR:
            case VuforiaInitError.LICENSE_CONFIG_MISSING_KEY:
            case VuforiaInitError.LICENSE_CONFIG_INVALID_KEY:
            case VuforiaInitError.LICENSE_CONFIG_KEY_CANCELED:
            case VuforiaInitError.LICENSE_CONFIG_PRODUCT_TYPE_MISMATCH:
            case VuforiaInitError.LICENSE_CONFIG_BAD_REQUEST:
            case VuforiaInitError.LICENSE_CONFIG_UNKNOWN:
                message = $"AR license problem ({error}). Please update the app.";
                break;
            default:
                message = $"AR could not start ({error}). Restart the app and try again.";
                break;
        }

        hud.ShowError(message, actionLabel, action, Leave);
    }

    private void HandleVuforiaError(VuforiaEngineError error)
    {
        AppLog.Error(Tag, "Vuforia runtime error: " + error);
        UINotifier.Show("AR stopped unexpectedly. Exit and try again.", NoticeKind.Error, 0f, "EXIT", Leave);
    }
}
