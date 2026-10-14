using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles live AR recording of a physical campus path using AnchorTracker:
/// - Screen-center floor raycast projects anchors cleanly onto the hallway floor.
/// - Supports both wall-mounted and floor-mounted calibration targets.
/// - Calibrated origin remains 100% frozen; waypoints are stored in anchor-local coordinates.
/// </summary>
public class ARPathRecorder : MonoBehaviour
{
    public enum RecordingProjectionMode
    {
        CenterScreenFloorRaycast,
        CameraFootprint
    }

    [Header("References")]
    [Tooltip("The Vuforia ARCamera representing the user's position.")]
    public Transform arCamera;

    [Tooltip("The AnchorTracker managing the stabilized world origin.")]
    public AnchorTracker anchorTracker;

    [Tooltip("The path renderer for live visualization.")]
    public ARPathRenderer pathRenderer;

    [Header("Floor Projection Settings")]
    public RecordingProjectionMode projectionMode = RecordingProjectionMode.CenterScreenFloorRaycast;

    [Tooltip("Estimated eye level height from floor in meters (used to locate ground plane).")]
    public float userCameraHeight = 1.4f;

    [Header("Current Recording State")]
    public bool isRecording = false;
    public bool isCalibrated = false;
    public string currentDestination = "";

    public event Action OnOriginLocked;
    public event Action<int, float> OnAnchorAdded; // count, totalDistance

    private PathData currentPath;
    private readonly List<Vector3> recordedLocalPoints = new List<Vector3>();
    private float establishedFloorY = float.NaN;

    void Awake()
    {
        if (arCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null) arCamera = cam.transform;
        }

        if (anchorTracker == null)
        {
            anchorTracker = FindFirstObjectByType<AnchorTracker>();
        }

        if (pathRenderer == null)
        {
            pathRenderer = FindFirstObjectByType<ARPathRenderer>();
        }
    }

    void Start()
    {
        if (anchorTracker != null)
        {
            anchorTracker.OnOriginCalibrated += HandleOriginCalibrated;
            anchorTracker.OnOriginRecalibrated += HandleOriginCalibrated;
        }
    }

    void OnDestroy()
    {
        if (anchorTracker != null)
        {
            anchorTracker.OnOriginCalibrated -= HandleOriginCalibrated;
            anchorTracker.OnOriginRecalibrated -= HandleOriginCalibrated;
        }
    }

    private void HandleOriginCalibrated()
    {
        if (isRecording && !isCalibrated)
        {
            CalibrateOriginInternal();
        }
    }

    /// <summary>
    /// Enters recording mode for a destination. Waits for target calibration if not already calibrated.
    /// </summary>
    public void StartRecording(string destinationName)
    {
        currentDestination = destinationName;
        isRecording = true;
        isCalibrated = false;
        establishedFloorY = float.NaN;
        currentPath = new PathData(destinationName);
        recordedLocalPoints.Clear();

        if (pathRenderer != null)
        {
            pathRenderer.ClearAll();
        }

        if (anchorTracker != null && anchorTracker.IsCalibrated)
        {
            CalibrateOriginInternal();
        }
        else
        {
            Debug.Log($"[ARPathRecorder] Started recording '{destinationName}'. Waiting for compass target scan...");
        }
    }

    public void CalibrateOrigin(Transform targetTransform)
    {
        if (!isRecording || isCalibrated) return;
        CalibrateOriginInternal();
    }

    private void CalibrateOriginInternal()
    {
        isCalibrated = true;
        OnOriginLocked?.Invoke();
        Debug.Log($"[ARPathRecorder] Origin locked for '{currentDestination}'! Ready to drop start anchor on floor.");
    }

    /// <summary>
    /// Projects screen-center raycast to the floor plane and drops an anchor.
    /// Works whether target was scanned on a wall or floor.
    /// </summary>
    public void DropAnchor()
    {
        if (!isRecording || !isCalibrated || arCamera == null || anchorTracker == null)
        {
            Debug.LogWarning("[ARPathRecorder] Cannot drop anchor - not recording or not calibrated yet.");
            return;
        }

        Camera cam = arCamera.GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        // Establish floor height:
        if (float.IsNaN(establishedFloorY))
        {
            // Initial floor height estimated from camera eye-level
            establishedFloorY = arCamera.position.y - userCameraHeight;
        }

        Vector3 worldFloorPos;

        if (projectionMode == RecordingProjectionMode.CenterScreenFloorRaycast && cam != null)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Plane floorPlane = new Plane(Vector3.up, new Vector3(0, establishedFloorY, 0));

            if (floorPlane.Raycast(ray, out float enterDistance) && enterDistance > 0.2f && enterDistance < 30f)
            {
                worldFloorPos = ray.GetPoint(enterDistance);
            }
            else
            {
                // Fallback: 1.5m in front of camera at established floor height
                Vector3 fwd = Vector3.ProjectOnPlane(arCamera.forward, Vector3.up).normalized;
                worldFloorPos = new Vector3(arCamera.position.x, establishedFloorY, arCamera.position.z) + fwd * 1.5f;
            }
        }
        else
        {
            // Camera Footprint fallback
            worldFloorPos = new Vector3(arCamera.position.x, establishedFloorY, arCamera.position.z);
        }

        // Lock floor Y on first anchor so subsequent anchors are coplanar
        if (recordedLocalPoints.Count == 0)
        {
            establishedFloorY = worldFloorPos.y;
        }

        // Convert world floor position into stabilized anchor-local space
        Vector3 localPos = anchorTracker.InverseTransformPoint(worldFloorPos);
        Quaternion localRot = Quaternion.Inverse(anchorTracker.StabilizedOrigin.rotation) * arCamera.rotation;

        currentPath.AddWaypoint(localPos, localRot);
        recordedLocalPoints.Add(localPos);
        currentPath.CalculateTotalDistance();

        if (pathRenderer != null)
        {
            pathRenderer.UpdateRecordingVisuals(recordedLocalPoints, currentDestination);
        }

        OnAnchorAdded?.Invoke(recordedLocalPoints.Count, currentPath.totalDistanceMeters);
        Debug.Log($"[ARPathRecorder] Dropped anchor #{recordedLocalPoints.Count} at world {worldFloorPos} (local {localPos}) | Total: {currentPath.totalDistanceMeters:F1}m");
    }

    /// <summary>
    /// Called when the user reaches the destination and taps 'FINISH & SAVE ROUTE'.
    /// </summary>
    public PathData FinishRecording()
    {
        if (!isRecording || !isCalibrated || currentPath == null || anchorTracker == null)
        {
            Debug.LogWarning("[ARPathRecorder] No active calibrated recording to finish.");
            return null;
        }

        if (recordedLocalPoints.Count == 0)
        {
            Debug.LogWarning("[ARPathRecorder] Cannot finish recording - no anchors placed yet!");
            return null;
        }

        currentPath.CalculateTotalDistance();

        // Save to persistent JSON storage
        PathStorageManager.SavePath(currentPath);

        // Render destination stop marker at end
        if (pathRenderer != null && recordedLocalPoints.Count > 0)
        {
            pathRenderer.UpdateRecordingVisuals(recordedLocalPoints, currentDestination);
            pathRenderer.CreateDestinationMarker(recordedLocalPoints[recordedLocalPoints.Count - 1], currentDestination);
        }

        isRecording = false;
        Debug.Log($"[ARPathRecorder] Successfully saved route '{currentDestination}': {currentPath.totalDistanceMeters:F1}m across {recordedLocalPoints.Count} waypoints.");
        return currentPath;
    }

    public int WaypointCount => recordedLocalPoints.Count;
    public float CurrentDistance => (currentPath != null) ? currentPath.totalDistanceMeters : 0f;

    /// <summary>
    /// Cancels recording without saving.
    /// </summary>
    public void CancelRecording()
    {
        isRecording = false;
        isCalibrated = false;
        establishedFloorY = float.NaN;
        currentPath = null;
        recordedLocalPoints.Clear();

        if (pathRenderer != null)
        {
            pathRenderer.ClearAll();
        }

        Debug.Log("[ARPathRecorder] Recording session cancelled.");
    }
}
