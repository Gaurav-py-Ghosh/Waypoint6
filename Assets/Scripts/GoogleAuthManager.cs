using System.Collections;
using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Google Sign-In through the Android bridge (com.bmu.googleauth.GoogleAuthBridge),
/// then Firebase Auth. Must live on the GameObject named "Ui Manager": the Java
/// bridge sends its result there with UnitySendMessage.
/// </summary>
public class GoogleAuthManager : MonoBehaviour
{
    private const string Tag = "GoogleAuth";
    private const float FirebaseWaitSeconds = 15f;

    [Header("UI")]
    public UIManager uiManager;
    public TMP_Text signedInText;

    [Tooltip("Optional: the 'Continue with Google' button, disabled while signing in.")]
    public Button signInButton;

    private FirebaseAuth auth;
    private bool signInInProgress;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass googleAuthBridge;
#endif

    void Start()
    {
        if (signedInText != null) signedInText.gameObject.SetActive(false);
        StartCoroutine(WaitForFirebase());

#if UNITY_ANDROID && !UNITY_EDITOR
        InitializeGoogleBridge();
#endif
    }

    /// <summary>True when Firebase already has a signed-in user from an earlier session.</summary>
    public bool HasSignedInUser => auth != null && auth.CurrentUser != null;

    /// <summary>Loads the remembered Firebase user into UserSession. Returns false when nobody is signed in.</summary>
    public bool RestoreSession()
    {
        if (!HasSignedInUser) return false;
        UserSession.SetSignedIn(auth.CurrentUser.DisplayName, auth.CurrentUser.Email);
        return true;
    }

    /// <summary>Signs out of Firebase and forgets the Google account, then shows the login screen.</summary>
    public void SignOut()
    {
        if (auth != null) auth.SignOut();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (googleAuthBridge != null)
        {
            try
            {
                googleAuthBridge.CallStatic("signOut");
            }
            catch (System.Exception e)
            {
                AppLog.Warn(Tag, "Google bridge sign-out failed: " + e.Message);
            }
        }
#endif

        UserSession.Clear();
        AppSession.OnboardingComplete = false;
        if (signedInText != null) signedInText.gameObject.SetActive(false);
        AppLog.Info(Tag, "Signed out.");
        if (uiManager != null) uiManager.GoToLogin();
    }

    private IEnumerator WaitForFirebase()
    {
        float deadline = Time.realtimeSinceStartup + FirebaseWaitSeconds;
        while (!FirebaseBootstrap.IsReady && !FirebaseBootstrap.IsFailed && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        auth = FirebaseBootstrap.Auth;
        if (auth == null) AppLog.Warn(Tag, "Firebase Auth unavailable; Google sign-in disabled.");
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void InitializeGoogleBridge()
    {
        try
        {
            googleAuthBridge = new AndroidJavaClass("com.bmu.googleauth.GoogleAuthBridge");
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                googleAuthBridge.CallStatic("initialize", activity);
            }
        }
        catch (System.Exception e)
        {
            googleAuthBridge = null;
            AppLog.Error(Tag, "Google bridge failed to initialise: " + e.Message);
        }
    }
#endif

    public void SignInWithGoogle()
    {
        if (signInInProgress) return;

        if (auth == null)
        {
            UINotifier.Show(FirebaseBootstrap.IsFailed
                ? "Sign-in is unavailable right now. You can continue without signing in."
                : "Still starting up. Try again in a moment.", NoticeKind.Warning);
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (googleAuthBridge == null)
        {
            UINotifier.Show("Google sign-in is not available on this device.", NoticeKind.Error);
            return;
        }

        SetBusy(true);
        googleAuthBridge.CallStatic("signIn");
#else
        UINotifier.Show("Google sign-in only works in the Android app.", NoticeKind.Info);
#endif
    }

    /// <summary>Called by GoogleAuthBridge via UnitySendMessage. Never log the payload: it holds the ID token.</summary>
    public void OnGoogleAuthResult(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            Fail("Empty sign-in result.", "Sign-in failed. Please try again.");
            return;
        }

        if (result.StartsWith("SUCCESS|"))
        {
            string idToken = result.Substring("SUCCESS|".Length);
            if (string.IsNullOrEmpty(idToken) || auth == null)
            {
                Fail("Missing ID token or Firebase not ready.", "Sign-in failed. Please try again.");
                return;
            }
            SignInToFirebase(idToken);
        }
        else if (result.StartsWith("ERROR|"))
        {
            // The bridge's error text is technical; keep it out of the UI.
            Fail("Google sign-in returned an error.", "Google sign-in was cancelled or failed.");
        }
        else
        {
            Fail("Unknown sign-in result format.", "Sign-in failed. Please try again.");
        }
    }

    private void SignInToFirebase(string idToken)
    {
        Credential credential = GoogleAuthProvider.GetCredential(idToken, null);

        auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Fail("Firebase sign-in failed: " + (task.Exception?.GetBaseException().Message ?? "cancelled"),
                     "Could not sign in. Check your connection and try again.");
                return;
            }

            SetBusy(false);
            FirebaseUser user = task.Result;
            UserSession.SetSignedIn(user.DisplayName, user.Email);

            if (signedInText != null)
            {
                string name = UserSession.FirstName ?? "your Google account";
                signedInText.text = UserSession.IsAdmin ? $"Welcome, {name}! (Admin)" : $"Welcome, {name}!";
                signedInText.gameObject.SetActive(true);
            }
            StartCoroutine(OpenMainPanelAfterLogin());
        });
    }

    private IEnumerator OpenMainPanelAfterLogin()
    {
        yield return new WaitForSeconds(1.5f);
        if (uiManager != null) uiManager.GoToMain();
    }

    public void SkipLogin()
    {
        if (uiManager != null) uiManager.GoToMain();
    }

    private void Fail(string logMessage, string userMessage)
    {
        SetBusy(false);
        AppLog.Warn(Tag, logMessage);
        UINotifier.Show(userMessage, NoticeKind.Error);
    }

    private void SetBusy(bool busy)
    {
        signInInProgress = busy;
        if (signInButton != null) signInButton.interactable = !busy;
    }
}
