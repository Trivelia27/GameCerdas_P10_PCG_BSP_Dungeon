using UnityEngine;

/// <summary>Ring shockwave datar yang membesar lalu memudar (dibuat oleh Fx.Ring).</summary>
public class RingFx : MonoBehaviour
{
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

    private Renderer rend;
    private MaterialPropertyBlock block;
    private Color color;
    private float radius;
    private float duration;
    private float time;

    public void Init(Color ringColor, float maxRadius, float lifetime)
    {
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        color = ringColor;
        radius = maxRadius;
        duration = Mathf.Max(0.05f, lifetime);
        Apply(0f);
    }

    private void Update()
    {
        time += Time.deltaTime;
        float k = time / duration;

        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        Apply(k);
    }

    private void Apply(float k)
    {
        float eased = 1f - Mathf.Pow(1f - k, 3f);
        float alpha = Mathf.Pow(1f - k, 1.4f);

        transform.localScale = Vector3.one * (radius * 2f * eased);

        block.SetColor(TintColorId, new Color(color.r, color.g, color.b, 0.55f * alpha));
        rend.SetPropertyBlock(block);
    }
}
