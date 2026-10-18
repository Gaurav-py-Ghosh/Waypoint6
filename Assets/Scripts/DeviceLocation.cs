using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public enum LocationState
{
    Stopped,
    WaitingForPermission,
    PermissionDenied,
    ServicesOff,
    Searching,
    Ready
}

/// <summary>
/// The phone's GPS position for the campus map, via com.bmu.location.LocationBridge on Android.
/// In the Editor it reports a fixed test position near the main gate.
/// </summary>
public static class DeviceLocation
{
    private const string Tag = "DeviceLocation";

    public static LocationState State { get; private set; } = LocationState.Stopped;

    /// <summary>Test position used in the Unity Editor (near the main gate).</summary>
    public static LatLng EditorTestLocation = new LatLng(28.24705, 76.81475);

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaClass bridge;
    private static AndroidJavaObject activity;
#endif

    /// <summary>Asks for permission if needed, then starts updates. Safe to call repeatedly.</summary>
    public static void Start(Action<LocationState> onStateKnown = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (State == LocationState.Ready || State == LocationState.Searching)
        {
            onStateKnown?.Invoke(State);
            return;
        }

        if (Permission.HasUserAuthorizedPermission(Permission.FineLocation) ||
            Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
        {
            Begin();
            onStateKnown?.Invoke(State);
            return;
        }

        State = LocationState.WaitingForPermission;
        var callbacks = new PermissionCallbacks();
        // Callbacks fire once per permission; "approximate only" grants coarse and denies fine.
        Action resolve = () =>
        {
            bool any = Permission.HasUserAuthorizedPermission(Permission.FineLocation) ||
                       Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
            if (any) { if (State != LocationState.Searching && State != LocationState.Ready) Begin(); }
            else State = LocationState.PermissionDenied;
            onStateKnown?.Invoke(State);
        };
        callbacks.PermissionGranted += _ => resolve();
        callbacks.PermissionDenied += _ => resolve();
        callbacks.PermissionRequestDismissed += _ => resolve();
        Permission.RequestUserPermissions(new[] { Permission.FineLocation, Permission.CoarseLocation }, callbacks);
#else
        State = LocationState.Ready;
        onStateKnown?.Invoke(State);
#endif
    }

    public static void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { bridge?.CallStatic("stop"); }
        catch (Exception e) { AppLog.Warn(Tag, "stop failed: " + e.Message); }
#endif
        State = LocationState.Stopped;
    }

    /// <summary>Latest position. Returns false while there is no recent fix.</summary>
    public static bool TryGetLocation(out LatLng location, out float accuracyMeters)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        location = default;
        accuracyMeters = -1f;
        if (bridge == null || State == LocationState.PermissionDenied || State == LocationState.ServicesOff) return false;

        try
        {
            if (!bridge.CallStatic<bool>("hasFix")) return false;
            location = new LatLng(bridge.CallStatic<double>("getLatitude"), bridge.CallStatic<double>("getLongitude"));
            accuracyMeters = bridge.CallStatic<float>("getAccuracy");
            State = LocationState.Ready;
            return true;
        }
        catch (Exception e)
        {
            AppLog.Warn(Tag, "Location read failed: " + e.Message);
            return false;
        }
#else
        location = EditorTestLocation;
        accuracyMeters = 5f;
        return true;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void Begin()
    {
        try
        {
            if (bridge == null) bridge = new AndroidJavaClass("com.bmu.location.LocationBridge");
            if (activity == null)
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
            }

            if (!bridge.CallStatic<bool>("isEnabled", activity))
            {
                State = LocationState.ServicesOff;
                return;
            }

            State = bridge.CallStatic<bool>("start", activity) ? LocationState.Searching : LocationState.ServicesOff;
        }
        catch (Exception e)
        {
            AppLog.Error(Tag, "Could not start location: " + e.Message);
            State = LocationState.ServicesOff;
        }
    }
#endif
}
