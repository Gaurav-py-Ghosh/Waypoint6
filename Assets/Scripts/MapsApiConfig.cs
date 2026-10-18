using UnityEngine;

/// <summary>
/// Google Maps Platform key for the campus map (Static Maps + Routes API).
///
/// The asset (Assets/Resources/MapsApiConfig.asset) is git-ignored: the key must
/// never be committed. In Google Cloud Console, restrict the key to Android apps
/// (package com.bmu.bmuarr + the release SHA-1) and to the Maps Static and Routes APIs.
/// </summary>
public class MapsApiConfig : ScriptableObject
{
    public const string ResourcePath = "MapsApiConfig";

    [Tooltip("Google Maps Platform API key.")]
    public string apiKey = "";

    [Tooltip("Release signing certificate SHA-1 without colons, sent so Android-restricted keys accept requests.")]
    public string androidCertSha1 = "";

    private static MapsApiConfig instance;
    private static bool loaded;

    public static MapsApiConfig Instance
    {
        get
        {
            if (!loaded)
            {
                instance = Resources.Load<MapsApiConfig>(ResourcePath);
                loaded = true;
            }
            return instance;
        }
    }

    public static bool HasKey => Instance != null && !string.IsNullOrWhiteSpace(Instance.apiKey);
}
