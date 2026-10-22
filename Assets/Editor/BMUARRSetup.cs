using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vuforia;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;

/// <summary>
/// Editor tools for BMUARR.
///
/// "Build Production Scenes" sets up the two-scene app:
///   MainMenu.unity   – splash, login, destination list. No AR camera.
///   Navigation.unity – Vuforia AR camera, marker, ground plane, route HUD.
/// It is safe to run again: generated objects are rebuilt, Vuforia objects are kept.
/// </summary>
public static class BMUARRSetup
{
    private const string LegacyScenePath = "Assets/Scenes/SampleScene.unity";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string NavigationScenePath = "Assets/Scenes/Navigation.unity";
    private const string MaterialsFolder = "Assets/Materials";
    private const string RouteMaterialPath = MaterialsFolder + "/RouteRibbon.mat";
    private const string MarkerMaterialPath = MaterialsFolder + "/RouteMarker.mat";
    private const string GeneratedUIFolder = "Assets/UI/Generated";
    private const string VoiceClipsFolder = "Assets/Audio/Voice";
    private const string TargetImagePath ="Assets/UI/ImageTargets/image_target_tree.png";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    // Rounded display font used by the app's button artwork; used for titles and buttons.
    private const string DisplayFontPath = "Assets/Fonts/PoetsenOne-Regular SDF.asset";
    private const string AdminConfigPath = "Assets/Resources/AdminConfig.asset";
    private const string CampusMapConfigPath = "Assets/Resources/CampusMapConfig.asset";
    private const string MapsApiConfigPath = "Assets/Resources/MapsApiConfig.asset"; // Git-ignored: holds the API key.
    private const string PinSpritePath = GeneratedUIFolder + "/MapPin.png";
    private const string DotSpritePath = GeneratedUIFolder + "/Dot.png";

    private const float MarkerWidthMeters = 0.2f;

    private const int UILayer = 5;

    // App theme, sampled from the existing screens: green #00B859, mint #DDFFEE / #B2EBCF, white cards.
    private static readonly Color BrandGreen = new Color32(0x00, 0xB8, 0x59, 0xFF);
    private static readonly Color BrandGreenDark = new Color32(0x00, 0x8C, 0x44, 0xFF);
    private static readonly Color Mint = new Color32(0xDD, 0xFF, 0xEE, 0xFF);
    private static readonly Color MintBorder = new Color32(0xB2, 0xEB, 0xCF, 0xFF);
    private static readonly Color TextDark = new Color32(0x1E, 0x2A, 0x24, 0xFF);

    private static readonly Color PanelColor = new Color(1f, 1f, 1f, 0.96f);
    private static readonly Color CardColor = Color.white;
    private static readonly Color PrimaryColor = BrandGreen;
    private static readonly Color SecondaryColor = Mint;
    private static readonly Color DangerColor = new Color32(0xE5, 0x48, 0x4D, 0xFF);
    private static readonly Color AccentColor = BrandGreen;
    private static readonly Color BannerColor = new Color(0.92f, 0.62f, 0.10f, 0.97f);
    private static readonly Color AccentFinishColor = BrandGreenDark;

    // ───────────────────────── Scene setup ─────────────────────────

    [MenuItem("Tools/BMU AR/Build Production Scenes")]
    public static void BuildProductionScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                Debug.LogError("BMU AR: Save or discard your open scene changes first, then run this again.");
                return;
            }
        }

        string menuPath = EnsureMenuSceneAsset();
        if (menuPath == null) return;
        EnsureAdminConfig();
        EnsureCampusMapConfig();
        EnsureMapsApiConfig();

        Material routeMaterial = EnsureMaterial(RouteMaterialPath, transparent: true, doubleSided: true);
        Material markerMaterial = EnsureMaterial(MarkerMaterialPath, transparent: false, doubleSided: false);

        Scene menu = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
        bool navExists = File.Exists(NavigationScenePath);
        Scene nav = navExists
            ? EditorSceneManager.OpenScene(NavigationScenePath, OpenSceneMode.Additive)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        MoveVuforiaObjects(menu, nav);

        SceneManager.SetActiveScene(nav);
        BuildNavigationScene(nav, routeMaterial, markerMaterial);

        SceneManager.SetActiveScene(menu);
        BuildMenuScene(menu);

        EditorSceneManager.MarkSceneDirty(nav);
        EditorSceneManager.MarkSceneDirty(menu);
        bool savedNav = EditorSceneManager.SaveScene(nav, NavigationScenePath);
        bool savedMenu = EditorSceneManager.SaveScene(menu, menuPath);
        EditorSceneManager.CloseScene(nav, true);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(menuPath, true),
            new EditorBuildSettingsScene(NavigationScenePath, true)
        };

        ConfigureScreenAndUI();
        AssetDatabase.SaveAssets();

        if (savedNav && savedMenu) Debug.Log("BMU AR: Production scenes built and saved (MainMenu + Navigation).");
        else Debug.LogError("BMU AR: Scenes were built but saving failed. Check the Console.");
    }

    /// <summary>Renames the old SampleScene to MainMenu (keeps its GUID and references).</summary>
    private static string EnsureMenuSceneAsset()
    {
        if (File.Exists(MenuScenePath)) return MenuScenePath;

        if (!File.Exists(LegacyScenePath))
        {
            Debug.LogError($"BMU AR: Neither '{MenuScenePath}' nor '{LegacyScenePath}' exists.");
            return null;
        }

        string error = AssetDatabase.MoveAsset(LegacyScenePath, MenuScenePath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError("BMU AR: Could not rename SampleScene: " + error);
            return null;
        }
        Debug.Log("BMU AR: Renamed SampleScene.unity to MainMenu.unity.");
        return MenuScenePath;
    }

    /// <summary>Moves the AR camera and marker out of the menu scene, so the camera only runs in Navigation.</summary>
    private static void MoveVuforiaObjects(Scene menu, Scene nav)
    {
        foreach (VuforiaBehaviour cam in FindInScene<VuforiaBehaviour>(menu))
        {
            GameObject root = cam.transform.root.gameObject;
            if (FindInScene<VuforiaBehaviour>(nav).Count == 0)
            {
                SceneManager.MoveGameObjectToScene(root, nav);
                Debug.Log("BMU AR: Moved AR Camera to Navigation scene.");
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }

        foreach (ImageTargetBehaviour target in FindInScene<ImageTargetBehaviour>(menu))
        {
            GameObject root = target.transform.root.gameObject;
            if (FindInScene<ImageTargetBehaviour>(nav).Count == 0)
            {
                SceneManager.MoveGameObjectToScene(root, nav);
                Debug.Log("BMU AR: Moved marker '" + root.name + "' to Navigation scene.");
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }
    }

    private static void BuildNavigationScene(Scene nav, Material routeMaterial, Material markerMaterial)
    {
        // Remove what an earlier run generated.
        foreach (string name in new[] { "NavigationSystems", "NavigationCanvas", "EventSystem", "Calibrated_World_Origin", "WaypointManager" })
        {
            foreach (GameObject go in FindByName(nav, name)) Object.DestroyImmediate(go);
        }

        // AR Camera
        VuforiaBehaviour vuforia = FindInScene<VuforiaBehaviour>(nav).FirstOrDefault();
        if (vuforia == null)
        {
            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/AR Camera");
            vuforia = FindInScene<VuforiaBehaviour>(nav).FirstOrDefault();
        }
        if (vuforia == null)
        {
            Debug.LogError("BMU AR: Could not create the Vuforia AR Camera.");
            return;
        }
        GameObject arCamera = vuforia.gameObject;
        arCamera.tag = "MainCamera";
        RemoveMissingScripts(arCamera);

        // NavigationSceneController shows its own error dialog; avoid a second popup.
        foreach (MonoBehaviour mb in arCamera.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "DefaultInitializationErrorHandler") Object.DestroyImmediate(mb);
        }

        // Marker
        ImageTargetBehaviour target = FindInScene<ImageTargetBehaviour>(nav).FirstOrDefault() ?? CreateImageTarget(nav);
        if (target == null)
        {
            Debug.LogError("BMU AR: Could not create the image target.");
            return;
        }
        GameObject targetGO = target.gameObject;
        RemoveMissingScripts(targetGO);
        AnchorTracker anchorTracker = GetOrAdd<AnchorTracker>(targetGO);
        SetSerialized(anchorTracker, "targetObserver", target);
        SetSerialized(anchorTracker, "physicalAnchor", targetGO.transform);
        ReportMarkerSize(target);

        // Ground plane, used for floor height when the marker is on a wall.
        PlaneFinderBehaviour planeFinder = EnsurePlaneFinder(nav);

        // Systems
        var systems = new GameObject("NavigationSystems");
        var floor = systems.AddComponent<FloorEstimator>();
        SetSerialized(floor, "anchorTracker", anchorTracker);
        SetSerialized(floor, "planeFinder", planeFinder);

        var tracking = systems.AddComponent<TrackingQualityMonitor>();
        SetSerialized(tracking, "anchorTracker", anchorTracker);

        var renderer = systems.AddComponent<ARPathRenderer>();
        renderer.anchorTracker = anchorTracker;
        renderer.routeMaterial = routeMaterial;
        renderer.markerMaterial = markerMaterial;

        var recorder = systems.AddComponent<ARPathRecorder>();
        recorder.arCamera = arCamera.transform;
        recorder.anchorTracker = anchorTracker;
        recorder.pathRenderer = renderer;
        recorder.floorEstimator = floor;
        recorder.trackingMonitor = tracking;

        var guidance = systems.AddComponent<GuidanceController>();
        guidance.arCamera = arCamera.transform;
        guidance.anchorTracker = anchorTracker;
        guidance.pathRenderer = renderer;
        guidance.floorEstimator = floor;

        var voice = systems.AddComponent<VoiceGuide>();
        voice.guidance = guidance;
        voice.clips = LoadVoiceClips();

        var controller = systems.AddComponent<NavigationSceneController>();
        controller.anchorTracker = anchorTracker;
        controller.recorder = recorder;
        controller.guidance = guidance;
        controller.trackingMonitor = tracking;
        controller.pathRenderer = renderer;

        // UI
        CreateEventSystem();
        Transform safeArea = CreateCanvas("NavigationCanvas", sortingOrder: 10);
        controller.hud = BuildNavigationHUD(safeArea);
        CreateNotifier(safeArea);
    }

    private static void BuildMenuScene(Scene menu)
    {
        foreach (string name in new[] { "AR Session", "XR Origin (AR Rig)", "WaypointManager", "Calibrated_World_Origin", "RecordingHUD", "GuidanceHUD", "NotifierArea", "LocationPromptModal", "MainHeader", "CampusMapPanel", "ModeChips" })
        {
            foreach (GameObject go in FindByName(menu, name)) Object.DestroyImmediate(go);
        }
        RemoveMissingPrefabInstances(menu);
        foreach (GameObject root in menu.GetRootGameObjects()) RemoveMissingScripts(root);

        // A plain camera: the menu never starts the AR camera.
        if (FindInScene<Camera>(menu).Count == 0)
        {
            var camGO = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            cam.cullingMask = 0;
        }

        GameObject canvasGO = FindByName(menu, "Canvas").FirstOrDefault();
        if (canvasGO == null)
        {
            Debug.LogError("BMU AR: No 'Canvas' in the menu scene.");
            return;
        }

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.sortingOrder = 0;
        ConfigureScaler(canvasGO.GetComponent<CanvasScaler>());

        Transform tCanvas = canvasGO.transform;
        var navMgr = GetOrAdd<CampusNavigationManager>(canvasGO);
        Transform tMain = tCanvas.Find("Main_Panel");
        Transform tDest = tCanvas.Find("DestinationPanel");
        if (tMain != null) navMgr.mainPanel = tMain.gameObject;
        if (tDest != null) navMgr.destinationPanel = tDest.gameObject;

        // Menu screens start hidden: they sit above the splash in the canvas and would cover it.
        if (tMain != null) tMain.gameObject.SetActive(false);
        if (tDest != null) tDest.gameObject.SetActive(false);

        navMgr.locationPromptModal = BuildLocationPromptModal(tCanvas);
        AlignLoginButtons(tCanvas);

        // Explore Campus: 2D campus map. Select Destination: 2D / AR tags on each place.
        Sprite pinSprite = EnsureGeneratedSprite(PinSpritePath, 192, 256, PinShape);
        Sprite dotSprite = EnsureGeneratedSprite(DotSpritePath, 128, 128, p => (p - new Vector2(0.5f, 0.5f)).magnitude <= 0.48f);
        Image destBack = tDest != null && tDest.Find("Back") != null ? tDest.Find("Back").GetComponent<Image>() : null;
        navMgr.campusMap = BuildCampusMapPanel(tCanvas, navMgr, pinSprite, dotSprite, destBack != null ? destBack.sprite : null);
        navMgr.campusMapPanel = navMgr.campusMap.gameObject;
        navMgr.campusMap.transform.SetSiblingIndex(navMgr.locationPromptModal.transform.GetSiblingIndex());
        if (tMain != null) RewireButton(tMain.Find("Explore_Button"), navMgr.ShowCampusMap);

        // Legacy QR / AR panels are unused; keep them hidden.
        foreach (string legacy in new[] { "QRMainPanel", "QRScanningPanel", "ARScreenPanel", "DestARScreenPanel" })
        {
            Transform t = tCanvas.Find(legacy);
            if (t != null) t.gameObject.SetActive(false);
        }

        GameObject uiManagerGO = FindByName(menu, "Ui Manager").FirstOrDefault();
        if (uiManagerGO != null)
        {
            var uiManager = uiManagerGO.GetComponent<UIManager>();
            var auth = uiManagerGO.GetComponent<GoogleAuthManager>();
            if (uiManager != null)
            {
                uiManager.campusNavigationManager = navMgr;
                uiManager.googleAuthManager = auth;
            }

            Transform googleButton = tCanvas.Find("Mail_Verification_Panel/Continue with Google");
            if (auth != null && googleButton != null) auth.signInButton = googleButton.GetComponent<Button>();

            if (tMain != null) BuildMainHeader(tMain, uiManager, auth);
        }
        else
        {
            Debug.LogWarning("BMU AR: 'Ui Manager' not found. The Google sign-in bridge sends results to that object name.");
        }

        // Banner on top of everything in the menu canvas.
        var notifierRoot = new GameObject("NotifierArea", typeof(RectTransform), typeof(SafeAreaFitter));
        notifierRoot.layer = UILayer;
        notifierRoot.transform.SetParent(tCanvas, false);
        Stretch(notifierRoot.GetComponent<RectTransform>());
        notifierRoot.transform.SetAsLastSibling();
        CreateNotifier(notifierRoot.transform);

        CreateEventSystemIfMissing(menu);
    }

    // ───────────────────────── Menu UI ─────────────────────────

    /// <summary>The Record / Guide Me prompt, in the app's white-and-green card style.</summary>
    private static LocationPromptModal BuildLocationPromptModal(Transform canvas)
    {
        var root = new GameObject("LocationPromptModal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.layer = UILayer;
        root.transform.SetParent(canvas, false);
        Stretch(root.GetComponent<RectTransform>());
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f); // Dims and blocks taps behind.
        root.transform.SetAsLastSibling();

        RectTransform card = CreatePanel("Card", root.transform, CardColor);
        card.anchorMin = new Vector2(0.07f, 0.5f);
        card.anchorMax = new Vector2(0.93f, 0.5f);
        card.sizeDelta = new Vector2(0f, 900f);
        card.anchoredPosition = Vector2.zero;
        var cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = MintBorder;
        cardOutline.effectDistance = new Vector2(4f, -4f);

        var modal = root.AddComponent<LocationPromptModal>();

        modal.titleText = CreateText("Title", card, "Destination", 58, FontStyles.Bold, BrandGreenDark);
        Place(modal.titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -95), new Vector2(-80, 100));
        modal.titleText.enableAutoSizing = true;
        modal.titleText.fontSizeMin = 36;
        modal.titleText.fontSizeMax = 58;

        modal.messageText = CreateText("Message", card, "", 34, FontStyles.Normal, TextDark);
        Place(modal.messageText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -260), new Vector2(-100, 200));
        modal.messageText.enableAutoSizing = true;
        modal.messageText.fontSizeMin = 22;
        modal.messageText.fontSizeMax = 34;

        modal.distanceText = CreateText("Distance", card, "", 32, FontStyles.Bold, BrandGreen);
        Place(modal.distanceText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -400), new Vector2(-80, 60));

        modal.recordButton = CreateButton("RecordButton", card, "Record Path", PrimaryColor, 40, out _);
        Place((RectTransform)modal.recordButton.transform, new Vector2(0.08f, 0), new Vector2(0.92f, 0), new Vector2(0, 345), new Vector2(0, 130));
        modal.guideMeButton = CreateButton("GuideMeButton", card, "Guide Me", PrimaryColor, 40, out _);
        Place((RectTransform)modal.guideMeButton.transform, new Vector2(0.08f, 0), new Vector2(0.92f, 0), new Vector2(0, 345), new Vector2(0, 130));
        modal.reRecordButton = CreateButton("ReRecordButton", card, "Re-record Path", SecondaryColor, 32, out _);
        Place((RectTransform)modal.reRecordButton.transform, new Vector2(0.08f, 0), new Vector2(0.92f, 0), new Vector2(0, 205), new Vector2(0, 100));
        modal.cancelButton = CreateButton("CancelButton", card, "Back", Color.white, 32, out _);
        Place((RectTransform)modal.cancelButton.transform, new Vector2(0.08f, 0), new Vector2(0.92f, 0), new Vector2(0, 80), new Vector2(0, 90));

        root.SetActive(false);
        return modal;
    }

    /// <summary>Welcome line, admin badge and Log out / Sign in button on the main screen.</summary>
    private static void BuildMainHeader(Transform mainPanel, UIManager uiManager, GoogleAuthManager auth)
    {
        var header = new GameObject("MainHeader", typeof(RectTransform), typeof(SafeAreaFitter));
        header.layer = UILayer;
        header.transform.SetParent(mainPanel, false);
        Stretch(header.GetComponent<RectTransform>());
        header.transform.SetAsLastSibling();

        var component = header.AddComponent<MainPanelHeader>();
        component.uiManager = uiManager;
        component.googleAuthManager = auth;

        component.accountButton = CreateButton("AccountButton", header.transform, "Log out", Color.white, 32, out component.accountButtonText);
        var accountRt = (RectTransform)component.accountButton.transform;
        accountRt.anchorMin = accountRt.anchorMax = accountRt.pivot = new Vector2(1f, 1f);
        accountRt.anchoredPosition = new Vector2(-36f, -36f);
        accountRt.sizeDelta = new Vector2(250f, 96f);

        component.welcomeText = CreateText("Welcome", header.transform, "Welcome!", 60, FontStyles.Bold, BrandGreenDark);
        Place(component.welcomeText.rectTransform, new Vector2(0.06f, 0.405f), new Vector2(0.94f, 0.455f), Vector2.zero, Vector2.zero);
        component.welcomeText.enableAutoSizing = true;
        component.welcomeText.fontSizeMin = 36;
        component.welcomeText.fontSizeMax = 60;
        component.welcomeText.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform badge = CreatePanel("AdminBadge", header.transform, BrandGreen);
        badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.381f);
        badge.sizeDelta = new Vector2(200f, 56f);
        badge.anchoredPosition = Vector2.zero;
        component.roleBadge = badge.gameObject;
        component.roleText = CreateText("Label", badge, "ADMIN", 30, FontStyles.Bold, Color.white);
        Stretch(component.roleText.rectTransform);
        badge.gameObject.SetActive(false);
    }

    /// <summary>
    /// Sign-in is required, so the Skip Login buttons are removed. The one remaining button
    /// on each login screen is centred in the white card at the bottom of the background
    /// (the card spans about 2–18% of the screen height).
    /// </summary>
    private static void AlignLoginButtons(Transform canvas)
    {
        foreach (string skip in new[] { "Login_Screen_Panel/SkipLogin (1)", "Mail_Verification_Panel/SkipLogin" })
        {
            Transform t = canvas.Find(skip);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var loginButton = canvas.Find("Login_Screen_Panel/LoginiwthID") as RectTransform;
        var googleButton = canvas.Find("Mail_Verification_Panel/Continue with Google") as RectTransform;
        if (loginButton != null)
        {
            loginButton.anchorMin = new Vector2(loginButton.anchorMin.x, 0.075f);
            loginButton.anchorMax = new Vector2(loginButton.anchorMax.x, 0.125f);
        }
        CopyLayout(loginButton, googleButton);
    }

    private static void CopyLayout(RectTransform from, RectTransform to)
    {
        if (from == null || to == null) return;
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.sizeDelta = from.sizeDelta;
        to.anchoredPosition = from.anchoredPosition;
        to.localScale = Vector3.one;
    }

    /// <summary>Explore Campus: title, place chips, styled map with pins and route, and a directions card.</summary>
    private static CampusMapController BuildCampusMapPanel(Transform canvas, CampusNavigationManager navMgr,
                                                           Sprite pinSprite, Sprite dotSprite, Sprite backSprite)
    {
        const float topBarHeight = 200f;
        const float chipsHeight = 112f;
        Color mutedText = new Color32(0x5E, 0x6E, 0x66, 0xFF);

        var root = new GameObject("CampusMapPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.layer = UILayer;
        root.transform.SetParent(canvas, false);
        Stretch(root.GetComponent<RectTransform>());
        root.GetComponent<Image>().color = new Color32(0xF1, 0xFB, 0xF5, 0xFF);
        var map = root.AddComponent<CampusMapController>();

        var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
        safe.layer = UILayer;
        safe.transform.SetParent(root.transform, false);
        Stretch(safe.GetComponent<RectTransform>());

        // Map viewport with pan (ScrollRect) below the title and chips.
        var viewportGO = new GameObject("MapViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewportGO.layer = UILayer;
        viewportGO.transform.SetParent(safe.transform, false);
        var viewport = viewportGO.GetComponent<RectTransform>();
        Stretch(viewport);
        viewport.offsetMax = new Vector2(0f, -(topBarHeight + chipsHeight));
        viewportGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // Catches drags.

        var contentGO = new GameObject("MapContent", typeof(RectTransform));
        contentGO.layer = UILayer;
        contentGO.transform.SetParent(viewport, false);
        var content = contentGO.GetComponent<RectTransform>();
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
        content.sizeDelta = new Vector2(1000f, 1000f);

        var scroll = viewportGO.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 0f; // Mouse wheel zooms instead.

        var imageGO = new GameObject("MapImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageGO.layer = UILayer;
        imageGO.transform.SetParent(content, false);
        Stretch(imageGO.GetComponent<RectTransform>());
        var mapImage = imageGO.GetComponent<RawImage>();
        mapImage.color = new Color32(0xE8, 0xF8, 0xEF, 0xFF);
        mapImage.raycastTarget = false;

        UILineGraphic casing = CreateLine("RouteCasing", content, Color.white);
        UILineGraphic line = CreateLine("RouteLine", content, BrandGreen);

        var pinsGO = new GameObject("Pins", typeof(RectTransform));
        pinsGO.layer = UILayer;
        pinsGO.transform.SetParent(content, false);
        Stretch(pinsGO.GetComponent<RectTransform>());

        var dotGO = new GameObject("UserDot", typeof(RectTransform));
        dotGO.layer = UILayer;
        dotGO.transform.SetParent(content, false);
        var dot = dotGO.GetComponent<RectTransform>();
        dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
        dot.sizeDelta = new Vector2(110f, 110f);
        RectTransform halo = AddDotLayer(dot, "Halo", dotSprite, new Color(BrandGreen.r, BrandGreen.g, BrandGreen.b, 0.3f), 110f);
        AddDotLayer(dot, "Ring", dotSprite, Color.white, 46f);
        AddDotLayer(dot, "Core", dotSprite, BrandGreen, 30f);
        dotGO.SetActive(false);

        // Title bar.
        RectTransform top = CreatePanel("TopBar", safe.transform, Color.white);
        AnchorTop(top, 0f, topBarHeight);
        Image topImage = top.GetComponent<Image>();
        topImage.sprite = null; // Plain bar, edge to edge.
        topImage.type = Image.Type.Simple;
        var backGO = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        backGO.layer = UILayer;
        backGO.transform.SetParent(top, false);
        var backRt = backGO.GetComponent<RectTransform>();
        backRt.anchorMin = backRt.anchorMax = new Vector2(0f, 0.5f);
        backRt.pivot = new Vector2(0f, 0.5f);
        backRt.anchoredPosition = new Vector2(36f, 0f);
        backRt.sizeDelta = new Vector2(110f, 110f);
        var backImage = backGO.GetComponent<Image>();
        backImage.sprite = backSprite;
        backImage.color = backSprite != null ? Color.white : BrandGreen;
        backImage.preserveAspect = true;
        UnityEventTools.AddVoidPersistentListener(backGO.GetComponent<Button>().onClick, navMgr.CloseCampusMap);
        TMP_Text title = CreateText("Title", top, "Explore Campus", 52, FontStyles.Bold, BrandGreenDark);
        Place(title.rectTransform, new Vector2(0, 0.42f), new Vector2(1, 1), Vector2.zero, new Vector2(-340, 0));
        TMP_Text subtitle = CreateText("Subtitle", top, "Tap a place to get walking directions", 28, FontStyles.Normal, mutedText);
        Place(subtitle.rectTransform, new Vector2(0, 0.08f), new Vector2(1, 0.42f), Vector2.zero, new Vector2(-340, 0));

        // Place chips: horizontal, scrollable.
        var chipsBarGO = new GameObject("PlaceChips", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        chipsBarGO.layer = UILayer;
        chipsBarGO.transform.SetParent(safe.transform, false);
        var chipsBar = chipsBarGO.GetComponent<RectTransform>();
        AnchorTop(chipsBar, -topBarHeight, chipsHeight);
        chipsBarGO.GetComponent<Image>().color = Color.white;
        var chipsContentGO = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        chipsContentGO.layer = UILayer;
        chipsContentGO.transform.SetParent(chipsBar, false);
        var chipsContent = chipsContentGO.GetComponent<RectTransform>();
        chipsContent.anchorMin = new Vector2(0f, 0f);
        chipsContent.anchorMax = new Vector2(0f, 1f);
        chipsContent.pivot = new Vector2(0f, 0.5f);
        chipsContent.anchoredPosition = Vector2.zero;
        var layout = chipsContentGO.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 16, 20);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        chipsContentGO.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var chipsScroll = chipsBarGO.GetComponent<ScrollRect>();
        chipsScroll.content = chipsContent;
        chipsScroll.viewport = chipsBar;
        chipsScroll.horizontal = true;
        chipsScroll.vertical = false;
        chipsScroll.movementType = ScrollRect.MovementType.Clamped;
        chipsScroll.scrollSensitivity = 0f;
        var divider = CreateImage("Divider", chipsBar, MintBorder, null, sliced: false);
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(1f, 0f);
        divider.rectTransform.pivot = new Vector2(0.5f, 0f);
        divider.rectTransform.sizeDelta = new Vector2(0f, 3f);
        divider.raycastTarget = false;

        // Status pill (loading / errors), floating over the map.
        RectTransform pill = CreatePanel("StatusPill", safe.transform, Color.white);
        AnchorTop(pill, -(topBarHeight + chipsHeight + 24f), 84f, horizontalInset: 90f);
        AddOutline(pill.gameObject);
        TMP_Text status = CreateText("Text", pill, "Loading map…", 28, FontStyles.Normal, TextDark);
        Place(status.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-40, -10));
        status.enableAutoSizing = true;
        status.fontSizeMin = 18;
        status.fontSizeMax = 28;

        // Recenter on the user.
        var recenterGO = new GameObject("Recenter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        recenterGO.layer = UILayer;
        recenterGO.transform.SetParent(safe.transform, false);
        var recenterRt = recenterGO.GetComponent<RectTransform>();
        recenterRt.anchorMin = recenterRt.anchorMax = recenterRt.pivot = new Vector2(1f, 0f);
        recenterRt.anchoredPosition = new Vector2(-36f, 360f);
        recenterRt.sizeDelta = new Vector2(110f, 110f);
        var recenterImage = recenterGO.GetComponent<Image>();
        recenterImage.sprite = dotSprite;
        recenterImage.color = Color.white;
        AddOutline(recenterGO);
        AddDotLayer(recenterRt, "Ring", dotSprite, BrandGreen, 44f);
        AddDotLayer(recenterRt, "Hole", dotSprite, Color.white, 26f);
        AddDotLayer(recenterRt, "Dot", dotSprite, BrandGreen, 14f);

        // Directions card.
        RectTransform card = CreatePanel("InfoCard", safe.transform, Color.white);
        AnchorBottom(card, 30f, 300f, horizontalInset: 30f);
        AddOutline(card.gameObject);
        Image icon = CreateImage("Icon", card, BrandGreen, pinSprite, sliced: false);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(36f, -36f);
        icon.rectTransform.sizeDelta = new Vector2(72f, 96f);
        TMP_Text infoTitle = CreateText("Title", card, "Library", 46, FontStyles.Bold, BrandGreenDark);
        Place(infoTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -64), new Vector2(-260, 70));
        infoTitle.alignment = TextAlignmentOptions.Left;
        infoTitle.enableAutoSizing = true;
        infoTitle.fontSizeMin = 30;
        infoTitle.fontSizeMax = 46;
        TMP_Text infoDetail = CreateText("Detail", card, "", 34, FontStyles.Bold, BrandGreen);
        Place(infoDetail.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -122), new Vector2(-260, 50));
        infoDetail.alignment = TextAlignmentOptions.Left;
        TMP_Text infoSub = CreateText("SubDetail", card, "", 26, FontStyles.Normal, mutedText);
        Place(infoSub.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -170), new Vector2(-260, 44));
        infoSub.alignment = TextAlignmentOptions.Left;
        infoSub.enableAutoSizing = true;
        infoSub.fontSizeMin = 18;
        infoSub.fontSizeMax = 26;
        Button clear = CreateButton("ClearButton", card, "Clear route", SecondaryColor, 30, out _);
        Place((RectTransform)clear.transform, new Vector2(0.05f, 0), new Vector2(0.95f, 0), new Vector2(0, 62), new Vector2(0, 84));
        Button close = CreateButton("CloseButton", card, "×", Color.white, 44, out _);
        var closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-24f, -24f);
        closeRt.sizeDelta = new Vector2(84f, 84f);
        card.gameObject.SetActive(false);

        map.scrollRect = scroll;
        map.viewport = viewport;
        map.mapContent = content;
        map.mapImage = mapImage;
        map.pathCasing = casing;
        map.pathLine = line;
        map.pinsRoot = pinsGO.GetComponent<RectTransform>();
        map.userDot = dot;
        map.userHalo = halo;
        map.pinSprite = pinSprite;
        map.roundedSprite = RoundedSprite;
        map.labelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
        map.placeChipsRoot = chipsContent;
        map.pinColor = BrandGreen;
        map.selectedPinColor = BrandGreenDark;
        map.statusText = status;
        map.statusPill = pill.gameObject;
        map.recenterButton = recenterGO.GetComponent<Button>();
        map.infoCard = card.gameObject;
        map.infoTitle = infoTitle;
        map.infoDetail = infoDetail;
        map.infoSubDetail = infoSub;
        map.infoClearButton = clear;
        map.infoCloseButton = close;

        root.SetActive(false);
        return map;
    }

    private static UILineGraphic CreateLine(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UILineGraphic));
        go.layer = UILayer;
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        var line = go.GetComponent<UILineGraphic>();
        line.color = color;
        line.raycastTarget = false;
        return line;
    }

    private static void RewireButton(Transform buttonTransform, UnityEngine.Events.UnityAction action)
    {
        if (buttonTransform == null) return;
        Button button = buttonTransform.GetComponent<Button>();
        if (button == null) return;
        RemovePersistentListeners(button.onClick);
        UnityEventTools.AddVoidPersistentListener(button.onClick, action);
    }

    private static RectTransform AddDotLayer(RectTransform parent, string name, Sprite sprite, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = UILayer;
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    private static void AddOutline(GameObject go)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = MintBorder;
        outline.effectDistance = new Vector2(3f, -3f);
    }

    /// <summary>Map pin: a teardrop with a hole, tip at the bottom centre.</summary>
    private static bool PinShape(Vector2 p)
    {
        var head = new Vector2(0.5f, 0.64f);
        float headRadius = 0.34f;
        bool inHead = (p - head).magnitude <= headRadius;
        bool inTip = PointInPolygon(p, new[] { new Vector2(0.5f, 0.02f), new Vector2(0.215f, 0.5f), new Vector2(0.785f, 0.5f) });
        bool inHole = (p - head).magnitude <= 0.14f;
        return (inHead || inTip) && !inHole;
    }

    /// <summary>Rasterises a white, anti-aliased shape to a PNG sprite (once).</summary>
    private static Sprite EnsureGeneratedSprite(string path, int width, int height, System.Func<Vector2, bool> inside)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
        if (!AssetDatabase.IsValidFolder(GeneratedUIFolder)) AssetDatabase.CreateFolder("Assets/UI", "Generated");

        const int supersample = 4;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < supersample; sy++)
                for (int sx = 0; sx < supersample; sx++)
                {
                    var p = new Vector2((x + (sx + 0.5f) / supersample) / width, (y + (sy + 0.5f) / supersample) / height);
                    if (inside(p)) hits++;
                }
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)(255 * hits / (supersample * supersample)));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    [MenuItem("Tools/BMU AR/Edit Campus Map")]
    public static void EditCampusMap()
    {
        CampusMapConfig config = EnsureCampusMapConfig();
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    private static CampusMapConfig EnsureCampusMapConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<CampusMapConfig>(CampusMapConfigPath);
        if (config != null) return config;

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        config = ScriptableObject.CreateInstance<CampusMapConfig>();
        // Library and Reception from Google Places; the rest marked on campus.
        config.places.Add(new CampusPlace { id = "Library", displayName = "Library", latitude = 28.246684, longitude = 76.814495, placed = true });
        config.places.Add(new CampusPlace { id = "Reception", displayName = "Reception", latitude = 28.247239, longitude = 76.814434, placed = true });
        config.places.Add(new CampusPlace { id = "Grand Auditorium", displayName = "Grand Auditorium", latitude = 28.24748598393818, longitude = 76.81404787416709, placed = true });
        config.places.Add(new CampusPlace { id = "Program Office", displayName = "Program Office", latitude = 28.247535457469354, longitude = 76.81436405015543, placed = true });
        config.places.Add(new CampusPlace { id = "D Cafe", displayName = "D'Cafe", latitude = 28.248251933382374, longitude = 76.81152110823511, placed = true });
        AssetDatabase.CreateAsset(config, CampusMapConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log("BMU AR: Created " + CampusMapConfigPath + " (Tools > BMU AR > Edit Campus Map).");
        return config;
    }

    private static void EnsureMapsApiConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<MapsApiConfig>(MapsApiConfigPath);
        if (config == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            config = ScriptableObject.CreateInstance<MapsApiConfig>();
            AssetDatabase.CreateAsset(config, MapsApiConfigPath);
            AssetDatabase.SaveAssets();
        }
        if (string.IsNullOrWhiteSpace(config.apiKey))
        {
            Debug.LogWarning("BMU AR: No Google Maps API key in " + MapsApiConfigPath + "; the campus map will not load. This file is git-ignored.");
        }
    }

    [MenuItem("Tools/BMU AR/Edit Admin List")]
    public static void EditAdminList()
    {
        AdminConfig config = EnsureAdminConfig();
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    private static AdminConfig EnsureAdminConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<AdminConfig>(AdminConfigPath);
        if (config != null) return config;

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        config = ScriptableObject.CreateInstance<AdminConfig>();
        AssetDatabase.CreateAsset(config, AdminConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log("BMU AR: Created " + AdminConfigPath + ". Add admin Google emails there (Tools > BMU AR > Edit Admin List).");
        return config;
    }

    // ───────────────────────── Vuforia helpers ─────────────────────────

    private static ImageTargetBehaviour CreateImageTarget(Scene nav)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TargetImagePath);
        if (texture == null)
        {
            Debug.LogError($"BMU AR: Marker image not found at '{TargetImagePath}'.");
            return null;
        }

        EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/Image Target");
        ImageTargetBehaviour itb = FindInScene<ImageTargetBehaviour>(nav).FirstOrDefault();
        if (itb == null) return null;

        itb.gameObject.name = "ImageTarget_Compass";
        var so = new SerializedObject(itb);
        SetProp(so, "mRuntimeTexture", texture);
        SetProp(so, "mWidth", MarkerWidthMeters);
        SetProp(so, "mHeight", MarkerWidthMeters * texture.height / Mathf.Max(1, texture.width));
        SetProp(so, "mImageTargetType", 3); // From image
        SetProp(so, "mTrackableName", "Image_target");
        SetProp(so, "mDataSetPath", "");
        so.ApplyModifiedPropertiesWithoutUndo();
        return itb;
    }

    private static void ReportMarkerSize(ImageTargetBehaviour target)
    {
        var so = new SerializedObject(target);

        // Keep the printed size fixed and the aspect matching the image. Vuforia caches the
        // aspect when the texture is imported, so a re-import can silently make the marker square.
        var markerTexture = so.FindProperty("mRuntimeTexture")?.objectReferenceValue as Texture2D;
        if (markerTexture != null && so.FindProperty("mAspectRatio") != null)
        {
            float aspect = markerTexture.height / (float)Mathf.Max(1, markerTexture.width);
            so.FindProperty("mAspectRatio").floatValue = aspect;
            so.FindProperty("mWidth").floatValue = MarkerWidthMeters;
            so.FindProperty("mHeight").floatValue = MarkerWidthMeters * aspect;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedProperty width = so.FindProperty("mWidth");
        if (width != null)
        {
            Debug.Log($"BMU AR: Marker width is set to {width.floatValue * 100f:F0} cm. The printed marker must be exactly this wide, or route distances will be scaled wrong.");
        }

        // Vuforia rejects runtime image targets over 2048 px on either side, and the marker then never detects.
        var texture = so.FindProperty("mRuntimeTexture")?.objectReferenceValue as Texture2D;
        if (texture != null && Mathf.Max(texture.width, texture.height) > 2048)
        {
            Debug.LogError($"BMU AR: Marker image is {texture.width}x{texture.height}. Vuforia allows at most 2048 px per side; lower Max Size in its import settings.");
        }
    }

    private static PlaneFinderBehaviour EnsurePlaneFinder(Scene nav)
    {
        PlaneFinderBehaviour planeFinder = FindInScene<PlaneFinderBehaviour>(nav).FirstOrDefault();
        if (planeFinder == null)
        {
            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/Ground Plane/Plane Finder");
            planeFinder = FindInScene<PlaneFinderBehaviour>(nav).FirstOrDefault();
        }
        if (planeFinder == null)
        {
            Debug.LogWarning("BMU AR: Could not create a Plane Finder; floor height will be estimated.");
            return null;
        }

        // We only want automatic floor hit tests: no tap-to-place content.
        RemovePersistentListeners(planeFinder.OnInteractiveHitTest);
        RemovePersistentListeners(planeFinder.OnAutomaticHitTest);
        foreach (MonoBehaviour mb in planeFinder.GetComponents<MonoBehaviour>())
        {
            string typeName = mb.GetType().Name;
            if (typeName == "ContentPositioningBehaviour" || typeName == "AnchorInputListenerBehaviour")
            {
                Object.DestroyImmediate(mb);
            }
        }
        // Vuforia needs an indicator object (it throws on start without one), but we
        // do not want Vuforia's visual: give it an empty one.
        if (planeFinder.PlaneIndicator == null || planeFinder.PlaneIndicator.name != "FloorIndicator_Hidden")
        {
            if (planeFinder.PlaneIndicator != null && planeFinder.PlaneIndicator.transform.IsChildOf(planeFinder.transform))
            {
                Object.DestroyImmediate(planeFinder.PlaneIndicator);
            }
            var indicator = new GameObject("FloorIndicator_Hidden");
            indicator.transform.SetParent(planeFinder.transform, false);
            planeFinder.PlaneIndicator = indicator;
        }
        planeFinder.HitTestMode = HitTestMode.AUTOMATIC;
        planeFinder.gameObject.name = "PlaneFinder_Floor";
        EditorUtility.SetDirty(planeFinder);
        return planeFinder;
    }

    private static void RemovePersistentListeners(UnityEngine.Events.UnityEventBase evt)
    {
        if (evt == null) return;
        while (evt.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(evt, 0);
    }

    // ───────────────────────── HUD ─────────────────────────

    private static NavigationHUD BuildNavigationHUD(Transform parent)
    {
        var hudGO = new GameObject("NavigationHUD", typeof(RectTransform));
        hudGO.layer = UILayer;
        hudGO.transform.SetParent(parent, false);
        Stretch(hudGO.GetComponent<RectTransform>());
        var hud = hudGO.AddComponent<NavigationHUD>();

        // Top bar
        RectTransform top = CreatePanel("TopBar", hudGO.transform, PanelColor);
        AnchorTop(top, 0f, 250f);
        hud.titleText = CreateText("Title", top, "RECORDING", 42, FontStyles.Bold, AccentColor);
        Place(hud.titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(-60, 70));
        hud.statusText = CreateText("Status", top, "Point the camera at the marker.", 30, FontStyles.Normal, TextDark);
        Place(hud.statusText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -35), new Vector2(-80, -110));
        hud.statusText.enableAutoSizing = true;
        hud.statusText.fontSizeMin = 20;
        hud.statusText.fontSizeMax = 30;

        // Guidance card
        RectTransform card = CreatePanel("GuidanceCard", hudGO.transform, CardColor);
        AnchorTop(card, -270f, 220f, horizontalInset: 40f);
        hud.guidanceCard = card.gameObject;
        hud.instructionText = CreateText("Instruction", card, "Continue straight", 50, FontStyles.Bold, TextDark);
        Place(hud.instructionText.rectTransform, new Vector2(0, 0.4f), new Vector2(1, 1), Vector2.zero, new Vector2(-50, -10));
        hud.instructionText.enableAutoSizing = true;
        hud.instructionText.fontSizeMin = 28;
        hud.instructionText.fontSizeMax = 50;
        hud.distanceText = CreateText("Distance", card, "0 m left", 34, FontStyles.Normal, AccentColor);
        Place(hud.distanceText.rectTransform, new Vector2(0, 0), new Vector2(1, 0.4f), Vector2.zero, new Vector2(-50, 0));

        // Aiming crosshair (recording): where DROP POINT places a point.
        TMP_Text crosshair = CreateText("Crosshair", hudGO.transform, "+", 110, FontStyles.Normal, new Color(1f, 1f, 1f, 0.85f));
        Place(crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 160));
        crosshair.outlineWidth = 0.25f;
        crosshair.outlineColor = new Color32(0, 0, 0, 200);
        hud.crosshair = crosshair.gameObject;

        // Tracking banner (above the buttons)
        RectTransform banner = CreatePanel("TrackingBanner", hudGO.transform, BannerColor);
        AnchorBottom(banner, 450f, 130f, horizontalInset: 40f);
        hud.trackingBanner = banner.gameObject;
        hud.trackingBannerText = CreateText("Text", banner, "", 28, FontStyles.Bold, Color.white);
        Place(hud.trackingBannerText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-40, -20));
        hud.trackingBannerText.enableAutoSizing = true;
        hud.trackingBannerText.fontSizeMin = 18;
        hud.trackingBannerText.fontSizeMax = 28;

        // Bottom buttons
        // Rows from the top: primary (DROP POINT), recording row (UNDO | FINISH), secondary (CANCEL / EXIT).
        RectTransform bottom = CreatePanel("BottomBar", hudGO.transform, PanelColor);
        AnchorBottom(bottom, 0f, 430f);
        Button primary = CreateButton("PrimaryButton", bottom, "+ DROP POINT", PrimaryColor, 38, out hud.primaryButtonText);
        Place((RectTransform)primary.transform, new Vector2(0.06f, 1), new Vector2(0.94f, 1), new Vector2(0, -85), new Vector2(0, 130));
        hud.primaryButton = primary;

        var recordingRow = new GameObject("RecordingButtons", typeof(RectTransform));
        recordingRow.layer = UILayer;
        recordingRow.transform.SetParent(bottom, false);
        Place((RectTransform)recordingRow.transform, new Vector2(0.06f, 1), new Vector2(0.94f, 1), new Vector2(0, -220), new Vector2(0, 100));
        hud.recordingButtons = recordingRow;
        hud.undoButton = CreateButton("UndoButton", recordingRow.transform, "UNDO", SecondaryColor, 30, out _);
        Place((RectTransform)hud.undoButton.transform, new Vector2(0f, 0f), new Vector2(0.38f, 1f), Vector2.zero, Vector2.zero);
        hud.finishButton = CreateButton("FinishButton", recordingRow.transform, "FINISH & SAVE", AccentFinishColor, 30, out _);
        Place((RectTransform)hud.finishButton.transform, new Vector2(0.42f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        Button secondary = CreateButton("SecondaryButton", bottom, "CANCEL", SecondaryColor, 30, out hud.secondaryButtonText);
        Place((RectTransform)secondary.transform, new Vector2(0.06f, 0), new Vector2(0.94f, 0), new Vector2(0, 65), new Vector2(0, 90));
        hud.secondaryButton = secondary;

        // Dialogs
        RectTransform confirmCard = CreateDialog("ConfirmPanel", hudGO.transform, 520f, out GameObject confirmPanel);
        hud.confirmPanel = confirmPanel;
        hud.confirmText = CreateText("Message", confirmCard, "Are you sure?", 34, FontStyles.Normal, TextDark);
        Place(hud.confirmText.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, new Vector2(-80, -60));
        hud.confirmYesButton = CreateButton("YesButton", confirmCard, "DISCARD", DangerColor, 30, out _);
        Place((RectTransform)hud.confirmYesButton.transform, new Vector2(0.06f, 0), new Vector2(0.48f, 0), new Vector2(0, 90), new Vector2(0, 110));
        hud.confirmNoButton = CreateButton("NoButton", confirmCard, "KEEP GOING", SecondaryColor, 30, out _);
        Place((RectTransform)hud.confirmNoButton.transform, new Vector2(0.52f, 0), new Vector2(0.94f, 0), new Vector2(0, 90), new Vector2(0, 110));

        RectTransform resultCard = CreateDialog("ResultPanel", hudGO.transform, 600f, out GameObject resultPanel);
        hud.resultPanel = resultPanel;
        hud.resultTitleText = CreateText("Title", resultCard, "Done", 48, FontStyles.Bold, AccentColor);
        Place(hud.resultTitleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-60, 90));
        hud.resultBodyText = CreateText("Body", resultCard, "", 32, FontStyles.Normal, TextDark);
        Place(hud.resultBodyText.rectTransform, new Vector2(0, 0.3f), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-80, -100));
        hud.resultButton = CreateButton("DoneButton", resultCard, "DONE", PrimaryColor, 34, out _);
        Place((RectTransform)hud.resultButton.transform, new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 90), new Vector2(0, 120));

        RectTransform errorCard = CreateDialog("ErrorPanel", hudGO.transform, 640f, out GameObject errorPanel);
        hud.errorPanel = errorPanel;
        hud.errorText = CreateText("Message", errorCard, "", 32, FontStyles.Normal, TextDark);
        Place(hud.errorText.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, new Vector2(-80, -60));
        hud.errorActionButton = CreateButton("ActionButton", errorCard, "OPEN SETTINGS", PrimaryColor, 30, out hud.errorActionText);
        Place((RectTransform)hud.errorActionButton.transform, new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 210), new Vector2(0, 110));
        hud.errorBackButton = CreateButton("BackButton", errorCard, "BACK TO MENU", SecondaryColor, 30, out _);
        Place((RectTransform)hud.errorBackButton.transform, new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 80), new Vector2(0, 100));

        return hud;
    }

    private static void CreateNotifier(Transform parent)
    {
        var notifierGO = new GameObject("Notifier", typeof(RectTransform));
        notifierGO.layer = UILayer;
        notifierGO.transform.SetParent(parent, false);
        Stretch(notifierGO.GetComponent<RectTransform>());
        notifierGO.transform.SetAsLastSibling();

        RectTransform banner = CreatePanel("Banner", notifierGO.transform, CardColor);
        AnchorTop(banner, -265f, 150f, horizontalInset: 30f);
        TMP_Text message = CreateText("Message", banner, "", 30, FontStyles.Bold, Color.white);
        Place(message.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(-110, 0), new Vector2(-260, -20));
        message.enableAutoSizing = true;
        message.fontSizeMin = 18;
        message.fontSizeMax = 30;

        Button action = CreateButton("Action", banner, "OK", Color.white, 26, out TMP_Text actionText);
        Place((RectTransform)action.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(200, 90));

        var notifier = notifierGO.AddComponent<UINotifier>();
        SetSerialized(notifier, "root", banner.gameObject);
        SetSerialized(notifier, "background", banner.GetComponent<Image>());
        SetSerialized(notifier, "messageText", message);
        SetSerialized(notifier, "actionButton", action);
        SetSerialized(notifier, "actionText", actionText);
        banner.gameObject.SetActive(false);
    }

    // ───────────────────────── UI building blocks ─────────────────────────

    private static Transform CreateCanvas(string name, int sortingOrder)
    {
        var canvasGO = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.layer = UILayer;
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        ConfigureScaler(canvasGO.GetComponent<CanvasScaler>());

        var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
        safe.layer = UILayer;
        safe.transform.SetParent(canvasGO.transform, false);
        Stretch(safe.GetComponent<RectTransform>());
        return safe.transform;
    }

    private static void ConfigureScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static RectTransform CreateDialog(string name, Transform parent, float height, out GameObject panel)
    {
        var dim = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dim.layer = UILayer;
        dim.transform.SetParent(parent, false);
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f); // Blocks taps behind the dialog.
        panel = dim;

        RectTransform card = CreatePanel("Card", dim.transform, CardColor);
        card.anchorMin = new Vector2(0.06f, 0.5f);
        card.anchorMax = new Vector2(0.94f, 0.5f);
        card.sizeDelta = new Vector2(0, height);
        card.anchoredPosition = Vector2.zero;
        dim.SetActive(false);
        return card;
    }

    private static RectTransform CreatePanel(string name, Transform parent, Color color)
    {
        Image image = CreateImage(name, parent, color, RoundedSprite, sliced: true);
        return image.rectTransform;
    }

    private static Image CreateImage(string name, Transform parent, Color color, Sprite sprite, bool sliced)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = UILayer;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        if (sprite != null && sliced)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.25f; // Larger corner radius at phone resolution.
        }
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = UILayer;
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();

        // Bold text (titles, buttons) uses the app's rounded display font, which is already heavy.
        bool display = (style & FontStyles.Bold) != 0;
        var font = display ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath) : null;
        if (font != null) style &= ~FontStyles.Bold;
        else font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) tmp.font = font;

        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateButton(string name, Transform parent, string label, Color color, float fontSize, out TMP_Text labelText)
    {
        Image image = CreateImage(name, parent, color, RoundedSprite, sliced: true);
        var button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
        button.colors = colors;

        // Light buttons (mint, white) get green text and a mint outline, like the app's card buttons.
        bool lightFill = color.grayscale > 0.6f;
        if (lightFill)
        {
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = MintBorder;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        labelText = CreateText("Label", image.transform, label, fontSize, FontStyles.Bold, lightFill ? BrandGreenDark : Color.white);
        Stretch(labelText.rectTransform);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 16;
        labelText.fontSizeMax = fontSize;
        labelText.margin = new Vector4(20, 0, 20, 0);
        return button;
    }

    private static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
    }

    private static void AnchorTop(RectTransform rt, float y, float height, float horizontalInset = 0f)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(-2f * horizontalInset, height);
    }

    private static void AnchorBottom(RectTransform rt, float y, float height, float horizontalInset = 0f)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(-2f * horizontalInset, height);
    }

    private static void CreateEventSystem()
    {
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private static void CreateEventSystemIfMissing(Scene scene)
    {
        EventSystem existing = FindInScene<EventSystem>(scene).FirstOrDefault();
        if (existing == null)
        {
            CreateEventSystem();
            return;
        }

        GameObject go = existing.gameObject;
        RemoveMissingScripts(go);
        var legacy = go.GetComponent<StandaloneInputModule>();
        if (legacy != null) Object.DestroyImmediate(legacy);
        var module = GetOrAdd<InputSystemUIInputModule>(go);
        module.AssignDefaultActions();
    }

    /// <summary>
    /// GetComponent can return Unity's "fake null" in the Editor, which the ??
    /// operator treats as non-null; compare with == instead.
    /// </summary>
    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null) component = go.AddComponent<T>();
        return component;
    }

    // ───────────────────────── Assets ─────────────────────────

    private static List<AudioClip> LoadVoiceClips()
    {
        List<AudioClip> clips = AssetDatabase.FindAssets("t:AudioClip", new[] { VoiceClipsFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(clip => clip != null)
            .ToList();
        if (clips.Count == 0) Debug.LogWarning($"BMU AR: No voice clips in {VoiceClipsFolder}; navigation will be silent.");
        return clips;
    }

    private static Material EnsureMaterial(string path, bool transparent, bool doubleSided)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError("BMU AR: URP Unlit shader not found.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets", "Materials");
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        ARPathRenderer.ConfigureUnlit(material, transparent, doubleSided);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    // ───────────────────────── Generic helpers ─────────────────────────

    private static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        var result = new List<T>();
        if (!scene.IsValid() || !scene.isLoaded) return result;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            result.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return result;
    }

    private static List<GameObject> FindByName(Scene scene, string name)
    {
        var result = new List<GameObject>();
        if (!scene.IsValid() || !scene.isLoaded) return result;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) result.Add(t.gameObject);
            }
        }
        // Parents before children, and never both: destroying a parent removes its children.
        return result.Where(go => !result.Any(other => other != go && go.transform.IsChildOf(other.transform))).ToList();
    }

    /// <summary>Deletes prefab instances whose prefab asset no longer exists (old AR template leftovers).</summary>
    private static void RemoveMissingPrefabInstances(Scene scene)
    {
        var broken = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                if (PrefabUtility.IsPrefabAssetMissing(go) && PrefabUtility.IsOutermostPrefabInstanceRoot(go)) broken.Add(go);
            }
        }
        foreach (GameObject go in broken)
        {
            if (go == null) continue;
            Debug.Log($"BMU AR: Removed missing prefab instance '{go.name}'.");
            Object.DestroyImmediate(go);
        }
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removed > 0) Debug.Log($"BMU AR: Removed {removed} missing script(s) from '{t.name}'.");
        }
    }

    private static void SetSerialized(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"BMU AR: Field '{field}' not found on {target.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetProp(SerializedObject so, string name, object value)
    {
        SerializedProperty prop = so.FindProperty(name);
        if (prop == null) return;
        switch (value)
        {
            case Object o: prop.objectReferenceValue = o; break;
            case float f: prop.floatValue = f; break;
            case int i: prop.intValue = i; break;
            case string s: prop.stringValue = s; break;
        }
    }

    // ───────────────────────── Project settings ─────────────────────────

    [MenuItem("Tools/BMU AR/Apply Portrait Settings")]
    public static void ConfigureScreenAndUI()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        Debug.Log("BMU AR: Portrait orientation configured.");
    }

    [MenuItem("Tools/BMU AR/Upgrade Plugin Importers (Fix Schema Warnings)")]
    public static void UpgradePluginImporters()
    {
        int upgraded = 0;
        foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/Plugins", "Assets/Firebase" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is PluginImporter importer)
            {
                importer.SaveAndReimport();
                upgraded++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"BMU AR: Re-saved {upgraded} plugin importers.");
    }

    // ───────────────────────── Builds ─────────────────────────

    [MenuItem("Tools/BMU AR/Build Development APK (for testing)")]
    public static void BuildDevelopmentApk()
    {
        // Dev builds use the release keystore too (so they update the installed app), so they need its passwords.
        if (PlayerSettings.Android.useCustomKeystore && !CheckReleaseKeystore()) return;
        EditorUserBuildSettings.buildAppBundle = false;
        Build(Path.Combine("Builds", "BMUARR-dev.apk"), BuildOptions.Development);
    }

    [MenuItem("Tools/BMU AR/Build Release AAB (Play Store)")]
    public static void BuildReleaseBundle()
    {
        if (!CheckReleaseKeystore()) return;
        EditorUserBuildSettings.buildAppBundle = true;
        Build(Path.Combine("Builds", "BMUARR.aab"), BuildOptions.None);
    }

    /// <summary>Release build as an APK, for installing directly on phones (testers, friends).</summary>
    [MenuItem("Tools/BMU AR/Build Release APK (share directly)")]
    public static void BuildReleaseApk()
    {
        if (!CheckReleaseKeystore()) return;
        EditorUserBuildSettings.buildAppBundle = false;
        Build(Path.Combine("Builds", "BMUARR.apk"), BuildOptions.None);
    }

    private static bool CheckReleaseKeystore()
    {
        bool configured = PlayerSettings.Android.useCustomKeystore &&
                          !string.IsNullOrEmpty(PlayerSettings.Android.keystoreName) &&
                          !string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) &&
                          !string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass);
        if (configured) return true;

        const string message =
            "Release builds must be signed with your own keystore, and Unity needs its passwords " +
            "(it forgets them when the Editor restarts).\n\n" +
            "Player Settings > Android > Publishing Settings: select your keystore and enter both passwords.";
        Debug.LogError("BMU AR: " + message);
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("Keystore required", message, "OK");
        return false;
    }

    private static void Build(string relativePath, BuildOptions options)
    {
        EnsureFirebasePluginsForAndroid();

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length < 2 || !scenes.Contains(MenuScenePath) || !scenes.Contains(NavigationScenePath))
        {
            Debug.LogError("BMU AR: Run 'Tools/BMU AR/Build Production Scenes' first.");
            return;
        }

        string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = options
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"BMU AR: Build succeeded: {outputPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(outputPath);
        }
        else
        {
            Debug.LogError($"BMU AR: Build failed with {report.summary.totalErrors} error(s). See the Console.");
        }
    }

    private static void EnsureFirebasePluginsForAndroid()
    {
        string[] requiredDlls =
        {
            "Assets/Firebase/Plugins/Firebase.App.dll",
            "Assets/Firebase/Plugins/Firebase.Auth.dll",
            "Assets/Firebase/Plugins/Firebase.Platform.dll",
            "Assets/Firebase/Plugins/Firebase.TaskExtension.dll",
            "Assets/Firebase/Plugins/Google.MiniJson.dll"
        };
        foreach (string dll in requiredDlls)
        {
            if (AssetImporter.GetAtPath(dll) is PluginImporter importer && !importer.GetCompatibleWithPlatform(BuildTarget.Android))
            {
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SaveAndReimport();
                Debug.Log($"BMU AR: Enabled Android for {dll}.");
            }
        }
    }
}
