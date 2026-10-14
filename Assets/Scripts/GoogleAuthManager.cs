using Firebase.Auth;
using Firebase.Extensions;
using System.Collections;
using TMPro;
using UnityEngine;

public class GoogleAuthManager : MonoBehaviour
{
    private FirebaseAuth auth;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass googleAuthBridge;
#endif

    [Header("UI")]
    public UIManager uiManager;
    public TMP_Text signedInText;

    private bool firebaseReady = false;
    private bool signInInProgress = false;

    void Start()
    {
        // Hide the signed-in message when the app starts
        if (signedInText != null)
        {
            signedInText.gameObject.SetActive(false);
        }

        // Wait for FirebaseTest to finish initializing Firebase
        StartCoroutine(WaitForFirebase());

#if UNITY_ANDROID && !UNITY_EDITOR
        InitializeGoogleBridge();
#endif
    }

    private IEnumerator WaitForFirebase()
    {
        Debug.Log(
            "GoogleAuthManager: Waiting for Firebase..."
        );

        // Wait until FirebaseTest reports that Firebase is ready
        while (!FirebaseTest.IsReady)
        {
            yield return null;
        }

        auth = FirebaseTest.Auth;

        if (auth != null)
        {
            firebaseReady = true;

            Debug.Log(
                "GoogleAuthManager: Firebase Auth is ready."
            );
        }
        else
        {
            Debug.LogError(
                "GoogleAuthManager: FirebaseTest reports ready, "
                + "but FirebaseAuth is null."
            );
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR

    private void InitializeGoogleBridge()
    {
        try
        {
            googleAuthBridge =
                new AndroidJavaClass(
                    "com.bmu.googleauth.GoogleAuthBridge"
                );

            using (AndroidJavaClass unityPlayer =
                   new AndroidJavaClass(
                       "com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity =
                       unityPlayer.GetStatic<AndroidJavaObject>(
                           "currentActivity"))
                {
                    googleAuthBridge.CallStatic(
                        "initialize",
                        activity
                    );
                }
            }

            Debug.Log(
                "GoogleAuthManager: Android Google bridge initialized."
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                "GoogleAuthManager: Failed to initialize Google bridge: "
                + e.Message
            );
        }
    }

#endif

    public void SignInWithGoogle()
    {
        Debug.Log(
            "GoogleAuthManager: Google Sign-In button pressed."
        );

        if (!firebaseReady)
        {
            Debug.Log(
                "GoogleAuthManager: Firebase is still initializing. Please wait."
            );
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR

        if (googleAuthBridge == null)
        {
            Debug.LogError(
                "GoogleAuthManager: Google bridge is not initialized."
            );
            return;
        }

        googleAuthBridge.CallStatic("signIn");

#else

        Debug.Log(
            "Google Sign-In works on an Android device build."
        );

#endif
    }

    // Called by GoogleAuthBridge
    public void OnGoogleAuthResult(string result)
    {
        Debug.Log(
            "GoogleAuthManager: Received result: " + result
        );

        if (string.IsNullOrEmpty(result))
        {
            Debug.LogError(
                "GoogleAuthManager: Empty Google Sign-In result."
            );
            return;
        }

        if (result.StartsWith("SUCCESS|"))
        {
            string googleIdToken =
                result.Substring("SUCCESS|".Length);

            if (string.IsNullOrEmpty(googleIdToken))
            {
                Debug.LogError(
                    "GoogleAuthManager: Google ID token is empty."
                );
                return;
            }

            if (!firebaseReady || auth == null)
            {
                Debug.LogError(
                    "GoogleAuthManager: Firebase is not ready."
                );
                return;
            }

            SignInToFirebase(googleIdToken);
        }
        else if (result.StartsWith("ERROR|"))
        {
            string errorMessage =
                result.Substring("ERROR|".Length);

            Debug.LogError(
                "Google Sign-In failed: " + errorMessage
            );
        }
        else
        {
            Debug.LogError(
                "GoogleAuthManager: Unknown result: " + result
            );
        }
    }

    private void SignInToFirebase(string googleIdToken)
    {
        if (signInInProgress)
        {
            Debug.Log(
                "GoogleAuthManager: Sign-in already in progress."
            );
            return;
        }

        if (auth == null)
        {
            Debug.LogError(
                "GoogleAuthManager: FirebaseAuth is null."
            );
            return;
        }

        signInInProgress = true;

        Credential credential =
            GoogleAuthProvider.GetCredential(
                googleIdToken,
                null
            );

        auth.SignInWithCredentialAsync(credential)
            .ContinueWithOnMainThread(task =>
            {
                signInInProgress = false;

                if (task.IsCanceled)
                {
                    Debug.LogError(
                        "GoogleAuthManager: Firebase Google Sign-In was canceled."
                    );
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "GoogleAuthManager: Firebase Google Sign-In failed: "
                        + task.Exception
                    );
                    return;
                }

                FirebaseUser user = task.Result;

                string displayName = user.DisplayName;

                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = user.Email;
                }

                Debug.Log(
                    "GOOGLE LOGIN SUCCESSFUL"
                );

                Debug.Log(
                    "Firebase User: " + displayName
                );

                Debug.Log(
                    "Firebase Email: " + user.Email
                );

                Debug.Log(
                    "Firebase UID: " + user.UserId
                );

                // Show the successful login message
                if (signedInText != null)
                {
                    signedInText.text =
                        "Signed in as " + displayName;

                    signedInText.gameObject.SetActive(true);
                }

                // Show it for 2 seconds, then open MainPanel
                StartCoroutine(
                    OpenMainPanelAfterLogin()
                );
            });
    }

    private IEnumerator OpenMainPanelAfterLogin()
    {
        yield return new WaitForSeconds(2f);

        if (uiManager != null)
        {
            uiManager.GoToMain();
        }
        else
        {
            Debug.LogError(
                "GoogleAuthManager: UI Manager is not assigned."
            );
        }
    }

    public void SkipLogin()
    {
        Debug.Log(
            "GoogleAuthManager: Skip Login pressed."
        );

        if (uiManager != null)
        {
            uiManager.GoToMain();
        }
    }
}