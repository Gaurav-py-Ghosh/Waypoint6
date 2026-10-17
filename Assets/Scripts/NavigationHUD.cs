using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View for the AR navigation scene. Holds UI references and simple setters;
/// all decisions live in NavigationSceneController.
/// </summary>
public class NavigationHUD : MonoBehaviour
{
    [Header("Top Bar")]
    public TMP_Text titleText;
    public TMP_Text statusText;

    [Header("Guidance")]
    public GameObject guidanceCard;
    public TMP_Text instructionText;
    public TMP_Text distanceText;
    public RectTransform directionArrow;

    [Header("Tracking Banner")]
    public GameObject trackingBanner;
    public TMP_Text trackingBannerText;

    [Header("Bottom Buttons")]
    public Button primaryButton;
    public TMP_Text primaryButtonText;
    public Button secondaryButton;
    public TMP_Text secondaryButtonText;

    [Header("Recording")]
    public GameObject recordingButtons;
    public Button undoButton;
    public Button finishButton;
    public GameObject crosshair;

    [Header("Dialogs")]
    public GameObject confirmPanel;
    public TMP_Text confirmText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    public GameObject resultPanel;
    public TMP_Text resultTitleText;
    public TMP_Text resultBodyText;
    public Button resultButton;

    public GameObject errorPanel;
    public TMP_Text errorText;
    public Button errorActionButton;
    public TMP_Text errorActionText;
    public Button errorBackButton;

    public event Action OnPrimary;
    public event Action OnSecondary;
    public event Action OnUndo;
    public event Action OnFinish;

    private Action confirmAction;
    private Action resultAction;
    private Action errorAction;
    private Action errorBack;

    void Awake()
    {
        if (primaryButton != null) primaryButton.onClick.AddListener(() => OnPrimary?.Invoke());
        if (secondaryButton != null) secondaryButton.onClick.AddListener(() => OnSecondary?.Invoke());
        if (undoButton != null) undoButton.onClick.AddListener(() => OnUndo?.Invoke());
        if (finishButton != null) finishButton.onClick.AddListener(() => OnFinish?.Invoke());
        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(ConfirmYes);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(() => SetActive(confirmPanel, false));
        if (resultButton != null) resultButton.onClick.AddListener(() => resultAction?.Invoke());
        if (errorActionButton != null) errorActionButton.onClick.AddListener(() => errorAction?.Invoke());
        if (errorBackButton != null) errorBackButton.onClick.AddListener(() => errorBack?.Invoke());

        SetActive(confirmPanel, false);
        SetActive(resultPanel, false);
        SetActive(errorPanel, false);
        SetActive(trackingBanner, false);
        SetActive(guidanceCard, false);
        SetArrowVisible(false);
        SetRecordingControls(false, false, false);
    }

    /// <summary>Shows the recording row (UNDO / FINISH) and the aiming crosshair.</summary>
    public void SetRecordingControls(bool visible, bool canUndo, bool canFinish)
    {
        SetActive(recordingButtons, visible);
        SetActive(crosshair, visible);
        if (undoButton != null) undoButton.interactable = canUndo;
        if (finishButton != null) finishButton.interactable = canFinish;
    }

    public bool IsDialogOpen =>
        (confirmPanel != null && confirmPanel.activeSelf) ||
        (resultPanel != null && resultPanel.activeSelf) ||
        (errorPanel != null && errorPanel.activeSelf);

    public void SetTitle(string text) => SetText(titleText, text);

    public void SetStatus(string text) => SetText(statusText, text);

    public void SetGuidanceVisible(bool visible) => SetActive(guidanceCard, visible);

    public void SetInstruction(string instruction, string distance)
    {
        SetText(instructionText, instruction);
        SetText(distanceText, distance);
    }

    public void SetArrowVisible(bool visible)
    {
        if (directionArrow != null) directionArrow.gameObject.SetActive(visible);
    }

    /// <param name="angle">Degrees; positive points to the right.</param>
    public void SetArrowAngle(float angle)
    {
        if (directionArrow != null) directionArrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    public void SetTrackingBanner(string message)
    {
        bool show = !string.IsNullOrEmpty(message);
        SetActive(trackingBanner, show);
        if (show) SetText(trackingBannerText, message);
    }

    public void SetPrimary(string label, bool visible, bool interactable)
    {
        if (primaryButton == null) return;
        primaryButton.gameObject.SetActive(visible);
        primaryButton.interactable = interactable;
        SetText(primaryButtonText, label);
    }

    public void SetSecondary(string label)
    {
        SetText(secondaryButtonText, label);
    }

    public void ShowConfirm(string message, Action onYes)
    {
        confirmAction = onYes;
        SetText(confirmText, message);
        SetActive(confirmPanel, true);
    }

    public void ShowResult(string title, string body, Action onDone)
    {
        resultAction = onDone;
        SetText(resultTitleText, title);
        SetText(resultBodyText, body);
        SetActive(resultPanel, true);
    }

    public void ShowError(string message, string actionLabel, Action action, Action onBack)
    {
        errorAction = action;
        errorBack = onBack;
        SetText(errorText, message);
        bool hasAction = action != null && !string.IsNullOrEmpty(actionLabel);
        if (errorActionButton != null) errorActionButton.gameObject.SetActive(hasAction);
        if (hasAction) SetText(errorActionText, actionLabel);
        SetActive(errorPanel, true);
    }

    /// <summary>Closes the top-most dialog. Returns false when no dialog was open.</summary>
    public bool CloseTopDialog()
    {
        if (confirmPanel != null && confirmPanel.activeSelf)
        {
            confirmPanel.SetActive(false);
            return true;
        }
        return false;
    }

    private void ConfirmYes()
    {
        SetActive(confirmPanel, false);
        Action action = confirmAction;
        confirmAction = null;
        action?.Invoke();
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label != null) label.text = text ?? "";
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
