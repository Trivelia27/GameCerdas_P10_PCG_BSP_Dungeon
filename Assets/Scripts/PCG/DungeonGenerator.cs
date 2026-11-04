using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Strategi penentuan Goal (Modul P10 §108 — Eksperimen 6).
/// </summary>
public enum GoalStrategy
{
    LastRoom,           // Strategi A: First Room -> Last Room
    FarthestEuclidean,  // Strategi B: First Room -> Geometrically Farthest Room
    FarthestPath        // Strategi C: Start -> Room dengan shortest-path (BFS) terjauh
}

/// <summary>
/// Metrics dungeon (Modul P10 §120) — dipakai HUD dan Experiment Runner.
/// </summary>
[System.Serializable]
public class DungeonMetrics
{
    public int seed;
    public int attempts;
    public bool valid;
    public int leafCount;
    public int roomCount;
    public int floorTiles;
    public int corridorTiles;
    public int wallObjects;
    public int floorObjects;
    public float startGoalEuclidean;
    public int startGoalPath;
    public int enemyCount;
    public int itemCount;
    public float floorPercentage;
    public float generationMs;
}

public class DungeonGenerator : MonoBehaviour
{
    // ------------------------------------------------------------------
    // §39 Parameter Dungeon
    // ------------------------------------------------------------------
    [Header("Dungeon Size")]
    [SerializeField] private int mapWidth = 60;
    [SerializeField] private int mapHeight = 40;
    [SerializeField] private float tileSize = 1f;

    [Header("BSP Settings")]
    [SerializeField] private int minLeafSize = 12;
    [SerializeField] private int maxLeafSize = 20;

    [Header("Room Settings")]
    [SerializeField] private int minRoomSize = 5;
    [SerializeField] private int roomMargin = 2;

    [Header("Corridor Settings")]
    [Tooltip("1 = versi dasar modul. 2/3 = pengembangan §114-115.")]
    [SerializeField, Range(1, 3)] private int corridorWidth = 1;

    [Header("Generation")]
    [SerializeField] private int seed = 12345;
    [SerializeField] private bool randomSeed = false;
    [Tooltip("Generate-and-Test (§119): jumlah percobaan maksimum bila dungeon tidak valid.")]
    [SerializeField] private int maxGenerationAttempts = 10;
    [SerializeField] private GoalStrategy goalStrategy = GoalStrategy.FarthestEuclidean;

    // ------------------------------------------------------------------
    // §41 Referensi Prefab
    // ------------------------------------------------------------------
    [Header("Prefabs")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject goalPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject itemPrefab;
    [Tooltip("Opsional: penanda visual di tile Start.")]
    [SerializeField] private GameObject startPrefab;
    [Tooltip("Opsional: material khusus untuk floor corridor (agar room vs corridor terlihat).")]
    [SerializeField] private Material corridorMaterial;

    // ------------------------------------------------------------------
    // §42 Spawn Settings
    // ------------------------------------------------------------------
    [Header("Spawn Settings")]
    [SerializeField] private int enemyCount = 8;
    [SerializeField] private int itemCount = 5;
    [Tooltip("Jumlah room awal (mulai dari room Start) yang tidak boleh berisi Enemy.")]
    [SerializeField] private int safeRoomCount = 1;
    [Tooltip("Constraint §80: Enemy tidak boleh terlalu dekat dengan Start.")]
    [SerializeField] private float minEnemyDistanceFromStart = 8f;
    [Tooltip("Constraint: Enemy tidak boleh berada di/bersebelahan dengan corridor (corridor lebar 1 bisa tertutup collider Enemy).")]
    [SerializeField] private bool keepCorridorsClear = true;

    [Header("Enemy Density (§107, opsional)")]
    [SerializeField] private bool useEnemyDensity = false;
    [SerializeField] private int floorTilesPerEnemy = 50;

    [Header("Scene")]
    [Tooltip("Parent untuk GeneratedLevel (objek LevelRoot di Scene). Boleh kosong.")]
    [SerializeField] private Transform levelParent;
    [SerializeField] private bool autoFrameCamera = true;
    [SerializeField] private bool logAsciiMap = true;
    [Tooltip("§97: tombol R untuk regenerate. Dimatikan bila DungeonGameManager yang menangani input.")]
    [SerializeField] private bool enableRegenerateKey = true;

    // ------------------------------------------------------------------
    // §43 Internal Data
    // ------------------------------------------------------------------
    private TileType[,] grid;

    private BSPNode rootNode;

    private List<BSPNode> leafNodes = new List<BSPNode>();
    private List<DungeonRoom> rooms = new List<DungeonRoom>();

    private Transform levelRoot;
    private Transform floorsParent;
    private Transform wallsParent;
    private Transform enemiesParent;
    private Transform itemsParent;
    private GameObject spawnedPlayer;

    private Vector2Int startPosition;
    private Vector2Int goalPosition;

    private readonly List<Vector2Int> enemyPositions = new List<Vector2Int>();
    private readonly List<Vector2Int> itemPositions = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();

    private bool dataOnly;      // true saat dipanggil Experiment Runner (tanpa Instantiate)
    private DungeonMetrics metrics = new DungeonMetrics();

    public DungeonMetrics Metrics => metrics;
    public int Seed => seed;
    public int ItemsCollected { get; set; }
    public int LevelsCleared { get; private set; }

    // ---- Akses read-only untuk Demo / Autopilot ----
    public event System.Action Generated;
    public event System.Action GenerationFailed;
    public int GenerationCount { get; private set; }

    // ---- Parameter yang dapat diubah dari UI (Generation Controls) ----
    public int MapWidth { get => mapWidth; set => mapWidth = Mathf.Clamp(value, 16, 200); }
    public int MapHeight { get => mapHeight; set => mapHeight = Mathf.Clamp(value, 16, 200); }
    public int MinLeafSize { get => minLeafSize; set => minLeafSize = Mathf.Max(4, value); }
    public int MaxLeafSize { get => maxLeafSize; set => maxLeafSize = Mathf.Max(4, value); }
    public int MinRoomSize { get => minRoomSize; set => minRoomSize = Mathf.Max(2, value); }
    public int RoomMargin { get => roomMargin; set => roomMargin = Mathf.Max(0, value); }
    public int CorridorWidth { get => corridorWidth; set => corridorWidth = Mathf.Clamp(value, 1, 3); }
    public int EnemyCount { get => enemyCount; set => enemyCount = Mathf.Max(0, value); }
    public int ItemCount { get => itemCount; set => itemCount = Mathf.Max(0, value); }
    public GoalStrategy GoalMode { get => goalStrategy; set => goalStrategy = value; }
    public bool KeepCorridorsClear { get => keepCorridorsClear; set => keepCorridorsClear = value; }
    public bool RandomSeedEnabled { get => randomSeed; set => randomSeed = value; }
    public float MinEnemyDistanceFromStart => minEnemyDistanceFromStart;
    public int SafeRoomCount => safeRoomCount;

    public void SetSeed(int value)
    {
        seed = value;
    }

    public bool IsCorridorTile(int x, int y)
    {
        return IsInsideGrid(x, y) && grid != null &&
               grid[x, y] != TileType.Wall && !IsInAnyRoom(new Vector2Int(x, y));
    }
    public float TileSize => tileSize;
    public BSPNode RootNode => rootNode;
    public IReadOnlyList<BSPNode> LeafNodes => leafNodes;
    public IReadOnlyList<DungeonRoom> Rooms => rooms;
    public IReadOnlyList<Vector2Int> EnemyPositions => enemyPositions;
    public Vector2Int StartPosition => startPosition;
    public Vector2Int GoalPosition => goalPosition;
    public Transform LevelRoot => levelRoot;
    public Transform FloorsParent => floorsParent;
    public Transform WallsParent => wallsParent;
    public Transform EnemiesParent => enemiesParent;
    public Transform ItemsParent => itemsParent;

    public TileType GetTile(int x, int y)
    {
        return IsInsideGrid(x, y) ? grid[x, y] : TileType.Wall;
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.z / tileSize));
    }

    public void Regenerate()
    {
        seed++;
        GenerateDungeon();
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    // ------------------------------------------------------------------
    // §44 Start  &  §97 Regeneration
    // ------------------------------------------------------------------
    private void Start()
    {
        GenerateDungeon();
    }

    private void Update()
    {
        if (enableRegenerateKey && Input.GetKeyDown(KeyCode.R))
        {
            seed++;
            GenerateDungeon();
        }
    }

    // ------------------------------------------------------------------
    // §45 + §94 + §119 Pipeline utama: Generate -> Validate -> Build
    // ------------------------------------------------------------------
    public void GenerateDungeon()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        ClearDungeon();

        SetupSeed();

        if (!GenerateValidLayout())
        {
            Debug.LogError(
                "Dungeon gagal dibuat setelah " + maxGenerationAttempts +
                " percobaan. Periksa parameter BSP / Room.");
            GenerationFailed?.Invoke();
            return;
        }

        BuildDungeon();

        SpawnGameplayObjects();

        stopwatch.Stop();
        ComputeMetrics((float)stopwatch.Elapsed.TotalMilliseconds);

        if (autoFrameCamera)
        {
            FrameCamera();
        }

        Debug.Log("Dungeon generated with seed: " + seed);
        Debug.Log(MetricsToString());

        if (logAsciiMap)
        {
            Debug.Log(ToAscii(true));
        }

        GenerationCount++;
        Generated?.Invoke();
    }

    /// <summary>
    /// Generate-and-Test: Generate -> Test -> Valid? (YES: use, NO: seed++ & regenerate).
    /// </summary>
    private bool GenerateValidLayout()
    {
        for (int attempt = 1; attempt <= maxGenerationAttempts; attempt++)
        {
            metrics = new DungeonMetrics { attempts = attempt };

            Random.InitState(seed);

            InitializeGrid();

            CreateBSP();

            CreateRooms();

            CreateCorridors();

            PlaceStartAndGoal();

            if (ValidateDungeon() && HasPathToGoal())
            {
                metrics.valid = true;
                return true;
            }

            Debug.LogWarning(
                "Dungeon tidak valid (seed " + seed + "), mencoba seed berikutnya.");
            seed++;
        }

        return false;
    }

    /// <summary>
    /// Dipakai Experiment Runner (Editor): menjalankan seluruh algoritma generation
    /// termasuk penempatan Enemy/Item, tetapi tanpa membuat GameObject.
    /// Urutan random sama persis dengan saat Play.
    /// </summary>
    public DungeonMetrics RunDataOnly(int testSeed, out string ascii)
    {
        dataOnly = true;
        seed = testSeed;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool ok = GenerateValidLayout();
        if (ok)
        {
            SpawnGameplayObjects();
        }
        stopwatch.Stop();

        ComputeMetrics((float)stopwatch.Elapsed.TotalMilliseconds);
        metrics.valid = ok;
        ascii = ok ? ToAscii(true) : "(invalid)";

        dataOnly = false;
        return metrics;
    }

    // ------------------------------------------------------------------
    // §46 Setup Seed
    // ------------------------------------------------------------------
    private void SetupSeed()
    {
        if (randomSeed)
        {
            seed = System.DateTime.Now.GetHashCode();
        }

        Random.InitState(seed);
    }

    // ------------------------------------------------------------------
    // §47 Initialize Grid (Wall-First)
    // ------------------------------------------------------------------
    private void InitializeGrid()
    {
        grid = new TileType[mapWidth, mapHeight];

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                grid[x, y] = TileType.Wall;
            }
        }
    }

    // ------------------------------------------------------------------
    // §48 - §52 BSP
    // ------------------------------------------------------------------
    private void CreateBSP()
    {
        rootNode = new BSPNode(
            1,
            1,
            mapWidth - 2,
            mapHeight - 2
        );

        leafNodes.Clear();

        SplitNode(rootNode);
    }

    private void SplitNode(BSPNode node)
    {
        if (node.width <= maxLeafSize &&
            node.height <= maxLeafSize)
        {
            leafNodes.Add(node);
            return;
        }

        bool splitHorizontal;

        if (node.width > node.height * 1.25f)
        {
            splitHorizontal = false;
        }
        else if (node.height > node.width * 1.25f)
        {
            splitHorizontal = true;
        }
        else
        {
            splitHorizontal = Random.value > 0.5f;
        }

        if (splitHorizontal)
        {
            SplitHorizontal(node);
        }
        else
        {
            SplitVertical(node);
        }
    }

    private void SplitHorizontal(BSPNode node)
    {
        if (node.height < minLeafSize * 2)
        {
            leafNodes.Add(node);
            return;
        }

        int split = Random.Range(
            minLeafSize,
            node.height - minLeafSize
        );

        node.left = new BSPNode(
            node.x,
            node.y,
            node.width,
            split
        );

        node.right = new BSPNode(
            node.x,
            node.y + split,
            node.width,
            node.height - split
        );

        SplitNode(node.left);
        SplitNode(node.right);
    }

    private void SplitVertical(BSPNode node)
    {
        if (node.width < minLeafSize * 2)
        {
            leafNodes.Add(node);
            return;
        }

        int split = Random.Range(
            minLeafSize,
            node.width - minLeafSize
        );

        node.left = new BSPNode(
            node.x,
            node.y,
            split,
            node.height
        );

        node.right = new BSPNode(
            node.x + split,
            node.y,
            node.width - split,
            node.height
        );

        SplitNode(node.left);
        SplitNode(node.right);
    }

    // ------------------------------------------------------------------
    // §54 - §56 Rooms
    // ------------------------------------------------------------------
    private void CreateRooms()
    {
        rooms.Clear();

        foreach (BSPNode leaf in leafNodes)
        {
            int maxWidth = leaf.width - roomMargin * 2;
            int maxHeight = leaf.height - roomMargin * 2;

            if (maxWidth < minRoomSize ||
                maxHeight < minRoomSize)
            {
                continue;
            }

            int roomWidth = Random.Range(
                minRoomSize,
                maxWidth + 1
            );

            int roomHeight = Random.Range(
                minRoomSize,
                maxHeight + 1
            );

            int maxXOffset =
                leaf.width - roomWidth - roomMargin;

            int maxYOffset =
                leaf.height - roomHeight - roomMargin;

            int roomX = leaf.x +
                        Random.Range(
                            roomMargin,
                            maxXOffset + 1
                        );

            int roomY = leaf.y +
                        Random.Range(
                            roomMargin,
                            maxYOffset + 1
                        );

            DungeonRoom room =
                new DungeonRoom(
                    roomX,
                    roomY,
                    roomWidth,
                    roomHeight
                );

            leaf.room = room;

            rooms.Add(room);

            CarveRoom(room);
        }
    }

    private void CarveRoom(DungeonRoom room)
    {
        for (int x = room.x;
             x < room.x + room.width;
             x++)
        {
            for (int y = room.y;
                 y < room.y + room.height;
                 y++)
            {
                if (IsInsideGrid(x, y))
                {
                    grid[x, y] = TileType.Floor;
                }
            }
        }
    }

    public bool IsInsideGrid(int x, int y)
    {
        return x >= 0 &&
               x < mapWidth &&
               y >= 0 &&
               y < mapHeight;
    }

    /// <summary>Carve hanya di dalam area dalam (boundary Wall tetap utuh).</summary>
    private void CarveFloor(int x, int y)
    {
        if (x >= 1 && x < mapWidth - 1 &&
            y >= 1 && y < mapHeight - 1)
        {
            grid[x, y] = TileType.Floor;
        }
    }

    // ------------------------------------------------------------------
    // §58 - §61 + §115 Corridors (L-shaped)
    // ------------------------------------------------------------------
    private void CreateCorridors()
    {
        if (rooms.Count < 2)
            return;

        for (int i = 0; i < rooms.Count - 1; i++)
        {
            Vector2Int from = rooms[i].Center;
            Vector2Int to = rooms[i + 1].Center;

            CreateLCorridor(from, to);
        }
    }

    private void CreateLCorridor(
        Vector2Int from,
        Vector2Int to)
    {
        bool horizontalFirst =
            Random.value > 0.5f;

        if (horizontalFirst)
        {
            CarveHorizontal(
                from.x,
                to.x,
                from.y
            );

            CarveVertical(
                from.y,
                to.y,
                to.x
            );
        }
        else
        {
            CarveVertical(
                from.y,
                to.y,
                from.x
            );

            CarveHorizontal(
                from.x,
                to.x,
                to.y
            );
        }
    }

    private void CarveHorizontal(
        int x1,
        int x2,
        int y)
    {
        int start = Mathf.Min(x1, x2);
        int end = Mathf.Max(x1, x2);

        // corridorWidth > 1 juga menebalkan ujung agar sudut L tidak berlubang
        for (int x = start; x <= end + corridorWidth - 1; x++)
        {
            for (int offset = 0; offset < corridorWidth; offset++)
            {
                CarveFloor(x, y + offset);
            }
        }
    }

    private void CarveVertical(
        int y1,
        int y2,
        int x)
    {
        int start = Mathf.Min(y1, y2);
        int end = Mathf.Max(y1, y2);

        for (int y = start; y <= end + corridorWidth - 1; y++)
        {
            for (int offset = 0; offset < corridorWidth; offset++)
            {
                CarveFloor(x + offset, y);
            }
        }
    }

    // ------------------------------------------------------------------
    // §63 - §64 + §108 Start & Goal
    // ------------------------------------------------------------------
    private void PlaceStartAndGoal()
    {
        if (rooms.Count == 0)
            return;

        startPosition = rooms[0].Center;

        DungeonRoom goalRoom;

        switch (goalStrategy)
        {
            case GoalStrategy.LastRoom:
                goalRoom = rooms[rooms.Count - 1];
                break;

            case GoalStrategy.FarthestPath:
                goalRoom = FindFarthestRoomByPath(startPosition);
                break;

            default:
                goalRoom = FindFarthestRoom(startPosition);
                break;
        }

        goalPosition = goalRoom.Center;

        grid[startPosition.x, startPosition.y]
            = TileType.Start;

        grid[goalPosition.x, goalPosition.y]
            = TileType.Goal;
    }

    private DungeonRoom FindFarthestRoom(
        Vector2Int position)
    {
        DungeonRoom farthest = rooms[0];
        float farthestDistance = 0f;

        foreach (DungeonRoom room in rooms)
        {
            float distance =
                Vector2Int.Distance(
                    position,
                    room.Center
                );

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = room;
            }
        }

        return farthest;
    }

    /// <summary>Strategi C: room dengan jarak BFS (langkah grid) terpanjang dari Start.</summary>
    private DungeonRoom FindFarthestRoomByPath(
        Vector2Int position)
    {
        int[,] distances = ComputeDistances(position);

        DungeonRoom farthest = rooms[0];
        int farthestDistance = 0;

        foreach (DungeonRoom room in rooms)
        {
            int distance = distances[room.Center.x, room.Center.y];

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = room;
            }
        }

        return farthest;
    }

    // ------------------------------------------------------------------
    // §65 - §70 Build 3D Level
    // ------------------------------------------------------------------
    private void CreateLevelRoot()
    {
        GameObject root =
            new GameObject("GeneratedLevel");

        levelRoot = root.transform;

        if (levelParent != null)
        {
            levelRoot.SetParent(levelParent, false);
        }

        floorsParent = CreateChild("Floors");
        wallsParent = CreateChild("Walls");
        enemiesParent = CreateChild("Enemies");
        itemsParent = CreateChild("Items");
    }

    private Transform CreateChild(string childName)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(levelRoot, false);
        return child.transform;
    }

    private void BuildDungeon()
    {
        CreateLevelRoot();

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3 worldPosition =
                    GridToWorld(x, y);

                switch (grid[x, y])
                {
                    case TileType.Floor:
                    case TileType.Start:
                    case TileType.Goal:

                        if (floorPrefab != null)
                        {
                            GameObject floor = Instantiate(
                                floorPrefab,
                                worldPosition,
                                Quaternion.identity,
                                floorsParent
                            );

                            if (corridorMaterial != null &&
                                !IsInAnyRoom(new Vector2Int(x, y)))
                            {
                                floor.GetComponent<Renderer>().sharedMaterial = corridorMaterial;
                            }
                        }

                        break;
                }

                if (grid[x, y] == TileType.Wall &&
                    wallPrefab != null &&
                    IsWallVisible(x, y))
                {
                    Instantiate(
                        wallPrefab,
                        worldPosition +
                        Vector3.up * 0.5f,
                        Quaternion.identity,
                        wallsParent
                    );
                }

                if (grid[x, y] == TileType.Start &&
                    startPrefab != null)
                {
                    Instantiate(
                        startPrefab,
                        worldPosition +
                        Vector3.up * 0.06f,
                        Quaternion.identity,
                        levelRoot
                    );
                }

                if (grid[x, y] == TileType.Goal &&
                    goalPrefab != null)
                {
                    Instantiate(
                        goalPrefab,
                        worldPosition +
                        Vector3.up * 0.15f,
                        Quaternion.identity,
                        levelRoot
                    );
                }
            }
        }
    }

    public Vector3 GridToWorld(int x, int y)
    {
        return new Vector3(
            x * tileSize,
            0f,
            y * tileSize
        );
    }

    private bool IsWallVisible(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if (!IsInsideGrid(nx, ny))
                    continue;

                if (grid[nx, ny] != TileType.Wall)
                    return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // §71 - §82 Gameplay Objects
    // ------------------------------------------------------------------
    private void SpawnGameplayObjects()
    {
        enemyPositions.Clear();
        itemPositions.Clear();
        occupied.Clear();
        occupied.Add(startPosition);
        occupied.Add(goalPosition);

        SpawnPlayer();
        SpawnEnemies();
        SpawnItems();
    }

    private void SpawnPlayer()
    {
        if (dataOnly || playerPrefab == null)
            return;

        Vector3 position =
            GridToWorld(
                startPosition.x,
                startPosition.y
            );

        spawnedPlayer = Instantiate(
            playerPrefab,
            position + Vector3.up * 1f,
            Quaternion.identity
        );
    }

    private Vector2Int GetRandomFloorPosition()
    {
        for (int attempt = 0;
             attempt < 100;
             attempt++)
        {
            int x = Random.Range(1, mapWidth - 1);
            int y = Random.Range(1, mapHeight - 1);

            if (grid[x, y] == TileType.Floor)
            {
                return new Vector2Int(x, y);
            }
        }

        return startPosition;
    }

    private int GetTargetEnemyCount()
    {
        if (!useEnemyDensity)
            return enemyCount;

        return Mathf.Max(1, CountFloorTiles() / Mathf.Max(1, floorTilesPerEnemy));
    }

    private bool IsInSafeRoom(Vector2Int position)
    {
        for (int i = 0; i < safeRoomCount && i < rooms.Count; i++)
        {
            if (rooms[i].Contains(position))
                return true;
        }

        return false;
    }

    private bool IsInAnyRoom(Vector2Int position)
    {
        foreach (DungeonRoom room in rooms)
        {
            if (room.Contains(position))
                return true;
        }

        return false;
    }

    /// <summary>True jika tile ini atau salah satu dari 8 tetangganya adalah corridor (floor di luar room).</summary>
    private bool IsNearCorridor(Vector2Int position)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int p = new Vector2Int(position.x + dx, position.y + dy);

                if (IsInsideGrid(p.x, p.y) &&
                    grid[p.x, p.y] != TileType.Wall &&
                    !IsInAnyRoom(p))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SpawnEnemies()
    {
        int target = GetTargetEnemyCount();
        int maxAttempts = target * 50;   // mencegah infinite loop bila constraint tidak bisa dipenuhi
        int attempts = 0;

        while (enemyPositions.Count < target && attempts < maxAttempts)
        {
            attempts++;

            Vector2Int gridPosition =
                GetRandomFloorPosition();

            // Hard constraint: hanya Floor & tidak menumpuk
            if (grid[gridPosition.x, gridPosition.y] != TileType.Floor ||
                occupied.Contains(gridPosition))
            {
                continue;
            }

            // Constraint §80: Enemy tidak boleh terlalu dekat dengan Start
            float distanceFromStart =
                Vector2Int.Distance(
                    gridPosition,
                    startPosition
                );

            if (distanceFromStart < minEnemyDistanceFromStart)
            {
                continue;
            }

            // Constraint §42: safe room tidak berisi Enemy
            if (IsInSafeRoom(gridPosition))
            {
                continue;
            }

            // Constraint: Enemy tidak menutup corridor (satu-satunya jalur antar-room)
            if (keepCorridorsClear && IsNearCorridor(gridPosition))
            {
                continue;
            }

            occupied.Add(gridPosition);
            enemyPositions.Add(gridPosition);

            if (dataOnly || enemyPrefab == null)
                continue;

            Vector3 worldPosition =
                GridToWorld(
                    gridPosition.x,
                    gridPosition.y
                );

            GameObject enemy =
                Instantiate(
                    enemyPrefab,
                    worldPosition +
                    Vector3.up * 1f,
                    Quaternion.identity,
                    enemiesParent
                );

            enemy.name = "Enemy_" + (enemyPositions.Count - 1);
        }

        if (enemyPositions.Count < target)
        {
            Debug.LogWarning(
                "Hanya " + enemyPositions.Count + "/" + target +
                " Enemy yang memenuhi constraint.");
        }
    }

    private void SpawnItems()
    {
        int maxAttempts = itemCount * 50;
        int attempts = 0;

        while (itemPositions.Count < itemCount && attempts < maxAttempts)
        {
            attempts++;

            Vector2Int gridPosition =
                GetRandomFloorPosition();

            if (grid[gridPosition.x, gridPosition.y] != TileType.Floor ||
                occupied.Contains(gridPosition))
            {
                continue;
            }

            occupied.Add(gridPosition);
            itemPositions.Add(gridPosition);

            if (dataOnly || itemPrefab == null)
                continue;

            Vector3 worldPosition =
                GridToWorld(
                    gridPosition.x,
                    gridPosition.y
                );

            GameObject item =
                Instantiate(
                    itemPrefab,
                    worldPosition +
                    Vector3.up * 0.5f,
                    Quaternion.identity,
                    itemsParent
                );

            item.name = "Item_" + (itemPositions.Count - 1);
        }
    }

    // ------------------------------------------------------------------
    // §83 + §98 Clear Dungeon
    // ------------------------------------------------------------------
    private void ClearDungeon()
    {
        GameObject existing =
            levelRoot != null ? levelRoot.gameObject : GameObject.Find("GeneratedLevel");

        if (existing != null)
        {
            // Rename dulu: Destroy() baru dieksekusi di akhir frame (§98)
            existing.name = "GeneratedLevel (destroyed)";
            Destroy(existing);
        }

        levelRoot = null;

        GameObject oldPlayer =
            spawnedPlayer != null ? spawnedPlayer : GameObject.FindWithTag("Player");

        if (oldPlayer != null)
        {
            oldPlayer.tag = "Untagged";
            Destroy(oldPlayer);
        }

        spawnedPlayer = null;
        ItemsCollected = 0;
    }

    /// <summary>Dipanggil GoalTrigger ketika Player mencapai Goal.</summary>
    public void OnGoalReached()
    {
        LevelsCleared++;
        Debug.Log("GOAL REACHED! Level " + LevelsCleared + " selesai. Membuat dungeon berikutnya...");
        seed++;
        GenerateDungeon();
    }

    // ------------------------------------------------------------------
    // §91 - §93 Validasi
    // ------------------------------------------------------------------
    private bool ValidateDungeon()
    {
        if (rooms == null || rooms.Count < 2)
        {
            Debug.LogWarning(
                "Dungeon invalid: jumlah room terlalu sedikit."
            );

            return false;
        }

        if (!IsInsideGrid(
                startPosition.x,
                startPosition.y))
        {
            return false;
        }

        if (!IsInsideGrid(
                goalPosition.x,
                goalPosition.y))
        {
            return false;
        }

        if (startPosition == goalPosition)
        {
            Debug.LogWarning("Dungeon invalid: Start dan Goal berada di tile yang sama.");
            return false;
        }

        if (grid[startPosition.x, startPosition.y] != TileType.Start ||
            grid[goalPosition.x, goalPosition.y] != TileType.Goal)
        {
            return false;
        }

        return true;
    }

    private bool HasPathToGoal()
    {
        bool[,] visited =
            new bool[mapWidth, mapHeight];

        Queue<Vector2Int> queue =
            new Queue<Vector2Int>();

        queue.Enqueue(startPosition);

        visited[
            startPosition.x,
            startPosition.y
        ] = true;

        while (queue.Count > 0)
        {
            Vector2Int current =
                queue.Dequeue();

            if (current == goalPosition)
            {
                return true;
            }

            foreach (Vector2Int direction
                     in Directions)
            {
                Vector2Int next =
                    current + direction;

                if (!IsInsideGrid(
                        next.x,
                        next.y))
                {
                    continue;
                }

                if (visited[
                        next.x,
                        next.y])
                {
                    continue;
                }

                if (grid[
                        next.x,
                        next.y]
                    == TileType.Wall)
                {
                    continue;
                }

                visited[
                    next.x,
                    next.y
                ] = true;

                queue.Enqueue(next);
            }
        }

        Debug.LogWarning(
            "Dungeon tidak valid: Goal tidak dapat dicapai."
        );

        return false;
    }

    /// <summary>BFS distance map (-1 = tidak terjangkau). Dipakai strategi C & metrics.</summary>
    private int[,] ComputeDistances(Vector2Int from)
    {
        int[,] distances = new int[mapWidth, mapHeight];

        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                distances[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        distances[from.x, from.y] = 0;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;

                if (!IsInsideGrid(next.x, next.y) ||
                    distances[next.x, next.y] >= 0 ||
                    grid[next.x, next.y] == TileType.Wall)
                {
                    continue;
                }

                distances[next.x, next.y] = distances[current.x, current.y] + 1;
                queue.Enqueue(next);
            }
        }

        return distances;
    }

    // ------------------------------------------------------------------
    // §120 Metrics
    // ------------------------------------------------------------------
    private int CountFloorTiles()
    {
        int count = 0;

        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                if (grid[x, y] != TileType.Wall)
                    count++;

        return count;
    }

    private void ComputeMetrics(float elapsedMs)
    {
        int attempts = metrics.attempts;
        bool valid = metrics.valid;

        metrics = new DungeonMetrics
        {
            seed = seed,
            attempts = attempts,
            valid = valid,
            leafCount = leafNodes.Count,
            roomCount = rooms.Count,
            enemyCount = enemyPositions.Count,
            itemCount = itemPositions.Count,
            generationMs = elapsedMs
        };

        if (grid == null)
            return;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                if (grid[x, y] == TileType.Wall)
                {
                    if (IsWallVisible(x, y))
                        metrics.wallObjects++;
                    continue;
                }

                metrics.floorTiles++;

                bool inRoom = false;
                Vector2Int p = new Vector2Int(x, y);
                foreach (DungeonRoom room in rooms)
                {
                    if (room.Contains(p)) { inRoom = true; break; }
                }

                if (!inRoom)
                    metrics.corridorTiles++;
            }
        }

        metrics.floorObjects = metrics.floorTiles;
        metrics.floorPercentage = 100f * metrics.floorTiles / (mapWidth * mapHeight);
        metrics.startGoalEuclidean = Vector2Int.Distance(startPosition, goalPosition);

        if (IsInsideGrid(startPosition.x, startPosition.y) &&
            IsInsideGrid(goalPosition.x, goalPosition.y))
        {
            metrics.startGoalPath = ComputeDistances(startPosition)[goalPosition.x, goalPosition.y];
        }
    }

    public string MetricsToString()
    {
        return
            "Seed           : " + metrics.seed + "\n" +
            "Attempts       : " + metrics.attempts + "\n" +
            "Valid          : " + (metrics.valid ? "Yes" : "No") + "\n" +
            "BSP Leaves     : " + metrics.leafCount + "\n" +
            "Rooms          : " + metrics.roomCount + "\n" +
            "Floor Tiles    : " + metrics.floorTiles + " (" + metrics.floorPercentage.ToString("0.0") + "%)\n" +
            "Corridor Tiles : " + metrics.corridorTiles + "\n" +
            "Wall Objects   : " + metrics.wallObjects + "\n" +
            "StartGoalDist  : " + metrics.startGoalPath + " langkah (euclid " + metrics.startGoalEuclidean.ToString("0.0") + ")\n" +
            "Enemies        : " + metrics.enemyCount + "\n" +
            "Items          : " + metrics.itemCount + "\n" +
            "Gen Time       : " + metrics.generationMs.ToString("0.0") + " ms";
    }

    /// <summary>
    /// Representasi ASCII (§3). Baris atas = y terbesar, sesuai tampilan kamera top-down.
    /// # Wall  . Floor  S Start  G Goal  E Enemy  I Item
    /// </summary>
    public string ToAscii(bool includeEntities)
    {
        if (grid == null)
            return string.Empty;

        HashSet<Vector2Int> enemySet = new HashSet<Vector2Int>(enemyPositions);
        HashSet<Vector2Int> itemSet = new HashSet<Vector2Int>(itemPositions);

        StringBuilder sb = new StringBuilder();

        for (int y = mapHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Vector2Int p = new Vector2Int(x, y);
                char c;

                switch (grid[x, y])
                {
                    case TileType.Start: c = 'S'; break;
                    case TileType.Goal: c = 'G'; break;
                    case TileType.Floor: c = '.'; break;
                    default: c = '#'; break;
                }

                if (includeEntities && c == '.')
                {
                    if (enemySet.Contains(p)) c = 'E';
                    else if (itemSet.Contains(p)) c = 'I';
                }

                sb.Append(c);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // §85 Kamera top-down otomatis
    // ------------------------------------------------------------------
    public void FrameCamera()
    {
        Camera cam = Camera.main;

        if (cam == null)
            return;

        float worldWidth = (mapWidth + 2) * tileSize;
        float worldHeight = (mapHeight + 2) * tileSize;

        Vector3 center = new Vector3(
            (mapWidth - 1) * tileSize * 0.5f,
            0f,
            (mapHeight - 1) * tileSize * 0.5f);

        float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float heightForZ = worldHeight * 0.5f / Mathf.Tan(halfFov);
        float heightForX = worldWidth * 0.5f / (Mathf.Tan(halfFov) * cam.aspect);
        float cameraHeight = Mathf.Max(heightForZ, heightForX) * 1.08f + 1f;

        cam.transform.position = center + Vector3.up * cameraHeight;
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // ------------------------------------------------------------------
    // §89 Gizmos
    // ------------------------------------------------------------------
    private void OnDrawGizmos()
    {
        if (rooms == null)
            return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);

        foreach (BSPNode leaf in leafNodes)
        {
            Gizmos.DrawWireCube(
                new Vector3(
                    (leaf.x + leaf.width / 2f - 0.5f) * tileSize,
                    0.1f,
                    (leaf.y + leaf.height / 2f - 0.5f) * tileSize),
                new Vector3(
                    leaf.width * tileSize,
                    0.1f,
                    leaf.height * tileSize));
        }

        Gizmos.color = Color.yellow;

        foreach (DungeonRoom room in rooms)
        {
            Vector3 center =
                new Vector3(
                    (room.x + room.width / 2f - 0.5f) * tileSize,
                    0.2f,
                    (room.y + room.height / 2f - 0.5f) * tileSize
                );

            Vector3 size =
                new Vector3(
                    room.width * tileSize,
                    0.2f,
                    room.height * tileSize
                );

            Gizmos.DrawWireCube(
                center,
                size
            );
        }

        if (rooms.Count > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(GridToWorld(startPosition.x, startPosition.y) + Vector3.up * 0.5f, 0.4f);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(GridToWorld(goalPosition.x, goalPosition.y) + Vector3.up * 0.5f, 0.4f);
        }
    }
}
