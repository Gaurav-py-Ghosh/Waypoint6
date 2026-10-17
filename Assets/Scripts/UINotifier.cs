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

    private static readonly Color InfoColor = new Color(0.10f, 0.14f, 0.24f, 0.96f);
    private static readonly Color SuccessColor = new Color(0.08f, 0.45f, 0.25f, 0.96f);
    private static readonly Color WarningColor = new Color(0.62f, 0.42f, 0.05f, 0.96f);
    private static readonly Color ErrorColor = new Color(0.62f, 0.12f, 0.12f, 0.96f);

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
