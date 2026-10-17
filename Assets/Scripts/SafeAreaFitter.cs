using UnityEngine;

/// <summary>
/// Shrinks a full-screen RectTransform to the device safe area, so HUD
/// elements stay clear of notches, punch-hole cameras and gesture bars.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect appliedArea;
    private Vector2Int appliedScreen;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != appliedArea || appliedScreen.x != Screen.width || appliedScreen.y != Screen.height)
        {
            Apply();
        }
    }

    private void Apply()
    {
        Rect area = Screen.safeArea;
        appliedArea = area;
        appliedScreen = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0) return;

        Vector2 min = area.position;
        Vector2 max = area.position + area.size;
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        rectTransform.anchorMin = min;
        rectTransform.anchorMax = max;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
