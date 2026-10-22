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

public enum WrongWayKind
{
    /// <summary>Walking away from the route's direction.</summary>
    WrongDirection,

    /// <summary>Walking far from the route, whatever the direction.</summary>
    Drifted
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

    [Header("Wrong way")]
    [Tooltip("Walking this far gives one sample of the walking direction (metres). Standing still gives none.")]
    [SerializeField] private float headingSampleDistance = 1.5f;

    [Tooltip("Walking direction at least this far from the route's direction is the wrong way (degrees).")]
    [SerializeField] private float wrongWayAngle = 60f;

    [Tooltip("Seconds of walking the wrong way before it is reported.")]
    [SerializeField] private float wrongWayDelay = 3f;

    [Tooltip("Walking this far from the route is reported whatever the direction (metres).")]
    [SerializeField] private float driftDistance = 8f;

    [Tooltip("Faster than this (m/s) is a tracking jump, not walking.")]
    [SerializeField] private float maxWalkingSpeed = 4f;

    public event Action<GuidanceUpdate> OnGuidanceUpdated;
    public event Action OnStarted;
    public event Action OnArrived;
    public event Action<WrongWayKind> OnWrongWay;
    public event Action OnBackOnRoute;

    public bool IsActive => route != null;
    public string Destination => route != null ? route.destinationName : null;
    public float TotalDistance => route != null ? route.totalDistanceMeters : 0f;
    public float Progress => progress;
    public IReadOnlyList<PathTurn> Turns => turns;
    public float TurnNowDistance => turnNowDistance;
    public bool IsWrongWay => wrongWay;

    private PathData route;
    private List<Vector2> points;
    private float[] cumulative;
    private List<PathTurn> turns;

    private GuidanceStatus status;
    private float progress;
    private float lastDrawnProgress = float.NegativeInfinity;
    private float offRouteSince = -1f;
    private float nextUpdateTime;

    private bool wrongWay;
    private float wrongHeadingSince = -1f;
    private bool hasHeadingSample;
    private Vector2 headingSample;
    private float headingSampleTime;

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
        wrongWay = false;
        wrongHeadingSince = -1f;
        hasHeadingSample = false;
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

        if (!TryGetUserPosition(out Vector2 position)) return;

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
        UpdateWrongWay(position, projection);

        float remaining = Mathf.Max(0f, route.totalDistanceMeters - progress);
        float toEnd = Vector2.Distance(position, points[points.Count - 1]);
        if (toEnd <= arrivalDistance || (remaining <= arrivalDistance && projection.lateralDistance <= offRouteDistance))
        {
            Arrive();
            return;
        }

        if (Mathf.Abs(progress - lastDrawnProgress) >= redrawDistance) DrawRemaining();

        Publish(BuildUpdate(remaining));
    }

    private void HandleCalibrated()
    {
        if (route == null || status != GuidanceStatus.WaitingForMarker) return;

        status = GuidanceStatus.OnRoute;
        nextUpdateTime = 0f;
        DrawRemaining();
        OnStarted?.Invoke();
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

    /// <summary>
    /// Stricter than off route: only walking clearly away from the route, or very
    /// far from it, counts. Uses the direction actually walked, not where the
    /// phone points, so looking around never triggers it.
    /// </summary>
    private void UpdateWrongWay(Vector2 position, PathProjection projection)
    {
        if (!hasHeadingSample)
        {
            hasHeadingSample = true;
            headingSample = position;
            headingSampleTime = Time.time;
            return;
        }

        Vector2 walked = position - headingSample;
        float walkedDistance = walked.magnitude;
        if (walkedDistance < headingSampleDistance) return;

        float speed = walkedDistance / Mathf.Max(0.01f, Time.time - headingSampleTime);
        headingSample = position;
        headingSampleTime = Time.time;
        if (speed > maxWalkingSpeed) return;

        Vector2 routeDirection = PathMath.PointAt(points, cumulative, projection.distanceAlong + lookAheadDistance) - projection.closestPoint;
        bool wrongHeading = routeDirection.sqrMagnitude > 1e-4f && Vector2.Angle(walked, routeDirection) >= wrongWayAngle;

        if (wrongWay)
        {
            if (!wrongHeading && projection.lateralDistance <= offRouteDistance)
            {
                wrongWay = false;
                wrongHeadingSince = -1f;
                OnBackOnRoute?.Invoke();
            }
            return;
        }

        if (projection.lateralDistance >= driftDistance)
        {
            StartWrongWay(WrongWayKind.Drifted);
        }
        else if (!wrongHeading)
        {
            wrongHeadingSince = -1f;
        }
        else if (wrongHeadingSince < 0f)
        {
            wrongHeadingSince = Time.time;
        }
        else if (Time.time - wrongHeadingSince >= wrongWayDelay)
        {
            StartWrongWay(WrongWayKind.WrongDirection);
        }
    }

    private void StartWrongWay(WrongWayKind kind)
    {
        wrongWay = true;
        wrongHeadingSince = -1f;
        AppLog.Info(Tag, $"Wrong way: {kind}.");
        OnWrongWay?.Invoke(kind);
    }

    private void Arrive()
    {
        status = GuidanceStatus.Arrived;
        wrongWay = false;
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
            string kind = turn.IsSlight ? $"Slight {side}" : $"Turn {side}";
            return distance <= turnNowDistance ? $"{kind} now" : $"{kind} in {Mathf.RoundToInt(distance)} m";
        }
        return remaining <= 10f ? $"Destination ahead, {Mathf.RoundToInt(remaining)} m" : "Continue straight";
    }

    private bool TryGetUserPosition(out Vector2 position)
    {
        position = Vector2.zero;
        if (arCamera == null || anchorTracker == null || !anchorTracker.IsCalibrated) return false;

        Vector3 local = anchorTracker.StabilizedOrigin.InverseTransformPoint(arCamera.position);
        position = new Vector2(local.x, local.z);
        return true;
    }

    private void Publish(GuidanceUpdate update)
    {
        OnGuidanceUpdated?.Invoke(update);
    }
}
