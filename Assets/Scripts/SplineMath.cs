using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catmull-Rom spline calculations for smooth AR pathway generation on the floor.
/// </summary>
public static class SplineMath
{
    public static List<Vector3> GenerateSmoothPath(List<Vector3> controlPoints, int pointsPerSegment = 8)
    {
        List<Vector3> smooth = new List<Vector3>();
        if (controlPoints == null || controlPoints.Count == 0) return smooth;
        if (controlPoints.Count == 1)
        {
            smooth.Add(controlPoints[0]);
            return smooth;
        }
        if (controlPoints.Count == 2)
        {
            for (int i = 0; i <= pointsPerSegment; i++)
            {
                float t = (float)i / pointsPerSegment;
                smooth.Add(Vector3.Lerp(controlPoints[0], controlPoints[1], t));
            }
            return smooth;
        }

        int count = controlPoints.Count;
        for (int i = 0; i < count - 1; i++)
        {
            Vector3 p0 = i == 0 ? controlPoints[0] : controlPoints[i - 1];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[i + 1];
            Vector3 p3 = (i + 2 < count) ? controlPoints[i + 2] : p2;

            int steps = (i == count - 2) ? pointsPerSegment : pointsPerSegment - 1;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / pointsPerSegment;
                smooth.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return smooth;
    }

    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    public static float CalculatePathLength(List<Vector3> points)
    {
        if (points == null || points.Count < 2) return 0f;
        float len = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            len += Vector3.Distance(points[i - 1], points[i]);
        }
        return len;
    }

    public static float CalculateTurnAngle(Vector3 fromPrevToCurrent, Vector3 fromCurrentToNext)
    {
        Vector3 v1 = new Vector3(fromPrevToCurrent.x, 0, fromPrevToCurrent.z).normalized;
        Vector3 v2 = new Vector3(fromCurrentToNext.x, 0, fromCurrentToNext.z).normalized;

        if (v1 == Vector3.zero || v2 == Vector3.zero) return 0f;

        float angle = Vector3.SignedAngle(v1, v2, Vector3.up);
        return angle;
    }
}
