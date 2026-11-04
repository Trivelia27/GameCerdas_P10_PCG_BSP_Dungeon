using UnityEngine;

/// <summary>Efek visual prosedural (partikel burst, ring shockwave, sparkle) tanpa asset prefab.</summary>
public static class Fx
{
    private static Material glow, ring, ringDash;

    public static Material GlowMat => glow != null ? glow : glow = Resources.Load<Material>("Fx/FxGlow");
    public static Material RingMat => ring != null ? ring : ring = Resources.Load<Material>("Fx/FxRing");
    public static Material RingDashMat => ringDash != null ? ringDash : ringDash = Resources.Load<Material>("Fx/FxRingDash");

    public static void Burst(Vector3 position, Color color, int count = 24, float speed = 5f, float size = 0.22f, float life = 0.65f, float gravity = 0.6f)
    {
        GameObject go = new GameObject("FxBurst");
        go.SetActive(false);
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = count * 2;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        FadeOut(ps, 1f);
        ShrinkOverLife(ps);

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowMat;
        go.SetActive(true);
    }

    public static void Ring(Vector3 position, Color color, float radius = 2f, float duration = 0.55f, bool dashed = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());

        go.name = "FxRing";
        go.transform.position = position + Vector3.up * 0.05f;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.zero;

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = dashed ? RingDashMat : RingMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        go.AddComponent<RingFx>().Init(color, radius, duration);
    }

    /// <summary>Partikel kilau yang terus muncul (dipakai item langka).</summary>
    public static void AttachSparkle(Transform parent, Vector3 localPosition, Color color, float rate = 10f)
    {
        GameObject go = new GameObject("FxSparkle");
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = color;
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;

        FadeOut(ps, 1f);
        ShrinkOverLife(ps);

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowMat;
        go.SetActive(true);
    }

    /// <summary>Debu cahaya melayang untuk suasana arena.</summary>
    public static void CreateAmbientMotes(Transform parent, Vector3 area, Color color)
    {
        GameObject go = new GameObject("AmbientMotes");
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, area.y * 0.5f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 220;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 18f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Unity mewajibkan x, y, z memakai mode kurva yang sama (di sini semuanya two constants).
        velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0.9f, 0.75f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GlowMat;
        go.SetActive(true);
    }

    private static void FadeOut(ParticleSystem ps, float startAlpha)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.8f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;
    }

    private static void ShrinkOverLife(ParticleSystem ps)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
    }
}
