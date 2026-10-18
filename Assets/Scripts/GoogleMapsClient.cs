using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Result of a walking-route request.</summary>
public class WalkingRoute
{
    public List<LatLng> points = new List<LatLng>();
    public double distanceMeters;
    public double durationSeconds;
    /// <summary>True when Google returned a real walking route; false for a straight-line fallback.</summary>
    public bool fromRoutesApi;
}

/// <summary>
/// Minimal Google Maps Platform client: a styled Static Map image and Routes API walking directions.
/// Map images are only kept in memory for the current session (Google's terms do not allow storing them).
/// </summary>
public static class GoogleMapsClient
{
    private const string Tag = "GoogleMaps";
    private const string StaticMapUrl = "https://maps.googleapis.com/maps/api/staticmap";
    private const string RoutesUrl = "https://routes.googleapis.com/directions/v2:computeRoutes";
    private const int TimeoutSeconds = 15;

    /// <summary>App colours: mint ground, white walkways, soft green buildings and parks, no Google labels.</summary>
    private static readonly string[] MapStyle =
    {
        "element:geometry|color:0xf1fbf5",
        "element:labels|visibility:off",
        "feature:administrative|visibility:off",
        "feature:landscape.man_made|element:geometry.fill|color:0xcdeedd",
        "feature:landscape.man_made|element:geometry.stroke|color:0xa3e5c4",
        "feature:landscape.natural|element:geometry|color:0xe8f8ef",
        "feature:poi|element:geometry|color:0xe3f7ec",
        "feature:poi.park|element:geometry|color:0xbfebd3",
        "feature:poi.sports_complex|element:geometry|color:0xbfebd3",
        "feature:road|element:geometry.fill|color:0xffffff",
        "feature:road|element:geometry.stroke|color:0xcdeedd",
        "feature:road.highway|element:geometry.fill|color:0xd6f2e4",
        "feature:road.highway|element:geometry.stroke|color:0xb2ebcf",
        "feature:transit|visibility:off",
        "feature:water|element:geometry|color:0xbfe6f2"
    };

    // In-memory only, for this app session.
    private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    /// <summary>
    /// Downloads a styled map (2x resolution) centred on <paramref name="center"/>.
    /// Calls back with null and an error on failure.
    /// </summary>
    public static IEnumerator FetchMap(LatLng center, int zoom, int size, Action<Texture2D, string> onDone)
    {
        if (!MapsApiConfig.HasKey)
        {
            onDone(null, "Map is not configured (missing API key).");
            yield break;
        }

        size = Mathf.Clamp(size, 100, 640);
        string cacheKey = $"{center}|{zoom}|{size}";
        if (cache.TryGetValue(cacheKey, out Texture2D cached) && cached != null)
        {
            onDone(cached, null);
            yield break;
        }

        var url = new StringBuilder(StaticMapUrl);
        url.Append("?center=").Append(center.ToString());
        url.Append("&zoom=").Append(zoom);
        url.Append("&size=").Append(size).Append('x').Append(size);
        url.Append("&scale=2&format=png&maptype=roadmap");
        foreach (string rule in MapStyle) url.Append("&style=").Append(UnityWebRequest.EscapeURL(rule));
        url.Append("&key=").Append(MapsApiConfig.Instance.apiKey);

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url.ToString()))
        {
            request.timeout = TimeoutSeconds;
            AddAndroidHeaders(request);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                AppLog.Warn(Tag, $"Map download failed: {request.responseCode} {request.error}");
                onDone(null, request.result == UnityWebRequest.Result.ConnectionError
                    ? "No internet connection. The campus map needs internet."
                    : "Could not load the campus map. Try again later.");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.name = "Map " + cacheKey;
            cache[cacheKey] = texture;
            onDone(texture, null);
        }
    }

    /// <summary>Requests a walking route. On failure, falls back to a straight line so the user still gets a direction.</summary>
    public static IEnumerator FetchWalkingRoute(LatLng origin, LatLng destination, Action<WalkingRoute, string> onDone)
    {
        WalkingRoute straight = StraightLine(origin, destination);
        if (!MapsApiConfig.HasKey)
        {
            onDone(straight, "Showing a straight line (routing is not configured).");
            yield break;
        }

        string body = string.Format(CultureInfo.InvariantCulture,
            "{{\"origin\":{{\"location\":{{\"latLng\":{{\"latitude\":{0},\"longitude\":{1}}}}}}}," +
            "\"destination\":{{\"location\":{{\"latLng\":{{\"latitude\":{2},\"longitude\":{3}}}}}}}," +
            "\"travelMode\":\"WALK\",\"polylineQuality\":\"HIGH_QUALITY\"}}",
            origin.lat, origin.lng, destination.lat, destination.lng);

        using (var request = new UnityWebRequest(RoutesUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Goog-Api-Key", MapsApiConfig.Instance.apiKey);
            request.SetRequestHeader("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline");
            AddAndroidHeaders(request);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                AppLog.Warn(Tag, $"Route request failed: {request.responseCode} {request.error}");
                onDone(straight, "Could not get walking directions. Showing a straight line.");
                yield break;
            }

            WalkingRoute route = ParseRoute(request.downloadHandler.text);
            if (route == null || route.points.Count < 2)
            {
                onDone(straight, "No walking route found. Showing a straight line.");
                yield break;
            }

            // Make the line start and end exactly at the user and the pin.
            route.points.Insert(0, origin);
            route.points.Add(destination);
            onDone(route, null);
        }
    }

    private static WalkingRoute ParseRoute(string json)
    {
        try
        {
            RoutesResponse response = JsonUtility.FromJson<RoutesResponse>(json);
            if (response?.routes == null || response.routes.Length == 0) return null;

            Route first = response.routes[0];
            var route = new WalkingRoute
            {
                points = GeoMath.DecodePolyline(first.polyline?.encodedPolyline),
                distanceMeters = first.distanceMeters,
                fromRoutesApi = true
            };

            // Duration comes as e.g. "312s".
            string duration = first.duration;
            if (!string.IsNullOrEmpty(duration) && duration.EndsWith("s") &&
                double.TryParse(duration.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                route.durationSeconds = seconds;
            }
            if (route.durationSeconds <= 0) route.durationSeconds = route.distanceMeters / 1.3;
            return route;
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Could not parse route: " + e.Message);
            return null;
        }
    }

    private static WalkingRoute StraightLine(LatLng origin, LatLng destination)
    {
        double distance = GeoMath.DistanceMeters(origin, destination);
        return new WalkingRoute
        {
            points = new List<LatLng> { origin, destination },
            distanceMeters = distance,
            durationSeconds = distance / 1.3,
            fromRoutesApi = false
        };
    }

    /// <summary>Lets keys restricted to the Android app accept these requests.</summary>
    private static void AddAndroidHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("X-Android-Package", Application.identifier);
        string sha1 = MapsApiConfig.Instance != null ? MapsApiConfig.Instance.androidCertSha1 : null;
        if (!string.IsNullOrEmpty(sha1)) request.SetRequestHeader("X-Android-Cert", sha1.Replace(":", "").ToUpperInvariant());
    }

    // JSON shapes for JsonUtility.
    [Serializable] private class RoutesResponse { public Route[] routes; }
    [Serializable] private class Route { public double distanceMeters; public string duration; public Polyline polyline; }
    [Serializable] private class Polyline { public string encodedPolyline; }
}
