using UnityEngine;

public class PulseText : MonoBehaviour
{
    public float scaleMin = 0.9f;
    public float scaleMax = 1.1f;
    public float speed = 2.5f;

    void Update()
    {
        float scale = Mathf.Lerp(scaleMin, scaleMax, (Mathf.Sin(Time.time * speed) + 1f) / 2f);
        transform.localScale = new Vector3(scale, scale, 1f);
    }
}