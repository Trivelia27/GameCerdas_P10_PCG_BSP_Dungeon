using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Denyut efek post-processing (chromatic aberration + vignette) saat dash, hit, dan level selesai.</summary>
public class PostFxController : MonoBehaviour
{
    private static PostFxController instance;

    private ChromaticAberration chromatic;
    private Vignette vignette;
    private float baseChromatic;
    private float baseVignette;
    private float chromaticPulse;
    private float vignettePulse;

    public static void Pulse(float chromaticAmount, float vignetteAmount)
    {
        if (instance == null)
        {
            return;
        }

        instance.chromaticPulse = Mathf.Max(instance.chromaticPulse, chromaticAmount);
        instance.vignettePulse = Mathf.Max(instance.vignettePulse, vignetteAmount);
    }

    private void Awake()
    {
        instance = this;

        Volume volume = FindFirstObjectByType<Volume>();

        if (volume == null || volume.profile == null)
        {
            enabled = false;
            return;
        }

        if (volume.profile.TryGet(out chromatic))
        {
            baseChromatic = chromatic.intensity.value;
        }

        if (volume.profile.TryGet(out vignette))
        {
            baseVignette = vignette.intensity.value;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        chromaticPulse = Mathf.MoveTowards(chromaticPulse, 0f, dt * 2.2f);
        vignettePulse = Mathf.MoveTowards(vignettePulse, 0f, dt * 0.6f);

        if (chromatic != null)
        {
            chromatic.intensity.value = baseChromatic + chromaticPulse;
        }

        if (vignette != null)
        {
            vignette.intensity.value = baseVignette + vignettePulse;
        }
    }
}
