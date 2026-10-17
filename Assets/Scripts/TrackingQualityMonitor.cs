using System;
using UnityEngine;
using Vuforia;

public enum TrackingQuality
{
    Initializing,
    Good,
    Limited,
    Lost
}

/// <summary>
/// Watches Vuforia device tracking (ARCore on Android) and turns its status into
/// a quality level plus a short hint for the user. Also flags when the world
/// may have shifted, so the user is asked to re-scan the marker.
/// </summary>
public class TrackingQualityMonitor : MonoBehaviour
{
    private const string Tag = "TrackingQuality";

    [Tooltip("Seconds of poor tracking after which the route may be misaligned.")]
    [SerializeField] private float poorTrackingRealignSeconds = 3f;

    [Tooltip("Seconds tracking must stay good before it counts as reliable again.")]
    [SerializeField] private float becomeReliableSeconds = 0.6f;

    [Tooltip("Seconds tracking must stay poor before it stops counting as reliable.")]
    [SerializeField] private float becomeUnreliableSeconds = 0.4f;

    [SerializeField] private AnchorTracker anchorTracker;

    public TrackingQuality Quality { get; private set; } = TrackingQuality.Initializing;
    public StatusInfo Reason { get; private set; } = StatusInfo.INITIALIZING;

    /// <summary>
    /// True when device tracking is good enough to record or follow a route.
    /// Debounced, so brief status flickers do not pause recording or flash the banner.
    /// </summary>
    public bool IsReliable { get; private set; }

    /// <summary>True when tracking was reset or poor for a while; cleared when the marker is seen again.</summary>
    public bool NeedsRealignment { get; private set; }

    /// <summary>Hint for the user, or null when tracking is reliable.</summary>
    public string Hint => IsReliable ? null : rawHint;

    public event Action OnQualityChanged;

    private DevicePoseBehaviour devicePose;
    private string rawHint = "Starting tracking… move the phone slowly.";
    private float poorSince = -1f;
    private float goodSince = -1f;

    void Awake()
    {
        if (anchorTracker == null) anchorTracker = FindAnyObjectByType<AnchorTracker>();
    }

    void Start()
    {
        if (VuforiaApplication.Instance.IsRunning) HookDevicePose();
        else VuforiaApplication.Instance.OnVuforiaStarted += HookDevicePose;

        if (anchorTracker != null) anchorTracker.OnVisualVisibilityChanged += HandleMarkerVisibility;
    }

    void OnDestroy()
    {
        if (VuforiaApplication.Instance != null) VuforiaApplication.Instance.OnVuforiaStarted -= HookDevicePose;
        if (devicePose != null) devicePose.OnTargetStatusChanged -= HandleStatusChanged;
        if (anchorTracker != null) anchorTracker.OnVisualVisibilityChanged -= HandleMarkerVisibility;
    }

    void Update()
    {
        if (!IsReliable && goodSince >= 0f && Time.time - goodSince >= becomeReliableSeconds)
        {
            SetReliable(true);
        }
        else if (IsReliable && poorSince >= 0f && Time.time - poorSince >= becomeUnreliableSeconds)
        {
            SetReliable(false);
        }

        if (!NeedsRealignment && poorSince >= 0f && Time.time - poorSince >= poorTrackingRealignSeconds)
        {
            SetNeedsRealignment(true);
        }
    }

    private void HookDevicePose()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= HookDevicePose;
        if (devicePose != null) return;

        devicePose = VuforiaBehaviour.Instance != null ? VuforiaBehaviour.Instance.DevicePoseBehaviour : null;
        if (devicePose == null)
        {
            AppLog.Error(Tag, "Device tracking is not available. Enable 'Device Tracker' in Vuforia Configuration.");
            Apply(TrackingQuality.Lost, StatusInfo.UNKNOWN);
            return;
        }

        if (!devicePose.enabled) devicePose.enabled = true;
        devicePose.OnTargetStatusChanged += HandleStatusChanged;
        HandleStatusChanged(devicePose, devicePose.TargetStatus);

        FusionProviderType provider = VuforiaRuntimeUtilities.GetActiveFusionProvider();
        AppLog.Info(Tag, "Fusion provider: " + provider);
        if (provider == FusionProviderType.VISION_ONLY)
        {
            AppLog.Warn(Tag, "ARCore is not active; tracking will drift more.");
        }
    }

    private void HandleStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        TrackingQuality quality;
        switch (status.Status)
        {
            case Status.TRACKED:
                quality = status.StatusInfo == StatusInfo.NORMAL ? TrackingQuality.Good : TrackingQuality.Limited;
                break;
            case Status.EXTENDED_TRACKED:
            case Status.LIMITED:
                quality = status.StatusInfo == StatusInfo.INITIALIZING ? TrackingQuality.Initializing : TrackingQuality.Limited;
                break;
            default:
                quality = status.StatusInfo == StatusInfo.INITIALIZING ? TrackingQuality.Initializing : TrackingQuality.Lost;
                break;
        }

        // Relocalizing means ARCore may move the world when it recovers.
        if (status.StatusInfo == StatusInfo.RELOCALIZING) SetNeedsRealignment(true);

        Apply(quality, status.StatusInfo);
    }

    private void Apply(TrackingQuality quality, StatusInfo reason)
    {
        bool changed = quality != Quality || reason != Reason;
        Quality = quality;
        Reason = reason;

        if (quality == TrackingQuality.Good)
        {
            poorSince = -1f;
            if (goodSince < 0f) goodSince = Time.time;
        }
        else
        {
            // Keep the last problem's hint while tracking settles back to good.
            rawHint = HintFor(quality, reason);
            goodSince = -1f;
            if (poorSince < 0f) poorSince = Time.time;
        }

        if (changed)
        {
            AppLog.Info(Tag, $"Device tracking: {quality} ({reason}).");
            OnQualityChanged?.Invoke();
        }
    }

    private void HandleMarkerVisibility(bool visible)
    {
        if (visible && NeedsRealignment) SetNeedsRealignment(false);
    }

    private void SetReliable(bool value)
    {
        if (IsReliable == value) return;
        IsReliable = value;
        OnQualityChanged?.Invoke();
    }

    private void SetNeedsRealignment(bool value)
    {
        if (NeedsRealignment == value) return;
        NeedsRealignment = value;
        OnQualityChanged?.Invoke();
    }

    private static string HintFor(TrackingQuality quality, StatusInfo reason)
    {
        if (quality == TrackingQuality.Good) return null;

        switch (reason)
        {
            case StatusInfo.INITIALIZING: return "Starting tracking… move the phone slowly.";
            case StatusInfo.EXCESSIVE_MOTION: return "Moving too fast. Slow down.";
            case StatusInfo.INSUFFICIENT_FEATURES: return "Point the camera at walls and floor with more detail.";
            case StatusInfo.INSUFFICIENT_LIGHT: return "Too dark for tracking. Find better light.";
            case StatusInfo.RELOCALIZING: return "Tracking lost. Point the camera around you.";
            default: return quality == TrackingQuality.Lost ? "Tracking lost. Point the camera around you." : "Tracking is weak. Move slowly.";
        }
    }
}
