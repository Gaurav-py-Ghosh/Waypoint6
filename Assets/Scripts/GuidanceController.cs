using System;
using System.Collections.Generic;
using UnityEngine;

public enum GuidanceStatus
{
    WaitingForMarker,
    OnRoute,
    OffRoute,
    Arrived
}

/// <summary>
/// Snapshot of guidance for the HUD.
/// </summary>
public struct GuidanceUpdate
{
    public GuidanceStatus status;
    public float remainingMeters;
    public float totalMeters;
    public string instruction;

    /// <summary>Angle (degrees) from the camera's heading to the way forward. Positive is to the right.</summary>
    public float directionAngle;
}

/// <summary>
/// Follows the user along a saved route: tracks progress, finds the next turn,
/// detects leaving the route and arriving, and redraws the route still ahead.
/// </summary>
public class GuidanceController : MonoBehaviour
{
    private const string Tag = "Guidance";

    [Header("References")]
    public Transform arCamera;
    public AnchorTracker anchorTracker;
    public ARPathRenderer pathRenderer;
    public FloorEstimator floorEstimator;

    [Header("Thresholds (metres)")]
    [SerializeField] private float arrivalDistance = 1.5f;
    [SerializeField] private float offRouteDistance = 3f;
    [SerializeField] private float backOnRouteDistance = 2f;
    [SerializeField] private float turnNowDistance = 3f;
    [SerializeField] private float lookAheadDistance = 3f;

    [Tooltip("Seconds the user must be away from the route before 'off route' shows.")]
    [SerializeField] private float offRouteDelay = 2f;

    [Tooltip("Progress updates per second.")]
    [SerializeField] private float updateRate = 10f;

    [Tooltip("Redraw the remaining route after moving this far along it (metres).")]
    [SerializeField] private float redrawDistance = 0.75f;

    public event Action<GuidanceUpdate> OnGuidanceUpdated;
    public event Action OnArrived;

    public bool IsActive => route != null;
    public string Destination => route != null ? route.destinationName : null;
    public float TotalDistance => route != null ? route.totalDistanceMeters : 0f;

    private PathData route;
    private List<Vector2> points;
    private float[] cumulative;
    private List<PathTurn> turns;

    private GuidanceStatus status;
    private float progress;
    private float lastDrawnProgress = float.NegativeInfinity;
    private float offRouteSince = -1f;
    private float nextUpdateTime;

    void Awake()
    {
        if (arCamera == null && Camera.main != null) arCamera = Camera.main.transform;
        if (anchorTracker == null) anchorTracker = FindAnyObjectByType<AnchorTracker>();
        if (pathRenderer == null) pathRenderer = FindAnyObjectByType<ARPathRenderer>();
        if (floorEstimator == null) floorEstimator = FindAnyObjectByType<FloorEstimator>();
    }

    void OnEnable()
    {
        if (anchorTracker != null) anchorTracker.OnOriginCalibrated += HandleCalibrated;
    }

    void OnDisable()
    {
        if (anchorTracker != null) anchorTracker.OnOriginCalibrated -= HandleCalibrated;
    }

    public void Begin(PathData path)
    {
        if (path == null || !path.IsValid)
        {
            AppLog.Error(Tag, "Cannot guide: route is missing or invalid.");
            return;
        }

        route = path;
        points = path.GetPoints2D();
        cumulative = PathMath.CumulativeDistances(points);
        turns = PathMath.FindTurns(points);
        progress = 0f;
        lastDrawnProgress = float.NegativeInfinity;
        offRouteSince = -1f;
        status = GuidanceStatus.WaitingForMarker;
        if (floorEstimator != null) floorEstimator.ResetEstimate();
        if (pathRenderer != null) pathRenderer.ClearAll();

        if (anchorTracker != null && anchorTracker.IsCalibrated) HandleCalibrated();
        else Publish(BuildUpdate(0f));

        AppLog.Info(Tag, $"Guiding to '{path.destinationName}': {path.totalDistanceMeters:F1} m, {turns.Count} turns.");
    }

    public void Stop()
    {
        route = null;
        if (pathRenderer != null) pathRenderer.ClearAll();
    }

    void Update()
    {
        if (route == null || status == GuidanceStatus.WaitingForMarker || status == GuidanceStatus.Arrived) return;
        if (Time.time < nextUpdateTime) return;
        nextUpdateTime = Time.time + 1f / Mathf.Max(1f, updateRate);

        if (!TryGetUserPosition(out Vector2 position, out Vector2 facing)) return;

        // Prefer the stretch of route near the last known progress, so routes that
        // pass close to themselves do not make progress jump.
        PathProjection projection = PathMath.Project(points, cumulative, position, progress - 5f, progress + 15f);
        if (projection.segmentIndex < 0 || projection.lateralDistance > offRouteDistance)
        {
            PathProjection global = PathMath.Project(points, cumulative, position);
            if (global.segmentIndex >= 0 && global.lateralDistance < projection.lateralDistance) projection = global;
        }
        if (projection.segmentIndex < 0) return;

        progress = projection.distanceAlong;
        UpdateOffRoute(projection.lateralDistance);

        float remaining = Mathf.Max(0f, route.totalDistanceMeters - progress);
        float toEnd = Vector2.Distance(position, points[points.Count - 1]);
        if (toEnd <= arrivalDistance || (remaining <= arrivalDistance && projection.lateralDistance <= offRouteDistance))
        {
            Arrive();
            return;
        }

        if (Mathf.Abs(progress - lastDrawnProgress) >= redrawDistance) DrawRemaining();

        GuidanceUpdate update = BuildUpdate(remaining);
        Vector2 target = status == GuidanceStatus.OffRoute
            ? projection.closestPoint
            : PathMath.PointAt(points, cumulative, progress + lookAheadDistance);
        update.directionAngle = PathMath.SignedTurnAngle(facing, target - position);
        Publish(update);
    }

    private void HandleCalibrated()
    {
        if (route == null || status != GuidanceStatus.WaitingForMarker) return;

        status = GuidanceStatus.OnRoute;
        nextUpdateTime = 0f;
        DrawRemaining();
        Publish(BuildUpdate(route.totalDistanceMeters));
    }

    private void UpdateOffRoute(float lateral)
    {
        if (status == GuidanceStatus.OffRoute)
        {
            if (lateral <= backOnRouteDistance)
            {
                status = GuidanceStatus.OnRoute;
                offRouteSince = -1f;
            }
            return;
        }

        if (lateral > offRouteDistance)
        {
            if (offRouteSince < 0f) offRouteSince = Time.time;
            else if (Time.time - offRouteSince >= offRouteDelay) status = GuidanceStatus.OffRoute;
        }
        else
        {
            offRouteSince = -1f;
        }
    }

    private void Arrive()
    {
        status = GuidanceStatus.Arrived;
        progress = route.totalDistanceMeters;
        DrawRemaining();
        Publish(BuildUpdate(0f));
        AppLog.Info(Tag, $"Arrived at '{route.destinationName}'.");
        OnArrived?.Invoke();
    }

    private void DrawRemaining()
    {
        if (pathRenderer == null || route == null) return;
        lastDrawnProgress = progress;

        List<Vector2> ahead = PathMath.Remaining(points, cumulative, progress);
        pathRenderer.RenderGuide(ahead, points[points.Count - 1], route.destinationName, FloorY);
    }

    private float FloorY
    {
        get
        {
            // A floor marker gives an exact floor; otherwise prefer a live measurement
            // over the value saved with the route.
            if (floorEstimator == null) return route.floorHeight;
            float live = floorEstimator.LocalFloorY;
            return floorEstimator.Source == FloorSource.Assumed ? route.floorHeight : live;
        }
    }

    private GuidanceUpdate BuildUpdate(float remaining)
    {
        var update = new GuidanceUpdate
        {
            status = status,
            remainingMeters = remaining,
            totalMeters = route.totalDistanceMeters
        };

        switch (status)
        {
            case GuidanceStatus.WaitingForMarker:
                update.instruction = "Scan the marker to start";
                break;
            case GuidanceStatus.OffRoute:
                update.instruction = "Off route. Head back to the path";
                break;
            case GuidanceStatus.Arrived:
                update.instruction = $"You have arrived at {route.destinationName}";
                break;
            default:
                update.instruction = NextInstruction(remaining);
                break;
        }
        return update;
    }

    private string NextInstruction(float remaining)
    {
        foreach (PathTurn turn in turns)
        {
            float distance = turn.distanceAlong - progress;
            if (distance < -0.5f) continue;

            string side = turn.IsRight ? "right" : "left";
            string kind = turn.IsSlight ? $"Bear {side}" : $"Turn {side}";
            return distance <= turnNowDistance ? $"{kind} now" : $"{kind} in {Mathf.RoundToInt(distance)} m";
        }
        return remaining <= 10f ? $"Destination ahead, {Mathf.RoundToInt(remaining)} m" : "Continue straight";
    }

    private bool TryGetUserPosition(out Vector2 position, out Vector2 facing)
    {
        position = Vector2.zero;
        facing = Vector2.up;
        if (arCamera == null || anchorTracker == null || !anchorTracker.IsCalibrated) return false;

        Transform origin = anchorTracker.StabilizedOrigin;
        Vector3 local = origin.InverseTransformPoint(arCamera.position);
        Vector3 forward = origin.InverseTransformDirection(arCamera.forward);
        position = new Vector2(local.x, local.z);

        var flat = new Vector2(forward.x, forward.z);
        // Phone pointed straight down: fall back to the phone's top edge.
        if (flat.sqrMagnitude < 0.01f)
        {
            Vector3 up = origin.InverseTransformDirection(arCamera.up);
            flat = new Vector2(up.x, up.z);
        }
        if (flat.sqrMagnitude > 1e-6f) facing = flat.normalized;
        return true;
    }

    private void Publish(GuidanceUpdate update)
    {
        OnGuidanceUpdated?.Invoke(update);
    }
}
