using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws routes on the floor:
/// - a flat ribbon with moving chevrons that point the way,
/// - a start disc while recording,
/// - a destination pin with a label while guiding.
/// Everything is parented to the marker origin, so drift corrections move it too.
/// </summary>
public class ARPathRenderer : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("References")]
    public AnchorTracker anchorTracker;

    [Tooltip("Transparent, double-sided URP Unlit material for the ribbon. Created at runtime if empty.")]
    public Material routeMaterial;

    [Tooltip("URP Unlit material for markers; tinted per marker. Created at runtime if empty.")]
    public Material markerMaterial;

    [Header("Ribbon")]
    public float lineWidth = 0.4f;
    public Color pathColor = new Color(0.12f, 0.95f, 0.45f, 0.9f);
    public Color recordingColor = new Color(0.1f, 0.75f, 1f, 0.9f);

    [Tooltip("Height above the floor (metres) to avoid flicker against the camera image.")]
    public float floorOffset = 0.02f;

    [Tooltip("Chevron scroll speed (tiles per second).")]
    public float flowSpeed = 0.8f;

    [Header("Markers")]
    public Color startColor = new Color(0.2f, 0.95f, 0.4f, 1f);
    public Color destinationColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Header("Recording")]
    public Color pointColor = new Color(0.1f, 0.75f, 1f, 1f);
    public Color reticleReadyColor = new Color(0.2f, 1f, 0.5f, 1f);
    public Color reticleBlockedColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    private LineRenderer line;
    private Transform lineTransform;
    private readonly List<Vector2> lineBuffer = new List<Vector2>();
    private Vector3[] positionBuffer = new Vector3[0];
    private readonly List<GameObject> pointDots = new List<GameObject>();
    private GameObject reticle;
    private MeshRenderer reticleRenderer;
    private bool? reticleColorReady;
    private GameObject startMarker;
    private GameObject destinationMarker;
    private Transform destinationBeacon;
    private MaterialPropertyBlock propertyBlock;
    private Texture2D chevronTexture;
    private bool ownsRouteMaterial;
    private bool ownsMarkerMaterial;

    // Points are stored with y up; the ribbon's transform has its Z axis pointing up,
    // so local points are rotated into its frame.
    private static readonly Quaternion OriginToLine = Quaternion.Euler(90f, 0f, 0f);

    void Awake()
    {
        if (anchorTracker == null) anchorTracker = FindAnyObjectByType<AnchorTracker>();
        propertyBlock = new MaterialPropertyBlock();
        EnsureMaterials();
    }

    void OnDestroy()
    {
        if (ownsRouteMaterial && routeMaterial != null) Destroy(routeMaterial);
        if (ownsMarkerMaterial && markerMaterial != null) Destroy(markerMaterial);
        if (chevronTexture != null) Destroy(chevronTexture);
        if (lineTransform != null) Destroy(lineTransform.gameObject);
        ClearMarkers();
    }

    void Update()
    {
        if (line != null && line.enabled && routeMaterial != null)
        {
            routeMaterial.mainTextureOffset = new Vector2(-Time.time * flowSpeed, 0f);
        }

        if (destinationBeacon != null)
        {
            destinationBeacon.localPosition = new Vector3(0f, 0.8f + Mathf.Sin(Time.time * 2.5f) * 0.04f, 0f);
            destinationBeacon.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        }
    }

    /// <summary>
    /// Live preview while recording: a dot per placed point, the line through them,
    /// and an aiming ring on the floor with a preview segment to it.
    /// Cheap enough to call every frame.
    /// </summary>
    public void RenderRecording(IReadOnlyList<Vector2> points, float floorY, Vector2? aim, bool canPlace)
    {
        RemoveDestinationMarker();

        int count = points != null ? points.Count : 0;
        lineBuffer.Clear();
        for (int i = 0; i < count; i++) lineBuffer.Add(points[i]);
        if (aim.HasValue && count > 0) lineBuffer.Add(aim.Value);

        if (lineBuffer.Count >= 2) SetLine(lineBuffer, floorY, recordingColor);
        else if (line != null) line.enabled = false;

        if (count > 0) EnsureStartMarker(points[0], floorY);
        else RemoveStartMarker();

        SyncPointDots(points, floorY);
        UpdateReticle(aim, floorY, canPlace);
    }

    /// <summary>Guidance: the route still ahead of the user and the destination pin.</summary>
    public void RenderGuide(IReadOnlyList<Vector2> remainingPoints, Vector2 destination, string destinationName, float floorY)
    {
        if (remainingPoints == null || remainingPoints.Count == 0)
        {
            ClearAll();
            return;
        }

        SetLine(remainingPoints, floorY, pathColor);
        RemoveStartMarker();
        RemoveRecordingVisuals();
        EnsureDestinationMarker(destination, floorY, destinationName);
    }

    public void ClearAll()
    {
        if (line != null)
        {
            line.positionCount = 0;
            line.enabled = false;
        }
        ClearMarkers();
    }

    private void SetLine(IReadOnlyList<Vector2> points, float floorY, Color color)
    {
        if (!EnsureLine()) return;

        if (points.Count < 2)
        {
            line.positionCount = 0;
            line.enabled = false;
            return;
        }

        if (positionBuffer.Length != points.Count) positionBuffer = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            positionBuffer[i] = OriginToLine * new Vector3(points[i].x, floorY + floorOffset, points[i].y);
        }

        line.positionCount = points.Count;
        line.SetPositions(positionBuffer);

        // URP Unlit ignores vertex colours, so tint through a property block.
        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        line.SetPropertyBlock(propertyBlock);
        line.enabled = true;
    }

    private bool EnsureLine()
    {
        if (line != null) return true;

        Transform origin = Origin;
        if (origin == null) return false;

        var go = new GameObject("RouteLine");
        lineTransform = go.transform;
        lineTransform.SetParent(origin, false);
        lineTransform.localPosition = Vector3.zero;
        lineTransform.localRotation = Quaternion.Inverse(OriginToLine);

        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.TransformZ; // Lie flat on the floor.
        line.widthMultiplier = lineWidth;
        line.numCornerVertices = 6;
        line.numCapVertices = 4;
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(1f / lineWidth, 1f); // One chevron per ribbon width.
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = routeMaterial;
        line.enabled = false;
        return true;
    }

    private void EnsureStartMarker(Vector2 position, float floorY)
    {
        Transform origin = Origin;
        if (origin == null) return;

        if (startMarker == null)
        {
            startMarker = new GameObject("StartMarker");
            startMarker.transform.SetParent(origin, false);

            CreatePart(PrimitiveType.Cylinder, startMarker.transform, Vector3.zero, new Vector3(0.45f, 0.01f, 0.45f), startColor);
            CreateLabel("START", startMarker.transform, new Vector3(0f, 0.25f, 0f), 0.6f);
        }
        startMarker.transform.localPosition = new Vector3(position.x, floorY + floorOffset, position.y);
    }

    private void EnsureDestinationMarker(Vector2 position, float floorY, string destinationName)
    {
        Transform origin = Origin;
        if (origin == null) return;

        if (destinationMarker == null)
        {
            destinationMarker = new GameObject("DestinationMarker");
            destinationMarker.transform.SetParent(origin, false);

            CreatePart(PrimitiveType.Cylinder, destinationMarker.transform, Vector3.zero, new Vector3(0.6f, 0.01f, 0.6f), destinationColor);
            CreatePart(PrimitiveType.Cylinder, destinationMarker.transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.03f, 0.35f, 0.03f), Color.white);
            destinationBeacon = CreatePart(PrimitiveType.Sphere, destinationMarker.transform, new Vector3(0f, 0.8f, 0f), Vector3.one * 0.16f, destinationColor).transform;

            string label = string.IsNullOrEmpty(destinationName) ? "DESTINATION" : destinationName.ToUpperInvariant();
            CreateLabel(label, destinationMarker.transform, new Vector3(0f, 1.1f, 0f), 0.9f);
        }
        destinationMarker.transform.localPosition = new Vector3(position.x, floorY + floorOffset, position.y);
    }

    private GameObject CreatePart(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
    {
        // Not GameObject.CreatePrimitive: it adds a collider, and engine code
        // stripping removes collider classes from builds that never use physics.
        var part = new GameObject(type.ToString());
        part.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        var meshRenderer = part.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = markerMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
        return part;
    }

    private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveMeshes = new Dictionary<PrimitiveType, Mesh>();

    /// <summary>The built-in meshes that GameObject.CreatePrimitive uses.</summary>
    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        if (PrimitiveMeshes.TryGetValue(type, out Mesh mesh) && mesh != null) return mesh;

        string resource = type == PrimitiveType.Sphere ? "New-Sphere.fbx" : "New-Cylinder.fbx";
        mesh = Resources.GetBuiltinResource<Mesh>(resource);
        PrimitiveMeshes[type] = mesh;
        return mesh;
    }

    private void CreateLabel(string text, Transform parent, Vector3 localPosition, float fontSize)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 200);
        tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        go.AddComponent<BillboardLookAt>();
    }

    private void SyncPointDots(IReadOnlyList<Vector2> points, float floorY)
    {
        Transform origin = Origin;
        int count = points != null ? points.Count : 0;

        while (pointDots.Count > count)
        {
            int last = pointDots.Count - 1;
            if (pointDots[last] != null) Destroy(pointDots[last]);
            pointDots.RemoveAt(last);
        }
        while (pointDots.Count < count && origin != null)
        {
            var dot = CreatePart(PrimitiveType.Sphere, origin, Vector3.zero, Vector3.one * 0.12f, pointColor);
            dot.name = "RoutePoint_" + (pointDots.Count + 1);
            pointDots.Add(dot);
        }
        for (int i = 0; i < pointDots.Count; i++)
        {
            if (pointDots[i] != null) pointDots[i].transform.localPosition = new Vector3(points[i].x, floorY + 0.06f, points[i].y);
        }
    }

    private void UpdateReticle(Vector2? aim, float floorY, bool canPlace)
    {
        Transform origin = Origin;
        if (!aim.HasValue || origin == null)
        {
            if (reticle != null) reticle.SetActive(false);
            return;
        }

        if (reticle == null)
        {
            reticle = CreatePart(PrimitiveType.Cylinder, origin, Vector3.zero, new Vector3(0.3f, 0.004f, 0.3f), reticleReadyColor);
            reticle.name = "AimReticle";
            reticleRenderer = reticle.GetComponent<MeshRenderer>();
            reticleColorReady = true;
        }

        if (reticleColorReady != canPlace)
        {
            reticleColorReady = canPlace;
            propertyBlock.Clear();
            Color color = canPlace ? reticleReadyColor : reticleBlockedColor;
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            reticleRenderer.SetPropertyBlock(propertyBlock);
        }

        reticle.SetActive(true);
        reticle.transform.localPosition = new Vector3(aim.Value.x, floorY + floorOffset, aim.Value.y);
    }

    private void RemoveRecordingVisuals()
    {
        foreach (GameObject dot in pointDots)
        {
            if (dot != null) Destroy(dot);
        }
        pointDots.Clear();
        if (reticle != null) Destroy(reticle);
        reticle = null;
        reticleRenderer = null;
        reticleColorReady = null;
    }

    private void RemoveStartMarker()
    {
        if (startMarker != null) Destroy(startMarker);
        startMarker = null;
    }

    private void RemoveDestinationMarker()
    {
        if (destinationMarker != null) Destroy(destinationMarker);
        destinationMarker = null;
        destinationBeacon = null;
    }

    private void ClearMarkers()
    {
        RemoveStartMarker();
        RemoveDestinationMarker();
        RemoveRecordingVisuals();
    }

    private Transform Origin => anchorTracker != null ? anchorTracker.StabilizedOrigin : null;

    private void EnsureMaterials()
    {
        if (routeMaterial == null)
        {
            routeMaterial = CreateUnlitMaterial(transparent: true, doubleSided: true);
            ownsRouteMaterial = routeMaterial != null;
        }
        else
        {
            // Never animate the shared asset itself.
            routeMaterial = new Material(routeMaterial);
            ownsRouteMaterial = true;
        }

        if (routeMaterial != null)
        {
            chevronTexture = CreateChevronTexture();
            routeMaterial.mainTexture = chevronTexture;
        }

        if (markerMaterial == null)
        {
            markerMaterial = CreateUnlitMaterial(transparent: false, doubleSided: false);
            ownsMarkerMaterial = markerMaterial != null;
        }
    }

    private static Material CreateUnlitMaterial(bool transparent, bool doubleSided)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            AppLog.Error("ARPathRenderer", "No unlit shader found. Assign route/marker materials in the inspector.");
            return null;
        }

        var mat = new Material(shader);
        ConfigureUnlit(mat, transparent, doubleSided);
        return mat;
    }

    /// <summary>Sets up a URP Unlit material. Shared with the editor scene builder.</summary>
    public static void ConfigureUnlit(Material mat, bool transparent, bool doubleSided)
    {
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
        if (!transparent) return;

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>Ribbon texture: a soft fill with one chevron pointing along the route.</summary>
    private static Texture2D CreateChevronTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "RouteChevron"
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;              // Across the ribbon.
            float across = Mathf.Abs(v - 0.5f) * 2f;  // 0 at centre, 1 at edges.
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;          // Along the ribbon.
                float chevronU = 0.65f - 0.35f * across;
                float chevron = Mathf.Clamp01(1f - Mathf.Abs(u - chevronU) / 0.09f) * (across < 0.8f ? 1f : 0f);
                float edge = Mathf.Clamp01((1f - across) / 0.15f);
                float alpha = Mathf.Max(0.35f, chevron) * edge;
                byte a = (byte)(alpha * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
