using System;
using UnityEngine;
using Vuforia;

public enum MarkerMount
{
    Floor,
    Wall
}

/// <summary>
/// Turns the printed marker into a stable world origin for routes.
///
/// - The origin is gravity-aligned: its Y axis is always world up, whatever way the
///   marker is mounted. Only the marker's heading (yaw) and position are used.
/// - First calibration waits until the marker pose has been steady for a short
///   window, so a blurry first frame cannot skew the whole route.
/// - The origin hangs under a Vuforia world anchor (an ARCore anchor on Android),
///   never under the marker, so Vuforia never hides it when the marker leaves the
///   view, and ARCore keeps it in place even when it relocalizes.
/// - Every time the marker is seen again, the origin is pulled back onto it,
///   which removes the drift built up while walking.
/// </summary>
public class AnchorTracker : MonoBehaviour
{
    private const string Tag = "AnchorTracker";

    [Header("Target Observer")]
    [SerializeField] private ObserverBehaviour targetObserver;
    [SerializeField] private Transform physicalAnchor;

    [Header("Calibration")]
    [Tooltip("Consecutive steady frames needed before the origin is locked.")]
    [SerializeField] private int stableFramesRequired = 12;

    [Tooltip("Max position spread (metres) across the steady window.")]
    [SerializeField] private float stablePositionTolerance = 0.03f;

    [Tooltip("Max heading spread (degrees) across the steady window.")]
    [SerializeField] private float stableHeadingTolerance = 3f;

    [Tooltip("Marker tilt from horizontal (degrees) above which it is treated as wall-mounted.")]
    [SerializeField] private float wallTiltThreshold = 45f;

    [Header("Drift Correction")]
    [Tooltip("Ignore marker sightings from further away than this (metres); far poses are less accurate.")]
    [SerializeField] private float maxCorrectionViewDistance = 3.5f;

    [Tooltip("Corrections smaller than this (metres) are blended in smoothly; larger ones snap.")]
    [SerializeField] private float maxRecalibrateDistance = 2.5f;

    [Tooltip("How quickly small corrections blend in (higher is faster).")]
    [SerializeField] private float correctionBlendSpeed = 6f;

    public event Action OnOriginCalibrated;

    /// <summary>Raised when the origin is corrected after the marker is seen again. Argument: correction in metres.</summary>
    public event Action<float> OnOriginCorrected;

    public event Action<bool> OnVisualVisibilityChanged;

    public bool IsCalibrated { get; private set; }
    public bool IsDirectlyVisible { get; private set; }
    public MarkerMount Mount { get; private set; } = MarkerMount.Floor;

    /// <summary>0..1 while the marker is being held steady before the first calibration.</summary>
    public float CalibrationProgress => IsCalibrated ? 1f : Mathf.Clamp01((float)sampleCount / stableFramesRequired);

    /// <summary>Gravity-aligned origin that routes are stored relative to.</summary>
    public Transform StabilizedOrigin
    {
        get
        {
            EnsureCalibratedOrigin();
            return calibratedOrigin;
        }
    }

    private Transform calibratedOrigin;
    private AnchorBehaviour worldAnchor;
    private Camera arCamera;

    private Vector3[] positionSamples;
    private Vector3[] headingSamples;
    private int sampleCount;
    private int sampleWriteIndex;

    private bool hasCorrectionTarget;
    private Vector3 correctionTargetPosition;
    private Quaternion correctionTargetRotation;
    private float lastReportedCorrectionTime = -10f;

    void Awake()
    {
        if (targetObserver == null) targetObserver = GetComponent<ObserverBehaviour>();
        if (physicalAnchor == null) physicalAnchor = transform;

        positionSamples = new Vector3[Mathf.Max(2, stableFramesRequired)];
        headingSamples = new Vector3[positionSamples.Length];
        EnsureCalibratedOrigin();
    }

    void Start()
    {
        if (targetObserver == null)
        {
            AppLog.Error(Tag, "No ObserverBehaviour assigned on " + gameObject.name);
        }
        arCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (calibratedOrigin != null) Destroy(calibratedOrigin.gameObject);
        if (worldAnchor != null) Destroy(worldAnchor.gameObject);
    }

    void Update()
    {
        if (targetObserver == null || physicalAnchor == null) return;

        bool visible = targetObserver.TargetStatus.Status == Status.TRACKED;
        if (visible != IsDirectlyVisible)
        {
            IsDirectlyVisible = visible;
            if (!visible) ResetSamples();
            OnVisualVisibilityChanged?.Invoke(visible);
        }

        if (visible)
        {
            AddSample(physicalAnchor.position, physicalAnchor.rotation);

            if (!IsCalibrated)
            {
                if (IsSampleWindowStable(out Vector3 position, out Quaternion rotation))
                {
                    PlaceOrigin(position, rotation);
                    IsCalibrated = true;
                    AppLog.Info(Tag, $"Origin calibrated ({Mount} marker).");
                    OnOriginCalibrated?.Invoke();
                }
            }
            else if (IsCloseEnoughForCorrection() && IsSampleWindowStable(out Vector3 position, out Quaternion rotation))
            {
                hasCorrectionTarget = true;
                correctionTargetPosition = position;
                correctionTargetRotation = rotation;
            }
        }

        ApplyCorrection();
    }

    /// <summary>Forgets the current origin; the next steady sighting calibrates again.</summary>
    public void ForceRecalibrate()
    {
        IsCalibrated = false;
        hasCorrectionTarget = false;
        ResetSamples();
    }

    public Vector3 InverseTransformPoint(Vector3 worldPoint) => StabilizedOrigin.InverseTransformPoint(worldPoint);

    public Vector3 TransformPoint(Vector3 localPoint) => StabilizedOrigin.TransformPoint(localPoint);

    /// <summary>Distance from the camera to the marker's last known position (metres).</summary>
    public float DistanceFromCamera()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || !IsCalibrated) return float.PositiveInfinity;
        return Vector3.Distance(arCamera.transform.position, calibratedOrigin.position);
    }

    private void ApplyCorrection()
    {
        if (!hasCorrectionTarget || !IsCalibrated) return;

        float error = Vector3.Distance(calibratedOrigin.position, correctionTargetPosition);
        float angleError = Quaternion.Angle(calibratedOrigin.rotation, correctionTargetRotation);

        if (error > maxRecalibrateDistance)
        {
            // Large jump: device tracking was reset or drifted badly. The marker is the truth.
            PlaceOrigin(correctionTargetPosition, correctionTargetRotation);
            hasCorrectionTarget = false;
            AppLog.Info(Tag, $"Origin snapped back onto marker ({error:F2} m).");
            OnOriginCorrected?.Invoke(error);
            return;
        }

        float blend = 1f - Mathf.Exp(-correctionBlendSpeed * Time.deltaTime);
        calibratedOrigin.SetPositionAndRotation(
            Vector3.Lerp(calibratedOrigin.position, correctionTargetPosition, blend),
            Quaternion.Slerp(calibratedOrigin.rotation, correctionTargetRotation, blend));

        if (error < 0.002f && angleError < 0.2f)
        {
            hasCorrectionTarget = false;
        }

        // Report noticeable corrections, at most once a second.
        if (error > 0.15f && Time.time - lastReportedCorrectionTime > 1f)
        {
            lastReportedCorrectionTime = Time.time;
            OnOriginCorrected?.Invoke(error);
        }
    }

    private bool IsCloseEnoughForCorrection()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return false;
        return Vector3.Distance(arCamera.transform.position, physicalAnchor.position) <= maxCorrectionViewDistance;
    }

    private void AddSample(Vector3 position, Quaternion rotation)
    {
        Vector3 markerUp = rotation * Vector3.up;
        float tilt = Vector3.Angle(markerUp, Vector3.up);

        // Classify the mount once per calibration, so the heading rule cannot flip mid-route.
        if (!IsCalibrated && sampleCount == 0)
        {
            Mount = (tilt > wallTiltThreshold && tilt < 180f - wallTiltThreshold) ? MarkerMount.Wall : MarkerMount.Floor;
        }

        // Floor marker: heading = marker's forward edge. Wall marker: heading = into the wall.
        Vector3 rawHeading = Mount == MarkerMount.Floor ? rotation * Vector3.forward : -markerUp;
        Vector3 heading = Vector3.ProjectOnPlane(rawHeading, Vector3.up);
        if (heading.sqrMagnitude < 1e-4f) return; // Degenerate pose (marker seen exactly edge-on).

        positionSamples[sampleWriteIndex] = position;
        headingSamples[sampleWriteIndex] = heading.normalized;
        sampleWriteIndex = (sampleWriteIndex + 1) % positionSamples.Length;
        sampleCount = Mathf.Min(sampleCount + 1, positionSamples.Length);
    }

    private bool IsSampleWindowStable(out Vector3 meanPosition, out Quaternion rotation)
    {
        meanPosition = Vector3.zero;
        rotation = Quaternion.identity;
        if (sampleCount < positionSamples.Length) return false;

        Vector3 meanHeading = Vector3.zero;
        for (int i = 0; i < sampleCount; i++)
        {
            meanPosition += positionSamples[i];
            meanHeading += headingSamples[i];
        }
        meanPosition /= sampleCount;
        if (meanHeading.sqrMagnitude < 1e-6f) return false;
        meanHeading.Normalize();

        for (int i = 0; i < sampleCount; i++)
        {
            if (Vector3.Distance(positionSamples[i], meanPosition) > stablePositionTolerance) return false;
            if (Vector3.Angle(headingSamples[i], meanHeading) > stableHeadingTolerance) return false;
        }

        rotation = Quaternion.LookRotation(meanHeading, Vector3.up);
        return true;
    }

    private void ResetSamples()
    {
        sampleCount = 0;
        sampleWriteIndex = 0;
    }

    /// <summary>
    /// Puts the origin at a world pose, under a fresh Vuforia anchor when device
    /// tracking supports it. Small later corrections move only the origin's local
    /// offset under that anchor.
    /// </summary>
    private void PlaceOrigin(Vector3 position, Quaternion rotation)
    {
        EnsureCalibratedOrigin();

        AnchorBehaviour newAnchor = null;
        try
        {
            if (VuforiaBehaviour.Instance != null && VuforiaBehaviour.Instance.ObserverFactory != null)
            {
                newAnchor = VuforiaBehaviour.Instance.ObserverFactory.CreateAnchorBehaviour("CampusOriginAnchor", position, rotation);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not create world anchor: " + e.Message);
        }

        if (newAnchor != null)
        {
            // The anchor may not have a tracked pose yet; start it at the requested
            // pose so the origin is right on this frame. Vuforia updates it afterwards.
            newAnchor.transform.SetPositionAndRotation(position, rotation);
            calibratedOrigin.SetParent(newAnchor.transform, false);
            calibratedOrigin.localPosition = Vector3.zero;
            calibratedOrigin.localRotation = Quaternion.identity;
        }
        else
        {
            // No anchor support: keep a plain world-space origin.
            calibratedOrigin.SetParent(null, false);
            calibratedOrigin.SetPositionAndRotation(position, rotation);
        }

        if (worldAnchor != null) Destroy(worldAnchor.gameObject);
        worldAnchor = newAnchor;
    }

    private void EnsureCalibratedOrigin()
    {
        if (calibratedOrigin != null) return;

        // Independent root object, never parented under the marker, so Vuforia's
        // observer event handler cannot disable the route visuals.
        calibratedOrigin = new GameObject("Calibrated_World_Origin").transform;
        calibratedOrigin.SetParent(null, false);
        calibratedOrigin.SetPositionAndRotation(physicalAnchor != null ? physicalAnchor.position : Vector3.zero, Quaternion.identity);
    }
}
