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
    private const string ArrowSpritePath = GeneratedUIFolder + "/NavArrow.png";
    private const string TargetImagePath = "Assets/UI/ImageTargets/Image_target.jpeg";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const float MarkerWidthMeters = 0.2f;

    private const int UILayer = 5;

    private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.12f, 0.94f);
    private static readonly Color CardColor = new Color(0.09f, 0.12f, 0.20f, 0.98f);
    private static readonly Color PrimaryColor = new Color(0.15f, 0.75f, 0.42f);
    private static readonly Color SecondaryColor = new Color(0.30f, 0.33f, 0.42f);
    private static readonly Color DangerColor = new Color(0.80f, 0.25f, 0.25f);
    private static readonly Color AccentColor = new Color(0.20f, 1f, 0.55f);
    private static readonly Color BannerColor = new Color(0.62f, 0.42f, 0.05f, 0.96f);
    private static readonly Color AccentFinishColor = new Color(0.12f, 0.55f, 0.95f);

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

        Material routeMaterial = EnsureMaterial(RouteMaterialPath, transparent: true, doubleSided: true);
        Material markerMaterial = EnsureMaterial(MarkerMaterialPath, transparent: false, doubleSided: false);
        Sprite arrowSprite = EnsureArrowSprite();

        Scene menu = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
        bool navExists = File.Exists(NavigationScenePath);
        Scene nav = navExists
            ? EditorSceneManager.OpenScene(NavigationScenePath, OpenSceneMode.Additive)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        MoveVuforiaObjects(menu, nav);

        SceneManager.SetActiveScene(nav);
        BuildNavigationScene(nav, routeMaterial, markerMaterial, arrowSprite);

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

    private static void BuildNavigationScene(Scene nav, Material routeMaterial, Material markerMaterial, Sprite arrowSprite)
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

        var controller = systems.AddComponent<NavigationSceneController>();
        controller.anchorTracker = anchorTracker;
        controller.recorder = recorder;
        controller.guidance = guidance;
        controller.trackingMonitor = tracking;
        controller.pathRenderer = renderer;

        // UI
        CreateEventSystem();
        Transform safeArea = CreateCanvas("NavigationCanvas", sortingOrder: 10);
        controller.hud = BuildNavigationHUD(safeArea, arrowSprite);
        CreateNotifier(safeArea);
    }

    private static void BuildMenuScene(Scene menu)
    {
        foreach (string name in new[] { "AR Session", "XR Origin (AR Rig)", "WaypointManager", "Calibrated_World_Origin", "RecordingHUD", "GuidanceHUD", "NotifierArea" })
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

        var modal = canvasGO.GetComponentInChildren<LocationPromptModal>(true);
        if (modal != null)
        {
            navMgr.locationPromptModal = modal;
            if (modal.messageText != null)
            {
                modal.messageText.enableAutoSizing = true;
                modal.messageText.fontSizeMin = 14;
                modal.messageText.fontSizeMax = 22;
            }
        }

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

            GameObject googleButton = FindByName(menu, "Continue with Google").FirstOrDefault();
            if (auth != null && googleButton != null) auth.signInButton = googleButton.GetComponent<Button>();
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
        SerializedProperty width = so.FindProperty("mWidth");
        if (width != null)
        {
            Debug.Log($"BMU AR: Marker width is set to {width.floatValue * 100f:F0} cm. The printed marker must be exactly this wide, or route distances will be scaled wrong.");
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

    private static NavigationHUD BuildNavigationHUD(Transform parent, Sprite arrowSprite)
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
        hud.statusText = CreateText("Status", top, "Point the camera at the marker.", 30, FontStyles.Normal, Color.white);
        Place(hud.statusText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -35), new Vector2(-80, -110));
        hud.statusText.enableAutoSizing = true;
        hud.statusText.fontSizeMin = 20;
        hud.statusText.fontSizeMax = 30;

        // Guidance card
        RectTransform card = CreatePanel("GuidanceCard", hudGO.transform, CardColor);
        AnchorTop(card, -270f, 220f, horizontalInset: 40f);
        hud.guidanceCard = card.gameObject;
        hud.instructionText = CreateText("Instruction", card, "Continue straight", 50, FontStyles.Bold, Color.white);
        Place(hud.instructionText.rectTransform, new Vector2(0, 0.4f), new Vector2(1, 1), Vector2.zero, new Vector2(-50, -10));
        hud.instructionText.enableAutoSizing = true;
        hud.instructionText.fontSizeMin = 28;
        hud.instructionText.fontSizeMax = 50;
        hud.distanceText = CreateText("Distance", card, "0 m left", 34, FontStyles.Normal, AccentColor);
        Place(hud.distanceText.rectTransform, new Vector2(0, 0), new Vector2(1, 0.4f), Vector2.zero, new Vector2(-50, 0));

        // Direction arrow
        var arrowRoot = new GameObject("DirectionArrow", typeof(RectTransform));
        arrowRoot.layer = UILayer;
        arrowRoot.transform.SetParent(hudGO.transform, false);
        var arrowRt = arrowRoot.GetComponent<RectTransform>();
        arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0.5f, 0.62f);
        arrowRt.sizeDelta = new Vector2(220, 220);
        var arrowImage = CreateImage("Arrow", arrowRoot.transform, AccentColor, arrowSprite, sliced: false);
        Stretch(arrowImage.rectTransform);
        arrowImage.raycastTarget = false;
        hud.directionArrow = arrowRt;

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
        hud.confirmText = CreateText("Message", confirmCard, "Are you sure?", 34, FontStyles.Normal, Color.white);
        Place(hud.confirmText.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, new Vector2(-80, -60));
        hud.confirmYesButton = CreateButton("YesButton", confirmCard, "DISCARD", DangerColor, 30, out _);
        Place((RectTransform)hud.confirmYesButton.transform, new Vector2(0.06f, 0), new Vector2(0.48f, 0), new Vector2(0, 90), new Vector2(0, 110));
        hud.confirmNoButton = CreateButton("NoButton", confirmCard, "KEEP GOING", SecondaryColor, 30, out _);
        Place((RectTransform)hud.confirmNoButton.transform, new Vector2(0.52f, 0), new Vector2(0.94f, 0), new Vector2(0, 90), new Vector2(0, 110));

        RectTransform resultCard = CreateDialog("ResultPanel", hudGO.transform, 600f, out GameObject resultPanel);
        hud.resultPanel = resultPanel;
        hud.resultTitleText = CreateText("Title", resultCard, "Done", 48, FontStyles.Bold, AccentColor);
        Place(hud.resultTitleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-60, 90));
        hud.resultBodyText = CreateText("Body", resultCard, "", 32, FontStyles.Normal, Color.white);
        Place(hud.resultBodyText.rectTransform, new Vector2(0, 0.3f), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-80, -100));
        hud.resultButton = CreateButton("DoneButton", resultCard, "DONE", PrimaryColor, 34, out _);
        Place((RectTransform)hud.resultButton.transform, new Vector2(0.1f, 0), new Vector2(0.9f, 0), new Vector2(0, 90), new Vector2(0, 120));

        RectTransform errorCard = CreateDialog("ErrorPanel", hudGO.transform, 640f, out GameObject errorPanel);
        hud.errorPanel = errorPanel;
        hud.errorText = CreateText("Message", errorCard, "", 32, FontStyles.Normal, Color.white);
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

        Button action = CreateButton("Action", banner, "OK", PrimaryColor, 26, out TMP_Text actionText);
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
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
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

        labelText = CreateText("Label", image.transform, label, fontSize, FontStyles.Bold, Color.white);
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

    /// <summary>Draws a clean, high-resolution arrow sprite (pointing up) and imports it.</summary>
    private static Sprite EnsureArrowSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
        if (!AssetDatabase.IsValidFolder(GeneratedUIFolder)) AssetDatabase.CreateFolder("Assets/UI", "Generated");

        const int size = 256;
        const int supersample = 4;
        Vector2[] polygon =
        {
            new Vector2(0.50f, 0.96f), // tip
            new Vector2(0.90f, 0.52f),
            new Vector2(0.64f, 0.52f),
            new Vector2(0.64f, 0.06f),
            new Vector2(0.36f, 0.06f),
            new Vector2(0.36f, 0.52f),
            new Vector2(0.10f, 0.52f)
        };

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < supersample; sy++)
                for (int sx = 0; sx < supersample; sx++)
                {
                    var p = new Vector2((x + (sx + 0.5f) / supersample) / size, (y + (sy + 0.5f) / supersample) / size);
                    if (PointInPolygon(p, polygon)) inside++;
                }
                byte alpha = (byte)(255 * inside / (supersample * supersample));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        File.WriteAllBytes(ArrowSpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ArrowSpritePath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(ArrowSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
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
