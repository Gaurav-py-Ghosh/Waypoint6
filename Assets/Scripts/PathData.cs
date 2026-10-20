using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One point of a recorded route on the floor, in the marker's gravity-aligned
/// frame (see AnchorTracker.StabilizedOrigin): x/z are metres, y is "up".
/// </summary>
[Serializable]
public struct PathPoint
{
    public float x;
    public float z;

    public PathPoint(float x, float z)
    {
        this.x = x;
        this.z = z;
    }

    public Vector2 ToVector2() => new Vector2(x, z);
}

/// <summary>
/// A recorded route from the marker to one campus destination.
/// </summary>
[Serializable]
public class PathData
{
    /// <summary>
    /// Version 2: gravity-aligned origin, floor-height stored once, breadcrumb points.
    /// Version 1 files (full marker rotation, tapped anchors only) are not compatible.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The printed marker routes are measured from. Change it whenever the marker image
    /// changes, so routes recorded against the old marker are ignored instead of misplaced.
    /// </summary>
    // "-b": the tree marker was briefly configured 17.6 cm wide instead of 20 cm; routes from then are scaled wrong.
    public const string CurrentMarkerId = "tree-pin-2026-10-b";

    public int version;
    public string markerId;
    public string destinationName;
    public string createdUtc;

    /// <summary>"Floor" or "Wall": how the marker was mounted when this route was recorded.</summary>
    public string markerMount;

    /// <summary>Floor height in the origin frame (metres, usually negative for wall markers).</summary>
    public float floorHeight;

    public float totalDistanceMeters;
    public List<PathPoint> points = new List<PathPoint>();

    public PathData() { }

    public PathData(string destination, MarkerMount mount)
    {
        version = CurrentVersion;
        markerId = CurrentMarkerId;
        destinationName = destination;
        createdUtc = DateTime.UtcNow.ToString("o");
        markerMount = mount.ToString();
        points = new List<PathPoint>();
    }

    public bool IsValid =>
        version == CurrentVersion &&
        markerId == CurrentMarkerId &&
        !string.IsNullOrEmpty(destinationName) &&
        points != null && points.Count >= 2;

    public List<Vector2> GetPoints2D()
    {
        var list = new List<Vector2>(points.Count);
        foreach (PathPoint p in points) list.Add(p.ToVector2());
        return list;
    }

    public void SetPoints(List<Vector2> newPoints)
    {
        points = new List<PathPoint>(newPoints.Count);
        foreach (Vector2 p in newPoints) points.Add(new PathPoint(p.x, p.y));
        totalDistanceMeters = PathMath.Length(newPoints);
    }

    public string ToJson() => JsonUtility.ToJson(this, false);

    public static PathData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonUtility.FromJson<PathData>(json);
        }
        catch (Exception e)
        {
            AppLog.Warn("PathData", "Could not parse route JSON: " + e.Message);
            return null;
        }
    }
}
