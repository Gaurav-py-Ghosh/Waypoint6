using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A marked spot on the campus map.</summary>
[Serializable]
public class CampusPlace
{
    [Tooltip("Must match the destination name used by Select Destination (e.g. \"Library\").")]
    public string id;

    [Tooltip("Name shown on the map.")]
    public string displayName;

    public double latitude;
    public double longitude;

    [Tooltip("Untick until the coordinates are known; unplaced spots are hidden on the map.")]
    public bool placed = true;

    public LatLng Location => new LatLng(latitude, longitude);
}

/// <summary>
/// The campus map: where it is, how far to zoom, and the marked spots.
/// Edit at Assets/Resources/CampusMapConfig.asset (Tools > BMU AR > Edit Campus Map).
/// To get a spot's coordinates: long-press it in Google Maps and copy the numbers.
/// </summary>
[CreateAssetMenu(fileName = "CampusMapConfig", menuName = "BMU AR/Campus Map Config")]
public class CampusMapConfig : ScriptableObject
{
    public const string ResourcePath = "CampusMapConfig";

    [Header("Map")]
    public double centerLatitude = 28.2479;
    public double centerLongitude = 76.8134;

    [Range(15, 19)] public int zoom = 17;

    [Tooltip("Static map size in logical pixels (max 640). Downloaded at 2x for sharpness.")]
    public int mapSizePixels = 640;

    [Header("Campus")]
    [Tooltip("Routes start from here when the user is off campus or has no GPS fix.")]
    public string entrancePlaceId = "Reception";

    [Tooltip("Users further than this from the map centre (metres) count as off campus.")]
    public float onCampusRadiusMeters = 800f;

    public List<CampusPlace> places = new List<CampusPlace>();

    private static CampusMapConfig instance;

    public static CampusMapConfig Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<CampusMapConfig>(ResourcePath);
            return instance;
        }
    }

    public LatLng Center => new LatLng(centerLatitude, centerLongitude);

    public CampusPlace Find(string id)
    {
        foreach (CampusPlace place in places)
        {
            if (string.Equals(place.id, id, StringComparison.OrdinalIgnoreCase)) return place;
        }
        return null;
    }

    public bool IsOnCampus(LatLng location) => GeoMath.DistanceMeters(location, Center) <= onCampusRadiusMeters;
}
