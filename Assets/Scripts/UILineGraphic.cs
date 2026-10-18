using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A thick polyline for uGUI (used for routes on the campus map), with rounded
/// joints and ends. Points are in this RectTransform's local space.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UILineGraphic : MaskableGraphic
{
    [SerializeField] private float thickness = 12f;
    [SerializeField, Range(4, 24)] private int roundSegments = 10;

    private readonly List<Vector2> points = new List<Vector2>();

    public float Thickness
    {
        get => thickness;
        set { thickness = value; SetVerticesDirty(); }
    }

    public void SetPoints(IReadOnlyList<Vector2> newPoints)
    {
        points.Clear();
        if (newPoints != null) points.AddRange(newPoints);
        SetVerticesDirty();
    }

    public void Clear()
    {
        points.Clear();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points.Count < 2) return;

        float half = thickness * 0.5f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) continue;

            Vector2 normal = new Vector2(-dir.y, dir.x).normalized * half;
            AddQuad(vh, a - normal, a + normal, b + normal, b - normal);
        }

        // Round every joint and both ends.
        foreach (Vector2 p in points) AddDisc(vh, p, half);
    }

    private void AddQuad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        int start = vh.currentVertCount;
        vh.AddVert(v0, color, Vector4.zero);
        vh.AddVert(v1, color, Vector4.zero);
        vh.AddVert(v2, color, Vector4.zero);
        vh.AddVert(v3, color, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private void AddDisc(VertexHelper vh, Vector2 center, float radius)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector4.zero);
        for (int i = 0; i <= roundSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / roundSegments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector4.zero);
        }
        for (int i = 1; i <= roundSegments; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }
}
