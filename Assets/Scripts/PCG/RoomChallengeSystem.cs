using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Peran (tantangan) sebuah room hasil BSP.</summary>
public enum RoomRole
{
    Start,
    Patrol,
    Guard,
    Trap,
    Sentry,
    Treasure,
    Boss
}

/// <summary>
/// Lapisan PCG kedua setelah layout BSP: <b>Room Role Assignment</b> (constraint-based).
///
///  Hard rule : room pertama = Start (safe room), room Goal = Boss (§116),
///              Treasure = room samping di luar jalur utama Start→Goal (§117).
///  Rule      : room lain mendapat tantangan Patrol / Guard / Trap / Sentry, diurutkan
///              berdasarkan difficulty (jarak BFS dari Start) sehingga kesulitan naik bertahap
///              dan setiap tipe muncul bila jumlah room mencukupi.
///
/// Memakai System.Random(seed) terpisah sehingga urutan UnityEngine.Random milik modul
/// (layout, enemy, item) tidak berubah — hasil eksperimen tetap reproducible.
/// </summary>
public class RoomChallengeSystem : MonoBehaviour
{
    [SerializeField] private bool challengesEnabled = true;

    [Header("Prefabs")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject spikePrefab;
    [SerializeField] private GameObject turretPrefab;
    [SerializeField] private GameObject chestPrefab;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material bossMaterial;

    [Header("Boss")]
    [SerializeField] private float bossLeashRadius = 4.5f;

    [Header("Trap")]
    [SerializeField] private float spikeDown = 1.7f;
    [SerializeField] private float spikeWarning = 0.7f;
    [SerializeField] private float spikeUp = 1.1f;
    [SerializeField] private float spikeWaveDelay = 0.38f;

    private readonly List<RoomRole> roles = new List<RoomRole>();
    private readonly List<float> difficulty = new List<float>();
    private readonly HashSet<int> mainPath = new HashSet<int>();
    private readonly List<List<GameObject>> roomObjects = new List<List<GameObject>>();
    private readonly List<SpikeTrap> spikes = new List<SpikeTrap>();
    private readonly HashSet<Vector2Int> solidTiles = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, GameObject> solidObjects = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<RoomRole, Material> floorMaterials = new Dictionary<RoomRole, Material>();

    private DungeonGenerator gen;
    private Transform root;

    public bool ChallengesEnabled
    {
        get => challengesEnabled;
        set => challengesEnabled = value;
    }

    public IReadOnlyList<RoomRole> Roles => roles;
    public IReadOnlyList<float> Difficulty => difficulty;
    public bool IsOnMainPath(int room) => mainPath.Contains(room);
    public int MainPathCount => mainPath.Count;
    public int TreasureRoom { get; private set; } = -1;
    public int BossRoom { get; private set; } = -1;
    public int ExtraEnemies { get; private set; }
    public int SpikeCount => spikes.Count;
    public int TurretCount { get; private set; }
    public bool HasTreasure => challengesEnabled && TreasureRoom >= 0;
    public bool Active => challengesEnabled && roles.Count > 0;
    public Transform ChallengeRoot => root;
    public IReadOnlyCollection<Vector2Int> SolidTiles => solidTiles;
    public Vector2Int? ChestTile { get; private set; }

    /// <summary>Repair: hapus objek solid (turret) yang menghalangi jalur wajib.</summary>
    public bool RemoveSolid(Vector2Int tile)
    {
        if (!solidTiles.Remove(tile)) return false;
        if (solidObjects.TryGetValue(tile, out GameObject go) && go != null)
        {
            foreach (List<GameObject> list in roomObjects) list.Remove(go);
            Destroy(go);
        }
        solidObjects.Remove(tile);
        TurretCount = Mathf.Max(0, TurretCount - 1);
        return true;
    }

    // ------------------------------------------------------------------
    // Info tampilan
    // ------------------------------------------------------------------
    public static Color RoleColor(RoomRole role)
    {
        switch (role)
        {
            case RoomRole.Start: return new Color(0.3f, 1f, 0.5f);
            case RoomRole.Patrol: return new Color(0.3f, 0.8f, 1f);
            case RoomRole.Guard: return new Color(1f, 0.6f, 0.2f);
            case RoomRole.Trap: return new Color(1f, 0.3f, 0.45f);
            case RoomRole.Sentry: return new Color(0.7f, 0.4f, 1f);
            case RoomRole.Treasure: return new Color(1f, 0.85f, 0.25f);
            default: return new Color(1f, 0.15f, 0.2f);
        }
    }

    public static string RoleName(RoomRole role)
    {
        switch (role)
        {
            case RoomRole.Start: return "START ROOM";
            case RoomRole.Patrol: return "PATROL ROOM";
            case RoomRole.Guard: return "GUARD ROOM";
            case RoomRole.Trap: return "TRAP ROOM";
            case RoomRole.Sentry: return "SENTRY ROOM";
            case RoomRole.Treasure: return "TREASURE ROOM";
            default: return "BOSS ROOM";
        }
    }

    public static string RoleHint(RoomRole role)
    {
        switch (role)
        {
            case RoomRole.Start: return "Safe room — tidak ada Enemy";
            case RoomRole.Patrol: return "Enemy berpatroli mengelilingi room — atur timing";
            case RoomRole.Guard: return "Penjaga mengejar bila kamu masuk radius deteksi";
            case RoomRole.Trap: return "Spike naik bergelombang — tunggu saat turun";
            case RoomRole.Sentry: return "Turret menembak bila melihatmu — terus bergerak";
            case RoomRole.Treasure: return "Peti harta (+300) di room samping, dijaga";
            default: return "Boss menjaga area GOAL — pancing keluar, lalu sentuh Goal";
        }
    }

    // ------------------------------------------------------------------
    // 1) Assignment (data saja — dipakai juga oleh Experiment Runner)
    // ------------------------------------------------------------------
    public void Assign(DungeonGenerator generator)
    {
        gen = generator;
        roles.Clear();
        difficulty.Clear();
        mainPath.Clear();
        TreasureRoom = -1;
        BossRoom = -1;

        IReadOnlyList<DungeonRoom> rooms = gen.Rooms;
        int n = rooms.Count;
        if (n == 0) return;

        System.Random rng = new System.Random(unchecked(gen.Seed * 7919 + 17));

        // Difficulty = jarak BFS center room dari Start (dinormalisasi)
        int[,] dist = Distances(gen.StartPosition);
        float maxD = 1f;
        for (int i = 0; i < n; i++)
            maxD = Mathf.Max(maxD, dist[rooms[i].Center.x, rooms[i].Center.y]);
        for (int i = 0; i < n; i++)
        {
            roles.Add(RoomRole.Patrol);
            difficulty.Add(Mathf.Clamp01(dist[rooms[i].Center.x, rooms[i].Center.y] / maxD));
        }

        // Jalur utama Start→Goal (dari parent BFS)
        foreach (Vector2Int cell in PathCells(gen.StartPosition, gen.GoalPosition))
        {
            int r = RoomIndexAt(cell);
            if (r >= 0) mainPath.Add(r);
        }

        // Hard rule: Start & Boss
        roles[0] = RoomRole.Start;
        BossRoom = RoomIndexAt(gen.GoalPosition);
        if (BossRoom > 0) roles[BossRoom] = RoomRole.Boss;

        List<int> remaining = new List<int>();
        for (int i = 1; i < n; i++)
            if (i != BossRoom) remaining.Add(i);

        // Treasure: room samping (bukan jalur utama) terjauh; fallback room tengah acak
        int bestSide = -1;
        foreach (int i in remaining)
            if (!mainPath.Contains(i) && (bestSide < 0 || difficulty[i] > difficulty[bestSide]))
                bestSide = i;

        if (bestSide >= 0) TreasureRoom = bestSide;
        else if (remaining.Count >= 3) TreasureRoom = remaining[rng.Next(remaining.Count)];

        if (TreasureRoom >= 0)
        {
            roles[TreasureRoom] = RoomRole.Treasure;
            remaining.Remove(TreasureRoom);
        }

        // Tantangan bertahap: room termudah dapat Patrol, sisanya bergilir (urutan diacak per seed)
        remaining.Sort((a, b) => difficulty[a].CompareTo(difficulty[b]));
        List<RoomRole> bag = new List<RoomRole> { RoomRole.Guard, RoomRole.Trap, RoomRole.Sentry };
        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
        bag.Insert(0, RoomRole.Patrol);

        for (int k = 0; k < remaining.Count; k++)
        {
            int index = remaining[k];
            RoomRole role = bag[k % bag.Count];
            DungeonRoom room = rooms[index];

            // Constraint ukuran: turret butuh room ≥ 6×6
            if (role == RoomRole.Sentry && (room.width < 6 || room.height < 6))
                role = RoomRole.Guard;

            roles[index] = role;
        }
    }

    // ------------------------------------------------------------------
    // 2) Build (runtime): tint floor, label, hazard, mode Enemy
    // ------------------------------------------------------------------
    public void Build(DungeonGenerator generator)
    {
        gen = generator;
        spikes.Clear();
        solidTiles.Clear();
        solidObjects.Clear();
        ChestTile = null;
        roomObjects.Clear();
        ExtraEnemies = 0;
        TurretCount = 0;

        Assign(generator);

        List<EnemyController> enemies = new List<EnemyController>();
        if (gen.EnemiesParent != null)
            foreach (Transform t in gen.EnemiesParent)
                if (t.TryGetComponent(out EnemyController ec)) enemies.Add(ec);

        if (!challengesEnabled)
        {
            roles.Clear();
            foreach (EnemyController e in enemies) e.Configure(EnemyMode.Idle, default, gen, null);
            return;
        }

        root = new GameObject("Challenges").transform;
        root.SetParent(gen.LevelRoot, false);

        IReadOnlyList<DungeonRoom> rooms = gen.Rooms;
        for (int i = 0; i < rooms.Count; i++) roomObjects.Add(new List<GameObject>());

        HashSet<Vector2Int> occupied = new HashSet<Vector2Int> { gen.StartPosition, gen.GoalPosition };
        if (gen.ItemsParent != null)
            foreach (Transform t in gen.ItemsParent) occupied.Add(gen.WorldToGrid(t.position));
        foreach (EnemyController e in enemies) occupied.Add(gen.WorldToGrid(e.transform.position));

        System.Random rng = new System.Random(unchecked(gen.Seed * 31337 + 5));

        TintFloors();

        for (int i = 0; i < rooms.Count; i++)
        {
            DungeonRoom room = rooms[i];
            RoomRole role = roles[i];
            Rect area = RoomRect(room);
            List<EnemyController> inRoom = enemies.FindAll(e => room.Contains(gen.WorldToGrid(e.transform.position)));

            CreateLabel(i, room, role);

            switch (role)
            {
                case RoomRole.Patrol:
                    if (inRoom.Count == 0) inRoom.Add(SpawnEnemy(PatrolStart(room), false));
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Patrol, area, gen, rng);
                    break;

                case RoomRole.Guard:
                    if (inRoom.Count == 0) inRoom.Add(SpawnEnemy(FreeTile(room, room.Center, occupied), false));
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Guard, area, gen, rng);
                    break;

                case RoomRole.Trap:
                    BuildSpikes(i, room, occupied);
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Wander, area, gen, rng);
                    break;

                case RoomRole.Sentry:
                    BuildTurret(i, room, occupied);
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Wander, area, gen, rng);
                    break;

                case RoomRole.Treasure:
                    BuildChest(i, room, occupied);
                    if (inRoom.Count == 0) inRoom.Add(SpawnEnemy(FreeTile(room, room.Center + Vector2Int.right * 2, occupied), false));
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Guard, area, gen, rng);
                    break;

                case RoomRole.Boss:
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Wander, area, gen, rng);
                    EnemyController boss = SpawnEnemy(BossStart(room), true);
                    boss.Configure(EnemyMode.Boss, area, gen, rng);
                    // Boss menjaga Goal (§116): hanya aktif di sekitar Goal sehingga tepi room tetap bisa dilintasi
                    boss.SetLeash(gen.GridToWorld(gen.GoalPosition.x, gen.GoalPosition.y), bossLeashRadius);
                    break;

                default:
                    foreach (EnemyController e in inRoom) e.Configure(EnemyMode.Idle, area, gen, rng);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Query untuk HUD / Autopilot / Director
    // ------------------------------------------------------------------
    public int RoomIndexAt(Vector2Int p)
    {
        if (gen == null) return -1;
        IReadOnlyList<DungeonRoom> rooms = gen.Rooms;
        for (int i = 0; i < rooms.Count; i++)
            if (rooms[i].Contains(p)) return i;
        return -1;
    }

    public RoomRole RoleOf(int room)
    {
        return room >= 0 && room < roles.Count ? roles[room] : RoomRole.Patrol;
    }

    public IReadOnlyList<GameObject> RoomObjects(int room)
    {
        return room >= 0 && room < roomObjects.Count ? roomObjects[room] : (IReadOnlyList<GameObject>)System.Array.Empty<GameObject>();
    }

    /// <summary>Tile spike yang sedang/akan berbahaya dalam <paramref name="lookahead"/> detik.</summary>
    public void CollectDangerTiles(HashSet<Vector2Int> into, float lookahead)
    {
        foreach (SpikeTrap s in spikes)
            if (s != null && s.DangerWithin(lookahead)) into.Add(s.Tile);

        foreach (Projectile p in Projectile.All)
            if (p != null) p.CollectPathTiles(into, 0.9f);
    }

    public string RoleSummary()
    {
        if (!Active) return "<color=#8899AA>Room challenges OFF (perilaku modul dasar)</color>";

        int[] count = new int[7];
        foreach (RoomRole r in roles) count[(int)r]++;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < count.Length; i++)
        {
            if (count[i] == 0) continue;
            RoomRole r = (RoomRole)i;
            if (sb.Length > 0) sb.Append("   ");
            sb.Append("<color=#" + ColorUtility.ToHtmlStringRGB(RoleColor(r)) + "><b>" + RoleName(r).Replace(" ROOM", "") + "</b></color> " + count[i]);
        }
        return sb.ToString();
    }

    /// <summary>Kode singkat per room (urutan list room) untuk tabel eksperimen.</summary>
    public string RoleSequence()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < roles.Count; i++)
        {
            if (i > 0) sb.Append("·");
            switch (roles[i])
            {
                case RoomRole.Start: sb.Append("S"); break;
                case RoomRole.Patrol: sb.Append("P"); break;
                case RoomRole.Guard: sb.Append("G"); break;
                case RoomRole.Trap: sb.Append("T"); break;
                case RoomRole.Sentry: sb.Append("Sn"); break;
                case RoomRole.Treasure: sb.Append("$"); break;
                default: sb.Append("B"); break;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------
    private Rect RoomRect(DungeonRoom room)
    {
        float ts = gen.TileSize;
        return Rect.MinMaxRect(room.x * ts, room.y * ts, (room.x + room.width - 1) * ts, (room.y + room.height - 1) * ts);
    }

    private void TintFloors()
    {
        if (floorMaterial == null || gen.FloorsParent == null) return;

        foreach (Transform tile in gen.FloorsParent)
        {
            int r = RoomIndexAt(gen.WorldToGrid(tile.position));
            if (r < 0) continue;

            RoomRole role = roles[r];
            if (!floorMaterials.TryGetValue(role, out Material mat) || mat == null)
            {
                mat = new Material(floorMaterial) { name = "Floor_" + role };
                Color c = RoleColor(role);
                mat.SetColor("_EmissionColor", c * (role == RoomRole.Boss || role == RoomRole.Trap ? 0.5f : 0.38f));
                mat.SetColor("_BaseColor", Color.Lerp(new Color(0.13f, 0.15f, 0.21f), c * 0.25f, 0.35f));
                floorMaterials[role] = mat;
            }

            tile.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    private void CreateLabel(int index, DungeonRoom room, RoomRole role)
    {
        GameObject go = new GameObject("Label_" + RoleName(role));
        go.transform.SetParent(root, false);

        // pojok kiri-atas room (sisi z terbesar), di atas floor
        float ts = gen.TileSize;
        go.transform.position = new Vector3((room.x - 0.35f) * ts, 0.13f, (room.y + room.height - 0.55f) * ts);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = RoleName(role).Replace(" ROOM", "");
        text.fontSize = 8f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 4f;
        text.fontSizeMax = 8f;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 4f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        Color c = RoleColor(role);
        text.color = new Color(c.r, c.g, c.b, 0.85f);
        text.rectTransform.pivot = new Vector2(0f, 1f);
        text.rectTransform.sizeDelta = new Vector2((room.width - 0.3f) * ts, 0.9f);

        roomObjects[index].Add(go);
    }

    private EnemyController SpawnEnemy(Vector2Int tile, bool boss)
    {
        float scale = boss ? 1.7f : 1f;
        Vector3 pos = gen.GridToWorld(tile.x, tile.y) + Vector3.up * scale;

        GameObject go = Instantiate(enemyPrefab, pos, Quaternion.identity, gen.EnemiesParent);
        go.transform.localScale = Vector3.one * scale;
        go.name = boss ? "Boss" : "Enemy_Extra_" + ExtraEnemies;

        if (boss)
        {
            if (bossMaterial != null)
            {
                Transform body = go.transform.Find("Body");
                if (body != null) body.GetComponent<Renderer>().sharedMaterial = bossMaterial;
            }

            GameObject label = new GameObject("BossLabel");
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMeshPro t = label.AddComponent<TextMeshPro>();
            t.text = "BOSS";
            t.fontSize = 2.2f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(1f, 0.85f, 0.85f);
            t.rectTransform.sizeDelta = new Vector2(3f, 1f);
        }
        else
        {
            ExtraEnemies++;
        }

        return go.GetComponent<EnemyController>();
    }

    private Vector2Int PatrolStart(DungeonRoom room)
    {
        return new Vector2Int(room.x + room.width - 2, room.y + room.height - 2);
    }

    private Vector2Int BossStart(DungeonRoom room)
    {
        // pojok room yang paling jauh dari pintu masuk (sel jalur utama pertama di room ini)
        Vector2Int entry = room.Center;
        foreach (Vector2Int cell in PathCells(gen.StartPosition, gen.GoalPosition))
        {
            if (room.Contains(cell)) { entry = cell; break; }
        }

        Vector2Int[] corners =
        {
            new Vector2Int(room.x + 1, room.y + 1),
            new Vector2Int(room.x + room.width - 2, room.y + 1),
            new Vector2Int(room.x + 1, room.y + room.height - 2),
            new Vector2Int(room.x + room.width - 2, room.y + room.height - 2)
        };

        Vector2Int best = corners[0];
        float bestD = -1f;
        foreach (Vector2Int c in corners)
        {
            float d = Vector2Int.Distance(c, entry);
            if (d > bestD) { bestD = d; best = c; }
        }

        // 2.5 tile dari Goal ke arah pojok terjauh dari pintu
        Vector2 dir = ((Vector2)(best - gen.GoalPosition)).normalized;
        Vector2Int near = gen.GoalPosition + new Vector2Int(Mathf.RoundToInt(dir.x * 2.5f), Mathf.RoundToInt(dir.y * 2.5f));
        near.x = Mathf.Clamp(near.x, room.x, room.x + room.width - 1);
        near.y = Mathf.Clamp(near.y, room.y, room.y + room.height - 1);
        return near;
    }

    private Vector2Int FreeTile(DungeonRoom room, Vector2Int preferred, HashSet<Vector2Int> occupied)
    {
        Vector2Int clamped = new Vector2Int(
            Mathf.Clamp(preferred.x, room.x, room.x + room.width - 1),
            Mathf.Clamp(preferred.y, room.y, room.y + room.height - 1));

        for (int radius = 0; radius < 6; radius++)
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int p = clamped + new Vector2Int(dx, dy);
                    if (room.Contains(p) && !occupied.Contains(p) && !IsDoorway(p))
                    {
                        occupied.Add(p);
                        return p;
                    }
                }

        return clamped;
    }

    /// <summary>Tile room yang bersebelahan langsung dengan corridor (pintu).</summary>
    private bool IsDoorway(Vector2Int p)
    {
        foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            if (gen.IsCorridorTile(p.x + d.x, p.y + d.y)) return true;
        return false;
    }

    private void BuildSpikes(int index, DungeonRoom room, HashSet<Vector2Int> occupied)
    {
        if (spikePrefab == null) return;

        bool columns = room.width >= room.height;
        float cycle = spikeDown + spikeWarning + spikeUp;

        for (int x = room.x; x < room.x + room.width; x++)
        {
            for (int y = room.y; y < room.y + room.height; y++)
            {
                int lane = columns ? x - room.x : y - room.y;
                if (lane % 2 == 0) continue;                    // lajur aman bergantian
                Vector2Int p = new Vector2Int(x, y);
                if (occupied.Contains(p) || IsDoorway(p)) continue;

                GameObject go = Instantiate(spikePrefab, gen.GridToWorld(x, y), Quaternion.identity, root);
                SpikeTrap trap = go.GetComponent<SpikeTrap>();
                trap.Init(p, spikeDown, spikeWarning, spikeUp, (lane / 2) * spikeWaveDelay % cycle);
                spikes.Add(trap);
                roomObjects[index].Add(go);
            }
        }
    }

    private void BuildTurret(int index, DungeonRoom room, HashSet<Vector2Int> occupied)
    {
        if (turretPrefab == null) return;

        Vector2Int tile = FreeTile(room, room.Center, occupied);
        GameObject go = Instantiate(turretPrefab, gen.GridToWorld(tile.x, tile.y), Quaternion.identity, root);
        go.GetComponent<Turret>().Init(gen);
        solidTiles.Add(tile);
        solidObjects[tile] = go;
        TurretCount++;
        roomObjects[index].Add(go);
    }

    private void BuildChest(int index, DungeonRoom room, HashSet<Vector2Int> occupied)
    {
        if (chestPrefab == null) return;

        Vector2Int tile = FreeTile(room, room.Center, occupied);
        GameObject go = Instantiate(chestPrefab, gen.GridToWorld(tile.x, tile.y) + Vector3.up * 0.3f, Quaternion.identity, root);
        ChestTile = tile;
        roomObjects[index].Add(go);
    }

    // ------------------------------------------------------------------
    // BFS helpers
    // ------------------------------------------------------------------
    private static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    private int[,] Distances(Vector2Int from)
    {
        int w = gen.MapWidth, h = gen.MapHeight;
        int[,] d = new int[w, h];
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) d[x, y] = -1;

        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(from);
        d[from.x, from.y] = 0;

        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            foreach (Vector2Int dir in Dirs)
            {
                Vector2Int n = c + dir;
                if (!gen.IsInsideGrid(n.x, n.y) || d[n.x, n.y] >= 0 || gen.GetTile(n.x, n.y) == TileType.Wall) continue;
                d[n.x, n.y] = d[c.x, c.y] + 1;
                q.Enqueue(n);
            }
        }
        return d;
    }

    private List<Vector2Int> PathCells(Vector2Int from, Vector2Int to)
    {
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(from);

        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            if (c == to) break;
            foreach (Vector2Int dir in Dirs)
            {
                Vector2Int n = c + dir;
                if (parent.ContainsKey(n) || !gen.IsInsideGrid(n.x, n.y) || gen.GetTile(n.x, n.y) == TileType.Wall) continue;
                parent[n] = c;
                q.Enqueue(n);
            }
        }

        List<Vector2Int> path = new List<Vector2Int>();
        if (!parent.ContainsKey(to)) return path;
        for (Vector2Int p = to; p != from; p = parent[p]) path.Add(p);
        path.Add(from);
        path.Reverse();
        return path;
    }
}
