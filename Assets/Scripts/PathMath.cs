using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A turn along a route, found from the route's shape.
/// </summary>
public struct PathTurn
{
    /// <summary>Distance from the route start to the turn, in metres.</summary>
    public float distanceAlong;

    /// <summary>Signed heading change in degrees. Positive is a right turn.</summary>
    public float angle;

    public bool IsRight => angle > 0f;
    public bool IsSlight => Mathf.Abs(angle) < 60f;
}

/// <summary>
/// Result of projecting a position onto a route.
/// </summary>
public struct PathProjection
{
    public int segmentIndex;
    public float distanceAlong;
    public float lateralDistance;
    public Vector2 closestPoint;
}

/// <summary>
/// 2D route geometry on the floor plane (x/z). Pure functions, no Unity objects.
/// </summary>
public static class PathMath
{
    public static float Length(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 2) return 0f;
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    /// <summary>Cumulative distance from the start to each point.</summary>
    public static float[] CumulativeDistances(IReadOnlyList<Vector2> points)
    {
        var cumulative = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }
        return cumulative;
    }

    /// <summary>
    /// Ramer-Douglas-Peucker simplification: drops points that lie within
    /// <paramref name="tolerance"/> metres of the simplified line. Keeps both ends.
    /// </summary>
    public static List<Vector2> Simplify(IReadOnlyList<Vector2> points, float tolerance)
    {
        var result = new List<Vector2>();
        if (points == null || points.Count == 0) return result;
        if (points.Count <= 2)
        {
            result.AddRange(points);
            return result;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        // Iterative, so long routes cannot overflow the stack.
        var stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(0, points.Count - 1));
        while (stack.Count > 0)
        {
            Vector2Int range = stack.Pop();
            int first = range.x;
            int last = range.y;
            float maxDistance = 0f;
            int maxIndex = -1;

            for (int i = first + 1; i < last; i++)
            {
                float d = DistanceToSegment(points[i], points[first], points[last], out _);
                if (d > maxDistance)
                {
                    maxDistance = d;
                    maxIndex = i;
                }
            }

            if (maxIndex >= 0 && maxDistance > tolerance)
            {
                keep[maxIndex] = true;
                stack.Push(new Vector2Int(first, maxIndex));
                stack.Push(new Vector2Int(maxIndex, last));
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i]) result.Add(points[i]);
        }
        return result;
    }

    /// <summary>
    /// Finds turns sharper than <paramref name="minAngle"/> degrees. Uses a coarse
    /// simplification first so walking wobble does not count as turns.
    /// </summary>
    public static List<PathTurn> FindTurns(IReadOnlyList<Vector2> points, float minAngle = 30f, float coarseTolerance = 0.75f)
    {
        var turns = new List<PathTurn>();
        List<Vector2> coarse = Simplify(points, coarseTolerance);
        if (coarse.Count < 3) return turns;

        float[] cumulative = CumulativeDistances(coarse);
        for (int i = 1; i < coarse.Count - 1; i++)
        {
            Vector2 inDir = coarse[i] - coarse[i - 1];
            Vector2 outDir = coarse[i + 1] - coarse[i];
            if (inDir.sqrMagnitude < 1e-6f || outDir.sqrMagnitude < 1e-6f) continue;

            float angle = SignedTurnAngle(inDir, outDir);
            if (Mathf.Abs(angle) >= minAngle)
            {
                turns.Add(new PathTurn { distanceAlong = cumulative[i], angle = angle });
            }
        }
        return turns;
    }

    /// <summary>
    /// Heading change from <paramref name="from"/> to <paramref name="to"/> on the
    /// floor plane, in degrees. Positive is clockwise seen from above (a right turn).
    /// </summary>
    public static float SignedTurnAngle(Vector2 from, Vector2 to)
    {
        // Vector2 (x, z): Vector2.SignedAngle is counter-clockwise positive in x/y,
        // which is clockwise seen from above in Unity's x/z ground plane.
        return -Vector2.SignedAngle(from, to);
    }

    /// <summary>
    /// Projects <paramref name="position"/> onto the route. Searches segments within
    /// [minAlong, maxAlong] first; pass the full range to search everywhere.
    /// </summary>
    public static PathProjection Project(IReadOnlyList<Vector2> points, float[] cumulative, Vector2 position,
                                         float minAlong = float.NegativeInfinity, float maxAlong = float.PositiveInfinity)
    {
        var best = new PathProjection { segmentIndex = -1, lateralDistance = float.PositiveInfinity };
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (cumulative[i + 1] < minAlong || cumulative[i] > maxAlong) continue;

            float d = DistanceToSegment(position, points[i], points[i + 1], out float t);
            if (d < best.lateralDistance)
            {
                best.segmentIndex = i;
                best.lateralDistance = d;
                best.closestPoint = Vector2.Lerp(points[i], points[i + 1], t);
                best.distanceAlong = Mathf.Lerp(cumulative[i], cumulative[i + 1], t);
            }
        }
        return best;
    }

    /// <summary>The point at <paramref name="distanceAlong"/> metres from the route start (clamped).</summary>
    public static Vector2 PointAt(IReadOnlyList<Vector2> points, float[] cumulative, float distanceAlong)
    {
        if (points.Count == 0) return Vector2.zero;
        if (distanceAlong <= 0f) return points[0];

        for (int i = 0; i < points.Count - 1; i++)
        {
            if (distanceAlong <= cumulative[i + 1])
            {
                float segment = cumulative[i + 1] - cumulative[i];
                float t = segment > 1e-5f ? (distanceAlong - cumulative[i]) / segment : 0f;
                return Vector2.Lerp(points[i], points[i + 1], t);
            }
        }
        return points[points.Count - 1];
    }

    /// <summary>The part of the route from <paramref name="distanceAlong"/> to the end.</summary>
    public static List<Vector2> Remaining(IReadOnlyList<Vector2> points, float[] cumulative, float distanceAlong)
    {
        var result = new List<Vector2>();
        if (points.Count == 0) return result;

        result.Add(PointAt(points, cumulative, distanceAlong));
        for (int i = 0; i < points.Count; i++)
        {
            if (cumulative[i] > distanceAlong) result.Add(points[i]);
        }
        return result;
    }

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }
}
