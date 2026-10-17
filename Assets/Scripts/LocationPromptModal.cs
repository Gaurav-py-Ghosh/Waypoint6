using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Modal dialog shown when the user taps a destination.
/// Checks if a path is already recorded:
/// - If NO: Shows "Record Location" button.
/// - If YES: Shows "Guide Me" and "Re-record" buttons.
/// </summary>
public class LocationPromptModal : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text titleText;
    public TMP_Text messageText;
    public TMP_Text distanceText;

    [Header("Buttons")]
    public Button recordButton;
    public Button guideMeButton;
    public Button reRecordButton;
    public Button cancelButton;

    private string currentDestination = "";
    private Action<string> onStartRecording;
    private Action<string> onStartGuidance;

    void Awake()
    {
        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(CloseModal);
        }

        if (recordButton != null)
        {
            recordButton.onClick.AddListener(() =>
            {
                CloseModal();
                onStartRecording?.Invoke(currentDestination);
            });
        }

        if (reRecordButton != null)
        {
            reRecordButton.onClick.AddListener(() =>
            {
                CloseModal();
                onStartRecording?.Invoke(currentDestination);
            });
        }

        if (guideMeButton != null)
        {
            guideMeButton.onClick.AddListener(() =>
            {
                CloseModal();
                onStartGuidance?.Invoke(currentDestination);
            });
        }
    }

    /// <summary>
    /// Displays the modal configured for the selected destination.
    /// </summary>
    public void Open(string destinationName, Action<string> onRecord, Action<string> onGuide)
    {
        currentDestination = destinationName;
        onStartRecording = onRecord;
        onStartGuidance = onGuide;

        if (titleText != null)
        {
            titleText.text = destinationName;
        }

        PathData data = PathStorageManager.LoadPath(destinationName);
        bool hasRecordedPath = data != null;

        if (hasRecordedPath)
        {
            int turns = PathMath.FindTurns(data.GetPoints2D()).Count;

            if (messageText != null)
            {
                messageText.text = "A route is saved for this place. Start at the marker and tap Guide Me. Re-recording replaces the saved route.";
            }

            if (distanceText != null)
            {
                distanceText.gameObject.SetActive(true);
                distanceText.text = $"Distance: ~{data.totalDistanceMeters:F0} m  •  Turns: {turns}";
            }

            if (recordButton != null) recordButton.gameObject.SetActive(false);
            if (guideMeButton != null) guideMeButton.gameObject.SetActive(true);
            if (reRecordButton != null) reRecordButton.gameObject.SetActive(true);
        }
        else
        {
            if (messageText != null)
            {
                messageText.text = "No route saved yet. Stand at the marker, tap Record Path, then walk to this place.";
            }

            if (distanceText != null)
            {
                distanceText.gameObject.SetActive(false);
            }

            if (recordButton != null) recordButton.gameObject.SetActive(true);
            if (guideMeButton != null) guideMeButton.gameObject.SetActive(false);
            if (reRecordButton != null) reRecordButton.gameObject.SetActive(false);
        }

        gameObject.SetActive(true);
    }

    public void CloseModal()
    {
        gameObject.SetActive(false);
    }
}
