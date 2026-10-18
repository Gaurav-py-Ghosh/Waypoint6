using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// One banner per scene for user-facing messages (errors, permission problems,
/// confirmations). Call UINotifier.Show from anywhere; it finds the scene's instance.
/// </summary>
public class UINotifier : MonoBehaviour
{
    [SerializeField] private GameObject root = null;
    [SerializeField] private Image background = null;
    [SerializeField] private TMP_Text messageText = null;
    [SerializeField] private Button actionButton = null;
    [SerializeField] private TMP_Text actionText = null;

    private static UINotifier instance;
    private Coroutine hideRoutine;
    private Action pendingAction;

    // App theme greens for info/success; amber and red stay for problems.
    private static readonly Color InfoColor = new Color32(0x00, 0xB8, 0x59, 0xF5);
    private static readonly Color SuccessColor = new Color32(0x00, 0x8C, 0x44, 0xF5);
    private static readonly Color WarningColor = new Color(0.92f, 0.62f, 0.10f, 0.97f);
    private static readonly Color ErrorColor = new Color32(0xE5, 0x48, 0x4D, 0xF5);

    void Awake()
    {
        instance = this;
        if (actionButton != null) actionButton.onClick.AddListener(OnActionClicked);
        if (root != null) root.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <param name="seconds">How long to show it. 0 or less keeps it until hidden or replaced.</param>
    public static void Show(string message, NoticeKind kind = NoticeKind.Info, float seconds = 4f,
                            string actionLabel = null, Action action = null)
    {
        if (instance == null)
        {
            // No banner in this scene: still surface it in logs.
            if (kind == NoticeKind.Error) AppLog.Error("Notice", message);
            else AppLog.Warn("Notice", message);
            return;
        }
        instance.ShowInternal(message, kind, seconds, actionLabel, action);
    }

    public static void Hide()
    {
        if (instance != null) instance.HideInternal();
    }

    private void ShowInternal(string message, NoticeKind kind, float seconds, string actionLabel, Action action)
    {
        if (messageText != null) messageText.text = message;
        if (background != null) background.color = ColorFor(kind);

        pendingAction = action;
        bool hasAction = action != null && !string.IsNullOrEmpty(actionLabel);
        if (actionButton != null) actionButton.gameObject.SetActive(hasAction);
        if (actionText != null && hasAction) actionText.text = actionLabel;

        if (root != null) root.SetActive(true);

        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = seconds > 0f ? StartCoroutine(HideAfter(seconds)) : null;
    }

    private void HideInternal()
    {
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = null;
        pendingAction = null;
        if (root != null) root.SetActive(false);
    }

    private IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        hideRoutine = null;
        HideInternal();
    }

    private void OnActionClicked()
    {
        Action action = pendingAction;
        HideInternal();
        action?.Invoke();
    }

    private static Color ColorFor(NoticeKind kind)
    {
        switch (kind)
        {
            case NoticeKind.Success: return SuccessColor;
            case NoticeKind.Warning: return WarningColor;
            case NoticeKind.Error: return ErrorColor;
            default: return InfoColor;
        }
    }
}
