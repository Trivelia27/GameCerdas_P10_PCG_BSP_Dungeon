using System;
using System.Collections.Generic;
using UnityEngine;

public enum DungeonState
{
    Title,
    Playing,
    LevelComplete,
    GameOver,
    Paused
}

/// <summary>
/// Alur game P10: Title → Playing → Level Cleared → dungeon berikutnya (seed + 1).
/// Mengelola skor, HP, item, timer, input global, mode demo (autoplay), dan riwayat seed.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DungeonGameManager : MonoBehaviour
{
    public struct LevelResult
    {
        public int level;
        public int seed;
        public float time;
        public int items;
        public int itemsTotal;
        public int pathLength;
        public int itemScore;
        public int clearBonus;
        public int timeBonus;
    }

    public struct HistoryEntry
    {
        public int seed;
        public int rooms;
        public int path;
        public bool valid;
    }

    public static DungeonGameManager Instance { get; private set; }

    [SerializeField] private DungeonGenerator generator;
    [SerializeField] private DungeonDemoDirector director;

    [Header("Mode")]
    [Tooltip("Demo: mulai otomatis, autopilot aktif, lanjut level otomatis.")]
    [SerializeField] private bool demoMode = false;
    [SerializeField] private float titleAutoStartDelay = 3.5f;
    [SerializeField] private float autoNextDelay = 4f;

    [Header("Rules")]
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float hitCooldown = 1.2f;
    [SerializeField] private int itemScore = 100;
    [SerializeField] private int clearScore = 500;

    private readonly List<HistoryEntry> history = new List<HistoryEntry>();
    private DungeonState stateBeforePause;
    private float hitTimer;

    public DungeonGenerator Generator => generator;
    public DungeonDemoDirector Director => director;
    public DungeonState State { get; private set; } = DungeonState.Title;
    public bool DemoMode => demoMode;
    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public int ItemsCollected { get; private set; }
    public int ItemsTotal { get; private set; }
    public float LevelTime { get; private set; }
    public float AutoTimer { get; private set; }
    public LevelResult LastResult { get; private set; }
    public IReadOnlyList<HistoryEntry> History => history;
    public SimplePlayerController Player { get; private set; }

    public event Action<string, Color> Toast;
    public event Action<Vector3, string, Color> Popup;
    public event Action Damaged;
    public event Action LevelReady;
    public event Action HistoryChanged;

    // ------------------------------------------------------------------
    private void Awake()
    {
        Instance = this;
        Health = maxHealth;

        if (generator == null) generator = FindFirstObjectByType<DungeonGenerator>();
        if (director == null) director = FindFirstObjectByType<DungeonDemoDirector>();

        generator.Generated += OnGenerated;
        generator.GenerationFailed += OnGenerationFailed;

        if (demoMode)
        {
            AutoTimer = titleAutoStartDelay;
            director.AutopilotEnabled = true;
        }
    }

    private void OnDestroy()
    {
        if (generator != null)
        {
            generator.Generated -= OnGenerated;
            generator.GenerationFailed -= OnGenerationFailed;
        }

        Time.timeScale = 1f;
    }

    private void Update()
    {
        HandleInput();

        float dt = Time.deltaTime;
        hitTimer -= dt;

        bool revealing = director != null && director.IsRevealing;

        if (Player == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) Player = go.GetComponent<SimplePlayerController>();
        }

        if (Player != null)
        {
            Player.InputLocked = State != DungeonState.Playing || revealing;
        }

        switch (State)
        {
            case DungeonState.Title:
                if (demoMode && !revealing)
                {
                    AutoTimer -= Time.unscaledDeltaTime;
                    if (AutoTimer <= 0f) StartGame();
                }
                break;

            case DungeonState.Playing:
                if (!revealing) LevelTime += dt;
                break;

            case DungeonState.LevelComplete:
            case DungeonState.GameOver:
                if (demoMode || (director != null && director.AutopilotEnabled))
                {
                    AutoTimer -= Time.unscaledDeltaTime;
                    if (AutoTimer <= 0f)
                    {
                        if (State == DungeonState.LevelComplete) NextLevel();
                        else Retry();
                    }
                }
                break;
        }
    }

    private void HandleInput()
    {
        if (UIFactory.TextInputFocused())
            return;

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            switch (State)
            {
                case DungeonState.Title: StartGame(); break;
                case DungeonState.LevelComplete: NextLevel(); break;
                case DungeonState.GameOver: Retry(); break;
                case DungeonState.Paused: TogglePause(); break;
            }
        }

        if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape))
        {
            if (State == DungeonState.Playing || State == DungeonState.Paused)
                TogglePause();
        }

        if (Input.GetKeyDown(KeyCode.F2)) ToggleAutopilot();
        if (Input.GetKeyDown(KeyCode.R)) Regenerate(generator.Seed + 1);
        if (Input.GetKeyDown(KeyCode.N)) Regenerate(UnityEngine.Random.Range(1, 99999));
        if (Input.GetKeyDown(KeyCode.V)) director.RevealEnabled = !director.RevealEnabled;
        if (Input.GetKeyDown(KeyCode.B)) director.ShowOverlay = !director.ShowOverlay;
        if (Input.GetKeyDown(KeyCode.C)) director.FollowCamera = !director.FollowCamera;
        if (Input.GetKeyDown(KeyCode.Space) && director.IsRevealing) director.SkipReveal();

        if (State != DungeonState.Paused)
        {
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                SetSpeed(Time.timeScale + 0.5f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                SetSpeed(Time.timeScale - 0.5f);
        }
    }

    // ------------------------------------------------------------------
    // Alur game
    // ------------------------------------------------------------------
    public void StartGame()
    {
        if (State != DungeonState.Title)
            return;

        Level = 1;
        Score = 0;
        Health = maxHealth;
        LevelTime = 0f;
        State = DungeonState.Playing;

        Toast?.Invoke("LEVEL 1", UIFactory.Cyan);
        AudioManager.Play(Sfx.Unlock, 0.7f);
    }

    public void StartDemo()
    {
        director.AutopilotEnabled = true;
        StartGame();
    }

    public void NextLevel()
    {
        Level++;
        State = DungeonState.Playing;
        generator.SetSeed(generator.Seed + 1);
        generator.GenerateDungeon();
        Toast?.Invoke("LEVEL " + Level, UIFactory.Cyan);
    }

    public void Retry()
    {
        Health = maxHealth;
        State = DungeonState.Playing;
        generator.GenerateDungeon(); // seed sama → layout sama (reproducible)
        Toast?.Invoke("RETRY · SEED " + generator.Seed, UIFactory.Gold);
    }

    public void Regenerate(int newSeed)
    {
        if (State == DungeonState.Paused)
            return;

        if (State == DungeonState.LevelComplete || State == DungeonState.GameOver)
            State = DungeonState.Playing;

        generator.SetSeed(newSeed);
        generator.GenerateDungeon();
    }

    public void RegenerateSameSeed()
    {
        Regenerate(generator.Seed);
    }

    public void TogglePause()
    {
        if (State == DungeonState.Paused)
        {
            State = stateBeforePause;
            Time.timeScale = pausedTimeScale;
        }
        else if (State == DungeonState.Playing)
        {
            stateBeforePause = State;
            pausedTimeScale = Time.timeScale;
            State = DungeonState.Paused;
            Time.timeScale = 0f;
        }
    }

    private float pausedTimeScale = 1f;

    public void ToggleAutopilot()
    {
        director.AutopilotEnabled = !director.AutopilotEnabled;
        Toast?.Invoke(director.AutopilotEnabled ? "AUTOPILOT ON" : "AUTOPILOT OFF", UIFactory.Gold);

        if (State == DungeonState.Title && director.AutopilotEnabled)
            StartGame();
    }

    public void SetSpeed(float value)
    {
        Time.timeScale = Mathf.Clamp(value, 0.5f, 4f);
        Toast?.Invoke("SPEED " + Time.timeScale.ToString("0.0") + "x", UIFactory.Dim);
    }

    // ------------------------------------------------------------------
    // Event dari generator & gameplay
    // ------------------------------------------------------------------
    private void OnGenerated()
    {
        Player = null;
        ItemsCollected = 0;
        ItemsTotal = generator.Metrics.itemCount;
        LevelTime = 0f;
        hitTimer = 0f;

        DungeonMetrics m = generator.Metrics;
        history.Insert(0, new HistoryEntry { seed = m.seed, rooms = m.roomCount, path = m.startGoalPath, valid = m.valid });
        if (history.Count > 10) history.RemoveAt(history.Count - 1);
        HistoryChanged?.Invoke();

        LevelReady?.Invoke();
    }

    private void OnGenerationFailed()
    {
        Toast?.Invoke("GENERATION FAILED — ubah parameter", UIFactory.Red);
        AudioManager.Play(Sfx.Lose, 0.6f);
    }

    public void CollectItem(ItemPickup item)
    {
        if (State != DungeonState.Playing)
            return;

        ItemsCollected++;
        Score += itemScore;

        Vector3 p = item.transform.position;
        Fx.Burst(p, UIFactory.Cyan, 26, 4.5f, 0.2f, 0.6f, 0.4f);
        Fx.Ring(p - Vector3.up * 0.4f, UIFactory.Cyan, 1.4f, 0.5f);
        AudioManager.Play(Sfx.Pickup, 0.8f, 0.9f + ItemsCollected * 0.07f);
        Popup?.Invoke(p, "+" + itemScore, UIFactory.Cyan);

        if (ItemsCollected >= ItemsTotal && ItemsTotal > 0)
        {
            Toast?.Invoke("ALL ITEMS COLLECTED", UIFactory.Gold);
            AudioManager.Play(Sfx.Rare, 0.8f);
        }
    }

    public void ReachGoal(Vector3 position)
    {
        if (State != DungeonState.Playing)
            return;

        int timeBonus = Mathf.Max(0, 300 - Mathf.RoundToInt(LevelTime * 5f));

        LastResult = new LevelResult
        {
            level = Level,
            seed = generator.Seed,
            time = LevelTime,
            items = ItemsCollected,
            itemsTotal = ItemsTotal,
            pathLength = generator.Metrics.startGoalPath,
            itemScore = ItemsCollected * itemScore,
            clearBonus = clearScore,
            timeBonus = timeBonus
        };

        Score += clearScore + timeBonus;
        State = DungeonState.LevelComplete;
        AutoTimer = autoNextDelay;

        Fx.Burst(position + Vector3.up * 0.5f, UIFactory.Gold, 60, 7f, 0.28f, 1f, 0.3f);
        Fx.Ring(position, UIFactory.Gold, 4f, 0.8f);
        Fx.Ring(position, UIFactory.Cyan, 6f, 1.1f, true);
        AudioManager.Play(Sfx.Win, 0.9f);
        PostFxController.Pulse(0.5f, 0.1f);
    }

    public void PlayerHit(Vector3 enemyPosition)
    {
        if (State != DungeonState.Playing || hitTimer > 0f || (director != null && director.IsRevealing))
            return;

        hitTimer = hitCooldown;
        Health--;

        if (Player != null)
        {
            Vector3 away = Player.transform.position - enemyPosition;
            away.y = 0f;
            Player.ApplyKnockback(away.normalized * 9f);
            Fx.Burst(Player.transform.position, UIFactory.Red, 30, 5f, 0.22f, 0.6f, 0.5f);
            Popup?.Invoke(Player.transform.position + Vector3.up, "-1 HP", UIFactory.Red);
        }

        AudioManager.Play(Sfx.Hit, 0.9f);
        PostFxController.Pulse(0.8f, 0.2f);
        Damaged?.Invoke();

        if (Health <= 0)
        {
            State = DungeonState.GameOver;
            AutoTimer = autoNextDelay;
            AudioManager.Play(Sfx.Lose, 0.9f);
        }
    }
}
