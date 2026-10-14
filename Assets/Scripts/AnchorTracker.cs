using System;
using UnityEngine;
using Vuforia;

/// <summary>
/// Manages the rock-solid origin anchor for BMU AR navigation:
/// - When the camera physically tracks the compass target (on floor or wall),
///   locks world origin at that physical marker pose.
/// - Calibrated origin is kept on an independent root GameObject ("Calibrated_World_Origin"),
///   so Vuforia's DefaultObserverEventHandler can NEVER cull or hide it when walking away.
/// - SLAM/Device tracking maintains this origin position across hallways and corridors.
/// - If the compass target is scanned again later, applies smooth drift correction.
/// </summary>
public class AnchorTracker : MonoBehaviour
{
    [Header("Target Observer")]
    [SerializeField] private ObserverBehaviour targetObserver;
    [SerializeField] private Transform physicalAnchor;

    [Header("Drift Re-alignment")]
    [Tooltip("Max distance threshold (in meters) to apply smooth recalibration when anchor is re-seen")]
    [SerializeField] private float maxRecalibrateDistance = 2.5f;

    // Events
    public event Action OnOriginCalibrated;
    public event Action OnOriginRecalibrated;
    public event Action<bool> OnVisualVisibilityChanged;

    // State
    public bool IsCalibrated { get; private set; } = false;
    public bool IsDirectlyVisible { get; private set; } = false;
    public Status CurrentStatus { get; private set; } = Status.NO_POSE;

    // Calibrated world origin (completely independent root GameObject)
    private Transform calibratedOrigin;
    public Transform StabilizedOrigin
    {
        get
        {
            EnsureCalibratedOrigin();
            return calibratedOrigin;
        }
    }

    private void Awake()
    {
        if (targetObserver == null)
        {
            targetObserver = GetComponent<ObserverBehaviour>();
        }
        if (physicalAnchor == null)
        {
            physicalAnchor = transform;
        }

        EnsureCalibratedOrigin();
    }

    private void Start()
    {
        if (targetObserver != null)
        {
            targetObserver.OnTargetStatusChanged += HandleStatusChanged;
            UpdateTrackingStatus(targetObserver.TargetStatus.Status);
        }
        else
        {
            Debug.LogWarning("[AnchorTracker] No ObserverBehaviour assigned on " + gameObject.name);
        }

        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
    }

    private void OnDestroy()
    {
        if (targetObserver != null)
        {
            targetObserver.OnTargetStatusChanged -= HandleStatusChanged;
        }

        if (VuforiaApplication.Instance != null)
        {
            VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;
        }
    }

    private void OnVuforiaStarted()
    {
        Debug.Log("[AnchorTracker] Vuforia Engine Started. TargetObserver valid: " + (targetObserver != null));
        if (targetObserver != null)
        {
            Debug.Log($"[AnchorTracker] Tracking Target '{targetObserver.TargetName}', Current Status: {targetObserver.TargetStatus.Status}");
        }
    }

    private void EnsureCalibratedOrigin()
    {
        if (calibratedOrigin == null)
        {
            // CRITICAL: Created as an independent root GameObject in the scene.
            // NEVER parented under ImageTarget_Compass!
            // This guarantees Vuforia's DefaultObserverEventHandler can NEVER disable our visuals.
            GameObject originObj = GameObject.Find("Calibrated_World_Origin");
            if (originObj == null)
            {
                originObj = new GameObject("Calibrated_World_Origin");
            }
            calibratedOrigin = originObj.transform;
            calibratedOrigin.SetParent(null, true);

            if (physicalAnchor != null)
            {
                calibratedOrigin.position = physicalAnchor.position;
                calibratedOrigin.rotation = physicalAnchor.rotation;
            }
        }
    }

    private void HandleStatusChanged(ObserverBehaviour observer, TargetStatus targetStatus)
    {
        UpdateTrackingStatus(targetStatus.Status);
    }

    private void UpdateTrackingStatus(Status newStatus)
    {
        CurrentStatus = newStatus;

        // Direct visual lock means camera physically sees the image target RIGHT NOW
        bool wasDirectlyVisible = IsDirectlyVisible;
        IsDirectlyVisible = (newStatus == Status.TRACKED);

        if (wasDirectlyVisible != IsDirectlyVisible)
        {
            OnVisualVisibilityChanged?.Invoke(IsDirectlyVisible);
        }

        // ── ORIGIN CALIBRATION & DRIFT-RESET LOGIC ──
        if (IsDirectlyVisible && physicalAnchor != null)
        {
            EnsureCalibratedOrigin();

            if (!IsCalibrated)
            {
                // 1. FIRST-TIME CALIBRATION: Lock origin at physical marker pose
                calibratedOrigin.position = physicalAnchor.position;
                calibratedOrigin.rotation = physicalAnchor.rotation;
                IsCalibrated = true;

                OnOriginCalibrated?.Invoke();
                Debug.Log($"[AnchorTracker] ✓ ORIGIN CALIBRATED at {calibratedOrigin.position}!");
            }
            else
            {
                // 2. RESCAN DRIFT-RESET: If re-scanning the physical target, eliminate SLAM drift
                float driftDist = Vector3.Distance(calibratedOrigin.position, physicalAnchor.position);
                if (driftDist < maxRecalibrateDistance)
                {
                    calibratedOrigin.position = physicalAnchor.position;
                    calibratedOrigin.rotation = physicalAnchor.rotation;
                    OnOriginRecalibrated?.Invoke();
                    Debug.Log($"[AnchorTracker] ✓ DRIFT-RESET: Origin re-aligned! Corrected {driftDist * 100f:F1} cm drift.");
                }
            }
        }

        // NOTE: When walking away (Status becomes EXTENDED_TRACKED or LIMITED),
        // we intentionally DO NOTHING to calibratedOrigin!
        // It stays 100% frozen at the physical anchor's real-world coordinates.
        // Device SLAM tracks the camera moving through world space relative to this rock-solid origin.
    }

    public void ForceRecalibrate()
    {
        IsCalibrated = false;
        if (physicalAnchor != null && IsDirectlyVisible)
        {
            UpdateTrackingStatus(CurrentStatus);
        }
    }

    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        return StabilizedOrigin.InverseTransformPoint(worldPoint);
    }

    public Vector3 TransformPoint(Vector3 localPoint)
    {
        return StabilizedOrigin.TransformPoint(localPoint);
    }
}
