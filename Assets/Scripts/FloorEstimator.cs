using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public enum FloorSource
{
    /// <summary>No measurement yet; using an assumed phone height.</summary>
    Assumed,

    /// <summary>Measured by Vuforia Ground Plane hit tests.</summary>
    GroundPlane,

    /// <summary>The marker itself lies on the floor: exact.</summary>
    Marker
}

/// <summary>
/// Works out where the floor is, in the marker origin's frame.
/// Best source first: a floor-mounted marker, then Vuforia Ground Plane hit
/// tests (ARCore planes), then an assumed phone height as a last resort.
/// </summary>
public class FloorEstimator : MonoBehaviour
{
    private const string Tag = "FloorEstimator";

    [SerializeField] private AnchorTracker anchorTracker;
    [SerializeField] private PlaneFinderBehaviour planeFinder;

    [Tooltip("Assumed phone height above the floor (metres) when nothing better is known.")]
    [SerializeField] private float assumedCameraHeight = 1.35f;

    [Tooltip("Accepted range of phone height above a detected plane (metres); rejects tables and desks.")]
    [SerializeField] private Vector2 acceptedCameraHeight = new Vector2(0.7f, 2.2f);

    [SerializeField] private int sampleWindow = 30;

    public FloorSource Source { get; private set; } = FloorSource.Assumed;

    private readonly List<float> localSamples = new List<float>();
    private readonly List<float> sortBuffer = new List<float>();
    private float assumedLocalFloor = float.NaN;
    private Camera arCamera;

    void Awake()
    {
        if (anchorTracker == null) anchorTracker = FindAnyObjectByType<AnchorTracker>();
        if (planeFinder == null) planeFinder = FindAnyObjectByType<PlaneFinderBehaviour>();
    }

    void OnEnable()
    {
        if (planeFinder != null)
        {
            planeFinder.HitTestMode = HitTestMode.AUTOMATIC;
            planeFinder.OnAutomaticHitTest.AddListener(OnHitTest);
        }
        else
        {
            AppLog.Warn(Tag, "No PlaneFinderBehaviour in scene; floor height will be estimated.");
        }
    }

    void OnDisable()
    {
        if (planeFinder != null) planeFinder.OnAutomaticHitTest.RemoveListener(OnHitTest);
    }

    void Start()
    {
        arCamera = Camera.main;
    }

    /// <summary>Floor height in the origin frame. Only meaningful once the marker is calibrated.</summary>
    public float LocalFloorY
    {
        get
        {
            if (anchorTracker != null && anchorTracker.Mount == MarkerMount.Floor)
            {
                Source = FloorSource.Marker;
                return 0f;
            }

            if (localSamples.Count > 0)
            {
                Source = FloorSource.GroundPlane;
                return Median(localSamples);
            }

            Source = FloorSource.Assumed;
            if (float.IsNaN(assumedLocalFloor)) assumedLocalFloor = EstimateFromCameraHeight();
            return assumedLocalFloor;
        }
    }

    /// <summary>Call when a new recording or guidance session starts.</summary>
    public void ResetEstimate()
    {
        localSamples.Clear();
        assumedLocalFloor = float.NaN;
    }

    private void OnHitTest(HitTestResult result)
    {
        if (result == null || anchorTracker == null || !anchorTracker.IsCalibrated) return;
        if (anchorTracker.Mount == MarkerMount.Floor) return; // Marker is already exact.

        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        float heightBelowCamera = arCamera.transform.position.y - result.Position.y;
        if (heightBelowCamera < acceptedCameraHeight.x || heightBelowCamera > acceptedCameraHeight.y) return;

        float localY = anchorTracker.InverseTransformPoint(result.Position).y;
        localSamples.Add(localY);
        if (localSamples.Count > sampleWindow) localSamples.RemoveAt(0);

        if (localSamples.Count == 1) AppLog.Info(Tag, "Floor detected by Ground Plane.");
    }

    private float EstimateFromCameraHeight()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || anchorTracker == null) return -assumedCameraHeight;

        Vector3 floorWorld = arCamera.transform.position + Vector3.down * assumedCameraHeight;
        return anchorTracker.InverseTransformPoint(floorWorld).y;
    }

    private float Median(List<float> values)
    {
        sortBuffer.Clear();
        sortBuffer.AddRange(values);
        sortBuffer.Sort();
        int mid = sortBuffer.Count / 2;
        return sortBuffer.Count % 2 == 1 ? sortBuffer[mid] : 0.5f * (sortBuffer[mid - 1] + sortBuffer[mid]);
    }
}
