using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which Google accounts are admins. Admins can record and re-record routes;
/// everyone else (including guests) can only use Guide Me.
///
/// Edit the list on Assets/Resources/AdminConfig.asset (Tools > BMU AR > Edit Admin List).
/// The list is built into the app, so changing it needs a new build.
/// </summary>
[CreateAssetMenu(fileName = "AdminConfig", menuName = "BMU AR/Admin Config")]
public class AdminConfig : ScriptableObject
{
    public const string ResourcePath = "AdminConfig";

    [Tooltip("Google account emails that may record routes, e.g. name@gmail.com. Case does not matter.")]
    [SerializeField] private List<string> adminEmails = new List<string>();

    [Tooltip("In the Unity Editor only, treat guests (Skip Login) as admins so recording can be tested without Google sign-in.")]
    [SerializeField] private bool editorGuestIsAdmin = true;

    private static AdminConfig instance;

    public static bool EditorGuestIsAdmin => Application.isEditor && Instance != null && Instance.editorGuestIsAdmin;

    public static AdminConfig Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<AdminConfig>(ResourcePath);
            if (instance == null) AppLog.Warn("AdminConfig", $"Resources/{ResourcePath} is missing; nobody is an admin.");
            return instance;
        }
    }

    public IReadOnlyList<string> AdminEmails => adminEmails;

    public static bool IsAdmin(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || Instance == null) return false;

        string normalized = email.Trim();
        foreach (string admin in Instance.adminEmails)
        {
            if (!string.IsNullOrWhiteSpace(admin) &&
                string.Equals(admin.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
