package com.bmu.googleauth;

import android.app.Activity;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

public class GoogleAuthBridge {

    private static final String TAG = "GoogleAuthBridge";

    // We will replace this with your Web Client ID in the next step.
    private static final String WEB_CLIENT_ID = "938559150857-ho60lta4321a48v6pt3ff4p7n82a5f3r.apps.googleusercontent.com";

    private static Activity activity;
    private static CredentialManager credentialManager;

    public static void initialize(Activity currentActivity) {
        activity = currentActivity;
        credentialManager = CredentialManager.create(activity);

        Log.d(TAG, "Google Auth Bridge initialized");
    }

    public static void signIn() {

        if (activity == null) {
            sendResult("ERROR|Activity is not initialized");
            return;
        }

        if (credentialManager == null) {
            credentialManager = CredentialManager.create(activity);
        }

        GetGoogleIdOption googleIdOption =
                new GetGoogleIdOption.Builder()
                        .setFilterByAuthorizedAccounts(false)
                        .setServerClientId(WEB_CLIENT_ID)
                        .build();

        GetCredentialRequest request =
                new GetCredentialRequest.Builder()
                        .addCredentialOption(googleIdOption)
                        .build();

        CancellationSignal cancellationSignal =
                new CancellationSignal();

        credentialManager.getCredentialAsync(
                activity,
                request,
                cancellationSignal,
                activity.getMainExecutor(),
                new androidx.credentials.CredentialManagerCallback<
                        GetCredentialResponse,
                        GetCredentialException>() {

                    @Override
                    public void onResult(GetCredentialResponse result) {

                        try {
                            Credential credential = result.getCredential();

                            if (credential.getType().equals(
                                    GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL)) {

                                GoogleIdTokenCredential googleCredential =
                                        GoogleIdTokenCredential.createFrom(
                                                credential.getData());

                                String idToken =
                                        googleCredential.getIdToken();

                                sendResult("SUCCESS|" + idToken);

                            } else {
                                sendResult("ERROR|Unexpected credential type");
                            }

                        } catch (Exception e) {
                            Log.e(TAG, "Credential parsing error", e);
                            sendResult("ERROR|" + e.getMessage());
                        }
                    }

                    @Override
                    public void onError(@NonNull GetCredentialException e) {
                        Log.e(TAG, "Google sign-in failed", e);
                        sendResult("ERROR|" + e.getMessage());
                    }
                });
    }

    private static void sendResult(final String result) {

    new Handler(Looper.getMainLooper()).post(() -> {

        // Never log the result itself: on success it carries the Google ID token.
        Log.d(TAG, "Sending sign-in result to Unity.");

        try {
            Class<?> unityPlayer =
                    Class.forName("com.unity3d.player.UnityPlayer");

            java.lang.reflect.Method sendMessage =
                    unityPlayer.getMethod(
                            "UnitySendMessage",
                            String.class,
                            String.class,
                            String.class
                    );

            Log.d(TAG, "UnityPlayer class found.");
            Log.d(TAG, "Sending message to GameObject: Ui Manager");
            Log.d(TAG, "Method: OnGoogleAuthResult");

            sendMessage.invoke(
                    null,
                    "Ui Manager",
                    "OnGoogleAuthResult",
                    result
            );

            Log.d(TAG, "UnitySendMessage invoked successfully.");

        } catch (Exception e) {

            Log.e(
                    TAG,
                    "FAILED to send Google result to Unity",
                    e
            );
        }
    });
}
}