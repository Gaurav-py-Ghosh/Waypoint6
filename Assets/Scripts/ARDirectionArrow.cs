using UnityEngine;

/// <summary>
/// Controls the animated 3D directional arrow anchored to the Vuforia Image Target.
/// Marked with [ExecuteAlways] so you can see and preview the 3D arrow
/// in the Unity Scene View even in Edit Mode!
/// </summary>
[ExecuteAlways]
public class ARDirectionArrow : MonoBehaviour
{
    [Header("Arrow Appearance")]
    [Tooltip("Color of the arrow.")]
    public Color arrowColor = new Color(0.18f, 0.85f, 0.45f, 1f); // Vibrant Emerald Green

    [Tooltip("Length of the arrow shaft in meters.")]
    public float shaftLength = 0.25f;

    [Tooltip("Width of the arrow shaft in meters.")]
    public float shaftWidth = 0.03f;

    [Tooltip("Size of the arrowhead in meters.")]
    public float headSize = 0.08f;

    [Header("Animation")]
    [Tooltip("Enable gentle bobbing animation.")]
    public bool enableBobbing = true;

    [Tooltip("Bobbing height in meters.")]
    public float bobHeight = 0.02f;

    [Tooltip("Bobbing speed.")]
    public float bobSpeed = 2.5f;

    [Tooltip("Enable pulse animation.")]
    public bool enablePulse = true;

    [Tooltip("Pulse scale range.")]
    public float pulseAmount = 0.06f;

    private GameObject arrowRoot;
    private Vector3 basePosition;
    private Vector3 baseScale;

    void OnEnable()
    {
        EnsureArrowExists();
    }

    void Start()
    {
        EnsureArrowExists();
    }

    void Update()
    {
        if (arrowRoot == null)
        {
            EnsureArrowExists();
            if (arrowRoot == null) return;
        }

        // Animated bobbing
        if (enableBobbing)
        {
            float yOffset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            arrowRoot.transform.localPosition = basePosition + new Vector3(0, yOffset, 0);
        }

        // Animated pulsing
        if (enablePulse)
        {
            float scaleFactor = 1f + Mathf.Sin(Time.time * bobSpeed * 1.5f) * pulseAmount;
            arrowRoot.transform.localScale = baseScale * scaleFactor;
        }
    }

    /// <summary>
    /// Checks if the DirectionArrow hierarchy already exists. If not, builds it.
    /// </summary>
    public void EnsureArrowExists()
    {
        Transform existing = transform.Find("DirectionArrow");
        if (existing != null)
        {
            arrowRoot = existing.gameObject;
            basePosition = new Vector3(0, 0.08f, 0);
            baseScale = Vector3.one;
            arrowRoot.SetActive(true);
            return;
        }

        BuildArrowGeometry();
    }

    /// <summary>
    /// Builds the 3D arrow geometry (Shaft + Head + 3D Text)
    /// </summary>
    public void BuildArrowGeometry()
    {
        // Clean up old if any
        Transform old = transform.Find("DirectionArrow");
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }

        arrowRoot = new GameObject("DirectionArrow");
        arrowRoot.transform.SetParent(transform, false);
        arrowRoot.transform.localPosition = new Vector3(0, 0.08f, 0);
        arrowRoot.transform.localRotation = Quaternion.identity;

        basePosition = arrowRoot.transform.localPosition;
        baseScale = Vector3.one;

        Material arrowMat = CreateMaterial(arrowColor);

        // 1. Shaft (stretched cube)
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "Shaft";
        shaft.transform.SetParent(arrowRoot.transform, false);
        shaft.transform.localPosition = new Vector3(0, 0, 0);
        shaft.transform.localScale = new Vector3(shaftWidth, shaftWidth * 0.7f, shaftLength);
        Collider shaftCol = shaft.GetComponent<Collider>();
        if (shaftCol != null) { if (Application.isPlaying) Destroy(shaftCol); else DestroyImmediate(shaftCol); }
        shaft.GetComponent<Renderer>().sharedMaterial = arrowMat;

        // 2. Head (rotated diamond / arrowhead)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(arrowRoot.transform, false);
        head.transform.localPosition = new Vector3(0, 0, shaftLength / 2f + headSize * 0.4f);
        head.transform.localRotation = Quaternion.Euler(0, 45, 0);
        head.transform.localScale = new Vector3(headSize, shaftWidth * 0.9f, headSize);
        Collider headCol = head.GetComponent<Collider>();
        if (headCol != null) { if (Application.isPlaying) Destroy(headCol); else DestroyImmediate(headCol); }
        head.GetComponent<Renderer>().sharedMaterial = arrowMat;

        // 3. Floating 3D Text label
        GameObject textGO = new GameObject("DestinationLabel3D");
        textGO.transform.SetParent(arrowRoot.transform, false);
        textGO.transform.localPosition = new Vector3(0, 0.08f, 0);
        textGO.transform.localRotation = Quaternion.Euler(90, 0, 0); // Flat facing upward / camera
        textGO.transform.localScale = new Vector3(0.015f, 0.015f, 0.015f);

        TextMesh tm = textGO.AddComponent<TextMesh>();
        tm.text = "NAVIGATE";
        tm.fontSize = 40;
        tm.fontStyle = FontStyle.Bold;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.white;

        // 4. Label background pill
        GameObject labelBg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        labelBg.name = "LabelBackground";
        labelBg.transform.SetParent(arrowRoot.transform, false);
        labelBg.transform.localPosition = new Vector3(0, 0.075f, 0);
        labelBg.transform.localScale = new Vector3(0.18f, 0.005f, 0.06f);
        Collider bgCol = labelBg.GetComponent<Collider>();
        if (bgCol != null) { if (Application.isPlaying) Destroy(bgCol); else DestroyImmediate(bgCol); }
        labelBg.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.08f, 0.12f, 0.22f, 0.9f));
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }
}
