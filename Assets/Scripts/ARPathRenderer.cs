using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders the 3D AR navigation pathway to real-world scale:
/// - Wide (0.45m), glowing transparent emerald ribbon line on the floor.
/// - Catmull-Rom spline smoothing (via SplineMath) for fluid turn curves.
/// - In Recording Mode: displays start circle and anchor spheres at each recorded turn.
/// - In Guide Mode: hides anchor dots, displaying only the smooth glowing line
///   and the 3D Destination Stop Marker with billboard text.
/// - Parented to StabilizedOrigin so it is NEVER culled when the phone turns away from the paper target.
/// </summary>
public class ARPathRenderer : MonoBehaviour
{
    [Header("Line Appearance")]
    [Tooltip("Width of the path ribbon on the floor in meters.")]
    public float lineWidth = 0.45f;

    [Tooltip("Primary glowing emerald color.")]
    public Color pathColor = new Color(0.12f, 0.95f, 0.45f, 0.85f);

    [Tooltip("Height offset above floor in meters to prevent z-fighting.")]
    public float floorOffset = 0.03f;

    [Header("Anchor & Marker Appearance")]
    public Color anchorColor = new Color(0.1f, 0.75f, 1f, 0.95f); // Cyan for anchor dots
    public Color destinationColor = new Color(1f, 0.28f, 0.28f, 0.98f); // Coral red for destination
    public Color startColor = new Color(0.2f, 0.95f, 0.4f, 0.98f);

    [Header("World Anchor Root")]
    public Transform worldAnchorRoot;

    private LineRenderer lineRenderer;
    private Material lineMaterial;
    private readonly List<GameObject> anchorMarkers = new List<GameObject>();
    private GameObject startMarker;
    private GameObject destinationMarker;

    void Awake()
    {
        EnsureWorldAnchorRoot();
        SetupLineRenderer();
    }

    public void EnsureWorldAnchorRoot()
    {
        if (worldAnchorRoot == null)
        {
            AnchorTracker tracker = FindFirstObjectByType<AnchorTracker>();
            if (tracker != null && tracker.StabilizedOrigin != null)
            {
                worldAnchorRoot = tracker.StabilizedOrigin;
            }
            else
            {
                GameObject rootGO = GameObject.Find("Calibrated_World_Origin");
                if (rootGO == null) rootGO = new GameObject("Calibrated_World_Origin");
                worldAnchorRoot = rootGO.transform;
                worldAnchorRoot.SetParent(null, true);
            }
        }
    }

    public void SetOriginPose(Vector3 worldPos, Quaternion worldRot)
    {
        EnsureWorldAnchorRoot();
        worldAnchorRoot.position = worldPos;
        worldAnchorRoot.rotation = worldRot;
        Debug.Log($"[ARPathRenderer] World Anchor locked at {worldPos}");
    }

    private void SetupLineRenderer()
    {
        EnsureWorldAnchorRoot();

        if (lineRenderer == null)
        {
            lineRenderer = worldAnchorRoot.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = worldAnchorRoot.gameObject.AddComponent<LineRenderer>();
            }
        }

        lineRenderer.useWorldSpace = false; // Local to worldAnchorRoot!
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.numCornerVertices = 8;
        lineRenderer.numCapVertices = 8;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.enabled = false;

        lineMaterial = CreateGlowingMaterial(pathColor);
        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = pathColor;
        lineRenderer.endColor = pathColor;
    }

    /// <summary>
    /// Renders the path for 'Guide Me' mode (smooth Catmull-Rom spline, NO anchor dots).
    /// </summary>
    public void RenderGuidedPath(PathData pathData)
    {
        ClearAll();
        if (pathData == null || pathData.waypoints == null || pathData.waypoints.Count == 0) return;

        EnsureWorldAnchorRoot();
        if (lineRenderer == null) SetupLineRenderer();

        List<Vector3> rawPoints = new List<Vector3>();
        for (int i = 0; i < pathData.waypoints.Count; i++)
        {
            Vector3 pos = pathData.waypoints[i].GetPosition();
            pos.y += floorOffset;
            rawPoints.Add(pos);
        }

        if (rawPoints.Count >= 2)
        {
            // Smooth spline interpolation for fluid, curved visual path
            List<Vector3> smoothPoints = SplineMath.GenerateSmoothPath(rawPoints, pointsPerSegment: 8);
            lineRenderer.positionCount = smoothPoints.Count;
            lineRenderer.SetPositions(smoothPoints.ToArray());
            lineRenderer.enabled = true;
        }
        else
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }

        // Start marker at (0, 0, 0)
        CreateStartMarker(rawPoints[0]);

        // Destination Stop Marker at end
        CreateDestinationMarker(rawPoints[rawPoints.Count - 1], pathData.destinationName);
    }

    /// <summary>
    /// Updates live visuals during recording: shows smooth path line + anchor spheres at each turn.
    /// </summary>
    public void UpdateRecordingVisuals(List<Vector3> localPoints, string destinationName)
    {
        if (localPoints == null || localPoints.Count == 0)
        {
            ClearAll();
            return;
        }

        EnsureWorldAnchorRoot();
        if (lineRenderer == null) SetupLineRenderer();

        List<Vector3> points = new List<Vector3>();
        for (int i = 0; i < localPoints.Count; i++)
        {
            Vector3 pos = localPoints[i];
            pos.y += floorOffset;
            points.Add(pos);
        }

        if (points.Count >= 2)
        {
            List<Vector3> smoothPoints = SplineMath.GenerateSmoothPath(points, pointsPerSegment: 6);
            lineRenderer.positionCount = smoothPoints.Count;
            lineRenderer.SetPositions(smoothPoints.ToArray());
            lineRenderer.enabled = true;
        }
        else
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }

        // Ensure Start Marker
        if (startMarker == null && points.Count > 0)
        {
            CreateStartMarker(points[0]);
        }

        // Rebuild anchor dots (turns)
        ClearAnchorMarkers();
        for (int i = 1; i < points.Count; i++)
        {
            CreateAnchorDot(points[i], i);
        }
    }

    public void CreateStartMarker(Vector3 localPos)
    {
        EnsureWorldAnchorRoot();
        if (startMarker != null) Destroy(startMarker);

        startMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        startMarker.name = "StartMarker_Origin";
        startMarker.transform.SetParent(worldAnchorRoot, false);
        startMarker.transform.localPosition = localPos;
        startMarker.transform.localScale = new Vector3(0.40f, 0.015f, 0.40f);
        DestroyCollider(startMarker);

        startMarker.GetComponent<Renderer>().sharedMaterial = CreateGlowingMaterial(startColor);
        Create3DText("START", startMarker.transform, new Vector3(0, 0.20f, 0), Color.white, 0.022f);
    }

    public void CreateAnchorDot(Vector3 localPos, int index)
    {
        EnsureWorldAnchorRoot();
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = $"AnchorDot_{index}";
        dot.transform.SetParent(worldAnchorRoot, false);
        dot.transform.localPosition = localPos + new Vector3(0, 0.06f, 0);
        dot.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
        DestroyCollider(dot);

        dot.GetComponent<Renderer>().sharedMaterial = CreateGlowingMaterial(anchorColor);
        anchorMarkers.Add(dot);
    }

    public void CreateDestinationMarker(Vector3 localPos, string destName)
    {
        EnsureWorldAnchorRoot();
        if (destinationMarker != null) Destroy(destinationMarker);

        destinationMarker = new GameObject("DestinationStopMarker");
        destinationMarker.transform.SetParent(worldAnchorRoot, false);
        destinationMarker.transform.localPosition = localPos;

        // 1. Pedestal Base Ring
        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Pedestal";
        pedestal.transform.SetParent(destinationMarker.transform, false);
        pedestal.transform.localPosition = Vector3.zero;
        pedestal.transform.localScale = new Vector3(0.50f, 0.02f, 0.50f);
        DestroyCollider(pedestal);
        pedestal.GetComponent<Renderer>().sharedMaterial = CreateGlowingMaterial(destinationColor);

        // 2. Vertical Pin Pole
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(destinationMarker.transform, false);
        pole.transform.localPosition = new Vector3(0, 0.35f, 0);
        pole.transform.localScale = new Vector3(0.035f, 0.35f, 0.035f);
        DestroyCollider(pole);
        pole.GetComponent<Renderer>().sharedMaterial = CreateGlowingMaterial(Color.white);

        // 3. Floating Stop Beacon Diamond
        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beacon.name = "Beacon";
        beacon.transform.SetParent(destinationMarker.transform, false);
        beacon.transform.localPosition = new Vector3(0, 0.75f, 0);
        beacon.transform.localRotation = Quaternion.Euler(45, 45, 45);
        beacon.transform.localScale = new Vector3(0.18f, 0.18f, 0.18f);
        DestroyCollider(beacon);
        beacon.GetComponent<Renderer>().sharedMaterial = CreateGlowingMaterial(destinationColor);

        // 4. Billboard 3D Destination Banner
        string label = string.IsNullOrEmpty(destName) ? "DESTINATION" : destName.ToUpper();
        Create3DText(label, destinationMarker.transform, new Vector3(0, 0.95f, 0), Color.white, 0.025f, billboard: true);
    }

    private void Create3DText(string text, Transform parent, Vector3 localPos, Color color, float scale, bool billboard = false)
    {
        GameObject textGO = new GameObject("Text3D");
        textGO.transform.SetParent(parent, false);
        textGO.transform.localPosition = localPos;
        textGO.transform.localRotation = Quaternion.identity;
        textGO.transform.localScale = Vector3.one * scale;

        TextMesh tm = textGO.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 44;
        tm.fontStyle = FontStyle.Bold;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = color;

        if (billboard)
        {
            textGO.AddComponent<BillboardLookAt>();
        }
    }

    private void DestroyCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }

    public void ClearAll()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }

        ClearAnchorMarkers();

        if (startMarker != null)
        {
            Destroy(startMarker);
            startMarker = null;
        }

        if (destinationMarker != null)
        {
            Destroy(destinationMarker);
            destinationMarker = null;
        }
    }

    public void ClearAnchorMarkers()
    {
        foreach (var m in anchorMarkers)
        {
            if (m != null) Destroy(m);
        }
        anchorMarkers.Clear();
    }

    private Material CreateGlowingMaterial(Color col)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = col;

        // Setup transparency & glow
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", col * 1.4f);
        }

        return mat;
    }
}
