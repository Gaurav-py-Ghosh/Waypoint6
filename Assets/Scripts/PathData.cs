using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a single waypoint along a recorded path.
/// Coordinates are stored LOCAL to the Vuforia Image Target origin (0,0,0).
/// </summary>
[Serializable]
public class PathWaypoint
{
    public float x;
    public float y;
    public float z;

    public float rotX;
    public float rotY;
    public float rotZ;
    public float rotW;

    public PathWaypoint() { }

    public PathWaypoint(Vector3 localPos, Quaternion localRot)
    {
        x = localPos.x;
        y = localPos.y;
        z = localPos.z;

        rotX = localRot.x;
        rotY = localRot.y;
        rotZ = localRot.z;
        rotW = localRot.w;
    }

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }

    public Quaternion GetRotation()
    {
        return new Quaternion(rotX, rotY, rotZ, rotW);
    }
}

/// <summary>
/// Contains complete recorded path data for a specific campus destination.
/// </summary>
[Serializable]
public class PathData
{
    public string destinationName;
    public string createdDate;
    public float totalDistanceMeters;
    public List<PathWaypoint> waypoints = new List<PathWaypoint>();

    public PathData() { }

    public PathData(string destName)
    {
        destinationName = destName;
        createdDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        waypoints = new List<PathWaypoint>();
    }

    public void AddWaypoint(Vector3 localPos, Quaternion localRot)
    {
        waypoints.Add(new PathWaypoint(localPos, localRot));
        CalculateTotalDistance();
    }

    public void CalculateTotalDistance()
    {
        totalDistanceMeters = 0f;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            totalDistanceMeters += Vector3.Distance(
                waypoints[i].GetPosition(),
                waypoints[i + 1].GetPosition()
            );
        }
    }

    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

    public static PathData FromJson(string json)
    {
        return JsonUtility.FromJson<PathData>(json);
    }
}
