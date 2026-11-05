using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dashboard P10 (gaya Praktikum 9):
/// 01 Generation Controls (kiri) · 02 Viewport dungeon live (tengah, RenderTexture) ·
/// 03 Dungeon Report + minimap BSP (kanan) · Seed History (bawah).
/// Di dalam viewport: HUD game, pipeline tracker, layar Title / Level Cleared / Game Over / Pause.
/// TAB = mode fokus (viewport penuh).
/// </summary>
public class DungeonHUD : MonoBehaviour
{
    private class Floating
    {
        public TextMeshProUGUI text;
        public Vector3 world;
        public float age;
    }

    private class ConstraintRow
    {
        public Image pip;
        public TextMeshProUGUI text;
    }

    private const float PopupLife = 1f;

    private DungeonGameManager gm;
    private DungeonGenerator gen;
    private DungeonDemoDirector dir;
    private Camera cam;
    private Canvas canvas;
    private RenderTexture renderTexture;

    private RectTransform root;
    private RectTransform chrome;
    private RectTransform frame;
    private RawImage view;
    private RectTransform hudRoot;
    private RectTransform titlePanel;
    private RectTransform levelPanel;
    private RectTransform overPanel;
    private RectTransform pausePanel;
    private RectTransform trackerRoot;
    private TextMeshProUGUI focusHint;

    // HUD dalam viewport
    private TextMeshProUGUI levelText;
    private TextMeshProUGUI scoreText;
    private TextMeshProUGUI itemsText;
    private RectTransform itemsFill;
    private TextMeshProUGUI goalText;
    private TextMeshProUGUI autoText;
    private TextMeshProUGUI roomBanner;
    private float roomBannerTimer;
    private Color roomBannerColor;
    private TextMeshProUGUI roleText;
    private readonly List<Image> staticMarkers = new List<Image>();
    private TextMeshProUGUI timerText;
    private TextMeshProUGUI seedText;
    private Image[] hpPips;
    private TextMeshProUGUI hpLabel;
    private Image flash;
    private TextMeshProUGUI toastText;
    private Image[] phaseChips;
    private TextMeshProUGUI[] phaseLabels;
    private TextMeshProUGUI captionText;

    // panel layar
    private TextMeshProUGUI titleCountdown;
    private TextMeshProUGUI levelStats;
    private TextMeshProUGUI levelCountdown;
    private TextMeshProUGUI overCountdown;

    // kontrol kiri
    private TMP_InputField seedInput;
    private readonly List<System.Action> controlRefreshers = new List<System.Action>();
    private Button[] goalButtons;
    private float pendingRegenerate = -1f;
    private bool suppressCallbacks;

    // laporan kanan
    private RawImage minimap;
    private RectTransform minimapRect;
    private Texture2D minimapTexture;
    private RectTransform markerRoot;
    private Image playerMarker;
    private Image goalMarker;
    private readonly List<Image> enemyMarkers = new List<Image>();
    private readonly List<Image> itemMarkers = new List<Image>();
    private readonly Dictionary<string, TextMeshProUGUI> metricValues = new Dictionary<string, TextMeshProUGUI>();
    private readonly List<ConstraintRow> constraintRows = new List<ConstraintRow>();

    // bar bawah
    private RectTransform historyRow;
    private int historySignature = -1;

    private readonly List<Floating> floating = new List<Floating>();

    private bool focusMode;
    private int builtGeneration = -1;
    private float displayScore;
    private float toastTimer;
    private Color toastColor;
    private float flashAlpha;
    private float reportTimer;

    // ==================================================================
    private void Awake()
    {
        gm = GetComponent<DungeonGameManager>();
        if (gm == null) gm = FindFirstObjectByType<DungeonGameManager>();
        gen = FindFirstObjectByType<DungeonGenerator>();
        dir = FindFirstObjectByType<DungeonDemoDirector>();

        UIFactory.EnsureEventSystem();
        BuildDisplayCamera();
        BuildCanvas();
        BuildChrome();
        BuildViewport();
        BuildHud();
        BuildTitle();
        BuildLevelComplete();
        BuildGameOver();
        BuildPause();
        BuildControls();
        BuildReport();
        ApplyLayout();
    }

    private void Start()
    {
        cam = Camera.main;

        gm.Toast += ShowToast;
        gm.RoomEntered += OnRoomEntered;
        gm.Popup += SpawnPopup;
        gm.Damaged += OnDamaged;

        Canvas.ForceUpdateCanvases();
        EnsureRenderTexture();
        RefreshControls();
    }

    private void OnDestroy()
    {
        if (gm != null)
        {
            gm.Toast -= ShowToast;
            gm.RoomEntered -= OnRoomEntered;
            gm.Popup -= SpawnPopup;
            gm.Damaged -= OnDamaged;
        }

        if (cam != null) cam.targetTexture = null;
        if (renderTexture != null) renderTexture.Release();
    }

    // ==================================================================
    // Kerangka dashboard
    // ==================================================================
    private void BuildDisplayCamera()
    {
        GameObject go = new GameObject("DisplayCamera");
        go.transform.SetParent(transform, false);

        Camera display = go.AddComponent<Camera>();
        display.clearFlags = CameraClearFlags.SolidColor;
        display.backgroundColor = new Color(0.016f, 0.024f, 0.05f);
        display.cullingMask = 0;
        display.depth = -50f;
        display.allowHDR = false;
        display.allowMSAA = false;
    }

    private void BuildCanvas()
    {
        GameObject go = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(transform, false);

        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        root = go.GetComponent<RectTransform>();

        Image background = UIFactory.MakeImage(root, "Background", new Color(0.016f, 0.024f, 0.05f, 1f));
        UIFactory.Stretch(background.rectTransform);
    }

    private void BuildChrome()
    {
        chrome = UIFactory.Node(root, "Chrome");
        UIFactory.Stretch(chrome);

        TextMeshProUGUI label = UIFactory.MakeText(chrome,
            "<color=#FF4DB8>02</color>   DUNGEON  /  BSP PROCEDURAL GENERATION",
            20f, UIFactory.Cyan, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        label.characterSpacing = 4f;
        UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(492f, -26f), new Vector2(990f, 28f));

        TextMeshProUGUI legend = UIFactory.MakeText(chrome,
            "Seed → Grid → BSP → Room → Corridor → Wall → Goal/BFS → Spawn   ·   [B] " +
            "<color=#4DD9FF>Leaf</color>  <color=#FFD84D>Room</color>  <color=#4DFF73>jalur BFS</color>  lingkaran = spawn",
            15f, UIFactory.Dim, TextAlignmentOptions.TopLeft);
        UIFactory.Place(legend.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(494f, -54f), new Vector2(990f, 24f));

        TextMeshProUGUI tips = UIFactory.MakeText(chrome,
            "WASD / panah gerak  ·  ENTER mulai/lanjut  ·  F2 autopilot  ·  R seed+1  ·  N seed acak  ·  SPACE skip animasi  ·  P pause\n" +
            "V animasi pipeline  ·  B overlay BSP  ·  C kamera follow  ·  +/- kecepatan  ·  TAB mode fokus",
            15f, UIFactory.Dim, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
        UIFactory.Place(tips.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(494f, 120f), new Vector2(990f, 44f));

        LeftPanel = UIFactory.MakeCard(chrome, "01", "GENERATION CONTROLS", UIFactory.Cyan);
        LeftPanel.anchorMin = new Vector2(0f, 0f);
        LeftPanel.anchorMax = new Vector2(0f, 1f);
        LeftPanel.offsetMin = new Vector2(24f, 132f);
        LeftPanel.offsetMax = new Vector2(474f, -24f);

        RightPanel = UIFactory.MakeCard(chrome, "03", "DUNGEON REPORT", UIFactory.Magenta);
        RightPanel.anchorMin = new Vector2(1f, 0f);
        RightPanel.anchorMax = new Vector2(1f, 1f);
        RightPanel.offsetMin = new Vector2(-424f, 132f);
        RightPanel.offsetMax = new Vector2(-24f, -24f);

        RectTransform bottom = UIFactory.Node(chrome, "BottomBar");
        bottom.anchorMin = new Vector2(0f, 0f);
        bottom.anchorMax = new Vector2(1f, 0f);
        bottom.offsetMin = new Vector2(24f, 20f);
        bottom.offsetMax = new Vector2(-24f, 112f);

        Image barBackground = bottom.gameObject.AddComponent<Image>();
        barBackground.sprite = UIFactory.Rounded;
        barBackground.type = Image.Type.Sliced;
        barBackground.color = UIFactory.PanelColor;

        Image barBorder = UIFactory.MakeImage(bottom, "Border", new Color(UIFactory.Cyan.r, UIFactory.Cyan.g, UIFactory.Cyan.b, 0.4f), UIFactory.Outline, true);
        UIFactory.Stretch(barBorder.rectTransform);
        barBorder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        HorizontalLayoutGroup barLayout = UIFactory.Horizontal(bottom.gameObject, 12f);
        barLayout.padding = new RectOffset(24, 24, 12, 12);
        barLayout.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI historyLabel = UIFactory.MakeText(bottom,
            "<color=#FF4DB8>04</color>  SEED\nHISTORY", 17f, UIFactory.Cyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        historyLabel.characterSpacing = 3f;
        UIFactory.Size(historyLabel.gameObject, 120f, 60f);

        historyRow = UIFactory.Node(bottom, "History");
        HorizontalLayoutGroup row = UIFactory.Horizontal(historyRow.gameObject, 10f);
        row.childForceExpandHeight = true;
        UIFactory.Size(historyRow.gameObject, -1f, 64f, 1f);
    }

    private RectTransform LeftPanel { get; set; }
    private RectTransform RightPanel { get; set; }

    private void BuildViewport()
    {
        frame = UIFactory.Node(root, "ViewportFrame");

        Image frameBackground = frame.gameObject.AddComponent<Image>();
        frameBackground.color = Color.black;
        frameBackground.raycastTarget = false;

        view = UIFactory.MakeRawImage(frame, "Viewport");
        view.color = Color.white;
        UIFactory.Stretch(view.rectTransform, 3f);
        view.gameObject.AddComponent<RectMask2D>();

        Image border = UIFactory.MakeImage(frame, "Border", new Color(UIFactory.Cyan.r, UIFactory.Cyan.g, UIFactory.Cyan.b, 0.7f), UIFactory.Outline, true);
        UIFactory.Stretch(border.rectTransform);
    }

    private void ApplyLayout()
    {
        chrome.gameObject.SetActive(!focusMode);

        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;

        if (focusMode)
        {
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;
        }
        else
        {
            frame.offsetMin = new Vector2(490f, 168f);
            frame.offsetMax = new Vector2(-440f, -82f);
        }

        if (focusHint != null) focusHint.gameObject.SetActive(focusMode);
    }

    private void EnsureRenderTexture()
    {
        if (cam == null || view == null)
            return;

        Vector2 size = view.rectTransform.rect.size * canvas.scaleFactor;
        int width = Mathf.Max(64, Mathf.RoundToInt(size.x));
        int height = Mathf.Max(64, Mathf.RoundToInt(size.y));

        if (renderTexture != null && renderTexture.width == width && renderTexture.height == height)
            return;

        cam.targetTexture = null;

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB)
        {
            name = "DungeonViewport",
            antiAliasing = 1
        };
        renderTexture.Create();

        cam.targetTexture = renderTexture;
        view.texture = renderTexture;

        if (dir == null || !dir.FollowCamera)
            gen.FrameCamera();
    }

    // ==================================================================
    // HUD di dalam viewport
    // ==================================================================
    private void BuildHud()
    {
        hudRoot = UIFactory.Node(view.transform, "HUD");
        UIFactory.Stretch(hudRoot);

        flash = UIFactory.MakeImage(hudRoot, "DamageFlash", new Color(1f, 0.1f, 0.15f, 0f), UIFactory.Vignette);
        UIFactory.Stretch(flash.rectTransform);

        // kiri atas
        levelText = UIFactory.MakeText(hudRoot, "LEVEL 1", 22f, UIFactory.Cyan, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.Place(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -16f), new Vector2(300f, 30f));
        levelText.characterSpacing = 5f;

        scoreText = UIFactory.MakeText(hudRoot, "000000", 50f, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.Place(scoreText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -42f), new Vector2(360f, 60f));

        // tengah atas
        itemsText = UIFactory.MakeText(hudRoot, "ITEMS 0 / 0", 22f, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.Place(itemsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(420f, 30f));
        itemsText.characterSpacing = 3f;

        Image barBackground = UIFactory.MakeImage(hudRoot, "ItemsBar", new Color(1f, 1f, 1f, 0.16f), UIFactory.Rounded, true);
        UIFactory.Place(barBackground.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(340f, 11f));

        Image barFill = UIFactory.MakeImage(barBackground.transform, "Fill", UIFactory.Cyan, UIFactory.Rounded, true);
        itemsFill = barFill.rectTransform;
        itemsFill.anchorMin = Vector2.zero;
        itemsFill.anchorMax = new Vector2(0f, 1f);
        itemsFill.offsetMin = Vector2.zero;
        itemsFill.offsetMax = Vector2.zero;

        goalText = UIFactory.MakeText(hudRoot, "", 17f, UIFactory.Gold, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.Place(goalText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(560f, 26f));
        goalText.characterSpacing = 4f;

        roomBanner = UIFactory.MakeText(hudRoot, "", 21f, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.Place(roomBanner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(900f, 30f));
        roomBanner.characterSpacing = 2f;

        autoText = UIFactory.MakeText(hudRoot, "", 16f, UIFactory.Gold, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.Place(autoText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -94f), new Vector2(620f, 24f));

        // kanan atas
        int maxHealth = gm != null ? gm.MaxHealth : 3;
        hpPips = new Image[maxHealth];
        for (int i = 0; i < maxHealth; i++)
        {
            Image pip = UIFactory.MakeImage(hudRoot, "HP" + i, UIFactory.Red, UIFactory.Rounded, true);
            UIFactory.Place(pip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f - i * 44f, -22f), new Vector2(38f, 14f));
            hpPips[maxHealth - 1 - i] = pip;
        }

        hpLabel = UIFactory.MakeText(hudRoot, "HP", 15f, UIFactory.Dim, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.Place(hpLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f - maxHealth * 44f, -18f), new Vector2(40f, 22f));

        timerText = UIFactory.MakeText(hudRoot, "00:00.0", 26f, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.Place(timerText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -44f), new Vector2(260f, 34f));

        seedText = UIFactory.MakeText(hudRoot, "SEED 0", 16f, UIFactory.Dim, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.Place(seedText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -80f), new Vector2(300f, 24f));
        seedText.characterSpacing = 3f;

        // pipeline tracker (bawah viewport)
        trackerRoot = UIFactory.Node(hudRoot, "PipelineTracker");
        trackerRoot.anchorMin = new Vector2(0f, 0f);
        trackerRoot.anchorMax = new Vector2(1f, 0f);
        trackerRoot.pivot = new Vector2(0.5f, 0f);
        trackerRoot.offsetMin = new Vector2(0f, 0f);
        trackerRoot.offsetMax = new Vector2(0f, 96f);

        Image band = UIFactory.MakeImage(trackerRoot, "Band", new Color(0.01f, 0.02f, 0.05f, 0.72f));
        UIFactory.Stretch(band.rectTransform);

        RectTransform chips = UIFactory.Node(trackerRoot, "Chips");
        chips.anchorMin = new Vector2(0f, 1f);
        chips.anchorMax = new Vector2(1f, 1f);
        chips.pivot = new Vector2(0.5f, 1f);
        chips.offsetMin = new Vector2(16f, -42f);
        chips.offsetMax = new Vector2(-16f, -10f);
        HorizontalLayoutGroup chipLayout = UIFactory.Horizontal(chips.gameObject, 6f);
        chipLayout.childForceExpandWidth = true;

        int n = DungeonDemoDirector.PhaseNames.Length;
        phaseChips = new Image[n];
        phaseLabels = new TextMeshProUGUI[n];
        for (int i = 0; i < n; i++)
        {
            Image chip = UIFactory.MakeImage(chips, "Chip" + i, new Color(1f, 1f, 1f, 0.08f), UIFactory.Rounded, true);
            UIFactory.Size(chip.gameObject, -1f, 30f, 1f);
            TextMeshProUGUI t = UIFactory.MakeText(chip.transform, (i + 1) + " " + DungeonDemoDirector.PhaseNames[i], 11.5f,
                UIFactory.Dim, TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Stretch(t.rectTransform);
            phaseChips[i] = chip;
            phaseLabels[i] = t;
        }

        captionText = UIFactory.MakeText(trackerRoot, "", 17f, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, true);
        captionText.rectTransform.anchorMin = new Vector2(0f, 0f);
        captionText.rectTransform.anchorMax = new Vector2(1f, 0f);
        captionText.rectTransform.pivot = new Vector2(0.5f, 0f);
        captionText.rectTransform.offsetMin = new Vector2(22f, 6f);
        captionText.rectTransform.offsetMax = new Vector2(-22f, 50f);

        // toast
        toastText = UIFactory.MakeText(hudRoot, "", 44f, UIFactory.Cyan, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Place(toastText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(900f, 70f));
        toastText.characterSpacing = 6f;

        focusHint = UIFactory.MakeText(hudRoot, "TAB: tampilkan panel lab", 14f, new Color(0.7f, 0.8f, 0.9f, 0.6f), TextAlignmentOptions.TopRight);
        UIFactory.Place(focusHint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -108f), new Vector2(400f, 22f));
    }

    private static RectTransform Band(RectTransform parent, bool top, float height, Color color)
    {
        Image band = UIFactory.MakeImage(parent, top ? "TopBand" : "BottomBand", color);
        RectTransform rt = band.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.offsetMin = top ? new Vector2(0f, -height) : Vector2.zero;
        rt.offsetMax = top ? Vector2.zero : new Vector2(0f, height);
        return rt;
    }

    private void BuildTitle()
    {
        titlePanel = UIFactory.Node(view.transform, "TitlePanel");
        UIFactory.Stretch(titlePanel);

        Color bandColor = new Color(0.01f, 0.02f, 0.05f, 0.7f);
        Band(titlePanel, true, 150f, bandColor);
        Band(titlePanel, false, 210f, bandColor);

        TextMeshProUGUI title = UIFactory.MakeText(titlePanel, "BSP DUNGEON", 66f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(940f, 84f));
        title.characterSpacing = 12f;

        TextMeshProUGUI subtitle = UIFactory.MakeText(titlePanel, "PROCEDURAL DUNGEON GENERATION  ·  BINARY SPACE PARTITIONING",
            19f, UIFactory.Cyan, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(980f, 30f));
        subtitle.characterSpacing = 6f;

        TextMeshProUGUI mission = UIFactory.MakeText(titlePanel,
            "Setiap dungeon dibangun oleh <b>BSP</b>: ruang dibagi rekursif, Room dibuat di tiap Leaf, lalu dihubungkan Corridor L\n" +
            "dan divalidasi <b>BFS</b>. Kumpulkan Item, hindari Enemy, capai <b><color=#FFD14D>GOAL</color></b>.",
            18f, new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center, FontStyles.Normal, true);
        UIFactory.Place(mission.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(940f, 64f));

        Button start = UIFactory.MakeButton(titlePanel, "START  [ENTER]", 24f, () => gm.StartGame(), new Color(0.1f, 0.55f, 0.65f));
        UIFactory.Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 52f), new Vector2(300f, 60f));

        Button demo = UIFactory.MakeButton(titlePanel, "DEMO AUTOPILOT  [F2]", 22f, () => gm.StartDemo(), new Color(0.55f, 0.38f, 0.08f));
        UIFactory.Place((RectTransform)demo.transform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(10f, 52f), new Vector2(300f, 60f));

        titleCountdown = UIFactory.MakeText(titlePanel, "", 17f, UIFactory.Dim, TextAlignmentOptions.Center);
        UIFactory.Place(titleCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(900f, 26f));
    }

    private RectTransform MakeOverlayPanel(string name, Color dimColor, string heading, Color headingColor, out TextMeshProUGUI headingText)
    {
        RectTransform panel = UIFactory.Node(view.transform, name);
        UIFactory.Stretch(panel);

        Image dim = UIFactory.MakeImage(panel, "Dim", dimColor);
        UIFactory.Stretch(dim.rectTransform);

        Image card = UIFactory.MakeImage(panel, "Card", new Color(0.03f, 0.05f, 0.1f, 0.92f), UIFactory.Rounded, true);
        UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 400f));

        Image border = UIFactory.MakeImage(card.transform, "Border", new Color(headingColor.r, headingColor.g, headingColor.b, 0.7f), UIFactory.Outline, true);
        UIFactory.Stretch(border.rectTransform);

        headingText = UIFactory.MakeText(card.transform, heading, 54f, headingColor, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.Place(headingText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(600f, 70f));
        headingText.characterSpacing = 10f;

        return card.rectTransform;
    }

    private void BuildLevelComplete()
    {
        RectTransform card = MakeOverlayPanel("LevelCompletePanel", new Color(0f, 0.04f, 0.02f, 0.4f), "LEVEL CLEARED", UIFactory.Gold, out _);
        levelPanel = (RectTransform)card.parent;

        levelStats = UIFactory.MakeText(card, "", 20f, Color.white, TextAlignmentOptions.Top, FontStyles.Normal, true);
        UIFactory.Place(levelStats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(560f, 170f));
        levelStats.lineSpacing = 10f;

        Button next = UIFactory.MakeButton(card, "NEXT DUNGEON  [ENTER]", 22f, () => gm.NextLevel(), new Color(0.1f, 0.55f, 0.65f));
        UIFactory.Place((RectTransform)next.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(340f, 56f));

        levelCountdown = UIFactory.MakeText(card, "", 15f, UIFactory.Dim, TextAlignmentOptions.Center);
        UIFactory.Place(levelCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(560f, 24f));
    }

    private void BuildGameOver()
    {
        RectTransform card = MakeOverlayPanel("GameOverPanel", new Color(0.08f, 0f, 0.02f, 0.45f), "CAUGHT!", UIFactory.Red, out _);
        overPanel = (RectTransform)card.parent;

        TextMeshProUGUI info = UIFactory.MakeText(card,
            "HP habis karena menabrak Enemy.\nRetry membuat ulang dungeon dengan <b>seed yang sama</b> —\nlayout identik (reproducible).",
            19f, Color.white, TextAlignmentOptions.Top, FontStyles.Normal, true);
        UIFactory.Place(info.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(560f, 120f));

        Button retry = UIFactory.MakeButton(card, "RETRY  [ENTER]", 22f, () => gm.Retry(), new Color(0.6f, 0.15f, 0.2f));
        UIFactory.Place((RectTransform)retry.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(300f, 56f));

        overCountdown = UIFactory.MakeText(card, "", 15f, UIFactory.Dim, TextAlignmentOptions.Center);
        UIFactory.Place(overCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(560f, 24f));
    }

    private void BuildPause()
    {
        RectTransform card = MakeOverlayPanel("PausePanel", new Color(0f, 0f, 0f, 0.5f), "PAUSED", UIFactory.Cyan, out _);
        pausePanel = (RectTransform)card.parent;

        TextMeshProUGUI info = UIFactory.MakeText(card, "Tekan <b>P</b> / <b>ENTER</b> untuk melanjutkan.", 20f, Color.white, TextAlignmentOptions.Top);
        UIFactory.Place(info.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(560f, 40f));

        Button resume = UIFactory.MakeButton(card, "RESUME", 22f, () => gm.TogglePause(), new Color(0.1f, 0.55f, 0.65f));
        UIFactory.Place((RectTransform)resume.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(260f, 56f));
    }

    // ==================================================================
    // 01 Generation Controls (kiri)
    // ==================================================================
    private TextMeshProUGUI Section(Transform parent, string text)
    {
        TextMeshProUGUI t = UIFactory.MakeText(parent, text, 14f, UIFactory.Magenta, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
        t.characterSpacing = 4f;
        UIFactory.Size(t.gameObject, -1f, 22f);
        return t;
    }

    private void BuildControls()
    {
        Transform p = LeftPanel;

        Section(p, "SEED");

        RectTransform seedRow = UIFactory.Node(p, "SeedRow");
        UIFactory.Horizontal(seedRow.gameObject, 8f);
        UIFactory.Size(seedRow.gameObject, -1f, 40f);

        seedInput = UIFactory.MakeInput(seedRow, "seed…", 20f);
        UIFactory.Size(seedInput.gameObject, -1f, 40f, 1f);
        seedInput.onSubmit.AddListener(_ => ApplySeedInput());

        Button generate = UIFactory.MakeButton(seedRow, "GENERATE", 17f, ApplySeedInput, new Color(0.1f, 0.55f, 0.65f));
        UIFactory.Size(generate.gameObject, 130f, 40f);

        RectTransform seedButtons = UIFactory.Node(p, "SeedButtons");
        HorizontalLayoutGroup sb = UIFactory.Horizontal(seedButtons.gameObject, 8f);
        sb.childForceExpandWidth = true;
        UIFactory.Size(seedButtons.gameObject, -1f, 34f);

        Button prev = UIFactory.MakeButton(seedButtons, "<  SEED-1", 15f, () => gm.Regenerate(gen.Seed - 1), new Color(0.14f, 0.2f, 0.32f));
        UIFactory.Size(prev.gameObject, -1f, 34f, 1f);
        Button nextSeed = UIFactory.MakeButton(seedButtons, "SEED+1  >", 15f, () => gm.Regenerate(gen.Seed + 1), new Color(0.14f, 0.2f, 0.32f));
        UIFactory.Size(nextSeed.gameObject, -1f, 34f, 1f);
        Button random = UIFactory.MakeButton(seedButtons, "RANDOM", 15f, () => gm.Regenerate(Random.Range(1, 99999)), new Color(0.45f, 0.16f, 0.4f));
        UIFactory.Size(random.gameObject, -1f, 34f, 1f);

        Section(p, "BSP & ROOM");
        SliderRow(p, "Map Width", 30, 90, () => gen.MapWidth, v => gen.MapWidth = v);
        SliderRow(p, "Map Height", 24, 60, () => gen.MapHeight, v => gen.MapHeight = v);
        SliderRow(p, "Min Leaf Size", 6, 20, () => gen.MinLeafSize, v => gen.MinLeafSize = v);
        SliderRow(p, "Max Leaf Size", 10, 32, () => gen.MaxLeafSize, v => gen.MaxLeafSize = v);
        SliderRow(p, "Min Room Size", 3, 10, () => gen.MinRoomSize, v => gen.MinRoomSize = v);
        SliderRow(p, "Room Margin", 1, 4, () => gen.RoomMargin, v => gen.RoomMargin = v);
        SliderRow(p, "Corridor Width", 1, 3, () => gen.CorridorWidth, v => gen.CorridorWidth = v);

        Section(p, "SPAWN");
        SliderRow(p, "Enemy Count", 0, 25, () => gen.EnemyCount, v => gen.EnemyCount = v);
        SliderRow(p, "Item Count", 0, 15, () => gen.ItemCount, v => gen.ItemCount = v);

        Section(p, "GOAL STRATEGY");
        RectTransform goalRow = UIFactory.Node(p, "GoalRow");
        HorizontalLayoutGroup gr = UIFactory.Horizontal(goalRow.gameObject, 6f);
        gr.childForceExpandWidth = true;
        UIFactory.Size(goalRow.gameObject, -1f, 32f);

        string[] goalLabels = { "A  LAST ROOM", "B  FARTHEST", "C  BFS PATH" };
        goalButtons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Button b = UIFactory.MakeButton(goalRow, goalLabels[i], 13.5f, () =>
            {
                gen.GoalMode = (GoalStrategy)index;
                RefreshGoalButtons();
                ScheduleRegenerate();
            }, new Color(0.14f, 0.2f, 0.32f));
            UIFactory.Size(b.gameObject, -1f, 32f, 1f);
            goalButtons[i] = b;
        }

        Section(p, "OPTIONS");
        ToggleRow(p, "Animasi pipeline PCG  [V]", () => dir.RevealEnabled, v => dir.RevealEnabled = v);
        ToggleRow(p, "Autopilot BFS  [F2]", () => dir.AutopilotEnabled, v =>
        {
            dir.AutopilotEnabled = v;
            if (v && gm.State == DungeonState.Title) gm.StartGame();
        });
        ToggleRow(p, "Overlay Leaf/Room BSP  [B]", () => dir.ShowOverlay, v => dir.ShowOverlay = v);
        ToggleRow(p, "Room challenges (AI musuh + hazard)", () => gen.Challenges != null && gen.Challenges.ChallengesEnabled, v =>
        {
            if (gen.Challenges == null) return;
            gen.Challenges.ChallengesEnabled = v;
            ScheduleRegenerate();
        });
        ToggleRow(p, "Constraint: Enemy jauh dari corridor", () => gen.KeepCorridorsClear, v =>
        {
            gen.KeepCorridorsClear = v;
            ScheduleRegenerate();
        });
    }

    private void SliderRow(Transform parent, string label, int min, int max, System.Func<int> getter, System.Action<int> setter)
    {
        RectTransform row = UIFactory.Node(parent, "Row_" + label);
        UIFactory.Horizontal(row.gameObject, 10f);
        UIFactory.Size(row.gameObject, -1f, 26f);

        TextMeshProUGUI name = UIFactory.MakeText(row, label, 16f, Color.white, TextAlignmentOptions.MidlineLeft);
        UIFactory.Size(name.gameObject, 140f, 26f);

        TextMeshProUGUI value = null;
        Slider slider = UIFactory.MakeSlider(row, min, max, true, getter(), v =>
        {
            if (suppressCallbacks) return;
            setter(Mathf.RoundToInt(v));
            value.text = getter().ToString();
            ScheduleRegenerate();
        }, UIFactory.Cyan);
        UIFactory.Size(slider.gameObject, -1f, 26f, 1f);

        value = UIFactory.MakeText(row, getter().ToString(), 17f, UIFactory.Cyan, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        UIFactory.Size(value.gameObject, 40f, 26f);

        controlRefreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(getter());
            value.text = getter().ToString();
        });
    }

    private void ToggleRow(Transform parent, string label, System.Func<bool> getter, System.Action<bool> setter)
    {
        Toggle toggle = UIFactory.MakeToggle(parent, label, getter(), v =>
        {
            if (!suppressCallbacks) setter(v);
        }, 16f);
        controlRefreshers.Add(() => toggle.SetIsOnWithoutNotify(getter()));
    }

    private void RefreshGoalButtons()
    {
        for (int i = 0; i < goalButtons.Length; i++)
        {
            bool selected = (int)gen.GoalMode == i;
            Color c = selected ? new Color(0.65f, 0.48f, 0.1f) : new Color(0.14f, 0.2f, 0.32f);
            ColorBlock colors = goalButtons[i].colors;
            colors.normalColor = c;
            colors.selectedColor = c;
            colors.highlightedColor = Color.Lerp(c, Color.white, 0.25f);
            goalButtons[i].colors = colors;
        }
    }

    private void RefreshControls()
    {
        suppressCallbacks = true;
        foreach (System.Action refresh in controlRefreshers) refresh();
        suppressCallbacks = false;
        RefreshGoalButtons();
        if (!seedInput.isFocused) seedInput.SetTextWithoutNotify(gen.Seed.ToString());
    }

    private void ApplySeedInput()
    {
        if (int.TryParse(seedInput.text, out int value))
            gm.Regenerate(value);
        UIFactory.ClearSelection();
    }

    private void ScheduleRegenerate()
    {
        pendingRegenerate = 0.45f;
    }

    // ==================================================================
    // 03 Dungeon Report (kanan)
    // ==================================================================
    private void BuildReport()
    {
        Transform p = RightPanel;

        Section(p, "MINIMAP · BSP LEAF & ROOM");

        RectTransform mapBox = UIFactory.Node(p, "MinimapBox");
        UIFactory.Size(mapBox.gameObject, -1f, 186f);

        Image mapBg = UIFactory.MakeImage(mapBox, "Bg", new Color(0f, 0f, 0f, 0.35f), UIFactory.Rounded, true);
        UIFactory.Stretch(mapBg.rectTransform);

        minimap = UIFactory.MakeRawImage(mapBox, "Minimap");
        minimapRect = minimap.rectTransform;
        UIFactory.Place(minimapRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(352f, 190f));

        markerRoot = UIFactory.Node(minimapRect, "Markers");
        UIFactory.Stretch(markerRoot);

        goalMarker = Marker(UIFactory.Gold, 11f);
        playerMarker = Marker(UIFactory.Cyan, 11f);

        Section(p, "ROOM ROLES");
        roleText = UIFactory.MakeText(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
        UIFactory.Size(roleText.gameObject, -1f, 40f);

        Section(p, "METRICS");
        MetricRow(p, "seed", "Seed / Attempt");
        MetricRow(p, "bsp", "BSP Leaves / Rooms");
        MetricRow(p, "floor", "Floor / Corridor");
        MetricRow(p, "wall", "Wall Objects");
        MetricRow(p, "dist", "Start → Goal");
        MetricRow(p, "spawn", "Enemy / Item / Hazard");
        MetricRow(p, "time", "Layout Hash / Time");

        Section(p, "CONSTRAINTS");
        string[] labels =
        {
            "Room ≥ 2, Start & Goal di Floor",
            "BFS: Goal dapat dicapai",
            "Flood fill: semua room & item terjangkau",
            "Enemy modul ≥ 8 tile dari Start",
            "Safe room & corridor bebas Enemy",
            "Boss hanya di Goal room (§116)",
            "Enemy & hazard tetap di room-nya",
            "Soft: Treasure di room samping (§117)",
            "Soft: Goal jauh (≥ 40 langkah)"
        };

        foreach (string label in labels)
        {
            RectTransform row = UIFactory.Node(p, "Constraint");
            UIFactory.Horizontal(row.gameObject, 10f).childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Size(row.gameObject, -1f, 19f);

            Image pip = UIFactory.MakeImage(row, "Pip", UIFactory.Green, UIFactory.Circle);
            UIFactory.Size(pip.gameObject, 12f, 12f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;

            TextMeshProUGUI text = UIFactory.MakeText(row, label, 15f, Color.white, TextAlignmentOptions.MidlineLeft);
            UIFactory.Size(text.gameObject, -1f, 19f, 1f);

            constraintRows.Add(new ConstraintRow { pip = pip, text = text });
        }
    }

    private Image Marker(Color color, float size)
    {
        Image m = UIFactory.MakeImage(markerRoot, "Marker", color, UIFactory.Circle);
        m.rectTransform.sizeDelta = new Vector2(size, size);
        m.rectTransform.anchorMin = m.rectTransform.anchorMax = Vector2.zero;
        return m;
    }

    private void MetricRow(Transform parent, string key, string label)
    {
        RectTransform row = UIFactory.Node(parent, "Metric_" + key);
        UIFactory.Horizontal(row.gameObject, 8f);
        UIFactory.Size(row.gameObject, -1f, 22f);

        TextMeshProUGUI name = UIFactory.MakeText(row, label, 15.5f, UIFactory.Dim, TextAlignmentOptions.MidlineLeft);
        UIFactory.Size(name.gameObject, -1f, 22f, 1f);

        TextMeshProUGUI value = UIFactory.MakeText(row, "-", 16f, Color.white, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        UIFactory.Size(value.gameObject, 190f, 22f);

        metricValues[key] = value;
    }

    private void SetMetric(string key, string value)
    {
        if (metricValues.TryGetValue(key, out TextMeshProUGUI t)) t.text = value;
    }

    private void RebuildMinimap()
    {
        int w = gen.MapWidth;
        int h = gen.MapHeight;
        const int px = 4;

        if (minimapTexture != null) Destroy(minimapTexture);
        minimapTexture = new Texture2D(w * px, h * px, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[w * px * h * px];
        Color empty = new Color(0f, 0f, 0f, 0f);
        Color wall = new Color(0.36f, 0.22f, 0.6f, 1f);
        Color room = new Color(0.23f, 0.29f, 0.42f, 1f);
        Color corridor = new Color(0.12f, 0.45f, 0.55f, 1f);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                TileType t = gen.GetTile(x, y);
                Color c;

                if (t == TileType.Wall)
                {
                    bool nearFloor = false;
                    for (int dx = -1; dx <= 1 && !nearFloor; dx++)
                        for (int dy = -1; dy <= 1 && !nearFloor; dy++)
                            if (gen.GetTile(x + dx, y + dy) != TileType.Wall && gen.IsInsideGrid(x + dx, y + dy))
                                nearFloor = true;
                    c = nearFloor ? wall : empty;
                }
                else if (t == TileType.Start) c = UIFactory.Green;
                else if (gen.IsCorridorTile(x, y)) c = corridor;
                else
                {
                    c = room;
                    RoomChallengeSystem rc = gen.Challenges;
                    if (rc != null && rc.Active)
                    {
                        int ri = rc.RoomIndexAt(new Vector2Int(x, y));
                        if (ri >= 0) c = Color.Lerp(room, RoomChallengeSystem.RoleColor(rc.RoleOf(ri)), 0.42f);
                    }
                }

                for (int i = 0; i < px; i++)
                    for (int j = 0; j < px; j++)
                        pixels[(y * px + j) * w * px + x * px + i] = c;
            }
        }

        // garis batas Leaf BSP
        Color leafLine = new Color(0.3f, 0.85f, 1f, 0.9f);
        foreach (BSPNode leaf in gen.LeafNodes)
        {
            int x0 = leaf.x * px, x1 = (leaf.x + leaf.width) * px - 1;
            int y0 = leaf.y * px, y1 = (leaf.y + leaf.height) * px - 1;
            for (int x = x0; x <= x1; x++) { Blend(pixels, w * px, x, y0, leafLine); Blend(pixels, w * px, x, y1, leafLine); }
            for (int y = y0; y <= y1; y++) { Blend(pixels, w * px, x0, y, leafLine); Blend(pixels, w * px, x1, y, leafLine); }
        }

        minimapTexture.SetPixels(pixels);
        minimapTexture.Apply();
        minimap.texture = minimapTexture;

        // fit aspect di dalam kotak 352×230
        float scale = Mathf.Min(352f / w, 190f / h);
        minimapRect.sizeDelta = new Vector2(w * scale, h * scale);

        // marker statis: turret
        foreach (Image m in staticMarkers) Destroy(m.gameObject);
        staticMarkers.Clear();
        if (gen.Challenges != null && gen.Challenges.Active)
        {
            foreach (Vector2Int t in gen.Challenges.SolidTiles)
            {
                Image m = Marker(new Color(0.8f, 0.5f, 1f), 9f);
                m.sprite = UIFactory.Rounded;
                PlaceMarker(m, new Vector2(t.x, t.y));
                staticMarkers.Add(m);
            }
        }

        PlaceMarker(goalMarker, gen.GoalPosition);
        goalMarker.transform.SetAsLastSibling();
        playerMarker.transform.SetAsLastSibling();
    }

    private static void Blend(Color[] pixels, int stride, int x, int y, Color c)
    {
        int i = y * stride + x;
        if (i < 0 || i >= pixels.Length) return;
        pixels[i] = Color.Lerp(pixels[i], new Color(c.r, c.g, c.b, 1f), c.a);
    }

    private void PlaceMarker(Image marker, Vector2 grid)
    {
        Vector2 size = minimapRect.sizeDelta;
        marker.rectTransform.anchoredPosition = new Vector2(
            (grid.x + 0.5f) / gen.MapWidth * size.x,
            (grid.y + 0.5f) / gen.MapHeight * size.y);
    }

    private void UpdateMinimapMarkers()
    {
        if (gm.Player != null)
        {
            Vector3 p = gm.Player.transform.position / gen.TileSize;
            PlaceMarker(playerMarker, new Vector2(p.x, p.z));
            playerMarker.enabled = true;
        }
        else
        {
            playerMarker.enabled = false;
        }

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
        goalMarker.rectTransform.sizeDelta = Vector2.one * (10f + 4f * pulse);

        // marker Enemy (dinamis karena Enemy bergerak)
        List<EnemyController> enemies = EnemyController.All;
        while (enemyMarkers.Count < enemies.Count) enemyMarkers.Add(Marker(UIFactory.Red, 8f));
        for (int i = 0; i < enemyMarkers.Count; i++)
        {
            bool active = i < enemies.Count;
            enemyMarkers[i].gameObject.SetActive(active);
            if (!active) continue;

            EnemyController e = enemies[i];
            Vector3 ep = e.transform.position / gen.TileSize;
            PlaceMarker(enemyMarkers[i], new Vector2(ep.x, ep.z));
            float size = e.IsBoss ? 14f : 8f;
            enemyMarkers[i].rectTransform.sizeDelta = Vector2.one * (e.IsThreatening ? size * (0.9f + 0.3f * pulse) : size);
            enemyMarkers[i].color = e.IsThreatening ? new Color(1f, 0.15f, 0.15f) : e.IsBoss ? new Color(0.8f, 0.05f, 0.15f) : UIFactory.Red;
        }
        playerMarker.transform.SetAsLastSibling();

        // marker Item (dinamis karena bisa diambil)
        ItemPickup[] items = FindObjectsByType<ItemPickup>(FindObjectsSortMode.None);
        while (itemMarkers.Count < items.Length) itemMarkers.Add(Marker(new Color(0.55f, 1f, 1f), 6f));
        for (int i = 0; i < itemMarkers.Count; i++)
        {
            bool active = i < items.Length;
            itemMarkers[i].gameObject.SetActive(active);
            if (active)
            {
                Vector3 p = items[i].transform.position / gen.TileSize;
                PlaceMarker(itemMarkers[i], new Vector2(p.x, p.z));
            }
        }
    }

    private void UpdateReport()
    {
        DungeonMetrics m = gen.Metrics;
        int totalWalls = gen.MapWidth * gen.MapHeight - m.floorTiles;

        SetMetric("seed", m.seed + "  /  #" + m.attempts + (m.valid ? "  <color=#4DFF80>VALID</color>" : "  <color=#FF5266>INVALID</color>"));
        SetMetric("bsp", m.leafCount + "  /  " + m.roomCount);
        SetMetric("floor", m.floorTiles + " (" + m.floorPercentage.ToString("0") + "%)  /  " + m.corridorTiles);
        SetMetric("wall", m.wallObjects + "  <color=#8899AA>dari " + totalWalls + "</color>");
        SetMetric("dist", m.startGoalPath + " langkah  <color=#8899AA>(euclid " + m.startGoalEuclidean.ToString("0.0") + ")</color>");
        RoomChallengeSystem rcs = gen.Challenges;
        int hazardCount = rcs != null && rcs.Active ? rcs.SpikeCount + rcs.TurretCount : 0;
        SetMetric("spawn", EnemyController.All.Count + "  /  " + m.itemCount + (rcs != null && rcs.HasTreasure ? "+1" : "") + "  /  " + hazardCount);
        if (roleText != null) roleText.text = rcs != null ? rcs.RoleSummary() : "";
        SetMetric("time", "<color=#4DF2FF>" + m.layoutHash + "</color>  <color=#8899AA>" + m.generationMs.ToString("0") + " ms</color>");

        // constraint check dari data generator
        bool minEnemy = true, safe = true;
        foreach (Vector2Int e in gen.EnemyPositions)
        {
            if (Vector2Int.Distance(e, gen.StartPosition) < gen.MinEnemyDistanceFromStart) minEnemy = false;
            if (gen.Rooms.Count > 0 && gen.Rooms[0].Contains(e)) safe = false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (gen.IsCorridorTile(e.x + dx, e.y + dy)) safe = false;
        }

        bool startGoalOk = gen.GetTile(gen.StartPosition.x, gen.StartPosition.y) == TileType.Start &&
                           gen.GetTile(gen.GoalPosition.x, gen.GoalPosition.y) == TileType.Goal &&
                           gen.StartPosition != gen.GoalPosition;

        bool active = rcs != null && rcs.Active;
        bool bossOk = true, contained = true;
        if (active)
        {
            int bossRooms = 0;
            foreach (RoomRole r in rcs.Roles) if (r == RoomRole.Boss) bossRooms++;
            bossOk = bossRooms == 1 && rcs.BossRoom == rcs.RoomIndexAt(gen.GoalPosition);

            foreach (EnemyController e in EnemyController.All)
                if (rcs.RoomIndexAt(gen.WorldToGrid(e.transform.position)) < 0) contained = false;
        }

        SetConstraint(0, m.roomCount >= 2 && startGoalOk, true);
        SetConstraint(1, m.valid && m.startGoalPath > 0, true);
        SetConstraint(2, m.mandatoryReachable, true);
        if (constraintRows.Count > 2)
            constraintRows[2].text.text = "Flood fill: " + m.mandatoryReached + "/" + m.mandatoryChecked + " area wajib terjangkau" +
                                          (m.repairs > 0 ? " (repair " + m.repairs + ")" : "");
        SetConstraint(3, minEnemy, true);
        SetConstraint(4, safe || !gen.KeepCorridorsClear, gen.KeepCorridorsClear);
        SetConstraint(5, bossOk, true, !active);
        SetConstraint(6, contained, true, !active);
        SetConstraint(7, active && rcs.TreasureRoom >= 0 && !rcs.IsOnMainPath(rcs.TreasureRoom), false, !active);
        SetConstraint(8, m.startGoalPath >= 40, false);
    }

    private void SetConstraint(int index, bool ok, bool hard, bool notApplicable = false)
    {
        if (index >= constraintRows.Count) return;
        ConstraintRow row = constraintRows[index];
        if (notApplicable)
        {
            row.pip.color = new Color(1f, 1f, 1f, 0.2f);
            row.text.color = new Color(0.55f, 0.6f, 0.68f);
            return;
        }
        row.pip.color = ok ? UIFactory.Green : (hard ? UIFactory.Red : UIFactory.Gold);
        row.text.color = ok ? Color.white : (hard ? UIFactory.Red : UIFactory.Gold);
    }

    // ==================================================================
    // 04 Seed History (bawah)
    // ==================================================================
    private void RebuildHistory()
    {
        for (int i = historyRow.childCount - 1; i >= 0; i--)
            Destroy(historyRow.GetChild(i).gameObject);

        IReadOnlyList<DungeonGameManager.HistoryEntry> history = gm.History;
        for (int i = 0; i < history.Count && i < 8; i++)
        {
            DungeonGameManager.HistoryEntry h = history[i];
            int s = h.seed;
            Color bg = i == 0 ? new Color(0.1f, 0.42f, 0.5f) : new Color(0.1f, 0.15f, 0.25f);
            Button chip = UIFactory.MakeButton(historyRow, "", 14f, () => gm.Regenerate(s), bg);
            UIFactory.Size(chip.gameObject, 158f, 64f);

            TextMeshProUGUI t = chip.GetComponentInChildren<TextMeshProUGUI>();
            t.fontStyle = FontStyles.Normal;
            t.text = "<b><size=17>#" + h.seed + "</size></b>\n<color=#AFC3D6>" + h.rooms + " room · " + h.path + " lgk</color>";
        }
    }

    // ==================================================================
    // Update
    // ==================================================================
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab) && !UIFactory.TextInputFocused())
        {
            focusMode = !focusMode;
            ApplyLayout();
            Canvas.ForceUpdateCanvases();
        }

        EnsureRenderTexture();

        if (pendingRegenerate > 0f)
        {
            pendingRegenerate -= Time.unscaledDeltaTime;
            if (pendingRegenerate <= 0f)
            {
                gm.RegenerateSameSeed();
                ShowToast("PARAMETER BERUBAH → REGENERATE", UIFactory.Dim);
            }
        }

        if (gen.GenerationCount != builtGeneration)
        {
            builtGeneration = gen.GenerationCount;
            RebuildMinimap();
            UpdateReport();
            RefreshControls();
        }

        int signature = gm.History.Count > 0 ? gm.History[0].seed * 31 + gm.History.Count + builtGeneration * 7 : 0;
        if (signature != historySignature)
        {
            historySignature = signature;
            RebuildHistory();
        }

        reportTimer -= Time.unscaledDeltaTime;
        if (reportTimer <= 0f)
        {
            reportTimer = 0.5f;
            UpdateReport();
            // sinkron slider/toggle bila diubah via keyboard (V/B/C/F2)
            suppressCallbacks = true;
            foreach (System.Action refresh in controlRefreshers) refresh();
            suppressCallbacks = false;
        }

        UpdateMinimapMarkers();
        UpdateHud();
        UpdatePanels();
        UpdateFloating();
    }

    private void UpdateHud()
    {
        bool title = gm.State == DungeonState.Title;
        bool revealing = dir.IsRevealing;

        levelText.gameObject.SetActive(!title);
        scoreText.gameObject.SetActive(!title);
        itemsText.gameObject.SetActive(!title);
        itemsFill.parent.gameObject.SetActive(!title);
        timerText.gameObject.SetActive(!title);
        foreach (Image pip in hpPips) pip.gameObject.SetActive(!title);
        hpLabel.gameObject.SetActive(!title);
        goalText.gameObject.SetActive(!title);
        trackerRoot.gameObject.SetActive(!title);

        levelText.text = "LEVEL " + gm.Level;
        displayScore = Mathf.MoveTowards(displayScore, gm.Score, Mathf.Max(30f, Mathf.Abs(gm.Score - displayScore) * 8f) * Time.unscaledDeltaTime);
        scoreText.text = Mathf.RoundToInt(displayScore).ToString("000000");

        itemsText.text = "ITEMS  " + gm.ItemsCollected + " / " + gm.ItemsTotal +
            (gen.Challenges != null && gen.Challenges.HasTreasure
                ? "   <color=#FFD84D>CHEST " + (gm.TreasureCollected ? "1" : "0") + "/1</color>" : "");

        if (roomBannerTimer > 0f)
        {
            roomBannerTimer -= Time.unscaledDeltaTime;
            float ba = Mathf.Clamp01(roomBannerTimer / 0.6f);
            roomBanner.color = new Color(roomBannerColor.r, roomBannerColor.g, roomBannerColor.b, ba);
        }
        else
        {
            roomBanner.text = "";
        }
        roomBanner.gameObject.SetActive(!title);
        float k = gm.ItemsTotal > 0 ? (float)gm.ItemsCollected / gm.ItemsTotal : 0f;
        itemsFill.anchorMax = new Vector2(Mathf.MoveTowards(itemsFill.anchorMax.x, k, Time.unscaledDeltaTime * 2f), 1f);

        DungeonMetrics m = gen.Metrics;
        goalText.text = revealing
            ? "<color=#4DD9FF>GENERATING…</color>  <size=14><color=#8899AA>SPACE skip</color></size>"
            : "GOAL · " + m.startGoalPath + " LANGKAH DARI START";

        if (dir.AutopilotEnabled)
        {
            autoText.text = "AUTOPILOT ON  [F2]" + (dir.AutopilotStatus.Length > 0 ? "  ·  " + dir.AutopilotStatus : "");
            autoText.gameObject.SetActive(!title);
        }
        else
        {
            autoText.gameObject.SetActive(false);
        }

        float t = gm.LevelTime;
        timerText.text = Mathf.FloorToInt(t / 60f).ToString("00") + ":" + (t % 60f).ToString("00.0");
        seedText.text = "SEED " + gen.Seed + (Time.timeScale != 1f && gm.State != DungeonState.Paused ? "  ·  " + Time.timeScale.ToString("0.0") + "x" : "");

        for (int i = 0; i < hpPips.Length; i++)
            hpPips[i].color = i < gm.Health ? UIFactory.Red : new Color(1f, 1f, 1f, 0.14f);

        // pipeline tracker
        int phase = dir.Phase;
        for (int i = 0; i < phaseChips.Length; i++)
        {
            Color chip, label;
            if (i == phase)
            {
                float pulse = revealing ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
                chip = new Color(UIFactory.Gold.r, UIFactory.Gold.g, UIFactory.Gold.b, 0.85f * pulse);
                label = new Color(0.08f, 0.06f, 0.02f);
            }
            else if (i < phase)
            {
                chip = new Color(UIFactory.Green.r, UIFactory.Green.g, UIFactory.Green.b, 0.22f);
                label = UIFactory.Green;
            }
            else
            {
                chip = new Color(1f, 1f, 1f, 0.06f);
                label = new Color(0.5f, 0.56f, 0.65f);
            }
            phaseChips[i].color = chip;
            phaseLabels[i].color = label;
        }

        captionText.text = dir.PhaseDetail;

        // toast
        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(toastTimer / 0.4f);
            toastText.color = new Color(toastColor.r, toastColor.g, toastColor.b, a);
            float s = 1f + Mathf.Max(0f, toastTimer - 1.3f) * 0.6f;
            toastText.rectTransform.localScale = Vector3.one * s;
        }
        else
        {
            toastText.text = "";
        }

        // damage flash
        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 1.8f);
        flash.color = new Color(1f, 0.1f, 0.15f, flashAlpha);
    }

    private void UpdatePanels()
    {
        DungeonState s = gm.State;

        titlePanel.gameObject.SetActive(s == DungeonState.Title);
        levelPanel.gameObject.SetActive(s == DungeonState.LevelComplete);
        overPanel.gameObject.SetActive(s == DungeonState.GameOver);
        pausePanel.gameObject.SetActive(s == DungeonState.Paused);

        bool auto = gm.DemoMode || dir.AutopilotEnabled;

        if (s == DungeonState.Title)
        {
            titleCountdown.text = gm.DemoMode
                ? (dir.IsRevealing ? "MODE DEMO · menunggu dungeon selesai dibangun…" : "MODE DEMO · mulai otomatis dalam " + Mathf.CeilToInt(gm.AutoTimer) + "…")
                : "Seed " + gen.Seed + "  ·  dungeon sedang dibangun di belakang layar ini";
        }

        if (s == DungeonState.LevelComplete)
        {
            DungeonGameManager.LevelResult r = gm.LastResult;
            float t = r.time;
            levelStats.text =
                "Seed <b>" + r.seed + "</b>   ·   Waktu <b>" + Mathf.FloorToInt(t / 60f).ToString("00") + ":" + (t % 60f).ToString("00.0") + "</b>\n" +
                "Item <b>" + r.items + " / " + r.itemsTotal + "</b>  <color=#4DF2FF>+" + r.itemScore + "</color>   ·   " +
                "Clear <color=#FFD14D>+" + r.clearBonus + "</color>   ·   Time <color=#FFD14D>+" + r.timeBonus + "</color>\n" +
                "Jarak Start→Goal (BFS): <b>" + r.pathLength + "</b> langkah   ·   Hit diterima <b>" + gm.HitsTaken + "</b>" +
                (gm.TreasureCollected ? "   ·   <color=#FFD14D>TREASURE +" + gm.TreasureScore + "</color>" : "") + "\n" +
                "<size=26><b>SCORE " + gm.Score.ToString("000000") + "</b></size>";
            levelCountdown.text = auto ? "Dungeon berikutnya (seed " + (gen.Seed + 1) + ") dalam " + Mathf.CeilToInt(gm.AutoTimer) + "…" : "Seed berikutnya: " + (gen.Seed + 1);
        }

        if (s == DungeonState.GameOver)
        {
            overCountdown.text = auto ? "Retry otomatis dalam " + Mathf.CeilToInt(gm.AutoTimer) + "…" : "";
        }
    }

    // ==================================================================
    // Toast, popup, damage
    // ==================================================================
    private void ShowToast(string message, Color color)
    {
        toastText.text = message;
        toastColor = color;
        toastTimer = 1.8f;
    }

    private void OnRoomEntered(int room)
    {
        RoomChallengeSystem rc = gen.Challenges;
        if (rc == null || !rc.Active) return;

        RoomRole role = rc.RoleOf(room);
        roomBannerColor = RoomChallengeSystem.RoleColor(role);
        roomBanner.text = RoomChallengeSystem.RoleName(role) + "  <size=16><color=#C8D4E0>— " + RoomChallengeSystem.RoleHint(role) + "</color></size>";
        roomBannerTimer = 3.2f;
    }

    private void OnDamaged()
    {
        flashAlpha = 0.85f;
    }

    private void SpawnPopup(Vector3 world, string text, Color color)
    {
        TextMeshProUGUI t = UIFactory.MakeText(hudRoot, text, 28f, color, TextAlignmentOptions.Center, FontStyles.Bold);
        t.rectTransform.sizeDelta = new Vector2(200f, 40f);
        floating.Add(new Floating { text = t, world = world, age = 0f });
    }

    private void UpdateFloating()
    {
        if (cam == null) return;
        Rect r = view.rectTransform.rect;

        for (int i = floating.Count - 1; i >= 0; i--)
        {
            Floating f = floating[i];
            f.age += Time.unscaledDeltaTime;

            if (f.age >= PopupLife || f.text == null)
            {
                if (f.text != null) Destroy(f.text.gameObject);
                floating.RemoveAt(i);
                continue;
            }

            Vector3 vp = cam.WorldToViewportPoint(f.world);
            f.text.rectTransform.anchorMin = f.text.rectTransform.anchorMax = Vector2.zero;
            f.text.rectTransform.anchoredPosition = new Vector2(vp.x * r.width, vp.y * r.height + f.age * 70f);
            Color c = f.text.color;
            f.text.color = new Color(c.r, c.g, c.b, 1f - f.age / PopupLife);
        }
    }
}
