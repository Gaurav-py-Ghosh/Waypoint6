using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public enum CameraPermissionResult
{
    Granted,
    Denied,
    PermanentlyDenied
}

/// <summary>
/// Requests the camera permission before the AR scene is loaded, so the user
/// gets a clear message instead of a black AR view.
/// </summary>
public static class CameraPermission
{
    public static void Request(Action<CameraPermissionResult> onResult)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            onResult?.Invoke(CameraPermissionResult.Granted);
            return;
        }

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => onResult?.Invoke(CameraPermissionResult.Granted);
        callbacks.PermissionDenied += _ =>
        {
            // After a denial, Android stops showing the dialog once the user has
            // chosen "Don't ask again"; the rationale flag is then false.
            bool canAskAgain = Permission.ShouldShowRequestPermissionRationale(Permission.Camera);
            onResult?.Invoke(canAskAgain ? CameraPermissionResult.Denied : CameraPermissionResult.PermanentlyDenied);
        };
        callbacks.PermissionRequestDismissed += _ => onResult?.Invoke(CameraPermissionResult.Denied);

        Permission.RequestUserPermission(Permission.Camera, callbacks);
#else
        onResult?.Invoke(CameraPermissionResult.Granted);
#endif
    }

    /// <summary>Opens this app's page in Android Settings so the user can grant the permission.</summary>
    public static void OpenAppSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var uriClass = new AndroidJavaClass("android.net.Uri"))
            using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", uri))
            {
                intent.Call<AndroidJavaObject>("addFlags", 0x10000000)?.Dispose(); // FLAG_ACTIVITY_NEW_TASK
                activity.Call("startActivity", intent);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn("CameraPermission", "Could not open app settings: " + e.Message);
        }
#endif
    }
}
