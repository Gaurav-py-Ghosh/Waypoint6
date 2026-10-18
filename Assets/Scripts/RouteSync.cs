using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Firebase;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Shares recorded AR routes through Cloud Firestore (collection "routes", one
/// document per destination). Everyone downloads; only admins upload — enforced
/// by Firestore security rules (see firestore.rules in the repo root).
///
/// Uses the Firestore REST API with the signed-in user's Firebase ID token, so
/// no extra Firebase SDK is needed. Routes are cached on the phone for offline use.
/// </summary>
public class RouteSync : MonoBehaviour
{
    private const string Tag = "RouteSync";
    private const string Collection = "routes";
    private const int TimeoutSeconds = 20;
    private const float FirebaseWaitSeconds = 15f;

    private static RouteSync instance;

    /// <summary>Raised after a sync that changed local routes. Argument: number of routes updated.</summary>
    public static event Action<int> OnRoutesUpdated;

    public static bool IsSyncing { get; private set; }

    /// <summary>When the last successful download finished (realtime seconds), or -1.</summary>
    public static float LastSyncTime { get; private set; } = -1f;

    private static RouteSync Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("RouteSync");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<RouteSync>();
            }
            return instance;
        }
    }

    /// <summary>Downloads all shared routes, then uploads any this admin saved while offline.</summary>
    public static void SyncAll()
    {
        if (IsSyncing) return;
        Instance.StartCoroutine(Instance.SyncAllRoutine());
    }

    /// <summary>Syncs unless a sync finished within the last <paramref name="maxAgeSeconds"/>.</summary>
    public static void SyncIfStale(float maxAgeSeconds = 60f)
    {
        if (LastSyncTime >= 0f && Time.realtimeSinceStartup - LastSyncTime < maxAgeSeconds) return;
        SyncAll();
    }

    /// <summary>Shares a route that was just saved on this phone. Calls back with success and a user message.</summary>
    public static void Upload(PathData route, Action<bool, string> onDone = null)
    {
        if (route == null) return;
        PathStorageManager.MarkPendingUpload(route.destinationName);
        Instance.StartCoroutine(Instance.UploadRoutine(route, onDone));
    }

    // ───────────────────────── Download ─────────────────────────

    private IEnumerator SyncAllRoutine()
    {
        IsSyncing = true;
        try
        {
            string projectId = null;
            yield return WaitForFirebase(id => projectId = id);
            if (projectId == null) yield break;

            string token = null;
            yield return GetIdToken(t => token = t);

            var remote = new List<PathData>();
            string pageToken = null;
            do
            {
                string url = $"{DocumentsUrl(projectId)}?pageSize=300" + (pageToken != null ? "&pageToken=" + UnityWebRequest.EscapeURL(pageToken) : "");
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = TimeoutSeconds;
                    if (token != null) request.SetRequestHeader("Authorization", "Bearer " + token);
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        AppLog.Warn(Tag, $"Download failed: {request.responseCode} {Describe(request)}");
                        yield break;
                    }

                    ListResponse page = JsonUtility.FromJson<ListResponse>(request.downloadHandler.text);
                    if (page?.documents != null)
                    {
                        foreach (Document doc in page.documents)
                        {
                            PathData data = PathData.FromJson(doc.fields?.json?.stringValue);
                            if (data != null && data.IsValid) remote.Add(data);
                        }
                    }
                    pageToken = string.IsNullOrEmpty(page?.nextPageToken) ? null : page.nextPageToken;
                }
            } while (pageToken != null);

            int updated = 0;
            foreach (PathData cloud in remote)
            {
                PathData local = PathStorageManager.LoadPath(cloud.destinationName);
                bool pending = PathStorageManager.GetPendingUploads().Contains(cloud.destinationName);
                bool cloudIsNewer = local == null || string.CompareOrdinal(cloud.createdUtc, local.createdUtc) > 0;

                // Keep an unshared local re-recording rather than overwrite it with an older cloud copy.
                if (cloudIsNewer && !(pending && local != null))
                {
                    if (PathStorageManager.SavePath(cloud)) updated++;
                }
            }

            LastSyncTime = Time.realtimeSinceStartup;
            AppLog.Info(Tag, $"Synced {remote.Count} shared routes ({updated} updated on this phone).");
            if (updated > 0) OnRoutesUpdated?.Invoke(updated);

            // Admins: share routes on this phone that the cloud doesn't have (or has older),
            // including ones recorded offline or before sharing existed.
            if (UserSession.IsAdmin && token != null)
            {
                var cloudByName = new Dictionary<string, PathData>();
                foreach (PathData cloud in remote) cloudByName[PathStorageManager.RouteKey(cloud.destinationName)] = cloud;

                foreach (PathData local in PathStorageManager.LoadAll())
                {
                    cloudByName.TryGetValue(PathStorageManager.RouteKey(local.destinationName), out PathData cloud);
                    bool missingOrOlder = cloud == null || string.CompareOrdinal(local.createdUtc, cloud.createdUtc) > 0;
                    if (missingOrOlder) yield return UploadRoutine(local, null);
                }

                // Pending entries whose route no longer exists locally.
                foreach (string destination in PathStorageManager.GetPendingUploads())
                {
                    if (PathStorageManager.LoadPath(destination) == null) PathStorageManager.ClearPendingUpload(destination);
                }
            }
        }
        finally
        {
            IsSyncing = false;
        }
    }

    // ───────────────────────── Upload ─────────────────────────

    private IEnumerator UploadRoutine(PathData route, Action<bool, string> onDone)
    {
        string projectId = null;
        yield return WaitForFirebase(id => projectId = id);
        if (projectId == null)
        {
            onDone?.Invoke(false, "Saved on this phone. It will be shared when you're online.");
            yield break;
        }

        string token = null;
        yield return GetIdToken(t => token = t);
        if (token == null)
        {
            onDone?.Invoke(false, "Saved on this phone. Sign in as an admin to share it.");
            yield break;
        }

        var body = new UploadDocument
        {
            fields = new Fields
            {
                json = new StringValue { stringValue = route.ToJson() },
                destination = new StringValue { stringValue = route.destinationName },
                updatedUtc = new StringValue { stringValue = route.createdUtc },
                updatedBy = new StringValue { stringValue = UserSession.Email ?? "" }
            }
        };

        string url = $"{DocumentsUrl(projectId)}/{UnityWebRequest.EscapeURL(PathStorageManager.RouteKey(route.destinationName))}";
        using (var request = new UnityWebRequest(url, "PATCH"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                PathStorageManager.ClearPendingUpload(route.destinationName);
                AppLog.Info(Tag, $"Shared route '{route.destinationName}'.");
                onDone?.Invoke(true, "Route saved and shared with everyone.");
                yield break;
            }

            AppLog.Warn(Tag, $"Upload failed: {request.responseCode} {Describe(request)}");
            string message;
            if (request.responseCode == 403 && Describe(request).Contains("SERVICE_DISABLED"))
                message = "Saved on this phone. Route sharing isn't set up in Firebase yet.";
            else if (request.responseCode == 403)
                message = "Saved on this phone. Only admins can share routes.";
            else if (request.result == UnityWebRequest.Result.ConnectionError)
                message = "Saved on this phone. It will be shared when you're online.";
            else
                message = "Saved on this phone. Sharing failed; it will retry later.";

            // A permission problem will not fix itself by retrying.
            if (request.responseCode == 403 && !Describe(request).Contains("SERVICE_DISABLED"))
            {
                PathStorageManager.ClearPendingUpload(route.destinationName);
            }
            onDone?.Invoke(false, message);
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static string DocumentsUrl(string projectId) =>
        $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/{Collection}";

    private static IEnumerator WaitForFirebase(Action<string> onProjectId)
    {
        float deadline = Time.realtimeSinceStartup + FirebaseWaitSeconds;
        while (!FirebaseBootstrap.IsReady && !FirebaseBootstrap.IsFailed && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (!FirebaseBootstrap.IsReady)
        {
            AppLog.Warn(Tag, "Firebase not ready; skipping route sync.");
            onProjectId(null);
            yield break;
        }

        string projectId = FirebaseApp.DefaultInstance?.Options?.ProjectId;
        if (string.IsNullOrEmpty(projectId)) AppLog.Warn(Tag, "Firebase project id missing.");
        onProjectId(string.IsNullOrEmpty(projectId) ? null : projectId);
    }

    /// <summary>The signed-in user's Firebase ID token, or null for guests.</summary>
    private static IEnumerator GetIdToken(Action<string> onToken)
    {
        FirebaseUser user = FirebaseBootstrap.Auth?.CurrentUser;
        if (user == null)
        {
            onToken(null);
            yield break;
        }

        var task = user.TokenAsync(false);
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted || task.IsCanceled)
        {
            AppLog.Warn(Tag, "Could not get sign-in token: " + task.Exception?.GetBaseException().Message);
            onToken(null);
            yield break;
        }
        onToken(task.Result);
    }

    private static string Describe(UnityWebRequest request)
    {
        string body = request.downloadHandler != null ? request.downloadHandler.text : null;
        return string.IsNullOrEmpty(body) ? request.error : body;
    }

    // Firestore REST shapes for JsonUtility (strings only; the route is stored as JSON text).
    [Serializable] private class ListResponse { public Document[] documents; public string nextPageToken; }
    [Serializable] private class Document { public string name; public Fields fields; }
    // No "name": Firestore takes the document name from the URL and rejects a mismatching one.
    [Serializable] private class UploadDocument { public Fields fields; }
    [Serializable] private class Fields { public StringValue json; public StringValue destination; public StringValue updatedUtc; public StringValue updatedBy; }
    [Serializable] private class StringValue { public string stringValue; }
}
