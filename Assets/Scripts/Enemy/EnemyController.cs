using System.Collections.Generic;
using UnityEngine;

public enum EnemyMode
{
    Idle,    // perilaku SimpleEnemy modul (§77): berputar di tempat
    Patrol,  // keliling sudut room
    Guard,   // berjaga → kejar bila player masuk radius deteksi → kembali
    Wander,  // jalan acak di dalam room
    Boss     // menjaga Goal room, mengejar selama player di dalam room
}

public enum EnemyState
{
    Idle,
    Patrol,
    Wander,
    Watch,
    Chase,
    Return
}

/// <summary>
/// Enemy bergerak dengan Finite State Machine sederhana.
/// Gerakan dibatasi di dalam rectangle room asal (room BSP selalu penuh Floor),
/// sehingga Enemy tidak pernah menembus Wall dan constraint spawn modul tetap berlaku.
/// </summary>
public class EnemyController : MonoBehaviour
{
    public static readonly List<EnemyController> All = new List<EnemyController>();

    [SerializeField] private float idleRotateSpeed = 60f;

    [Header("Speed")]
    [SerializeField] private float patrolSpeed = 2.3f;
    [SerializeField] private float wanderSpeed = 1.6f;
    [SerializeField] private float chaseSpeed = 3.3f;
    [SerializeField] private float returnSpeed = 2.4f;
    [SerializeField] private float bossSpeed = 2.8f;

    [Header("Guard")]
    [SerializeField] private float detectRadius = 4.5f;
    [SerializeField] private float loseRadius = 7.5f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private EnemyMode mode = EnemyMode.Idle;
    private Rect area;
    private DungeonGenerator gen;
    private System.Random rng;
    private Vector3 home;
    private readonly List<Vector3> waypoints = new List<Vector3>();
    private int waypointIndex;
    private Vector3 wanderTarget;
    private float wanderTimer;
    private float loseTimer;
    private float alertFlash;
    private float recoilTimer;
    private bool leashed;
    private Vector3 leashCenter;
    private float leashRadius;

    private Renderer eye;
    private MaterialPropertyBlock block;
    private LineRenderer ring;
    private Transform player;

    public EnemyMode Mode => mode;
    public EnemyState State { get; private set; } = EnemyState.Idle;
    public bool IsBoss => mode == EnemyMode.Boss;
    public bool IsThreatening => State == EnemyState.Chase;
    public float DetectRadius => mode == EnemyMode.Boss ? 99f : detectRadius;
    public float ContactRadius => 0.32f * transform.localScale.x + 0.3f;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Awake()
    {
        Transform eyeT = transform.Find("Body/Eye");
        if (eyeT != null) eye = eyeT.GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
    }

    /// <summary>Boss: hanya menjaga area sekitar Goal (radius tertentu), tidak seluruh room.</summary>
    public void SetLeash(Vector3 center, float radius)
    {
        leashed = true;
        leashCenter = center;
        leashRadius = radius;
    }

    public bool IsLeashed => leashed;
    public Vector3 LeashCenter => leashCenter;
    public float LeashRadius => leashRadius;

    public void Configure(EnemyMode newMode, Rect roomArea, DungeonGenerator generator, System.Random random)
    {
        mode = newMode;
        area = roomArea;
        gen = generator;
        rng = random ?? new System.Random(GetInstanceID());
        home = transform.position;
        waypoints.Clear();

        switch (mode)
        {
            case EnemyMode.Patrol:
                float inset = Mathf.Min(1f, Mathf.Min(area.width, area.height) * 0.25f);
                Rect r = Rect.MinMaxRect(area.xMin + inset, area.yMin + inset, area.xMax - inset, area.yMax - inset);
                waypoints.Add(new Vector3(r.xMin, home.y, r.yMin));
                waypoints.Add(new Vector3(r.xMax, home.y, r.yMin));
                waypoints.Add(new Vector3(r.xMax, home.y, r.yMax));
                waypoints.Add(new Vector3(r.xMin, home.y, r.yMax));
                // mulai dari sudut terdekat, arah putar acak
                waypointIndex = Nearest(waypoints, home);
                if (rng.Next(2) == 0) waypoints.Reverse();
                waypointIndex = Nearest(waypoints, home);
                State = EnemyState.Patrol;
                break;

            case EnemyMode.Wander:
                State = EnemyState.Wander;
                PickWanderTarget();
                break;

            case EnemyMode.Guard:
            case EnemyMode.Boss:
                State = EnemyState.Watch;
                BuildRing();
                break;

            default:
                State = EnemyState.Idle;
                break;
        }
    }

    // ------------------------------------------------------------------
    private void Update()
    {
        float dt = Time.deltaTime;

        if (!DungeonGameManager.IsLive || gen == null)
        {
            if (mode == EnemyMode.Idle || gen == null)
                transform.Rotate(Vector3.up, idleRotateSpeed * dt);
            UpdateVisuals();
            return;
        }

        if (player == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) player = go.transform;
        }

        if (recoilTimer > 0f)
        {
            recoilTimer -= dt;
            UpdateVisuals();
            return;
        }

        switch (State)
        {
            case EnemyState.Idle:
                transform.Rotate(Vector3.up, idleRotateSpeed * dt);
                break;

            case EnemyState.Patrol:
                if (MoveTowards(waypoints[waypointIndex], patrolSpeed, dt))
                    waypointIndex = (waypointIndex + 1) % waypoints.Count;
                break;

            case EnemyState.Wander:
                wanderTimer -= dt;
                if (MoveTowards(wanderTarget, wanderSpeed, dt) || wanderTimer <= 0f) PickWanderTarget();
                break;

            case EnemyState.Watch:
                transform.Rotate(Vector3.up, 40f * Mathf.Sin(Time.time * 0.8f + home.x) * dt);
                if (PlayerDetected()) StartChase();
                break;

            case EnemyState.Chase:
                if (player == null) { State = EnemyState.Return; break; }
                MoveTowards(player.position, mode == EnemyMode.Boss ? bossSpeed : chaseSpeed, dt);

                bool lost = !PlayerInRoom() ||
                            (mode != EnemyMode.Boss && Flat(player.position - transform.position).magnitude > loseRadius) ||
                            (mode == EnemyMode.Boss && leashed && Flat(player.position - leashCenter).magnitude > leashRadius + 1.5f);
                loseTimer = lost ? loseTimer + dt : 0f;
                if (loseTimer > 1.2f) State = EnemyState.Return;
                break;

            case EnemyState.Return:
                if (PlayerDetected()) { StartChase(); break; }
                if (MoveTowards(home, returnSpeed, dt)) State = EnemyState.Watch;
                break;
        }

        CheckContact();
        UpdateVisuals();
    }

    private void StartChase()
    {
        State = EnemyState.Chase;
        loseTimer = 0f;
        alertFlash = 1f;
        Fx.Ring(new Vector3(transform.position.x, 0.08f, transform.position.z), new Color(1f, 0.25f, 0.2f), mode == EnemyMode.Boss ? 3f : 1.8f, 0.45f);
        AudioManager.Play(Sfx.Dash, 0.35f, mode == EnemyMode.Boss ? 0.6f : 1.4f);
    }

    private bool PlayerInRoom()
    {
        if (player == null) return false;
        Vector3 p = player.position;
        return p.x >= area.xMin - 0.5f && p.x <= area.xMax + 0.5f && p.z >= area.yMin - 0.5f && p.z <= area.yMax + 0.5f;
    }

    private bool PlayerDetected()
    {
        if (player == null || !PlayerInRoom()) return false;
        if (mode == EnemyMode.Boss)
            return !leashed || Flat(player.position - leashCenter).magnitude <= leashRadius;
        return Flat(player.position - transform.position).magnitude <= detectRadius;
    }

    /// <summary>Bergerak ke target (dibatasi rectangle room). True bila sudah sampai.</summary>
    private bool MoveTowards(Vector3 target, float speed, float dt)
    {
        target.x = Mathf.Clamp(target.x, area.xMin, area.xMax);
        target.z = Mathf.Clamp(target.z, area.yMin, area.yMax);

        if (leashed)
        {
            Vector3 off = Flat(target - leashCenter);
            if (off.magnitude > leashRadius) target = leashCenter + off.normalized * leashRadius + Vector3.up * (target.y - leashCenter.y);
            target.y = transform.position.y;
        }

        Vector3 delta = Flat(target - transform.position);
        float dist = delta.magnitude;
        if (dist < 0.05f) return true;

        Vector3 step = delta / dist * Mathf.Min(dist, speed * dt);
        Vector3 next = transform.position + step;
        next.x = Mathf.Clamp(next.x, area.xMin, area.xMax);
        next.z = Mathf.Clamp(next.z, area.yMin, area.yMax);
        transform.position = next;

        Quaternion look = Quaternion.LookRotation(delta);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * dt);
        return dist <= speed * dt;
    }

    private void PickWanderTarget()
    {
        wanderTarget = new Vector3(
            Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()),
            home.y,
            Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
        wanderTimer = 2.5f + (float)rng.NextDouble() * 2f;
    }

    private void CheckContact()
    {
        if (player == null || DungeonGameManager.Instance == null) return;
        if (Flat(player.position - transform.position).magnitude < ContactRadius &&
            DungeonGameManager.Instance.PlayerHit(transform.position))
        {
            // recoil: setelah menyerang, Enemy mundur & diam sejenak (memberi pemain kesempatan kabur)
            recoilTimer = mode == EnemyMode.Boss ? 1.2f : 1.6f;
            Vector3 away = Flat(transform.position - player.position).normalized * 0.8f;
            Vector3 p = transform.position + away;
            p.x = Mathf.Clamp(p.x, area.xMin, area.xMax);
            p.z = Mathf.Clamp(p.z, area.yMin, area.yMax);
            transform.position = p;
            if (State == EnemyState.Chase) State = mode == EnemyMode.Boss ? EnemyState.Chase : EnemyState.Return;
        }
    }

    // ------------------------------------------------------------------
    // Visual: warna mata per state + cincin radius deteksi
    // ------------------------------------------------------------------
    private Color StateColor()
    {
        switch (State)
        {
            case EnemyState.Patrol: return new Color(1f, 0.55f, 0.15f);
            case EnemyState.Wander: return new Color(1f, 0.35f, 0.25f);
            case EnemyState.Watch: return new Color(1f, 0.85f, 0.25f);
            case EnemyState.Chase: return new Color(1f, 0.1f, 0.1f);
            case EnemyState.Return: return new Color(0.9f, 0.75f, 0.3f);
            default: return new Color(1f, 0.25f, 0.15f);
        }
    }

    private void UpdateVisuals()
    {
        alertFlash = Mathf.MoveTowards(alertFlash, 0f, Time.deltaTime * 2f);
        Color c = StateColor();
        float intensity = State == EnemyState.Chase ? 5f + 2f * Mathf.Sin(Time.time * 18f) : 3.5f;

        if (eye != null)
        {
            eye.GetPropertyBlock(block);
            block.SetColor(EmissionId, c * (intensity + alertFlash * 6f));
            eye.SetPropertyBlock(block);
        }

        if (ring != null)
        {
            bool show = mode == EnemyMode.Guard && (State == EnemyState.Watch || State == EnemyState.Chase || State == EnemyState.Return);
            ring.enabled = show && eye != null && eye.enabled;
            Color rc = State == EnemyState.Chase ? new Color(1f, 0.15f, 0.15f, 0.7f) : new Color(1f, 0.8f, 0.3f, 0.35f);
            ring.startColor = ring.endColor = rc;
        }
    }

    private void BuildRing()
    {
        if (ring != null || mode != EnemyMode.Guard) return;

        GameObject go = new GameObject("DetectRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.sharedMaterial = Resources.Load<Material>("Fx/FxLine");
        ring.startWidth = ring.endWidth = 0.06f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        const int segments = 48;
        float r = detectRadius / transform.localScale.x;
        float y = -0.93f / transform.localScale.x * transform.localScale.x;
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private static int Nearest(List<Vector3> points, Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float d = Flat(points[i] - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
