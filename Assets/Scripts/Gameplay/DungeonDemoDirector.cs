using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visualisasi & demo Praktikum 10.
/// 1) Reveal: menganimasikan pipeline PCG tahap demi tahap
///    (Seed & Grid → BSP Split → Rooms → Corridors → Walls → Start/Goal + BFS → Spawn).
/// 2) Autopilot: Player berjalan sendiri dengan BFS — mengambil Item terdekat,
///    menghindari Enemy, lalu menuju Goal.
/// State dibaca oleh DungeonHUD; input global ditangani DungeonGameManager.
/// </summary>
[RequireComponent(typeof(DungeonGenerator))]
public class DungeonDemoDirector : MonoBehaviour
{
    [Header("Demo")]
    [SerializeField] private bool revealSequence = true;
    [SerializeField] private bool autopilot = false;
    [SerializeField] private bool followCamera = false;
    [SerializeField] private bool showOverlay = false;
    [Tooltip("Pengali kecepatan animasi reveal (lebih besar = lebih cepat).")]
    [SerializeField] private float revealSpeed = 1f;
    [SerializeField] private Material lineMaterial;

    [Header("Autopilot")]
    [SerializeField] private float waypointTolerance = 0.1f;
    [SerializeField] private float stuckTime = 2f;

    public static readonly string[] PhaseNames =
    {
        "SEED & GRID",
        "BSP SPLIT",
        "ROOMS",
        "CORRIDORS",
        "WALLS",
        "GOAL + BFS",
        "ROOM ROLES",
        "SPAWN",
        "PLAY"
    };

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private static readonly Color LeafColor = new Color(0.3f, 0.85f, 1f);
    private static readonly Color RoomColor = new Color(1f, 0.85f, 0.3f);

    private DungeonGenerator generator;
    private Coroutine sequence;
    private Transform overlayRoot;
    private Transform persistentRoot;
    private bool skipRequested;

    // Autopilot state
    private bool autopilotRunning;
    private SimplePlayerController player;
    private List<Vector2Int> path;
    private int pathIndex;
    private ItemPickup targetItem;
    private bool targetIsGoal;
    private LineRenderer pathLine;
    private float stuckTimer;
    private float replanTimer;
    private float noPathTimer;
    private Vector3 lastPlayerPosition;
    private int stuckCount;
    private readonly HashSet<Vector2Int> extraBlocked = new HashSet<Vector2Int>();

    // ---- State untuk HUD ----
    public int Phase { get; private set; } = -1;
    public string PhaseDetail { get; private set; } = "";
    public string AutopilotStatus { get; private set; } = "";
    public bool IsRevealing { get; private set; }
    public bool AutopilotRunning => autopilotRunning;

    /// <summary>False bila autopilot sedang mengincar Item (Goal tidak dihitung agar demo mengumpulkan semua Item).</summary>
    public bool WantsGoal => !autopilotRunning || targetIsGoal;

    public bool RevealEnabled
    {
        get => revealSequence;
        set => revealSequence = value;
    }

    public bool AutopilotEnabled
    {
        get => autopilot;
        set => autopilot = value;
    }

    public bool FollowCamera
    {
        get => followCamera;
        set
        {
            followCamera = value;
            if (!followCamera && generator != null) generator.FrameCamera();
        }
    }

    public bool ShowOverlay
    {
        get => showOverlay;
        set
        {
            showOverlay = value;
            RefreshPersistentOverlay();
        }
    }

    // ------------------------------------------------------------------
    private void Awake()
    {
        generator = GetComponent<DungeonGenerator>();
        generator.Generated += OnGenerated;

        if (lineMaterial == null)
            lineMaterial = Resources.Load<Material>("Fx/FxLine");
    }

    private void OnDestroy()
    {
        if (generator != null)
            generator.Generated -= OnGenerated;
    }

    private void OnGenerated()
    {
        if (sequence != null)
            StopCoroutine(sequence);

        StopAutopilot();
        player = null;
        ClearOverlay();
        ClearPersistentOverlay();
        stuckCount = 0;
        extraBlocked.Clear();
        skipRequested = false;

        if (revealSequence)
        {
            IsRevealing = true; // set segera agar Player langsung terkunci
            sequence = StartCoroutine(RevealSequence());
        }
        else
        {
            InstantReveal();
        }
    }

    public void SkipReveal()
    {
        skipRequested = true;
    }

    private void Update()
    {
        DungeonGameManager manager = DungeonGameManager.Instance;
        bool canPlay = !IsRevealing && (manager == null || manager.State == DungeonState.Playing);
        bool shouldRun = autopilot && canPlay;

        if (shouldRun && !autopilotRunning) StartAutopilot();
        if (!shouldRun && autopilotRunning) StopAutopilot();

        if (autopilotRunning)
            TickAutopilot();
        else
            AutopilotStatus = "";
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (!followCamera || cam == null)
            return;

        if (player == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) player = go.GetComponent<SimplePlayerController>();
            if (player == null) return;
        }

        Vector3 target = player.transform.position + new Vector3(0f, 13f, -8.5f);
        float k = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, k);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation,
            Quaternion.LookRotation(player.transform.position - target), k);
    }

    // ==================================================================
    // 1) REVEAL SEQUENCE
    // ==================================================================
    private IEnumerator Wait(float seconds)
    {
        float t = seconds / Mathf.Max(0.1f, revealSpeed);
        while (t > 0f && !skipRequested)
        {
            t -= Time.deltaTime;
            yield return null;
        }
    }

    private void SetPhase(int index, string detail)
    {
        Phase = index;
        PhaseDetail = detail;
    }

    private void InstantReveal()
    {
        IsRevealing = false;
        DungeonMetrics m = generator.Metrics;
        SetPhase(8, "Dungeon siap · " + m.roomCount + " room · Start→Goal " + m.startGoalPath +
                    " langkah. (Animasi pipeline OFF — tekan V)");
        RefreshPersistentOverlay();
    }

    private IEnumerator RevealSequence()
    {
        IsRevealing = true;
        yield return null; // tunggu 1 frame agar level lama benar-benar terhapus

        DungeonMetrics m = generator.Metrics;

        var floorTiles = CollectByGrid(generator.FloorsParent);
        var wallTiles = CollectByGrid(generator.WallsParent);
        List<GameObject> enemies = Children(generator.EnemiesParent);
        List<GameObject> items = Children(generator.ItemsParent);
        GameObject goal = FindChildWith<GoalTrigger>();
        GameObject startMarker = FindChildNamed("StartPrefab");
        GameObject playerGo = GameObject.FindWithTag("Player");
        RoomChallengeSystem rc = generator.Challenges;
        List<GameObject> challengeObjects = rc != null && rc.Active && rc.ChallengeRoot != null
            ? Children(rc.ChallengeRoot) : new List<GameObject>();
        foreach (GameObject go in challengeObjects) SetVisible(go, false);

        foreach (var list in floorTiles.Values) SetVisible(list, false);
        foreach (var list in wallTiles.Values) SetVisible(list, false);
        foreach (GameObject go in enemies) SetVisible(go, false);
        foreach (GameObject go in items) SetVisible(go, false);
        SetVisible(goal, false);
        SetVisible(startMarker, false);
        SetVisible(playerGo, false);

        // 1. Seed & Grid
        SetPhase(0, "Seed <b>" + m.seed + "</b> → Random.InitState(seed). Grid " + generator.MapWidth + "×" +
                    generator.MapHeight + " = " + (generator.MapWidth * generator.MapHeight) + " cell, semua diisi Wall (Wall-First).");
        DrawRect(null, 0, 0, generator.MapWidth, generator.MapHeight, new Color(0.7f, 0.8f, 1f, 0.8f), 0.2f, 1.2f);
        AudioManager.Play(Sfx.Unlock, 0.4f, 0.8f);
        yield return Wait(1.6f);

        // 2. BSP Split — tampilkan node per kedalaman
        List<List<BSPNode>> levels = NodesByDepth(generator.RootNode);
        for (int d = 1; d < levels.Count; d++)
        {
            Color c = Color.Lerp(LeafColor, new Color(0.45f, 0.45f, 1f), d / (float)Mathf.Max(1, levels.Count - 1));
            foreach (BSPNode node in levels[d])
                DrawRect(null, node.x, node.y, node.width, node.height, c, Mathf.Max(0.07f, 0.17f - d * 0.025f), 1.25f + d * 0.01f);

            int leaves = 0;
            foreach (BSPNode node in levels[d]) if (node.IsLeaf) leaves++;
            SetPhase(1, "Kedalaman " + d + ": " + levels[d].Count + " area, " + leaves +
                        " menjadi <b>Leaf</b>. Split rekursif berhenti saat area ≤ maxLeafSize (" + generator.MaxLeafSize + ").");
            if (!skipRequested) AudioManager.Play(Sfx.Spawn, 0.35f, 0.7f + d * 0.15f);
            yield return Wait(1f);
        }
        SetPhase(1, "BSP selesai: <b>" + m.leafCount + " Leaf</b> — setiap Leaf dapat berisi satu Room.");
        yield return Wait(0.9f);

        // 3. Rooms
        IReadOnlyList<DungeonRoom> rooms = generator.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            DungeonRoom r = rooms[i];
            DrawRect(null, r.x, r.y, r.width, r.height, RoomColor, 0.13f, 1.35f);

            for (int x = r.x; x < r.x + r.width; x++)
                for (int y = r.y; y < r.y + r.height; y++)
                    if (floorTiles.TryGetValue(new Vector2Int(x, y), out var list)) SetVisible(list, true);

            if (!skipRequested)
            {
                Fx.Ring(ToWorld(r.Center, 0.1f), RoomColor, Mathf.Max(r.width, r.height) * 0.6f, 0.6f);
                AudioManager.Play(Sfx.Spawn, 0.3f, 0.9f + i * 0.08f);
            }

            SetPhase(2, "Room " + (i + 1) + "/" + rooms.Count + ": " + r.width + "×" + r.height +
                        " — ukuran & posisi acak di dalam Leaf, roomMargin " + generator.RoomMargin + ".");
            yield return Wait(0.4f);
        }
        yield return Wait(0.5f);

        // 4. Corridors (L-shaped, room i → room i+1)
        for (int i = 0; i < rooms.Count - 1; i++)
        {
            Vector2Int a = rooms[i].Center;
            Vector2Int b = rooms[i + 1].Center;
            DrawLine(null, new[] { ToWorld(a, 1.4f), ToWorld(b, 1.4f) }, new Color(1f, 1f, 1f, 0.9f), 0.09f);

            int w = generator.CorridorWidth;
            for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x) + w; x++)
                for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y) + w; y++)
                    if (generator.IsCorridorTile(x, y) && floorTiles.TryGetValue(new Vector2Int(x, y), out var list))
                        SetVisible(list, true);

            if (!skipRequested) AudioManager.Play(Sfx.Dash, 0.25f, 1.2f + i * 0.05f);
            SetPhase(3, "Corridor L: Room " + (i + 1) + " → Room " + (i + 2) +
                        " (urutan belokan acak). Room dirantai berurutan → semua terhubung.");
            yield return Wait(0.4f);
        }
        foreach (var list in floorTiles.Values) SetVisible(list, true);
        SetPhase(3, "Corridor selesai: <b>" + m.corridorTiles + "</b> tile corridor, total <b>" + m.floorTiles + "</b> floor (" +
                    m.floorPercentage.ToString("0.0") + "% map).");
        yield return Wait(0.8f);

        // 5. Walls — sweep kolom kiri → kanan
        SetPhase(4, "Hanya Wall yang bertetangga dengan Floor di-Instantiate: <b>" + m.wallObjects + "</b> object dari " +
                    (generator.MapWidth * generator.MapHeight - m.floorTiles) + " cell Wall (optimasi §69).");
        for (int x = 0; x < generator.MapWidth; x++)
        {
            for (int y = 0; y < generator.MapHeight; y++)
                if (wallTiles.TryGetValue(new Vector2Int(x, y), out var list)) SetVisible(list, true);

            if (x % 3 == 0) yield return Wait(0.035f);
        }
        ClearOverlay();
        yield return Wait(0.5f);

        // 6. Start / Goal + BFS validation
        Vector2Int start = generator.StartPosition;
        Vector2Int goalPos = generator.GoalPosition;
        Pop(startMarker, new Color(0.3f, 1f, 0.5f));
        Pop(goal, UIFactory.Gold);
        SetPhase(5, "Start = center room pertama. Goal = room " + GoalStrategyLabel() + ". Validasi BFS dari Start…");
        yield return Wait(0.8f);

        List<Vector2Int> bfs = FindPathTo(start, p => p == goalPos, new HashSet<Vector2Int>());
        if (bfs != null) yield return AnimatePath(bfs, new Color(0.3f, 1f, 0.45f));
        if (!skipRequested) AudioManager.Play(Sfx.Unlock, 0.7f);
        SetPhase(5, "BFS: Goal <b>" + (bfs != null ? "TERJANGKAU" : "TIDAK terjangkau") + "</b> — " + m.startGoalPath +
                    " langkah. Hard constraint terpenuhi → dungeon <b>VALID</b> (attempt " + m.attempts + ").");
        yield return Wait(1.8f);
        ClearOverlay();

        // 7. Room Roles — lapisan PCG ke-2 (constraint-based)
        if (rc != null && rc.Active)
        {
            SetPhase(6, "Room Role Assignment: Start & Boss (hard rule), Treasure di room samping, " +
                        "sisanya Patrol/Guard/Trap/Sentry sesuai difficulty (jarak BFS).");
            yield return Wait(1.4f);

            for (int i = 0; i < rooms.Count; i++)
            {
                RoomRole role = rc.RoleOf(i);
                Color color = RoomChallengeSystem.RoleColor(role);
                DrawRect(null, rooms[i].x, rooms[i].y, rooms[i].width, rooms[i].height, color, 0.16f, 1.35f);

                foreach (GameObject go in rc.RoomObjects(i)) SetVisible(go, true);
                if (!skipRequested)
                {
                    Fx.Ring(ToWorld(rooms[i].Center, 0.1f), color, Mathf.Max(rooms[i].width, rooms[i].height) * 0.55f, 0.6f);
                    AudioManager.Play(Sfx.Unlock, 0.3f, 0.8f + i * 0.07f);
                }

                SetPhase(6, "Room " + (i + 1) + " → <color=#" + ColorUtility.ToHtmlStringRGB(color) + "><b>" +
                            RoomChallengeSystem.RoleName(role) + "</b></color>  (difficulty " + rc.Difficulty[i].ToString("0.00") +
                            (rc.IsOnMainPath(i) ? ", jalur utama" : ", room samping") + ") — " + RoomChallengeSystem.RoleHint(role));
                yield return Wait(0.85f);
            }
            ClearOverlay();
        }

        // 8. Spawn
        Pop(playerGo, UIFactory.Cyan);
        SetPhase(7, "Player di Start. Enemy: hanya di Floor, ≥ " + generator.MinEnemyDistanceFromStart.ToString("0") +
                    " tile dari Start, bukan safe room, tidak menutup corridor.");
        yield return Wait(0.5f);
        for (int i = 0; i < enemies.Count; i++)
        {
            Pop(enemies[i], UIFactory.Red);
            yield return Wait(0.16f);
        }
        SetPhase(7, "<b>" + enemies.Count + " Enemy</b> (" + m.enemyCount + " dari modul" +
                    (rc != null && rc.Active ? " + " + rc.ExtraEnemies + " penjaga room + Boss" : "") + ") & <b>" + m.itemCount +
                    " Item</b> — perilaku Enemy mengikuti peran room (FSM).");
        for (int i = 0; i < items.Count; i++)
        {
            Pop(items[i], UIFactory.Cyan);
            yield return Wait(0.16f);
        }
        yield return Wait(0.5f);

        // Pastikan semua terlihat (bila di-skip)
        foreach (var list in floorTiles.Values) SetVisible(list, true);
        foreach (var list in wallTiles.Values) SetVisible(list, true);
        foreach (GameObject go in enemies) SetVisible(go, true);
        foreach (GameObject go in items) SetVisible(go, true);
        SetVisible(goal, true);
        SetVisible(startMarker, true);
        SetVisible(playerGo, true);
        foreach (GameObject go in challengeObjects) SetVisible(go, true);
        ClearOverlay();

        SetPhase(8, "Dungeon siap · " + m.roomCount + " room · Start→Goal " + m.startGoalPath +
                    " langkah. Ambil Item, hindari Enemy, capai Goal!");
        IsRevealing = false;
        skipRequested = false;
        RefreshPersistentOverlay();
        sequence = null;
    }

    private string GoalStrategyLabel()
    {
        switch (generator.GoalMode)
        {
            case GoalStrategy.LastRoom: return "terakhir (strategi A)";
            case GoalStrategy.FarthestPath: return "dengan jarak BFS terjauh (strategi C)";
            default: return "terjauh secara Euclidean (strategi B)";
        }
    }

    private void Pop(GameObject go, Color color)
    {
        if (go == null) return;
        SetVisible(go, true);
        if (skipRequested) return;

        Fx.Ring(new Vector3(go.transform.position.x, 0.08f, go.transform.position.z), color, 1.3f, 0.5f);
        AudioManager.Play(Sfx.Spawn, 0.25f, Random.Range(0.9f, 1.4f));
        StartCoroutine(PopScale(go.transform));
    }

    private static IEnumerator PopScale(Transform t)
    {
        Vector3 target = t.localScale;
        float time = 0f;
        const float duration = 0.4f;

        while (time < duration && t != null)
        {
            time += Time.deltaTime;
            float x = Mathf.Clamp01(time / duration) - 1f;
            float ease = 1f + 3.2f * x * x * x + 2.2f * x * x;
            t.localScale = target * ease;
            yield return null;
        }

        if (t != null) t.localScale = target;
    }

    // ==================================================================
    // Overlay BSP permanen (tombol B)
    // ==================================================================
    private void RefreshPersistentOverlay()
    {
        ClearPersistentOverlay();

        if (!showOverlay || IsRevealing || generator == null || generator.Rooms == null)
            return;

        persistentRoot = new GameObject("BSPOverlay").transform;

        foreach (BSPNode leaf in generator.LeafNodes)
            DrawRect(persistentRoot, leaf.x, leaf.y, leaf.width, leaf.height, new Color(LeafColor.r, LeafColor.g, LeafColor.b, 0.55f), 0.07f, 1.25f);

        foreach (DungeonRoom room in generator.Rooms)
            DrawRect(persistentRoot, room.x, room.y, room.width, room.height, new Color(RoomColor.r, RoomColor.g, RoomColor.b, 0.75f), 0.07f, 1.3f);
    }

    private void ClearPersistentOverlay()
    {
        if (persistentRoot != null)
            Destroy(persistentRoot.gameObject);
        persistentRoot = null;
    }

    // ==================================================================
    // 2) AUTOPILOT
    // ==================================================================
    private void StartAutopilot()
    {
        GameObject go = GameObject.FindWithTag("Player");
        if (go == null) return;

        player = go.GetComponent<SimplePlayerController>();
        player.ExternalControl = true;
        player.ExternalInput = Vector2.zero;
        autopilotRunning = true;
        path = null;
        lastPlayerPosition = player.transform.position;
        stuckTimer = 0f;
    }

    private void StopAutopilot()
    {
        autopilotRunning = false;
        path = null;

        if (player != null)
        {
            player.ExternalControl = false;
            player.ExternalInput = Vector2.zero;
        }

        if (pathLine != null)
            Destroy(pathLine.gameObject);
        pathLine = null;
    }

    private void TickAutopilot()
    {
        if (player == null)
        {
            autopilotRunning = false;
            return;
        }

        bool targetLost = !targetIsGoal && targetItem == null;
        replanTimer -= Time.deltaTime;
        if (path == null || pathIndex >= path.Count || targetLost || replanTimer <= 0f)
        {
            replanTimer = 0.3f;
            Plan();
            if (path == null)
            {
                player.ExternalInput = Vector2.zero;   // semua jalan sedang berbahaya → tunggu
                noPathTimer += Time.deltaTime;
                StepOffDanger();
                return;
            }
        }

        Vector3 position = player.transform.position;

        // menghindar bila tile saat ini berada di garis tembak proyektil
        if (InProjectileLine(generator.WorldToGrid(position)))
        {
            StepOffDanger();
            return;
        }
        Vector3 waypoint = generator.GridToWorld(path[pathIndex].x, path[pathIndex].y);
        Vector3 delta = waypoint - position;
        delta.y = 0f;

        if (delta.magnitude < waypointTolerance)
        {
            pathIndex++;
            UpdatePathLine();
            return;
        }

        // Input dibatasi agar langkah frame ini tidak melewati waypoint (stabil pada FPS rendah)
        float step = Mathf.Max(0.0001f, player.MoveSpeed * Time.deltaTime);
        player.ExternalInput = Vector2.ClampMagnitude(new Vector2(delta.x, delta.z) / step, 1f);

        // Deteksi macet
        stuckTimer += Time.deltaTime;
        if (stuckTimer >= stuckTime)
        {
            if ((position - lastPlayerPosition).sqrMagnitude < 0.04f)
            {
                stuckCount++;
                extraBlocked.Add(path[pathIndex]);

                if (stuckCount >= 3)
                {
                    DungeonGameManager manager = DungeonGameManager.Instance;
                    if (manager != null) manager.Regenerate(generator.Seed + 1);
                    else generator.Regenerate();
                    return;
                }

                path = null;
            }

            stuckTimer = 0f;
            lastPlayerPosition = position;
        }
    }

    private void Plan()
    {
        Vector2Int from = generator.WorldToGrid(player.transform.position);

        // Bahaya dinamis: Enemy bergerak (posisi saat ini), spike aktif/akan aktif, turret (solid)
        HashSet<Vector2Int> hazards = new HashSet<Vector2Int>(extraBlocked);
        RoomChallengeSystem rc = generator.Challenges;
        if (rc != null && rc.Active)
        {
            rc.CollectDangerTiles(hazards, 0.55f);
            hazards.UnionWith(rc.SolidTiles);
        }

        // Constraint kuat: jauhi Enemy (radius 1, pengejar/boss radius 2). Lemah: hanya tile Enemy.
        HashSet<Vector2Int> strong = new HashSet<Vector2Int>(hazards);
        HashSet<Vector2Int> weak = new HashSet<Vector2Int>(hazards);
        foreach (EnemyController e in EnemyController.All)
        {
            Vector2Int g = generator.WorldToGrid(e.transform.position);

            // jangan memicu Guard: hindari radius deteksinya selama ia berjaga
            if (e.Mode == EnemyMode.Guard && e.State != EnemyState.Chase)
                for (int dx = -3; dx <= 3; dx++)
                    for (int dy = -3; dy <= 3; dy++)
                        if (dx * dx + dy * dy <= 10) strong.Add(g + new Vector2Int(dx, dy));

            int r = e.IsBoss || e.IsThreatening ? 2 : 1;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    strong.Add(g + new Vector2Int(dx, dy));
            weak.Add(g);
            if (e.IsBoss) foreach (Vector2Int d in Directions) weak.Add(g + d);
        }
        strong.Remove(from);
        weak.Remove(from);

        // dikejar dari dekat → jangan menunggu, langsung lari ke target
        bool threatNear = false;
        foreach (EnemyController e in EnemyController.All)
        {
            Vector3 d = e.transform.position - player.transform.position;
            d.y = 0f;
            if ((e.IsThreatening || e.IsBoss) && d.magnitude < 3.2f) threatNear = true;
        }
        if (threatNear) noPathTimer = Mathf.Max(noPathTimer, 1.3f);

        // Tier "nekat": abaikan Enemy, tetapi tetap hindari spike yang aktif SEKARANG dan turret
        HashSet<Vector2Int> mustAvoid = new HashSet<Vector2Int>(extraBlocked);
        if (rc != null && rc.Active)
        {
            rc.CollectDangerTiles(mustAvoid, 0.15f);
            mustAvoid.UnionWith(rc.SolidTiles);
        }
        mustAvoid.Remove(from);

        // saat mengejar Item, tile Goal diblokir agar level tidak selesai tanpa sengaja
        HashSet<Vector2Int> strongItem = new HashSet<Vector2Int>(strong) { generator.GoalPosition };
        HashSet<Vector2Int> weakItem = new HashSet<Vector2Int>(weak) { generator.GoalPosition };
        HashSet<Vector2Int> openItem = new HashSet<Vector2Int>(mustAvoid) { generator.GoalPosition };
        HashSet<Vector2Int> reachItem = new HashSet<Vector2Int> { generator.GoalPosition };

        // Target: Item terdekat yang tersisa, lalu Goal
        Dictionary<Vector2Int, ItemPickup> items = new Dictionary<Vector2Int, ItemPickup>();
        foreach (ItemPickup item in FindObjectsByType<ItemPickup>(FindObjectsSortMode.None))
        {
            // lewati Item yang sedang dijaga Boss dari dekat (risiko > hadiah)
            bool guardedByBoss = false;
            foreach (EnemyController e in EnemyController.All)
            {
                if (!e.IsBoss) continue;
                Vector3 c = e.IsLeashed ? e.LeashCenter : e.transform.position;
                Vector3 d = c - item.transform.position;
                d.y = 0f;
                if (d.magnitude < (e.IsLeashed ? e.LeashRadius + 0.5f : 4.5f)) guardedByBoss = true;
            }

            if (!guardedByBoss)
                items[generator.WorldToGrid(item.transform.position)] = item;
        }

        targetIsGoal = false;
        targetItem = null;
        path = null;

        if (items.Count > 0)
        {
            path = FindPathTo(from, p => items.ContainsKey(p), strongItem)
                ?? FindPathTo(from, p => items.ContainsKey(p), weakItem);

            if (path == null && noPathTimer > 1.2f)
                path = FindPathTo(from, p => items.ContainsKey(p), openItem);

            if (path != null)
            {
                targetItem = items[path[path.Count - 1]];
                AutopilotStatus = (targetItem.IsTreasure ? "BFS → TREASURE" : "BFS → Item terdekat (" + items.Count + " tersisa)") +
                                  " · " + (path.Count - 1) + " langkah";
                noPathTimer = 0f;
            }
            else if (FindPathTo(from, p => items.ContainsKey(p), reachItem) != null)
            {
                // Item dapat dicapai, tetapi jalurnya sedang berbahaya (spike/enemy) → tunggu celah
                AutopilotStatus = "Menunggu celah aman…";
                stuckTimer = 0f;
                lastPlayerPosition = player.transform.position;
                return;
            }
        }

        if (path == null)
        {
            Vector2Int goal = generator.GoalPosition;
            path = FindPathTo(from, p => p == goal, strong)
                ?? FindPathTo(from, p => p == goal, weak);

            // terlalu lama menunggu → nekat (abaikan bahaya)
            if (path == null && noPathTimer > 1.2f)
                path = FindPathTo(from, p => p == goal, mustAvoid);

            targetIsGoal = true;
            AutopilotStatus = path != null
                ? (items.Count > 0 ? "Item tidak aman → " : "Semua Item diambil → ") + "BFS ke GOAL · " + (path.Count - 1) + " langkah"
                : "Menunggu celah aman…";
        }

        if (path != null) noPathTimer = 0f;

        pathIndex = path != null && path.Count > 1 ? 1 : 0;
        stuckTimer = 0f;
        lastPlayerPosition = player.transform.position;
        UpdatePathLine();
    }

    private static bool InProjectileLine(Vector2Int tile)
    {
        if (Projectile.All.Count == 0) return false;
        HashSet<Vector2Int> line = new HashSet<Vector2Int>();
        foreach (Projectile p in Projectile.All)
            if (p != null) p.CollectPathTiles(line, 0.8f);
        return line.Contains(tile);
    }

    /// <summary>Bila sedang berdiri di tile berbahaya saat menunggu, geser ke tile aman terdekat.</summary>
    private void StepOffDanger()
    {
        RoomChallengeSystem rc = generator.Challenges;
        if (rc == null || !rc.Active) return;

        HashSet<Vector2Int> danger = new HashSet<Vector2Int>(rc.SolidTiles);
        rc.CollectDangerTiles(danger, 0.6f);
        foreach (EnemyController e in EnemyController.All)
            danger.Add(generator.WorldToGrid(e.transform.position));

        Vector2Int here = generator.WorldToGrid(player.transform.position);
        if (!danger.Contains(here)) return;

        List<Vector2Int> escape = FindPathTo(here, p => !danger.Contains(p), new HashSet<Vector2Int>(rc.SolidTiles));
        if (escape == null || escape.Count < 2) return;

        Vector3 target = generator.GridToWorld(escape[1].x, escape[1].y);
        Vector3 delta = target - player.transform.position;
        delta.y = 0f;
        player.ExternalInput = Vector2.ClampMagnitude(new Vector2(delta.x, delta.z) / Mathf.Max(0.0001f, player.MoveSpeed * Time.deltaTime), 1f);
    }

    /// <summary>BFS 4 arah pada grid; berhenti pada tile pertama yang memenuhi isTarget.</summary>
    private List<Vector2Int> FindPathTo(Vector2Int from, System.Predicate<Vector2Int> isTarget, HashSet<Vector2Int> blocked)
    {
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        parent[from] = from;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (isTarget(current))
            {
                List<Vector2Int> result = new List<Vector2Int>();
                for (Vector2Int p = current; p != from; p = parent[p]) result.Add(p);
                result.Add(from);
                result.Reverse();
                return result;
            }

            foreach (Vector2Int dir in Directions)
            {
                Vector2Int next = current + dir;
                if (parent.ContainsKey(next) || blocked.Contains(next)) continue;
                if (generator.GetTile(next.x, next.y) == TileType.Wall) continue;
                parent[next] = current;
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private void UpdatePathLine()
    {
        if (path == null) return;

        Color c = targetIsGoal ? new Color(1f, 0.8f, 0.25f, 0.9f) : new Color(0.3f, 0.95f, 1f, 0.9f);

        if (pathLine == null)
            pathLine = CreateLine("AutopilotPath", c, 0.12f, null);

        pathLine.startColor = pathLine.endColor = c;

        int start = Mathf.Max(0, pathIndex - 1);
        pathLine.positionCount = path.Count - start;
        for (int i = start; i < path.Count; i++)
            pathLine.SetPosition(i - start, ToWorld(path[i], 0.2f));
    }

    // ==================================================================
    // Overlay helpers
    // ==================================================================
    private Vector3 ToWorld(Vector2Int p, float height)
    {
        return generator.GridToWorld(p.x, p.y) + Vector3.up * height;
    }

    private void ClearOverlay()
    {
        if (overlayRoot != null)
            Destroy(overlayRoot.gameObject);
        overlayRoot = null;
    }

    private Transform EnsureOverlayRoot()
    {
        if (overlayRoot == null)
            overlayRoot = new GameObject("DemoOverlay").transform;
        return overlayRoot;
    }

    private LineRenderer CreateLine(string lineName, Color color, float width, Transform parent)
    {
        GameObject go = new GameObject(lineName);
        if (parent != null) go.transform.SetParent(parent, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = width;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private void DrawRect(Transform parent, int x, int y, int w, int h, Color color, float width, float height)
    {
        if (parent == null) parent = EnsureOverlayRoot();

        float ts = generator.TileSize;
        float x0 = (x - 0.5f) * ts, x1 = (x + w - 0.5f) * ts;
        float z0 = (y - 0.5f) * ts, z1 = (y + h - 0.5f) * ts;

        LineRenderer lr = CreateLine("Rect", color, Mathf.Max(0.05f, width), parent);
        lr.loop = true;
        lr.positionCount = 4;
        lr.SetPositions(new[]
        {
            new Vector3(x0, height, z0), new Vector3(x1, height, z0),
            new Vector3(x1, height, z1), new Vector3(x0, height, z1)
        });
    }

    private void DrawLine(Transform parent, Vector3[] points, Color color, float width)
    {
        if (parent == null) parent = EnsureOverlayRoot();
        LineRenderer lr = CreateLine("Line", color, width, parent);
        lr.positionCount = points.Length;
        lr.SetPositions(points);
    }

    private IEnumerator AnimatePath(List<Vector2Int> cells, Color color)
    {
        LineRenderer lr = CreateLine("BFSPath", color, 0.2f, EnsureOverlayRoot());
        int step = Mathf.Max(1, cells.Count / 45);
        for (int i = 0; i < cells.Count && !skipRequested; i += step)
        {
            lr.positionCount = i + 1;
            for (int k = 0; k <= i; k++) lr.SetPosition(k, ToWorld(cells[k], 1.3f));
            yield return Wait(0.025f);
        }
        lr.positionCount = cells.Count;
        for (int k = 0; k < cells.Count; k++) lr.SetPosition(k, ToWorld(cells[k], 1.3f));
    }

    private static List<List<BSPNode>> NodesByDepth(BSPNode root)
    {
        List<List<BSPNode>> result = new List<List<BSPNode>>();
        if (root == null) return result;

        List<BSPNode> current = new List<BSPNode> { root };
        while (current.Count > 0)
        {
            result.Add(current);
            List<BSPNode> next = new List<BSPNode>();
            foreach (BSPNode n in current)
            {
                if (n.left != null) next.Add(n.left);
                if (n.right != null) next.Add(n.right);
            }
            current = next;
        }
        return result;
    }

    private GameObject FindChildWith<T>() where T : Component
    {
        if (generator.LevelRoot == null) return null;
        T c = generator.LevelRoot.GetComponentInChildren<T>(true);
        return c != null ? c.gameObject : null;
    }

    private GameObject FindChildNamed(string prefix)
    {
        if (generator.LevelRoot == null) return null;
        foreach (Transform child in generator.LevelRoot)
            if (child.name.StartsWith(prefix)) return child.gameObject;
        return null;
    }

    private Dictionary<Vector2Int, List<Renderer>> CollectByGrid(Transform parent)
    {
        var map = new Dictionary<Vector2Int, List<Renderer>>();
        if (parent == null) return map;

        foreach (Transform child in parent)
        {
            Vector2Int g = generator.WorldToGrid(child.position);
            if (!map.TryGetValue(g, out var list)) map[g] = list = new List<Renderer>();
            list.AddRange(child.GetComponentsInChildren<Renderer>(true));
        }
        return map;
    }

    private static List<GameObject> Children(Transform parent)
    {
        List<GameObject> list = new List<GameObject>();
        if (parent == null) return list;
        foreach (Transform child in parent) list.Add(child.gameObject);
        return list;
    }

    private static void SetVisible(List<Renderer> renderers, bool visible)
    {
        foreach (Renderer r in renderers) if (r != null) r.enabled = visible;
    }

    private static void SetVisible(GameObject go, bool visible)
    {
        if (go == null) return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = visible;
    }
}
