using System.Collections;
using UnityEngine;

/// <summary>Animasi muncul (scale overshoot + ring) agar proses PCG terlihat saat object di-spawn.</summary>
public class SpawnPop : MonoBehaviour
{
    [SerializeField] private Color ringColor = new Color(0.3f, 0.95f, 1f);
    [SerializeField] private float ringRadius = 1.3f;
    [SerializeField] private float duration = 0.45f;

    private Vector3 targetScale;
    private bool played;

    public bool Done { get; private set; }

    public void Configure(Color color, float radius)
    {
        ringColor = color;
        ringRadius = radius;
    }

    private void Awake()
    {
        targetScale = transform.localScale;
        transform.localScale = Vector3.zero;
    }

    private void Start()
    {
        if (!played)
        {
            Play(0f);
        }
    }

    public void Play(float delay)
    {
        if (played)
        {
            return;
        }

        played = true;
        StartCoroutine(Run(delay));
    }

    private IEnumerator Run(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        Fx.Ring(transform.position, ringColor, ringRadius, 0.55f);
        AudioManager.Play(Sfx.Spawn, 0.3f, Random.Range(0.85f, 1.5f));

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = targetScale * EaseOutBack(Mathf.Clamp01(t / duration));
            yield return null;
        }

        transform.localScale = targetScale;
        Done = true;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 2.2f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
