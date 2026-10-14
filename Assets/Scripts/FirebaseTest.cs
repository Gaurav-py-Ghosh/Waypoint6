using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;

public class FirebaseTest : MonoBehaviour
{
    public static FirebaseAuth Auth { get; private set; }
    public static bool IsReady { get; private set; }

    void Start()
    {
        IsReady = false;

        Debug.Log("FirebaseTest: Starting Firebase initialization...");

        FirebaseApp.CheckAndFixDependenciesAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError(
                        "FirebaseTest: Firebase dependency check was canceled."
                    );
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "FirebaseTest: Firebase dependency check failed: "
                        + task.Exception
                    );
                    return;
                }

                if (task.Result == DependencyStatus.Available)
                {
                    FirebaseApp app =
                        FirebaseApp.DefaultInstance;

                    Auth =
                        FirebaseAuth.DefaultInstance;

                    IsReady = true;

                    Debug.Log(
                        "FIREBASE INITIALIZED SUCCESSFULLY"
                    );

                    Debug.Log(
                        "Firebase Auth is ready."
                    );
                }
                else
                {
                    Debug.LogError(
                        "Firebase dependency error: "
                        + task.Result
                    );
                }
            });
    }
}