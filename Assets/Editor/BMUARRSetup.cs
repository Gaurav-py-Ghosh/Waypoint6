using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using Vuforia;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;

public static class BMUARRSetup
{
    [MenuItem("Tools/BMU AR/Auto-Setup Vuforia & Navigation Scene")]
    public static void SetupScene()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Setup BMU AR Vuforia Scene");
        int undoGroup = Undo.GetCurrentGroup();

        Debug.Log("BMU AR: Starting automated Vuforia & Scene Setup...");

        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Debug.Log("BMU AR: Opened 'Assets/Scenes/SampleScene.unity'.");
        }

        // 1. Deactivate conflicting AR Foundation objects
        GameObject arSession = GameObject.Find("AR Session");
        if (arSession != null)
        {
            Undo.RecordObject(arSession, "Deactivate AR Session");
            arSession.SetActive(false);
            Debug.Log("BMU AR: Deactivated 'AR Session' to prevent camera conflict.");
        }

        GameObject xrOrigin = GameObject.Find("XR Origin (AR Rig)");
        if (xrOrigin != null)
        {
            Undo.RecordObject(xrOrigin, "Deactivate XR Origin");
            xrOrigin.SetActive(false);
            Debug.Log("BMU AR: Deactivated 'XR Origin (AR Rig)' to prevent camera conflict.");
        }

        // 2. Setup Vuforia ARCamera
        VuforiaBehaviour vuforiaCam = Object.FindFirstObjectByType<VuforiaBehaviour>();
        GameObject arCameraGO = null;
        if (vuforiaCam == null)
        {
            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/AR Camera");
            vuforiaCam = Object.FindFirstObjectByType<VuforiaBehaviour>();
            if (vuforiaCam != null)
            {
                arCameraGO = vuforiaCam.gameObject;
                Debug.Log("BMU AR: Created Vuforia ARCamera.");
            }
        }
        else
        {
            arCameraGO = vuforiaCam.gameObject;
            Debug.Log("BMU AR: Found existing Vuforia ARCamera.");
        }

        if (arCameraGO != null)
        {
            arCameraGO.tag = "MainCamera";
            VuforiaCameraManager vcm = arCameraGO.GetComponent<VuforiaCameraManager>();
            if (vcm != null) Undo.DestroyObjectImmediate(vcm);
        }

        // 3. Setup Image Target with Image_target.jpeg
        string targetImagePath = "Assets/UI/ImageTargets/Image_target.jpeg";
        Texture2D targetTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetImagePath);
        if (targetTexture == null)
        {
            Debug.LogError($"BMU AR: Could not load target image at '{targetImagePath}'.");
        }

        GameObject imageTargetGO = GameObject.Find("ImageTarget_Compass");
        ImageTargetBehaviour itb = null;

        if (imageTargetGO == null)
        {
            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/Image Target");
            itb = Object.FindFirstObjectByType<ImageTargetBehaviour>();
            if (itb != null)
            {
                imageTargetGO = itb.gameObject;
                imageTargetGO.name = "ImageTarget_Compass";
                Debug.Log("BMU AR: Created 'ImageTarget_Compass' GameObject.");
            }
        }
        else
        {
            itb = imageTargetGO.GetComponent<ImageTargetBehaviour>();
        }

        if (imageTargetGO == null)
        {
            imageTargetGO = new GameObject("ImageTarget_Compass");
            itb = imageTargetGO.AddComponent<ImageTargetBehaviour>();
        }

        // Configure ImageTargetBehaviour properly for FromImage runtime tracking
        if (itb != null && targetTexture != null)
        {
            SerializedObject soTarget = new SerializedObject(itb);
            soTarget.Update();

            var runtimeTexProp = soTarget.FindProperty("mRuntimeTexture");
            var widthProp = soTarget.FindProperty("mWidth");
            var heightProp = soTarget.FindProperty("mHeight");
            var typeProp = soTarget.FindProperty("mImageTargetType");
            var trackableNameProp = soTarget.FindProperty("mTrackableName");
            var aspectProp = soTarget.FindProperty("mAspectRatio");
            var dataSetPathProp = soTarget.FindProperty("mDataSetPath");

            if (runtimeTexProp != null) runtimeTexProp.objectReferenceValue = targetTexture;
            if (widthProp != null) widthProp.floatValue = 0.2f;
            if (heightProp != null) heightProp.floatValue = 0.2f;
            if (typeProp != null) typeProp.intValue = 3; // 3 = FromImage!
            if (trackableNameProp != null) trackableNameProp.stringValue = "Image_target";
            if (aspectProp != null) aspectProp.floatValue = 1.0f;
            if (dataSetPathProp != null) dataSetPathProp.stringValue = "";

            soTarget.ApplyModifiedProperties();
            Debug.Log("BMU AR: Configured ImageTarget_Compass with 'FromImage' mode and target texture.");
        }

        // Configure DefaultObserverEventHandler
        DefaultObserverEventHandler observerHandler = imageTargetGO.GetComponent<DefaultObserverEventHandler>();
        if (observerHandler == null)
        {
            observerHandler = imageTargetGO.AddComponent<DefaultObserverEventHandler>();
        }
        observerHandler.StatusFilter = DefaultObserverEventHandler.TrackingStatusFilter.Tracked_ExtendedTracked_Limited;

        // 4. Setup AnchorTracker on ImageTarget_Compass and WaypointManager at root
        imageTargetGO.transform.SetParent(null, true);
        imageTargetGO.transform.position = Vector3.zero;
        imageTargetGO.transform.rotation = Quaternion.identity;
        imageTargetGO.transform.localScale = Vector3.one;
        SetLayerRecursively(imageTargetGO, 0);

        AnchorTracker anchorTracker = imageTargetGO.GetComponent<AnchorTracker>();
        if (anchorTracker == null)
        {
            anchorTracker = Undo.AddComponent<AnchorTracker>(imageTargetGO);
        }

        // Clean any old ARPathRenderer / ARPathRecorder off ImageTarget_Compass
        ARPathRenderer oldPR = imageTargetGO.GetComponent<ARPathRenderer>();
        if (oldPR != null) Object.DestroyImmediate(oldPR, true);

        ARPathRecorder oldRec = imageTargetGO.GetComponent<ARPathRecorder>();
        if (oldRec != null) Object.DestroyImmediate(oldRec, true);

        // Independent WaypointManager root GameObject (matches waypoint architecture)
        GameObject managerObj = GameObject.Find("WaypointManager");
        if (managerObj == null)
        {
            managerObj = new GameObject("WaypointManager");
        }
        managerObj.transform.SetParent(null, true);
        managerObj.transform.position = Vector3.zero;
        managerObj.transform.rotation = Quaternion.identity;

        ARPathRenderer pathRenderer = managerObj.GetComponent<ARPathRenderer>();
        if (pathRenderer == null)
        {
            pathRenderer = managerObj.AddComponent<ARPathRenderer>();
        }
        pathRenderer.worldAnchorRoot = anchorTracker.StabilizedOrigin;

        ARPathRecorder pathRecorder = managerObj.GetComponent<ARPathRecorder>();
        if (pathRecorder == null)
        {
            pathRecorder = managerObj.AddComponent<ARPathRecorder>();
        }
        pathRecorder.anchorTracker = anchorTracker;
        if (arCameraGO != null) pathRecorder.arCamera = arCameraGO.transform;
        pathRecorder.pathRenderer = pathRenderer;
        Debug.Log("BMU AR: Configured WaypointManager with ARPathRenderer and ARPathRecorder.");

        VuforiaImageTargetHandler handler = imageTargetGO.GetComponent<VuforiaImageTargetHandler>();
        if (handler == null)
        {
            handler = Undo.AddComponent<VuforiaImageTargetHandler>(imageTargetGO);
        }

        Transform contentRoot = imageTargetGO.transform.Find("ARContentRoot");
        if (contentRoot == null)
        {
            GameObject rootGO = new GameObject("ARContentRoot");
            rootGO.transform.SetParent(imageTargetGO.transform, false);
            rootGO.transform.localPosition = Vector3.zero;
            contentRoot = rootGO.transform;
            Undo.RegisterCreatedObjectUndo(rootGO, "Create ARContentRoot");
        }
        contentRoot.gameObject.SetActive(true);

        // Clean up any old ARDirectionArrow geometry if present
        ARDirectionArrow oldArrow = contentRoot.GetComponent<ARDirectionArrow>();
        if (oldArrow != null) Undo.DestroyObjectImmediate(oldArrow);

        Transform oldArrowGeom = contentRoot.Find("DirectionArrow");
        if (oldArrowGeom != null) Undo.DestroyObjectImmediate(oldArrowGeom.gameObject);

        // Add subtle flat floor ring on compass target
        Transform ringT = contentRoot.Find("FloorCompassRing");
        if (ringT == null)
        {
            GameObject ringGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringGO.name = "FloorCompassRing";
            ringGO.transform.SetParent(contentRoot, false);
            ringGO.transform.localPosition = new Vector3(0, 0.002f, 0);
            ringGO.transform.localScale = new Vector3(0.24f, 0.001f, 0.24f);
            Collider col = ringGO.GetComponent<Collider>();
            if (col != null) Undo.DestroyObjectImmediate(col);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
            if (unlitShader == null) unlitShader = Shader.Find("Standard");
            Material ringMat = new Material(unlitShader);
            ringMat.color = new Color(0.15f, 0.85f, 0.45f, 0.7f);
            ringGO.GetComponent<Renderer>().sharedMaterial = ringMat;
            Undo.RegisterCreatedObjectUndo(ringGO, "Create FloorCompassRing");
        }
        handler.arContentRoot = contentRoot.gameObject;

        // 5. Setup Canvas & UI Hierarchy
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null)
        {
            Debug.LogError("BMU AR: Could not find 'Canvas' in the scene.");
            return;
        }

        Canvas canvas = canvasGO.GetComponent<Canvas>();
        if (canvas != null)
        {
            SerializedObject soCanvas = new SerializedObject(canvas);
            soCanvas.Update();
            var propRenderMode = soCanvas.FindProperty("m_RenderMode");
            var propCamera = soCanvas.FindProperty("m_Camera");
            var propPlaneDistance = soCanvas.FindProperty("m_PlaneDistance");
            var propSortingOrder = soCanvas.FindProperty("m_SortingOrder");

            if (propRenderMode != null) propRenderMode.intValue = 1; // 1 = ScreenSpaceCamera
            if (propCamera != null && arCameraGO != null) propCamera.objectReferenceValue = arCameraGO.GetComponent<Camera>();
            if (propPlaneDistance != null) propPlaneDistance.floatValue = 0.5f;
            if (propSortingOrder != null) propSortingOrder.intValue = 100;
            soCanvas.ApplyModifiedProperties();
            EditorUtility.SetDirty(canvas);
        }

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            EditorUtility.SetDirty(scaler);
        }

        CampusNavigationManager navMgr = canvasGO.GetComponent<CampusNavigationManager>();
        if (navMgr == null)
        {
            navMgr = Undo.AddComponent<CampusNavigationManager>(canvasGO);
        }

        Transform tCanvas = canvasGO.transform;
        Transform tMain = tCanvas.Find("Main_Panel");
        Transform tDest = tCanvas.Find("DestinationPanel");

        // Deactivate old unused panels
        Transform tOldAR1 = tCanvas.Find("ARScreenPanel");
        if (tOldAR1 != null) tOldAR1.gameObject.SetActive(false);
        Transform tOldAR2 = tCanvas.Find("DestARScreenPanel");
        if (tOldAR2 != null) tOldAR2.gameObject.SetActive(false);
        Transform tOldQR = tCanvas.Find("QRScanningPanel");
        if (tOldQR != null) tOldQR.gameObject.SetActive(false);
        Transform tOldQRMain = tCanvas.Find("QRMainPanel");
        if (tOldQRMain != null) tOldQRMain.gameObject.SetActive(false);

        // Ensure clean onboarding flow: Splash active, others inactive on launch
        Transform tSplash = tCanvas.Find("Splash_Screen");
        Transform tLogin = tCanvas.Find("Login_Screen_Panel");
        Transform tMail = tCanvas.Find("Mail_Verification_Panel");
        if (tSplash != null) tSplash.gameObject.SetActive(true);
        if (tLogin != null) tLogin.gameObject.SetActive(false);
        if (tMail != null) tMail.gameObject.SetActive(false);
        if (tMain != null) tMain.gameObject.SetActive(false);
        if (tDest != null) tDest.gameObject.SetActive(false);

        // Build or Get LocationPromptModal
        LocationPromptModal modal = SetupLocationPromptModal(tCanvas);
        navMgr.locationPromptModal = modal;

        // Build or Get RecordingHUD
        GameObject recHUD = SetupRecordingHUD(tCanvas, navMgr);
        navMgr.recordingHUD = recHUD;

        // Build or Get GuidanceHUD
        GameObject guideHUD = SetupGuidanceHUD(tCanvas, navMgr);
        navMgr.guidanceHUD = guideHUD;

        // Link Managers
        if (tMain != null) navMgr.mainPanel = tMain.gameObject;
        if (tDest != null) navMgr.destinationPanel = tDest.gameObject;
        navMgr.anchorTracker = anchorTracker;
        navMgr.pathRecorder = pathRecorder;
        navMgr.pathRenderer = pathRenderer;
        navMgr.imageTargetHandler = handler;
        if (handler != null) handler.navigationManager = navMgr;

        // Wire Destination Buttons
        if (tDest != null)
        {
            WireDestinationButton(tDest, "Library_Button", navMgr, "Library");
            WireDestinationButton(tDest, "Reception_Button", navMgr, "Reception");
            WireDestinationButton(tDest, "Program_Office_Button", navMgr, "Program Office");
            WireDestinationButton(tDest, "DCafe_Button", navMgr, "D Cafe");
            WireDestinationButton(tDest, "GA_Button", navMgr, "Grand Auditorium");

            Transform backBtn = tDest.Find("Back");
            if (backBtn != null)
            {
                Button btn = backBtn.GetComponent<Button>();
                if (btn != null)
                {
                    while (btn.onClick.GetPersistentEventCount() > 0)
                    {
                        UnityEventTools.RemovePersistentListener(btn.onClick, 0);
                    }
                    UnityEventTools.AddVoidPersistentListener(btn.onClick, navMgr.ShowMainPanel);
                }
            }
        }

        // Wire Main Panel Buttons
        if (tMain != null)
        {
            Transform selectDestBtn = tMain.Find("SelectDestination_Button");
            if (selectDestBtn != null)
            {
                Button btn = selectDestBtn.GetComponent<Button>();
                if (btn != null)
                {
                    while (btn.onClick.GetPersistentEventCount() > 0)
                    {
                        UnityEventTools.RemovePersistentListener(btn.onClick, 0);
                    }
                    UnityEventTools.AddVoidPersistentListener(btn.onClick, navMgr.ShowDestinationPanel);
                }
            }

            Transform exploreBtn = tMain.Find("Explore_Button");
            if (exploreBtn != null)
            {
                Button btn = exploreBtn.GetComponent<Button>();
                if (btn != null)
                {
                    while (btn.onClick.GetPersistentEventCount() > 0)
                    {
                        UnityEventTools.RemovePersistentListener(btn.onClick, 0);
                    }
                    UnityEventTools.AddVoidPersistentListener(btn.onClick, navMgr.ShowDestinationPanel);
                }
            }
        }

        // Connect UIManager
        GameObject uiMgrGO = GameObject.Find("Ui Manager");
        if (uiMgrGO != null)
        {
            UIManager uiMgr = uiMgrGO.GetComponent<UIManager>();
            if (uiMgr != null)
            {
                if (tSplash != null) uiMgr.splashScreen = tSplash.gameObject;
                if (tLogin != null) uiMgr.loginScreen = tLogin.gameObject;
                if (tMail != null) uiMgr.mailVerify = tMail.gameObject;
                uiMgr.campusNavigationManager = navMgr;
            }
        }

        // Auto-wire skipLoginButton on Login_Screen_Panel
        if (tLogin != null)
        {
            LoginScreen loginComp = tLogin.GetComponentInChildren<LoginScreen>();
            if (loginComp != null)
            {
                Transform skipBtnT = tLogin.Find("SkipLogin (1)");
                if (skipBtnT == null) skipBtnT = tLogin.Find("SkipButton");
                if (skipBtnT != null)
                {
                    loginComp.skipLoginButton = skipBtnT.GetComponent<Button>();
                }
            }
        }

        // 6. Setup EventSystem & Input System UI Module
        ConfigureEventSystemAndInput();

        // 7. Configure Portrait Orientation & Screen Scaling
        ConfigureScreenAndUI();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("BMU AR: Setup complete and scene saved successfully!");
    }

    [MenuItem("Tools/BMU AR/Upgrade Plugin Importers (Fix Schema Warnings)")]
    public static void UpgradePluginImporters()
    {
        Debug.Log("BMU AR: Upgrading PluginImporter metadata to Unity 6 format...");
        string[] searchFolders = new[] { "Assets/Plugins", "Assets/Firebase" };
        string[] allGuids = AssetDatabase.FindAssets("", searchFolders);
        int upgradedCount = 0;

        foreach (string guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer != null)
            {
                importer.SaveAndReimport();
                upgradedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"BMU AR: Successfully upgraded and re-saved {upgradedCount} PluginImporters to Unity 6 schema!");
    }

    [MenuItem("Tools/BMU AR/Fix Inputs & Phone UI Scaling")]
    public static void FixInputsAndUIScaling()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Fix Inputs & Phone UI Scaling");
        int undoGroup = Undo.GetCurrentGroup();

        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }

        ConfigureEventSystemAndInput();
        ConfigureScreenAndUI();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("BMU AR: Inputs and UI Scaling fixed and saved successfully!");
    }

    public static void ConfigureEventSystemAndInput()
    {
        GameObject eventSystemGO = GameObject.Find("EventSystem");
        if (eventSystemGO == null)
        {
            eventSystemGO = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
        }

        // Clean any broken/missing script components (like legacy XRUIInputModule)
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(eventSystemGO);

        var legacyModule = eventSystemGO.GetComponent<StandaloneInputModule>();
        if (legacyModule != null)
        {
            Undo.DestroyObjectImmediate(legacyModule);
        }

        var es = eventSystemGO.GetComponent<EventSystem>();
        if (es == null)
        {
            es = Undo.AddComponent<EventSystem>(eventSystemGO);
        }
        es.sendNavigationEvents = true;

        var inputModule = eventSystemGO.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
        {
            inputModule = Undo.AddComponent<InputSystemUIInputModule>(eventSystemGO);
        }
        inputModule.AssignDefaultActions();
        EditorUtility.SetDirty(eventSystemGO);
        Debug.Log("BMU AR: Configured EventSystem with InputSystemUIInputModule and default actions.");
    }

    public static void ConfigureScreenAndUI()
    {
        // 1. Force Portrait orientation in PlayerSettings
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.defaultScreenWidth = 1080;
        PlayerSettings.defaultScreenHeight = 1920;

        // 2. Adjust CanvasScaler on Canvas
        GameObject canvasGO = GameObject.Find("Canvas");
        if (canvasGO != null)
        {
            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                EditorUtility.SetDirty(scaler);
            }
        }

        // 3. Set GameView to phone portrait resolution (1080x1920) if editor window is open
        EnsurePortraitGameViewSize();

        Debug.Log("BMU AR: Configured PlayerSettings and CanvasScaler for phone portrait orientation.");
    }

    private static void EnsurePortraitGameViewSize()
    {
        try
        {
            var gvSizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
            if (gvSizesType == null) return;
            var singleType = typeof(ScriptableSingleton<>).MakeGenericType(gvSizesType);
            var instanceProp = singleType.GetProperty("instance");
            var instance = instanceProp?.GetValue(null);
            if (instance == null) return;

            var currentGroupProp = instance.GetType().GetProperty("currentGroup");
            var currentGroup = currentGroupProp?.GetValue(instance);
            if (currentGroup == null) return;

            var getTotalCountMethod = currentGroup.GetType().GetMethod("GetTotalCount");
            var getGameViewSizeMethod = currentGroup.GetType().GetMethod("GetGameViewSize");
            var addCustomSizeMethod = currentGroup.GetType().GetMethod("AddCustomSize");

            int total = (int)getTotalCountMethod.Invoke(currentGroup, null);
            bool found = false;
            int targetIndex = 0;

            for (int i = 0; i < total; i++)
            {
                var size = getGameViewSizeMethod.Invoke(currentGroup, new object[] { i });
                var baseText = (string)size.GetType().GetProperty("baseText")?.GetValue(size);
                if (baseText != null && (baseText.Contains("1080x1920") || baseText.Contains("Phone Portrait") || baseText.Contains("9:16")))
                {
                    found = true;
                    targetIndex = i;
                    break;
                }
            }

            if (!found && addCustomSizeMethod != null)
            {
                var gvSizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
                var gvSizeTypeEnum = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
                if (gvSizeType != null && gvSizeTypeEnum != null)
                {
                    object fixedRes = System.Enum.ToObject(gvSizeTypeEnum, 1);
                    var ctor = gvSizeType.GetConstructor(new System.Type[] { gvSizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                    if (ctor != null)
                    {
                        var newSize = ctor.Invoke(new object[] { fixedRes, 1080, 1920, "Phone Portrait (1080x1920)" });
                        addCustomSizeMethod.Invoke(currentGroup, new object[] { newSize });
                        targetIndex = (int)getTotalCountMethod.Invoke(currentGroup, null) - 1;
                        Debug.Log("BMU AR: Added 'Phone Portrait (1080x1920)' to Game View sizes.");
                    }
                }
            }

            var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null)
            {
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView != null)
                {
                    var selectedSizeIndexProp = gameViewType.GetProperty("selectedSizeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (selectedSizeIndexProp != null)
                    {
                        selectedSizeIndexProp.SetValue(gameView, targetIndex);
                        gameView.Repaint();
                        Debug.Log($"BMU AR: Set Game View to Phone Portrait (index {targetIndex}).");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("BMU AR: GameView size notice: " + ex.Message);
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static LocationPromptModal SetupLocationPromptModal(Transform parent)
    {
        Transform existing = parent.Find("LocationPromptModal");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        GameObject modalGO = new GameObject("LocationPromptModal", typeof(RectTransform));
        modalGO.transform.SetParent(parent, false);
        RectTransform rt = modalGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // Semi-transparent backdrop
        Image bg = modalGO.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.75f);

        // Dialog Card
        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(modalGO.transform, false);
        RectTransform cardRt = card.GetComponent<RectTransform>();
        cardRt.sizeDelta = new Vector2(620, 520);
        card.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.20f, 0.98f);

        // Title
        GameObject titleGO = CreateText("Title", card.transform, new Vector2(0, 175), new Vector2(560, 60), 32, FontStyles.Bold, Color.white);
        // Message
        GameObject msgGO = CreateText("Message", card.transform, new Vector2(0, 105), new Vector2(540, 75), 20, FontStyles.Normal, new Color(0.85f, 0.90f, 0.98f));
        // Distance
        GameObject distGO = CreateText("Distance", card.transform, new Vector2(0, 40), new Vector2(540, 40), 18, FontStyles.Italic, new Color(0.2f, 0.95f, 0.5f));

        // Buttons
        GameObject recBtn = CreateButton("RecordButton", card.transform, new Vector2(0, -45), new Vector2(480, 70), "RECORD PATH", new Color(0.12f, 0.55f, 0.95f));
        GameObject guideBtn = CreateButton("GuideMeButton", card.transform, new Vector2(0, -35), new Vector2(480, 70), "GUIDE ME", new Color(0.15f, 0.85f, 0.45f));
        GameObject reRecBtn = CreateButton("ReRecordButton", card.transform, new Vector2(0, -115), new Vector2(480, 50), "Re-record Path", new Color(0.25f, 0.3f, 0.4f));
        GameObject cancelBtn = CreateButton("CancelButton", card.transform, new Vector2(0, -180), new Vector2(480, 48), "Back", new Color(0.18f, 0.20f, 0.28f));

        LocationPromptModal prompt = modalGO.AddComponent<LocationPromptModal>();
        prompt.titleText = titleGO.GetComponent<TMP_Text>();
        prompt.messageText = msgGO.GetComponent<TMP_Text>();
        prompt.distanceText = distGO.GetComponent<TMP_Text>();
        prompt.recordButton = recBtn.GetComponent<Button>();
        prompt.guideMeButton = guideBtn.GetComponent<Button>();
        prompt.reRecordButton = reRecBtn.GetComponent<Button>();
        prompt.cancelButton = cancelBtn.GetComponent<Button>();

        SetLayerRecursively(modalGO, 5);
        modalGO.SetActive(false);
        return prompt;
    }

    private static GameObject SetupRecordingHUD(Transform parent, CampusNavigationManager navMgr)
    {
        Transform existing = parent.Find("RecordingHUD");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        GameObject hudGO = new GameObject("RecordingHUD", typeof(RectTransform));
        hudGO.transform.SetParent(parent, false);
        RectTransform rt = hudGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // Top Status Header
        GameObject topBar = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
        topBar.transform.SetParent(hudGO.transform, false);
        RectTransform barRt = topBar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0, 1);
        barRt.anchorMax = new Vector2(1, 1);
        barRt.pivot = new Vector2(0.5f, 1);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0, 170);
        topBar.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

        GameObject cancelBtn = CreateButton("CancelButton", topBar.transform, Vector2.zero, new Vector2(110, 48), "Exit", new Color(0.35f, 0.35f, 0.40f));
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.anchorMin = new Vector2(0f, 0.5f);
        cancelRt.anchorMax = new Vector2(0f, 0.5f);
        cancelRt.pivot = new Vector2(0f, 0.5f);
        cancelRt.anchoredPosition = new Vector2(25, 0);

        GameObject title = CreateText("RecordingTitle", topBar.transform, new Vector2(30, -35), new Vector2(550, 45), 24, FontStyles.Bold, new Color(1f, 0.4f, 0.4f));
        GameObject count = CreateText("AnchorCount", topBar.transform, new Vector2(0, -95), new Vector2(750, 50), 20, FontStyles.Normal, Color.yellow);

        // Bottom Action Control Card
        GameObject botCard = new GameObject("BottomControlCard", typeof(RectTransform), typeof(Image));
        botCard.transform.SetParent(hudGO.transform, false);
        RectTransform botRt = botCard.GetComponent<RectTransform>();
        botRt.anchorMin = new Vector2(0.05f, 0.02f);
        botRt.anchorMax = new Vector2(0.95f, 0.22f);
        botRt.pivot = new Vector2(0.5f, 0);
        botRt.offsetMin = botRt.offsetMax = Vector2.zero;
        botCard.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

        // Large '+ DROP START ANCHOR' Button
        GameObject dropBtn = CreateButton("DropAnchorButton", botCard.transform, new Vector2(0, 60), new Vector2(480, 80), "+ DROP START ANCHOR", new Color(0.12f, 0.55f, 0.95f), centeredInParent: true);

        // Large 'FINISH & SAVE' Button
        GameObject finishBtn = CreateButton("FinishButton", botCard.transform, new Vector2(0, -45), new Vector2(480, 75), "FINISH & SAVE ROUTE", new Color(0.15f, 0.85f, 0.45f), centeredInParent: true);

        navMgr.recordingTitleText = title.GetComponent<TMP_Text>();
        navMgr.anchorCountText = count.GetComponent<TMP_Text>();
        navMgr.dropAnchorButton = dropBtn.GetComponent<Button>();
        navMgr.finishRecordingButton = finishBtn.GetComponent<Button>();
        navMgr.cancelRecordingButton = cancelBtn.GetComponent<Button>();

        SetLayerRecursively(hudGO, 5);
        hudGO.SetActive(false);
        return hudGO;
    }

    private static GameObject SetupGuidanceHUD(Transform parent, CampusNavigationManager navMgr)
    {
        Transform existing = parent.Find("GuidanceHUD");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        GameObject hudGO = new GameObject("GuidanceHUD", typeof(RectTransform));
        hudGO.transform.SetParent(parent, false);
        RectTransform rt = hudGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // Top Status Header
        GameObject topBar = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
        topBar.transform.SetParent(hudGO.transform, false);
        RectTransform barRt = topBar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0, 1);
        barRt.anchorMax = new Vector2(1, 1);
        barRt.pivot = new Vector2(0.5f, 1);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0, 160);
        topBar.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

        GameObject destTitle = CreateText("GuidanceTitle", topBar.transform, new Vector2(0, -35), new Vector2(650, 45), 26, FontStyles.Bold, new Color(0.2f, 1f, 0.5f));
        GameObject distInfo = CreateText("DistanceInfo", topBar.transform, new Vector2(0, -95), new Vector2(750, 45), 20, FontStyles.Normal, Color.white);

        // Bottom Exit Button
        GameObject botCard = new GameObject("BottomControlCard", typeof(RectTransform), typeof(Image));
        botCard.transform.SetParent(hudGO.transform, false);
        RectTransform botRt = botCard.GetComponent<RectTransform>();
        botRt.anchorMin = new Vector2(0.08f, 0.03f);
        botRt.anchorMax = new Vector2(0.92f, 0.12f);
        botRt.offsetMin = botRt.offsetMax = Vector2.zero;
        botCard.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.94f);

        GameObject endBtn = CreateButton("EndNavButton", botCard.transform, Vector2.zero, new Vector2(460, 65), "EXIT NAVIGATION", new Color(0.85f, 0.25f, 0.25f), centeredInParent: true);

        navMgr.guidanceDestinationText = destTitle.GetComponent<TMP_Text>();
        navMgr.guidanceDistanceText = distInfo.GetComponent<TMP_Text>();
        navMgr.endNavigationButton = endBtn.GetComponent<Button>();

        SetLayerRecursively(hudGO, 5);
        hudGO.SetActive(false);
        return hudGO;
    }

    private static GameObject CreateText(string name, Transform parent, Vector2 anchoredPos, Vector2 size, float fontSize, FontStyles style, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font != null) tmp.font = font;
        tmp.text = name;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return go;
    }

    private static GameObject CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, string text, Color color, bool centeredInParent = false)
    {
        GameObject btnGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.layer = 5;
        btnGO.transform.SetParent(parent, false);
        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = btnGO.GetComponent<Image>();
        img.color = color;

        Button btn = btnGO.GetComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
        cb.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        cb.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
        btn.colors = cb;

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.layer = 5;
        textGO.transform.SetParent(btnGO.transform, false);
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return btnGO;
    }

    private static void WireDestinationButton(Transform parent, string buttonName, CampusNavigationManager navMgr, string destName)
    {
        Transform btnT = parent.Find(buttonName);
        if (btnT == null) return;

        Button btn = btnT.GetComponent<Button>();
        if (btn == null) return;

        while (btn.onClick.GetPersistentEventCount() > 0)
        {
            UnityEventTools.RemovePersistentListener(btn.onClick, 0);
        }

        UnityEventTools.AddStringPersistentListener(btn.onClick, navMgr.SelectDestination, destName);
        Debug.Log($"BMU AR: Wired '{buttonName}' -> SelectDestination(\"{destName}\").");
    }

    [MenuItem("Tools/BMU AR/Refresh Compass Ring In Scene")]
    public static void RefreshCompassRing()
    {
        GameObject itGO = GameObject.Find("ImageTarget_Compass");
        if (itGO == null) return;

        Transform root = itGO.transform.Find("ARContentRoot");
        if (root == null)
        {
            root = new GameObject("ARContentRoot").transform;
            root.SetParent(itGO.transform, false);
        }
        root.gameObject.SetActive(true);

        Transform oldArrow = root.Find("DirectionArrow");
        if (oldArrow != null) Undo.DestroyObjectImmediate(oldArrow.gameObject);
        ARDirectionArrow arrowComp = root.GetComponent<ARDirectionArrow>();
        if (arrowComp != null) Undo.DestroyObjectImmediate(arrowComp);

        Transform ringT = root.Find("FloorCompassRing");
        if (ringT == null)
        {
            GameObject ringGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringGO.name = "FloorCompassRing";
            ringGO.transform.SetParent(root, false);
            ringGO.transform.localPosition = new Vector3(0, 0.002f, 0);
            ringGO.transform.localScale = new Vector3(0.24f, 0.001f, 0.24f);
            Collider col = ringGO.GetComponent<Collider>();
            if (col != null) Undo.DestroyObjectImmediate(col);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
            if (unlitShader == null) unlitShader = Shader.Find("Standard");
            Material ringMat = new Material(unlitShader);
            ringMat.color = new Color(0.15f, 0.85f, 0.45f, 0.7f);
            ringGO.GetComponent<Renderer>().sharedMaterial = ringMat;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("BMU AR: Floor Compass Ring refreshed in Scene View!");
    }

    [MenuItem("Tools/BMU AR/Build Android APK")]
    public static void BuildAPK()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // Ensure all required plugins are explicitly enabled for Android
        string[] requiredDlls = new string[]
        {
            "Assets/Firebase/Plugins/Firebase.App.dll",
            "Assets/Firebase/Plugins/Firebase.Auth.dll",
            "Assets/Firebase/Plugins/Firebase.Platform.dll",
            "Assets/Firebase/Plugins/Firebase.TaskExtension.dll",
            "Assets/Firebase/Plugins/Google.MiniJson.dll"
        };
        foreach (var dll in requiredDlls)
        {
            PluginImporter importer = AssetImporter.GetAtPath(dll) as PluginImporter;
            if (importer != null && !importer.GetCompatibleWithPlatform(BuildTarget.Android))
            {
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SaveAndReimport();
                Debug.Log($"BMU AR: Re-enabled Android platform compatibility for {dll}.");
            }
        }

        string buildFolder = System.IO.Path.Combine(Application.dataPath, "..", "Builds");
        if (!System.IO.Directory.Exists(buildFolder))
        {
            System.IO.Directory.CreateDirectory(buildFolder);
        }

        string apkPath = System.IO.Path.Combine(buildFolder, "BMUARR.apk");
        Debug.Log($"BMU AR: Starting build to {apkPath}...");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"BMU AR: Build succeeded! APK saved at: {apkPath}");
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(apkPath);
                EditorUtility.DisplayDialog("Build Succeeded",
                    $"APK successfully generated!\n\nFile location:\n{apkPath}\n\nTransfer this APK to your phone and install it to test on campus!",
                    "OK");
            }
        }
        else
        {
            Debug.LogError($"BMU AR: Build failed with {report.summary.totalErrors} errors.");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Build Failed", "Android build failed. Please check the Unity Console for details.", "OK");
            }
        }
    }
}
