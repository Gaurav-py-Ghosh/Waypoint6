using System;

/// <summary>
/// Who is using the app right now: a signed-in Google user (admin or not) or a guest.
/// Lives for the whole app run, across scene loads.
/// </summary>
public static class UserSession
{
    public static bool IsSignedIn { get; private set; }
    public static bool IsGuest { get; private set; }
    public static bool IsAdmin { get; private set; }
    public static string DisplayName { get; private set; }
    public static string Email { get; private set; }

    /// <summary>First name for greetings, e.g. "Gaurav" from "Gaurav Ghosh".</summary>
    public static string FirstName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName.Trim().Split(' ')[0];
            if (!string.IsNullOrWhiteSpace(Email)) return Email.Split('@')[0];
            return null;
        }
    }

    public static event Action OnChanged;

    public static void SetSignedIn(string displayName, string email)
    {
        IsSignedIn = true;
        IsGuest = false;
        DisplayName = displayName;
        Email = email;
        IsAdmin = AdminConfig.IsAdmin(email);
        AppLog.Info("UserSession", IsAdmin ? "Signed in as admin." : "Signed in as user.");
        OnChanged?.Invoke();
    }

    public static void SetGuest()
    {
        IsSignedIn = false;
        IsGuest = true;
        IsAdmin = AdminConfig.EditorGuestIsAdmin;
        DisplayName = null;
        Email = null;
        OnChanged?.Invoke();
    }

    public static void Clear()
    {
        IsSignedIn = false;
        IsGuest = false;
        IsAdmin = false;
        DisplayName = null;
        Email = null;
        OnChanged?.Invoke();
    }
}
