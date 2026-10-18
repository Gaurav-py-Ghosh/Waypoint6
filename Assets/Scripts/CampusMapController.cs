using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Explore Campus: a styled map with the marked places and the user's live position.
/// Tapping a place (pin or chip) draws the walking route from where the user is,
/// like Google Maps. When the user is outside the campus map, a wider map is
/// loaded so both ends of the route are visible.
/// </summary>
public class CampusMapController : MonoBehaviour
{
    private const string Tag = "CampusMap";
    private const float LocationWaitSeconds = 8f;
    private const float UsableAccuracyMeters = 200f;

    [Header("Map")]
    public ScrollRect scrollRect;
    public RectTransform viewport;
    public RectTransform mapContent;
    public RawImage mapImage;
    public UILineGraphic pathCasing;
    public UILineGraphic pathLine;
    public RectTransform pinsRoot;
    public RectTransform userDot;
    public RectTransform userHalo;

    [Header("Pins & Chips")]
    public Sprite pinSprite;
    public Sprite roundedSprite;
    public TMP_FontAsset labelFont;
    public RectTransform placeChipsRoot;
    public Color pinColor = new Color32(0x00, 0xB8, 0x59, 0xFF);
    public Color selectedPinColor = new Color32(0x00, 0x8C, 0x44, 0xFF);

    [Header("UI")]
    public TMP_Text statusText;
    public GameObject statusPill;
    public Button recenterButton;
    public GameObject infoCard;
    public TMP_Text infoTitle;
    public TMP_Text infoDetail;
    public TMP_Text infoSubDetail;
    public Button infoClearButton;
    public Button infoCloseButton;

    [Header("Zoom")]
    [SerializeField] private float minZoom = 1f;
    [SerializeField] private float maxZoom = 3.5f;
    [SerializeField] private float pathThickness = 14f;
    [SerializeField] private float pinSize = 92f;

    private static readonly Color TextDark = new Color32(0x1E, 0x2A, 0x24, 0xFF);
    private static readonly Color TextMuted = new Color32(0x5E, 0x6E, 0x66, 0xFF);
    private static readonly Color MintBorder = new Color32(0xB2, 0xEB, 0xCF, 0xFF);

    private CampusMapConfig config;
    private readonly Dictionary<string, PinView> pins = new Dictionary<string, PinView>();
    private readonly Dictionary<string, ChipView> chips = new Dictionary<string, ChipView>();

    // The area the current map image shows.
    private LatLng viewCenter;
    private int viewZoom;
    private bool mapReady;
    private bool loadingMap;

    private CampusPlace selected;
    private string pendingFocusId;
    private List<LatLng> routePoints;
    private Coroutine routeRoutine;
    private float zoom = 1f;
    private float lastPinchDistance;
    private float nextLocationUpdate;

    private class PinView
    {
        public RectTransform root;
        public Image icon;
        public GameObject label;
        public CampusPlace place;
    }

    // Names show on the selected pin, or on all pins once zoomed in enough to not overlap.
    private const float LabelZoom = 1.8f;

    private class ChipView
    {
        public Image background;
        public TMP_Text label;
    }

    void Awake()
    {
        config = CampusMapConfig.Instance;
        if (recenterButton != null) recenterButton.onClick.AddListener(Recenter);
        if (infoCloseButton != null) infoCloseButton.onClick.AddListener(Deselect);
        if (infoClearButton != null) infoClearButton.onClick.AddListener(Deselect);
    }

    void OnEnable()
    {
        SetInfoCardVisible(false);
        if (config == null)
        {
            ShowStatus("Campus map is not set up.");
            return;
        }

        if (chips.Count == 0) BuildChips();
        DeviceLocation.Start(OnLocationState);

        if (!mapReady && !loadingMap) StartCoroutine(LoadView(config.Center, config.zoom, Recenter));
        else ApplyPendingFocus();
    }

    void OnDisable()
    {
        DeviceLocation.Stop();
        if (routeRoutine != null) StopCoroutine(routeRoutine);
        routeRoutine = null;
        loadingMap = false;
    }

    /// <summary>Opens the map with a place selected and its route drawn.</summary>
    public void Focus(string placeId)
    {
        pendingFocusId = placeId;
        if (mapReady && isActiveAndEnabled) ApplyPendingFocus();
    }

    void Update()
    {
        if (!mapReady) return;
        HandleZoomInput();
        AnimateUserHalo();

        if (Time.unscaledTime >= nextLocationUpdate)
        {
            nextLocationUpdate = Time.unscaledTime + 1f;
            UpdateUserDot();
        }
    }

    // ───────────────────────── Map area ─────────────────────────

    private bool IsCampusView => viewZoom == config.zoom &&
                                 GeoMath.DistanceMeters(viewCenter, config.Center) < 1.0;

    private IEnumerator LoadView(LatLng center, int mapZoom, System.Action onLoaded)
    {
        loadingMap = true;
        ShowStatus("Loading map…");
        Texture2D texture = null;
        string error = null;
        yield return GoogleMapsClient.FetchMap(center, mapZoom, config.mapSizePixels, (t, e) => { texture = t; error = e; });
        loadingMap = false;

        if (texture == null)
        {
            ShowStatus(error ?? "Could not load the map.");
            yield break;
        }

        viewCenter = center;
        viewZoom = mapZoom;
        mapImage.texture = texture;
        mapImage.color = Color.white;
        LayoutContent();
        BuildPins();
        mapReady = true;
        HideStatus();
        UpdateUserDot();
        DrawRoute();
        onLoaded?.Invoke();
        ApplyPendingFocus();
    }

    /// <summary>Square map content that fills the viewport; panning reveals the rest.</summary>
    private void LayoutContent()
    {
        Rect view = viewport.rect;
        float side = Mathf.Max(view.width, view.height);
        mapContent.anchorMin = mapContent.anchorMax = new Vector2(0.5f, 0.5f);
        mapContent.pivot = new Vector2(0.5f, 0.5f);
        mapContent.sizeDelta = new Vector2(side, side);
        // Allow zooming out until the whole map fits the narrow side of the screen.
        minZoom = Mathf.Min(view.width, view.height) / side;
        SetZoom(1f);
    }

    private Vector2 ToUV(LatLng location) => GeoMath.ToMapUV(location, viewCenter, viewZoom, config.mapSizePixels);

    private Vector2 ToContentPosition(LatLng location)
    {
        Vector2 uv = ToUV(location);
        Vector2 size = mapContent.rect.size;
        return new Vector2((uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y);
    }

    private static bool IsInside(Vector2 uv, float margin = 0f) =>
        uv.x >= margin && uv.x <= 1f - margin && uv.y >= margin && uv.y <= 1f - margin;

    // ───────────────────────── Pins & chips ─────────────────────────

    private void BuildPins()
    {
        foreach (PinView pin in pins.Values)
        {
            if (pin.root != null) Destroy(pin.root.gameObject);
        }
        pins.Clear();

        foreach (CampusPlace place in config.places)
        {
            if (!place.placed) continue;
            Vector2 uv = ToUV(place.Location);
            if (!IsInside(uv)) continue; // Outside this map image.
            pins[place.id] = CreatePin(place);
        }
        RefreshSelectionVisuals();
    }

    private PinView CreatePin(CampusPlace place)
    {
        var root = new GameObject("Pin_" + place.id, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(pinsRoot, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0f); // The pin's tip sits on the spot.
        root.sizeDelta = new Vector2(pinSize * 0.75f, pinSize);
        root.anchoredPosition = ToContentPosition(place.Location);

        var icon = root.gameObject.AddComponent<Image>();
        icon.sprite = pinSprite;
        icon.color = pinColor;
        icon.preserveAspect = true;
        root.gameObject.AddComponent<Button>().onClick.AddListener(() => Select(place));

        // Name on a white pill above the pin.
        string name = DisplayName(place);
        var labelBg = new GameObject("LabelBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var bgRt = labelBg.GetComponent<RectTransform>();
        bgRt.SetParent(root, false);
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 1f);
        bgRt.pivot = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = new Vector2(0f, 6f);
        var bg = labelBg.GetComponent<Image>();
        bg.sprite = roundedSprite;
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 0.35f;
        bg.color = new Color(1f, 1f, 1f, 0.95f);
        bg.raycastTarget = false;
        var outline = labelBg.AddComponent<Outline>();
        outline.effectColor = MintBorder;
        outline.effectDistance = new Vector2(2f, -2f);

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var labelRt = labelGO.GetComponent<RectTransform>();
        labelRt.SetParent(bgRt, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        var label = labelGO.GetComponent<TextMeshProUGUI>();
        if (labelFont != null) label.font = labelFont;
        label.text = name;
        label.fontSize = pinSize * 0.3f;
        label.color = TextDark;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        Vector2 preferred = label.GetPreferredValues(name);
        bgRt.sizeDelta = new Vector2(preferred.x + 36f, preferred.y + 14f);

        return new PinView { root = root, icon = icon, label = labelBg, place = place };
    }

    /// <summary>Quick-select chips under the title, one per place.</summary>
    private void BuildChips()
    {
        if (placeChipsRoot == null) return;
        foreach (Transform child in placeChipsRoot) Destroy(child.gameObject);
        chips.Clear();

        foreach (CampusPlace place in config.places)
        {
            if (!place.placed) continue;
            CampusPlace target = place;

            var chipGO = new GameObject("Chip_" + place.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            chipGO.transform.SetParent(placeChipsRoot, false);
            var background = chipGO.GetComponent<Image>();
            background.sprite = roundedSprite;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 0.3f;
            background.color = Color.white;
            var outline = chipGO.AddComponent<Outline>();
            outline.effectColor = MintBorder;
            outline.effectDistance = new Vector2(2f, -2f);
            chipGO.GetComponent<Button>().onClick.AddListener(() => Select(target));

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var labelRt = labelGO.GetComponent<RectTransform>();
            labelRt.SetParent(chipGO.transform, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            var label = labelGO.GetComponent<TextMeshProUGUI>();
            if (labelFont != null) label.font = labelFont;
            label.text = DisplayName(place);
            label.fontSize = 30;
            label.color = selectedPinColor;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            var layout = chipGO.GetComponent<LayoutElement>();
            layout.preferredWidth = label.GetPreferredValues(label.text).x + 56f;
            layout.preferredHeight = 76f;

            chips[place.id] = new ChipView { background = background, label = label };
        }
    }

    private void RefreshSelectionVisuals()
    {
        foreach (PinView pin in pins.Values)
        {
            bool isSelected = selected != null && pin.place.id == selected.id;
            pin.icon.color = isSelected ? selectedPinColor : pinColor;
            if (isSelected) pin.root.SetAsLastSibling();
        }
        foreach (KeyValuePair<string, ChipView> chip in chips)
        {
            bool isSelected = selected != null && chip.Key == selected.id;
            chip.Value.background.color = isSelected ? selectedPinColor : Color.white;
            chip.Value.label.color = isSelected ? Color.white : selectedPinColor;
        }
        ApplyCounterScale();
    }

    // ───────────────────────── Selection & routes ─────────────────────────

    private void ApplyPendingFocus()
    {
        if (string.IsNullOrEmpty(pendingFocusId) || !mapReady) return;
        CampusPlace place = config.Find(pendingFocusId);
        pendingFocusId = null;
        if (place != null && place.placed) Select(place);
    }

    /// <summary>Selecting a place immediately routes to it from the user's location.</summary>
    private void Select(CampusPlace place)
    {
        selected = place;
        ClearRoute();
        RefreshSelectionVisuals();

        infoTitle.text = DisplayName(place);
        infoDetail.text = "Finding the walking route…";
        infoSubDetail.text = "";
        SetInfoCardVisible(true);

        routeRoutine = StartCoroutine(RouteTo(place));
    }

    private void Deselect()
    {
        selected = null;
        ClearRoute();
        SetInfoCardVisible(false);
        RefreshSelectionVisuals();
        if (!IsCampusView && !loadingMap) StartCoroutine(LoadView(config.Center, config.zoom, Recenter));
    }

    private IEnumerator RouteTo(CampusPlace place)
    {
        // Give GPS a moment for its first fix.
        float waitUntil = Time.unscaledTime + LocationWaitSeconds;
        while (!HasUsableLocation(out _) &&
               (DeviceLocation.State == LocationState.Searching || DeviceLocation.State == LocationState.WaitingForPermission) &&
               Time.unscaledTime < waitUntil)
        {
            infoDetail.text = "Finding your location…";
            yield return null;
        }

        LatLng origin = ChooseOrigin(out string originNote);
        infoDetail.text = "Finding the walking route…";

        WalkingRoute route = null;
        string warning = null;
        yield return GoogleMapsClient.FetchWalkingRoute(origin, place.Location, (r, w) => { route = r; warning = w; });
        if (selected != place || route == null) yield break;

        routePoints = route.points;

        // If the route leaves the current map, load a map that fits all of it.
        if (!RouteFitsView())
        {
            GetRouteBounds(out LatLng southWest, out LatLng northEast);
            var center = new LatLng((southWest.lat + northEast.lat) * 0.5, (southWest.lng + northEast.lng) * 0.5);
            int fitZoom = Mathf.Min(GeoMath.ZoomToFit(southWest, northEast, config.mapSizePixels), config.zoom);
            yield return LoadView(center, fitZoom, null);
            if (selected != place) yield break;
        }

        DrawRoute();
        FitRoute();

        infoDetail.text = $"{FormatDuration(route.durationSeconds)} walk · {FormatDistance(route.distanceMeters)}";
        infoSubDetail.text = !string.IsNullOrEmpty(warning) ? warning
                           : !string.IsNullOrEmpty(originNote) ? originNote
                           : "From your location";
        routeRoutine = null;
    }

    private bool HasUsableLocation(out LatLng here)
    {
        return DeviceLocation.TryGetLocation(out here, out float accuracy) && (accuracy < 0 || accuracy <= UsableAccuracyMeters);
    }

    /// <summary>The user's position whenever there is a usable fix; otherwise the main gate.</summary>
    private LatLng ChooseOrigin(out string note)
    {
        note = null;
        if (HasUsableLocation(out LatLng here)) return here;

        CampusPlace entrance = config.Find(config.entrancePlaceId);
        LatLng start = entrance != null && entrance.placed ? entrance.Location : config.Center;
        string from = entrance != null ? DisplayName(entrance) : "the campus centre";

        note = DeviceLocation.State == LocationState.PermissionDenied ? $"Location is off · route starts at {from}"
             : DeviceLocation.State == LocationState.ServicesOff ? $"Turn on location · route starts at {from}"
             : $"Couldn't find your location · route starts at {from}";
        return start;
    }

    private bool RouteFitsView()
    {
        if (routePoints == null) return true;
        foreach (LatLng p in routePoints)
        {
            if (!IsInside(ToUV(p), 0.04f)) return false;
        }
        return true;
    }

    private void GetRouteBounds(out LatLng southWest, out LatLng northEast)
    {
        double minLat = double.MaxValue, minLng = double.MaxValue, maxLat = double.MinValue, maxLng = double.MinValue;
        foreach (LatLng p in routePoints)
        {
            minLat = System.Math.Min(minLat, p.lat);
            maxLat = System.Math.Max(maxLat, p.lat);
            minLng = System.Math.Min(minLng, p.lng);
            maxLng = System.Math.Max(maxLng, p.lng);
        }
        southWest = new LatLng(minLat, minLng);
        northEast = new LatLng(maxLat, maxLng);
    }

    private void DrawRoute()
    {
        if (routePoints == null)
        {
            if (pathLine != null) pathLine.Clear();
            if (pathCasing != null) pathCasing.Clear();
            return;
        }
        var local = new List<Vector2>(routePoints.Count);
        foreach (LatLng p in routePoints) local.Add(ToContentPosition(p));
        pathCasing.SetPoints(local);
        pathLine.SetPoints(local);
        ApplyLineThickness();
    }

    private void ClearRoute()
    {
        if (routeRoutine != null) StopCoroutine(routeRoutine);
        routeRoutine = null;
        routePoints = null;
        DrawRoute();
    }

    private static string DisplayName(CampusPlace place) =>
        string.IsNullOrEmpty(place.displayName) ? place.id : place.displayName;

    private static string FormatDuration(double seconds)
    {
        int minutes = Mathf.Max(1, Mathf.RoundToInt((float)seconds / 60f));
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60} min";
    }

    private static string FormatDistance(double meters) =>
        meters >= 1000 ? $"{meters / 1000:F1} km" : $"{Mathf.RoundToInt((float)meters)} m";

    // ───────────────────────── User location ─────────────────────────

    private void OnLocationState(LocationState state)
    {
        switch (state)
        {
            case LocationState.PermissionDenied:
                ShowStatus("Allow location to get directions from where you are.", 4f);
                break;
            case LocationState.ServicesOff:
                ShowStatus("Turn on location to get directions from where you are.", 4f);
                break;
        }
    }

    private void UpdateUserDot()
    {
        if (userDot == null) return;
        bool visible = DeviceLocation.TryGetLocation(out LatLng here, out _) && IsInside(ToUV(here));
        userDot.gameObject.SetActive(visible);
        if (visible)
        {
            userDot.anchoredPosition = ToContentPosition(here);
            userDot.SetAsLastSibling();
        }
    }

    private void AnimateUserHalo()
    {
        if (userHalo == null || !userHalo.gameObject.activeInHierarchy) return;
        float t = Mathf.Repeat(Time.unscaledTime / 1.8f, 1f);
        userHalo.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, t);
        var image = userHalo.GetComponent<Image>();
        if (image != null)
        {
            Color c = image.color;
            c.a = Mathf.Lerp(0.35f, 0f, t);
            image.color = c;
        }
    }

    // ───────────────────────── Pan & zoom ─────────────────────────

    private void Recenter()
    {
        if (DeviceLocation.TryGetLocation(out LatLng here, out _) && IsInside(ToUV(here), 0.05f)) CenterOn(here);
        else CenterOn(viewCenter);
    }

    private void CenterOn(LatLng location)
    {
        Vector2 local = ToContentPosition(location) * zoom;
        mapContent.anchoredPosition = ClampPosition(-local);
        if (scrollRect != null) scrollRect.StopMovement();
    }

    /// <summary>Zooms and pans so the whole route is on screen above the card.</summary>
    private void FitRoute()
    {
        if (routePoints == null || routePoints.Count == 0) return;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (LatLng p in routePoints)
        {
            Vector2 local = ToContentPosition(p);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        Vector2 span = (max - min) * 1.3f + Vector2.one * 220f; // Room for pins and labels.
        Rect view = viewport.rect;
        float fit = Mathf.Min(view.width / Mathf.Max(span.x, 1f), view.height * 0.62f / Mathf.Max(span.y, 1f));
        // Cap the automatic zoom so short routes still show their surroundings.
        SetZoom(Mathf.Clamp(fit, minZoom, Mathf.Min(maxZoom, 2.2f)));
        Vector2 center = (min + max) * 0.5f;
        mapContent.anchoredPosition = ClampPosition(-center * zoom + new Vector2(0f, view.height * 0.14f));
        if (scrollRect != null) scrollRect.StopMovement();
    }

    private void HandleZoomInput()
    {
        // Pinch on phones.
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var t0 = touchscreen.touches[0];
            var t1 = touchscreen.touches[1];
            bool pinching = t0.press.isPressed && t1.press.isPressed;
            if (scrollRect != null) scrollRect.enabled = !pinching;

            if (pinching)
            {
                Vector2 a = t0.position.ReadValue();
                Vector2 b = t1.position.ReadValue();
                float distance = Vector2.Distance(a, b);
                if (lastPinchDistance > 0f && distance > 0f)
                {
                    ZoomAroundScreenPoint(zoom * distance / lastPinchDistance, (a + b) * 0.5f);
                }
                lastPinchDistance = distance;
                return;
            }
            lastPinchDistance = 0f;
        }

        // Mouse wheel in the Editor.
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) ZoomAroundScreenPoint(zoom * (scroll > 0 ? 1.15f : 1f / 1.15f), mouse.position.ReadValue());
        }
    }

    private void ZoomAroundScreenPoint(float newZoom, Vector2 screenPoint)
    {
        Camera uiCamera = null;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) uiCamera = canvas.worldCamera;

        // Map point under the fingers, in the content's unscaled local space.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapContent, screenPoint, uiCamera, out Vector2 underFingers)) return;

        float oldZoom = zoom;
        Vector2 oldPosition = mapContent.anchoredPosition;
        SetZoom(newZoom);

        // Keep that point under the fingers: position + point * zoom must not change.
        mapContent.anchoredPosition = ClampPosition(oldPosition + underFingers * (oldZoom - zoom));
    }

    private void SetZoom(float newZoom)
    {
        zoom = Mathf.Clamp(newZoom, minZoom, maxZoom);
        mapContent.localScale = Vector3.one * zoom;
        ApplyCounterScale();
        ApplyLineThickness();
        mapContent.anchoredPosition = ClampPosition(mapContent.anchoredPosition);
    }

    private void ApplyLineThickness()
    {
        if (pathLine != null) pathLine.Thickness = pathThickness / zoom;
        if (pathCasing != null) pathCasing.Thickness = (pathThickness + 8f) / zoom;
    }

    /// <summary>Pins and the user dot keep the same on-screen size at any zoom.</summary>
    private void ApplyCounterScale()
    {
        foreach (PinView pin in pins.Values)
        {
            bool isSelected = selected != null && pin.place.id == selected.id;
            pin.root.localScale = Vector3.one * (isSelected ? 1.25f : 1f) / zoom;
            if (pin.label != null) pin.label.SetActive(isSelected || zoom >= LabelZoom);
        }
        if (userDot != null) userDot.localScale = Vector3.one / zoom;
    }

    private Vector2 ClampPosition(Vector2 position)
    {
        Rect view = viewport.rect;
        Vector2 half = mapContent.rect.size * zoom * 0.5f;
        float maxX = Mathf.Max(0f, half.x - view.width * 0.5f);
        float maxY = Mathf.Max(0f, half.y - view.height * 0.5f);
        return new Vector2(Mathf.Clamp(position.x, -maxX, maxX), Mathf.Clamp(position.y, -maxY, maxY));
    }

    // ───────────────────────── Status & card ─────────────────────────

    private Coroutine statusRoutine;

    private void ShowStatus(string message, float seconds = 0f)
    {
        if (statusText != null) statusText.text = message;
        if (statusPill != null) statusPill.SetActive(true);
        if (statusRoutine != null) StopCoroutine(statusRoutine);
        statusRoutine = seconds > 0f && isActiveAndEnabled ? StartCoroutine(HideStatusAfter(seconds)) : null;
    }

    private void HideStatus()
    {
        if (statusPill != null) statusPill.SetActive(false);
    }

    private IEnumerator HideStatusAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        HideStatus();
        statusRoutine = null;
    }

    private void SetInfoCardVisible(bool visible)
    {
        if (infoCard != null) infoCard.SetActive(visible);
    }
}
