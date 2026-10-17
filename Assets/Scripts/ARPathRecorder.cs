using System;
using System.Collections.Generic;
using UnityEngine;

public enum RecorderState
{
    Idle,
    WaitingForMarker,
    Ready,
    Recording
}

/// <summary>
/// Records a route from points the user places on the floor: aim the ring at the
/// floor and drop a point, or tap the floor on screen. The route is the line
/// through the points, in the marker origin's gravity-aligned frame.
/// </summary>
public class ARPathRecorder : MonoBehaviour
{
    private const string Tag = "ARPathRecorder";

    [Header("References")]
    [Tooltip("The Vuforia ARCamera. Defaults to Camera.main.")]
    public Transform arCamera;
    public AnchorTracker anchorTracker;
    public ARPathRenderer pathRenderer;
    public FloorEstimator floorEstimator;
    public TrackingQualityMonitor trackingMonitor;

    [Header("Placement")]
    [Tooltip("Points can be placed on the floor between these distances from the phone (metres).")]
    [SerializeField] private Vector2 placementRange = new Vector2(0.3f, 8f);

    [Tooltip("The aim must point at least this far below the horizon (degrees); far, flat hits are inaccurate.")]
    [SerializeField] private float minDownAngle = 8f;

    [Tooltip("Points closer than this to the previous point are rejected (metres).")]
    [SerializeField] private float minPointSpacing = 0.3f;

    [Tooltip("Shortest route that can be saved (metres).")]
    [SerializeField] private float minRouteLength = 2f;

    [SerializeField] private int maxPoints = 500;

    public RecorderState State { get; private set; } = RecorderState.Idle;
    public string Destination { get; private set; } = "";
    public float RecordedDistance { get; private set; }
    public int PointCount => points.Count;

    /// <summary>True when the centre of the screen is aimed at the floor this frame.</summary>
    public bool HasAim { get; private set; }

    public event Action OnStateChanged;

    private readonly List<Vector2> points = new List<Vector2>();
    private Camera cameraComponent;
    private Vector2 aimPoint;

    void Awake()
    {
        if (arCamera == null && Camera.main != null) arCamera = Camera.main.transform;
        if (anchorTracker == null) anchorTracker = FindAnyObjectByType<AnchorTracker>();
        if (pathRenderer == null) pathRenderer = FindAnyObjectByType<ARPathRenderer>();
        if (floorEstimator == null) floorEstimator = FindAnyObjectByType<FloorEstimator>();
        if (trackingMonitor == null) trackingMonitor = FindAnyObjectByType<TrackingQualityMonitor>();
        if (arCamera != null) cameraComponent = arCamera.GetComponent<Camera>();
    }

    void OnEnable()
    {
        if (anchorTracker != null) anchorTracker.OnOriginCalibrated += HandleCalibrated;
    }

    void OnDisable()
    {
        if (anchorTracker != null) anchorTracker.OnOriginCalibrated -= HandleCalibrated;
    }

    /// <summary>Enters recording mode. The marker must be scanned before points can be placed.</summary>
    public void Begin(string destination)
    {
        Destination = destination;
        points.Clear();
        RecordedDistance = 0f;
        if (pathRenderer != null) pathRenderer.ClearAll();
        if (floorEstimator != null) floorEstimator.ResetEstimate();

        SetState(anchorTracker != null && anchorTracker.IsCalibrated ? RecorderState.Ready : RecorderState.WaitingForMarker);
    }

    /// <summary>Places a point where the centre of the screen meets the floor.</summary>
    public bool DropAtAim(out string error)
    {
        if (!HasAim)
        {
            error = "Aim the ring at the floor, 0.3–8 m in front of you.";
            return false;
        }
        return AddPoint(aimPoint, out error);
    }

    /// <summary>Places a point where a screen position meets the floor (a tap).</summary>
    public bool DropAtScreenPoint(Vector2 screenPosition, out string error)
    {
        if (!TryRaycastFloor(screenPosition, out Vector2 point))
        {
            error = "Tap on the floor, 0.3–8 m in front of you.";
            return false;
        }
        return AddPoint(point, out error);
    }

    /// <summary>Removes the last point. Returns false when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (State != RecorderState.Recording || points.Count == 0) return false;

        points.RemoveAt(points.Count - 1);
        RecordedDistance = PathMath.Length(points);
        if (points.Count == 0) SetState(RecorderState.Ready);
        else OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Finishes and saves the route. Returns the saved route, or null with an
    /// error message for the user.
    /// </summary>
    public PathData Finish(out string error)
    {
        error = null;
        if (State != RecorderState.Recording || points.Count < 2)
        {
            error = "Place at least two points: one at the start and one at the destination.";
            return null;
        }

        if (RecordedDistance < minRouteLength)
        {
            error = $"Route is too short. It must be at least {minRouteLength:F0} m.";
            return null;
        }

        var data = new PathData(Destination, anchorTracker != null ? anchorTracker.Mount : MarkerMount.Floor)
        {
            floorHeight = CurrentFloorY
        };
        data.SetPoints(points);

        if (!PathStorageManager.SavePath(data))
        {
            error = "Could not save the route. Check free storage and try again.";
            return null;
        }

        AppLog.Info(Tag, $"Saved '{Destination}': {data.totalDistanceMeters:F1} m, {points.Count} points.");
        points.Clear();
        HasAim = false;
        SetState(RecorderState.Idle);
        return data;
    }

    public void Cancel()
    {
        points.Clear();
        RecordedDistance = 0f;
        HasAim = false;
        if (pathRenderer != null) pathRenderer.ClearAll();
        if (State != RecorderState.Idle) AppLog.Info(Tag, "Recording cancelled.");
        SetState(RecorderState.Idle);
    }

    void Update()
    {
        if (State != RecorderState.Ready && State != RecorderState.Recording) return;

        HasAim = cameraComponent != null &&
                 TryRaycastFloor(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out aimPoint);

        if (pathRenderer != null)
        {
            pathRenderer.RenderRecording(points, CurrentFloorY, HasAim ? aimPoint : (Vector2?)null, IsTrackingReliable());
        }
    }

    private bool AddPoint(Vector2 point, out string error)
    {
        error = null;
        if (State != RecorderState.Ready && State != RecorderState.Recording)
        {
            error = "Scan the marker first.";
            return false;
        }
        if (!IsTrackingReliable())
        {
            error = "Tracking is weak. Hold still for a moment, then try again.";
            return false;
        }
        if (points.Count >= maxPoints)
        {
            error = "This route has the maximum number of points.";
            return false;
        }
        if (points.Count > 0 && Vector2.Distance(points[points.Count - 1], point) < minPointSpacing)
        {
            error = "Too close to the last point.";
            return false;
        }

        points.Add(point);
        RecordedDistance = PathMath.Length(points);
        AppLog.Info(Tag, $"Point {points.Count} placed ({RecordedDistance:F1} m).");

        if (State == RecorderState.Ready) SetState(RecorderState.Recording);
        else OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>Intersects a screen ray with the floor, returning the hit in the origin frame (x, z).</summary>
    private bool TryRaycastFloor(Vector2 screenPosition, out Vector2 point)
    {
        point = Vector2.zero;
        if (cameraComponent == null || anchorTracker == null || !anchorTracker.IsCalibrated) return false;

        Transform origin = anchorTracker.StabilizedOrigin;
        Ray ray = cameraComponent.ScreenPointToRay(screenPosition);

        // Reject aims near or above the horizon: the hit would be far and unreliable.
        if (Vector3.Dot(ray.direction, -origin.up) < Mathf.Sin(minDownAngle * Mathf.Deg2Rad)) return false;

        var floor = new Plane(origin.up, origin.TransformPoint(new Vector3(0f, CurrentFloorY, 0f)));
        if (!floor.Raycast(ray, out float distance)) return false;
        if (distance < placementRange.x || distance > placementRange.y) return false;

        Vector3 local = origin.InverseTransformPoint(ray.GetPoint(distance));
        point = new Vector2(local.x, local.z);
        return true;
    }

    private float CurrentFloorY => floorEstimator != null ? floorEstimator.LocalFloorY : 0f;

    private bool IsTrackingReliable()
    {
        return trackingMonitor == null || trackingMonitor.IsReliable;
    }

    private void HandleCalibrated()
    {
        if (State == RecorderState.WaitingForMarker) SetState(RecorderState.Ready);
    }

    private void SetState(RecorderState state)
    {
        if (State == state) return;
        State = state;
        OnStateChanged?.Invoke();
    }
}
