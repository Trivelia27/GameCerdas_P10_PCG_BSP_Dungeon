using UnityEngine;

/// <summary>
/// Turret pada Sentry Room: memutar kepala ke arah player dan menembakkan proyektil lambat
/// bila player dalam jangkauan dan terlihat (line of sight). Charge 0.45 s sebagai telegraph.
/// </summary>
public class Turret : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Renderer eye;
    [SerializeField] private Material projectileMaterial;
    [SerializeField] private float range = 8f;
    [SerializeField] private float fireInterval = 2.1f;
    [SerializeField] private float chargeTime = 0.55f;
    [SerializeField] private float projectileSpeed = 4.2f;
    [SerializeField] private float turnSpeed = 140f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private DungeonGenerator gen;
    private MaterialPropertyBlock block;
    private float timer = 1.2f;
    private bool hasTarget;

    public void Init(DungeonGenerator generator)
    {
        gen = generator;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        DungeonGameManager gm = DungeonGameManager.Instance;
        bool live = DungeonGameManager.IsLive && gm != null && gm.Player != null && gen != null;

        hasTarget = false;
        if (live)
        {
            Vector3 to = gm.Player.transform.position - head.position;
            to.y = 0f;
            if (to.magnitude <= range && HasLineOfSight(gm.Player.transform))
            {
                hasTarget = true;
                Quaternion look = Quaternion.LookRotation(to);
                head.rotation = Quaternion.RotateTowards(head.rotation, look, turnSpeed * Time.deltaTime);
            }
        }

        if (!hasTarget)
        {
            head.Rotate(Vector3.up, 35f * Time.deltaTime, Space.World);
            timer = Mathf.Max(timer, chargeTime + 0.2f);
        }
        else
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                Fire();
                timer = fireInterval;
            }
        }

        // telegraph: mata berkedip saat charging
        bool charging = hasTarget && timer < chargeTime;
        Color c = new Color(0.7f, 0.35f, 1f) * (charging ? 4f + 4f * Mathf.Abs(Mathf.Sin(Time.time * 30f)) : hasTarget ? 3f : 1.4f);
        if (eye != null)
        {
            eye.GetPropertyBlock(block);
            block.SetColor(EmissionId, c);
            eye.SetPropertyBlock(block);
        }
    }

    private bool HasLineOfSight(Transform target)
    {
        Vector3 from = muzzle.position;
        Vector3 to = target.position;
        to.y = from.y;

        if (Physics.Linecast(from, to, out RaycastHit hit))
            return hit.collider.CompareTag("Player");
        return true;
    }

    private void Fire()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Projectile";
        go.transform.SetParent(transform, true);
        go.transform.position = muzzle.position;
        go.transform.localScale = Vector3.one * 0.24f;

        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = projectileMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 dir = head.forward;
        dir.y = 0f;
        go.AddComponent<Projectile>().Init(gen, dir.normalized * projectileSpeed);

        Fx.Burst(muzzle.position, new Color(0.75f, 0.45f, 1f), 10, 2.5f, 0.12f, 0.3f, 0f);
        AudioManager.Play(Sfx.Dash, 0.25f, 1.9f);
    }
}
