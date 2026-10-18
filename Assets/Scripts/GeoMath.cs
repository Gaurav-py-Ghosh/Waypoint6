using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A latitude/longitude pair in degrees (WGS84).</summary>
[Serializable]
public struct LatLng
{
    public double lat;
    public double lng;

    public LatLng(double lat, double lng)
    {
        this.lat = lat;
        this.lng = lng;
    }

    public override string ToString() => $"{lat:F6},{lng:F6}";
}

/// <summary>
/// Geography helpers: Web Mercator projection (as used by Google Static Maps),
/// distances, and Google's encoded polyline format.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6371008.8;
    private const double TileSize = 256.0;

    /// <summary>World pixel coordinates at a zoom level (origin top-left, y down).</summary>
    public static Vector2d ToWorldPixels(LatLng p, int zoom)
    {
        double scale = TileSize * Math.Pow(2, zoom);
        double sinLat = Math.Sin(p.lat * Math.PI / 180.0);
        sinLat = Math.Min(Math.Max(sinLat, -0.9999), 0.9999);
        double x = (p.lng + 180.0) / 360.0 * scale;
        double y = (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * scale;
        return new Vector2d(x, y);
    }

    /// <summary>
    /// Position of a point on a static map image as 0..1 (x right, y up), for a map
    /// centred on <paramref name="center"/> covering <paramref name="sizePixels"/> logical pixels.
    /// </summary>
    public static Vector2 ToMapUV(LatLng p, LatLng center, int zoom, int sizePixels)
    {
        Vector2d world = ToWorldPixels(p, zoom);
        Vector2d origin = ToWorldPixels(center, zoom);
        double px = world.x - origin.x + sizePixels * 0.5;
        double py = world.y - origin.y + sizePixels * 0.5;
        return new Vector2((float)(px / sizePixels), 1f - (float)(py / sizePixels));
    }

    /// <summary>
    /// The highest zoom at which the box spanned by <paramref name="a"/> and <paramref name="b"/>
    /// fits within <paramref name="fill"/> of a square map of <paramref name="sizePixels"/>.
    /// </summary>
    public static int ZoomToFit(LatLng a, LatLng b, int sizePixels, float fill = 0.7f, int maxZoom = 18, int minZoom = 3)
    {
        for (int zoom = maxZoom; zoom >= minZoom; zoom--)
        {
            Vector2d pa = ToWorldPixels(a, zoom);
            Vector2d pb = ToWorldPixels(b, zoom);
            if (Math.Abs(pa.x - pb.x) <= sizePixels * fill && Math.Abs(pa.y - pb.y) <= sizePixels * fill) return zoom;
        }
        return minZoom;
    }

    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceMeters(LatLng a, LatLng b)
    {
        double dLat = (b.lat - a.lat) * Math.PI / 180.0;
        double dLng = (b.lng - a.lng) * Math.PI / 180.0;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(a.lat * Math.PI / 180.0) * Math.Cos(b.lat * Math.PI / 180.0) *
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    public static double PathLengthMeters(IReadOnlyList<LatLng> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++) total += DistanceMeters(points[i - 1], points[i]);
        return total;
    }

    /// <summary>Decodes Google's encoded polyline format (precision 5).</summary>
    public static List<LatLng> DecodePolyline(string encoded)
    {
        var points = new List<LatLng>();
        if (string.IsNullOrEmpty(encoded)) return points;

        int index = 0, lat = 0, lng = 0;
        while (index < encoded.Length)
        {
            lat += DecodeValue(encoded, ref index);
            if (index >= encoded.Length) break;
            lng += DecodeValue(encoded, ref index);
            points.Add(new LatLng(lat / 1e5, lng / 1e5));
        }
        return points;
    }

    private static int DecodeValue(string encoded, ref int index)
    {
        int result = 0, shift = 0, b;
        do
        {
            b = encoded[index++] - 63;
            result |= (b & 0x1f) << shift;
            shift += 5;
        } while (b >= 0x20 && index < encoded.Length);
        return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
    }
}

/// <summary>Double-precision 2D vector for map projection maths.</summary>
public struct Vector2d
{
    public double x;
    public double y;

    public Vector2d(double x, double y)
    {
        this.x = x;
        this.y = y;
    }
}
