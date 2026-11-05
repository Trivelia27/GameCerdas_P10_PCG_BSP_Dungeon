using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Membangun seluruh aset Praktikum 10 secara otomatis:
/// Materials (neon), Prefabs (Floor/Wall/Goal/Start/Player/Enemy/Item), Post-processing,
/// PCG_Dungeon_Scene (main) dan PCG_Dungeon_Demo (autoplay).
/// Menu: Tools > GameCerdas P10 > ...
/// </summary>
public static class DungeonProjectBuilder
{
    private const string ScenePath = "Assets/Scenes/PCG_Dungeon_Scene.unity";
    private const string DemoScenePath = "Assets/Scenes/PCG_Dungeon_Demo.unity";
    private const string MatDir = "Assets/Materials";
    private const string DungeonPrefabDir = "Assets/Prefabs/Dungeon";
    private const string CharacterPrefabDir = "Assets/Prefabs/Characters";
    private const string ItemPrefabDir = "Assets/Prefabs/Items";
    private const string PostFxPath = "Assets/Settings/P10_PostFX.asset";

    private static readonly Color Cyan = new Color(0.3f, 0.95f, 1f);
    private static readonly Color Gold = new Color(1f, 0.78f, 0.25f);
    private static readonly Color Violet = new Color(0.55f, 0.28f, 1f);

    private struct Mats
    {
        public Material floor, corridor, wall, goal, beam, start, enemy, enemyEye, enemyRing, item, player, visor, halo;
        public Material boss, spikePlate, spikeMetal, turretBody, turretEye, projectile, chest, chestTrim;
    }

    [MenuItem("Tools/GameCerdas P10/1. Build Assets + Scene")]
    public static void BuildAll()
    {
        EnsureFolders();

        Texture2D edge = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/T_Edge.png");

        Mats m = new Mats
        {
            floor = CreateMaterial("FloorMaterial", new Color(0.13f, 0.15f, 0.21f), 0.35f, new Color(0.15f, 0.5f, 0.75f) * 0.4f, edge),
            corridor = CreateMaterial("CorridorMaterial", new Color(0.09f, 0.13f, 0.16f), 0.35f, new Color(0.1f, 0.75f, 0.65f) * 0.65f, edge),
            wall = CreateMaterial("WallMaterial", new Color(0.075f, 0.065f, 0.12f), 0.2f, Violet * 1.15f, edge),
            goal = CreateMaterial("GoalMaterial", Gold, 0.7f, Gold * 3f),
            beam = CreateMaterial("GoalBeamMaterial", Gold, 0.5f, Gold * 5f),
            start = CreateMaterial("StartMaterial", new Color(0.2f, 0.9f, 0.45f), 0.6f, new Color(0.2f, 1f, 0.45f) * 1.8f, edge),
            enemy = CreateMaterial("EnemyMaterial", new Color(0.72f, 0.1f, 0.15f), 0.55f, new Color(0.7f, 0.03f, 0.06f) * 0.7f),
            enemyEye = CreateMaterial("EnemyEyeMaterial", new Color(1f, 0.4f, 0.3f), 0.8f, new Color(1f, 0.25f, 0.15f) * 5f),
            enemyRing = CreateMaterial("EnemyRingMaterial", new Color(0.6f, 0.05f, 0.08f), 0.3f, new Color(1f, 0.08f, 0.1f) * 0.9f),
            halo = CreateMaterial("PlayerHaloMaterial", new Color(0.1f, 0.5f, 0.7f), 0.3f, Cyan * 0.6f),
            boss = CreateMaterial("BossMaterial", new Color(0.35f, 0.02f, 0.06f), 0.7f, new Color(0.9f, 0.05f, 0.12f) * 0.9f),
            spikePlate = CreateMaterial("SpikePlateMaterial", new Color(0.12f, 0.05f, 0.07f), 0.4f, new Color(0.5f, 0.08f, 0.12f) * 0.5f, edge),
            spikeMetal = CreateMaterial("SpikeMetalMaterial", new Color(0.75f, 0.75f, 0.8f), 0.85f, new Color(1f, 0.2f, 0.25f) * 0.35f),
            turretBody = CreateMaterial("TurretBodyMaterial", new Color(0.22f, 0.16f, 0.34f), 0.6f, Violet * 1.1f, edge),
            turretEye = CreateMaterial("TurretEyeMaterial", new Color(0.7f, 0.4f, 1f), 0.8f, new Color(0.7f, 0.35f, 1f) * 3f),
            projectile = CreateMaterial("ProjectileMaterial", new Color(0.8f, 0.5f, 1f), 0.8f, new Color(0.75f, 0.4f, 1f) * 5f),
            chest = CreateMaterial("ChestMaterial", new Color(0.45f, 0.27f, 0.1f), 0.5f, new Color(1f, 0.6f, 0.15f) * 0.35f, edge),
            chestTrim = CreateMaterial("ChestTrimMaterial", Gold, 0.85f, Gold * 2.5f),
            item = CreateMaterial("ItemMaterial", Cyan, 0.85f, Cyan * 3.2f),
            player = CreateMaterial("PlayerMaterial", new Color(0.16f, 0.42f, 1f), 0.75f, new Color(0.1f, 0.3f, 1f) * 0.35f),
            visor = CreateMaterial("PlayerVisorMaterial", Cyan, 0.9f, Cyan * 3.5f)
        };

        GameObject floor = CreateFloorPrefab(m.floor);
        GameObject wall = CreateWallPrefab(m.wall);
        GameObject goal = CreateGoalPrefab(m.goal, m.beam);
        GameObject start = CreateStartPrefab(m.start);
        GameObject player = CreatePlayerPrefab(m.player, m.visor, m.halo);
        GameObject enemy = CreateEnemyPrefab(m.enemy, m.enemyEye, m.enemyRing);
        GameObject item = CreateItemPrefab(m.item);
        GameObject spike = CreateSpikePrefab(m.spikePlate, m.spikeMetal);
        GameObject turret = CreateTurretPrefab(m.turretBody, m.turretEye, m.projectile);
        GameObject chest = CreateChestPrefab(m.chest, m.chestTrim);

        VolumeProfile profile = CreatePostFxProfile();
        Material lineMat = Resources.Load<Material>("Fx/FxLine");

        CreateScene(ScenePath, false, m, profile, lineMat, floor, wall, goal, start, player, enemy, item, spike, turret, chest);
        CreateScene(DemoScenePath, true, m, profile, lineMat, floor, wall, goal, start, player, enemy, item, spike, turret, chest);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(DemoScenePath, true)
        };

        // Skala UI konsisten di Game view
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[P10] Assets + Scene selesai dibuat: " + ScenePath + " & " + DemoScenePath);
    }

    [MenuItem("Tools/GameCerdas P10/3. Open Scene And Play")]
    public static void OpenAndPlay()
    {
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/GameCerdas P10/4. Open DEMO (Autoplay)")]
    public static void OpenDemoAndPlay()
    {
        EditorSceneManager.OpenScene(DemoScenePath);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Entry point batch mode: build + experiments.</summary>
    public static void BuildAllBatch()
    {
        BuildAll();
        DungeonExperimentRunner.Run();
    }

    // ------------------------------------------------------------------
    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets", "Settings");
        EnsureFolder("Assets/Prefabs", "Dungeon");
        EnsureFolder("Assets/Prefabs", "Characters");
        EnsureFolder("Assets/Prefabs", "Items");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static Material CreateMaterial(string name, Color color, float smoothness, Color? emission = null, Texture2D emissionMap = null)
    {
        string path = MatDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0.1f);

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.SetTexture("_EmissionMap", emissionMap);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.SetTexture("_EmissionMap", null);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static GameObject SavePrefab(GameObject go, string dir)
    {
        string path = dir + "/" + go.name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // §28 Floor Prefab
    private static GameObject CreateFloorPrefab(Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "FloorPrefab";
        go.transform.localScale = new Vector3(1f, 0.1f, 1f);
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; // hindari shadow acne pada kamera top-down
        go.isStatic = true;
        return SavePrefab(go, DungeonPrefabDir);
    }

    // §29 Wall Prefab
    private static GameObject CreateWallPrefab(Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "WallPrefab";
        go.transform.localScale = Vector3.one;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        return SavePrefab(go, DungeonPrefabDir);
    }

    // §30 Goal Prefab: pad + beam + core + light + trigger
    private static GameObject CreateGoalPrefab(Material mat, Material beamMat)
    {
        GameObject go = new GameObject("GoalPrefab");

        BoxCollider trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.85f, 0f);
        trigger.size = new Vector3(0.9f, 2f, 0.9f);
        go.AddComponent<GoalTrigger>();

        Part(PrimitiveType.Cylinder, "Pad", go.transform, Vector3.zero, new Vector3(0.85f, 0.03f, 0.85f), mat);
        Part(PrimitiveType.Cylinder, "Beam", go.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.08f, 1.4f, 0.08f), beamMat)
            .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObject core = Part(PrimitiveType.Cube, "Core", go.transform, new Vector3(0f, 0.65f, 0f), new Vector3(0.32f, 0.32f, 0.32f), mat);
        core.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);

        GameObject lightGo = new GameObject("GoalLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.75f, 0.3f);
        light.range = 6f;
        light.intensity = 4f;

        return SavePrefab(go, DungeonPrefabDir);
    }

    private static GameObject CreateStartPrefab(Material mat)
    {
        GameObject go = new GameObject("StartPrefab");
        Part(PrimitiveType.Cylinder, "Pad", go.transform, Vector3.zero, new Vector3(0.9f, 0.015f, 0.9f), mat);
        return SavePrefab(go, DungeonPrefabDir);
    }

    // §72 Player Prefab
    private static GameObject CreatePlayerPrefab(Material bodyMat, Material visorMat, Material haloMat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "PlayerPrefab";
        go.tag = "Player";
        // Corridor lebar 1 tile → Player diperkecil agar muat (radius 0.3)
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        go.GetComponent<Renderer>().sharedMaterial = bodyMat;

        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;
        cc.skinWidth = 0.03f;
        cc.stepOffset = 0.3f;

        go.AddComponent<SimplePlayerController>();

        Part(PrimitiveType.Cube, "Visor", go.transform, new Vector3(0f, 0.45f, 0.38f), new Vector3(0.75f, 0.22f, 0.35f), visorMat);
        Part(PrimitiveType.Cylinder, "Halo", go.transform, new Vector3(0f, -0.95f, 0f), new Vector3(1.7f, 0.01f, 1.7f), haloMat)
            .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        GameObject lightGo = new GameObject("PlayerLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.45f, 0.85f, 1f);
        light.range = 6f;
        light.intensity = 2.5f;

        return SavePrefab(go, CharacterPrefabDir);
    }

    // §76 Enemy Prefab (root di y=1, body diturunkan agar menapak floor)
    private static GameObject CreateEnemyPrefab(Material bodyMat, Material eyeMat, Material ringMat)
    {
        GameObject go = new GameObject("EnemyPrefab");
        go.AddComponent<EnemyController>();   // FSM (mode Idle = perilaku SimpleEnemy modul)

        GameObject body = Part(PrimitiveType.Capsule, "Body", go.transform, new Vector3(0f, -0.3f, 0f), new Vector3(0.7f, 0.7f, 0.7f), bodyMat, true);

        GameObject eye = Part(PrimitiveType.Cube, "Eye", body.transform, new Vector3(0f, 0.45f, 0.4f), new Vector3(0.65f, 0.14f, 0.3f), eyeMat);
        eye.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        Part(PrimitiveType.Cylinder, "DangerRing", go.transform, new Vector3(0f, -0.94f, 0f), new Vector3(1.5f, 0.008f, 1.5f), ringMat)
            .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        return SavePrefab(go, CharacterPrefabDir);
    }

    // Trap Room: plate + 4 spike yang naik-turun
    private static GameObject CreateSpikePrefab(Material plateMat, Material metalMat)
    {
        GameObject go = new GameObject("SpikePrefab");
        SpikeTrap trap = go.AddComponent<SpikeTrap>();

        GameObject plate = Part(PrimitiveType.Cube, "Plate", go.transform, new Vector3(0f, 0.065f, 0f), new Vector3(0.9f, 0.03f, 0.9f), plateMat);
        plate.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        GameObject spikes = new GameObject("Spikes");
        spikes.transform.SetParent(go.transform, false);
        spikes.transform.localPosition = new Vector3(0f, 0.25f, 0f);

        Vector3[] offsets = { new Vector3(-0.2f, 0f, -0.2f), new Vector3(0.2f, 0f, -0.2f), new Vector3(-0.2f, 0f, 0.2f), new Vector3(0.2f, 0f, 0.2f) };
        foreach (Vector3 o in offsets)
        {
            GameObject spike = Part(PrimitiveType.Cube, "Spike", spikes.transform, o, new Vector3(0.17f, 0.17f, 0.17f), metalMat);
            spike.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        }

        SerializedObject so = new SerializedObject(trap);
        so.FindProperty("spikes").objectReferenceValue = spikes.transform;
        so.FindProperty("plate").objectReferenceValue = plate.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(go, DungeonPrefabDir);
    }

    // Sentry Room: turret yang membidik & menembak
    private static GameObject CreateTurretPrefab(Material bodyMat, Material eyeMat, Material projectileMat)
    {
        GameObject go = new GameObject("TurretPrefab");
        Turret turret = go.AddComponent<Turret>();

        Part(PrimitiveType.Cylinder, "Base", go.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.3f, 0.8f), bodyMat, true);

        GameObject head = new GameObject("Head");
        head.transform.SetParent(go.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.78f, 0f);

        Part(PrimitiveType.Cube, "HeadBox", head.transform, Vector3.zero, new Vector3(0.52f, 0.3f, 0.52f), bodyMat);
        Part(PrimitiveType.Cube, "Barrel", head.transform, new Vector3(0f, 0f, 0.38f), new Vector3(0.13f, 0.13f, 0.5f), bodyMat);
        GameObject eye = Part(PrimitiveType.Cube, "Eye", head.transform, new Vector3(0f, 0.06f, 0.27f), new Vector3(0.36f, 0.08f, 0.04f), eyeMat);

        GameObject tLight = new GameObject("TurretLight");
        tLight.transform.SetParent(go.transform, false);
        tLight.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Light lt = tLight.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = new Color(0.7f, 0.45f, 1f);
        lt.range = 4f;
        lt.intensity = 2.5f;

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(head.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.7f);

        SerializedObject so = new SerializedObject(turret);
        so.FindProperty("head").objectReferenceValue = head.transform;
        so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        so.FindProperty("eye").objectReferenceValue = eye.GetComponent<Renderer>();
        so.FindProperty("projectileMaterial").objectReferenceValue = projectileMat;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(go, DungeonPrefabDir);
    }

    // Treasure Room: peti harta (+300)
    private static GameObject CreateChestPrefab(Material bodyMat, Material trimMat)
    {
        GameObject go = new GameObject("ChestPrefab");

        SphereCollider trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.6f;

        ItemPickup pickup = go.AddComponent<ItemPickup>();
        SerializedObject so = new SerializedObject(pickup);
        so.FindProperty("isTreasure").boolValue = true;
        so.FindProperty("value").intValue = 300;
        so.FindProperty("bobAmplitude").floatValue = 0.04f;
        so.FindProperty("spinSpeed").floatValue = 25f;
        so.FindProperty("sparkleColor").colorValue = new Color(1f, 0.85f, 0.3f, 0.95f);
        so.ApplyModifiedPropertiesWithoutUndo();

        Part(PrimitiveType.Cube, "Box", go.transform, Vector3.zero, new Vector3(0.7f, 0.4f, 0.5f), bodyMat);
        Part(PrimitiveType.Cube, "Lid", go.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.74f, 0.12f, 0.54f), trimMat);
        Part(PrimitiveType.Cube, "Lock", go.transform, new Vector3(0f, 0.08f, 0.26f), new Vector3(0.14f, 0.16f, 0.04f), trimMat);

        GameObject lightGo = new GameObject("ChestLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.35f);
        light.range = 4f;
        light.intensity = 2.5f;

        return SavePrefab(go, ItemPrefabDir);
    }

    // §81 Item Prefab: kristal berputar + trigger
    private static GameObject CreateItemPrefab(Material mat)
    {
        GameObject go = new GameObject("ItemPrefab");

        SphereCollider trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.4f;
        go.AddComponent<ItemPickup>();

        GameObject gem = Part(PrimitiveType.Cube, "Gem", go.transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), mat);
        gem.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        gem.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        return SavePrefab(go, ItemPrefabDir);
    }

    private static VolumeProfile CreatePostFxProfile()
    {
        AssetDatabase.DeleteAsset(PostFxPath);

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, PostFxPath);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.3f);
        bloom.scatter.Override(0.7f);
        bloom.highQualityFiltering.Override(true);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.5f);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.2f);
        color.contrast.Override(14f);
        color.saturation.Override(12f);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);

        ChromaticAberration chromatic = profile.Add<ChromaticAberration>(true);
        chromatic.intensity.Override(0.05f);

        foreach (VolumeComponent component in profile.components)
            AssetDatabase.AddObjectToAsset(component, profile);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // §27 Scene
    private static void CreateScene(string scenePath, bool demo, Mats m, VolumeProfile profile, Material lineMat,
                                    GameObject floor, GameObject wall, GameObject goal, GameObject start,
                                    GameObject player, GameObject enemy, GameObject item,
                                    GameObject spike, GameObject turret, GameObject chest)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Main Camera (§85) — dirender ke RenderTexture oleh DungeonHUD
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.016f, 0.02f, 0.045f);
        cam.fieldOfView = 45f;
        cam.farClipPlane = 400f;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(30f, 45f, 20f);
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // Directional Light (redup → elemen emissive lebih menonjol)
        GameObject lightGo = new GameObject("Directional Light");
        Light sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.65f;
        sun.color = new Color(0.72f, 0.78f, 1f);
        sun.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(62f, -35f, 0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.2f);

        // Post-processing
        GameObject volumeGo = new GameObject("Global Volume");
        Volume volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        // Systems: audio sintetis + denyut post-fx
        GameObject systems = new GameObject("Systems");
        systems.AddComponent<AudioManager>();
        systems.AddComponent<PostFxController>();

        GameObject motes = new GameObject("AmbientMotes");
        motes.transform.position = new Vector3(30f, 0f, 20f);
        AmbientMotes am = motes.AddComponent<AmbientMotes>();
        SerializedObject mso = new SerializedObject(am);
        mso.FindProperty("area").vector3Value = new Vector3(64f, 5f, 44f);
        mso.FindProperty("color").colorValue = new Color(0.45f, 0.75f, 1f, 0.5f);
        mso.ApplyModifiedPropertiesWithoutUndo();

        // LevelRoot (parent GeneratedLevel)
        GameObject levelRoot = new GameObject("LevelRoot");

        // DungeonGenerator (§84) + Director + GameManager + HUD
        GameObject genGo = new GameObject("DungeonGenerator");
        DungeonGenerator gen = genGo.AddComponent<DungeonGenerator>();
        DungeonDemoDirector director = genGo.AddComponent<DungeonDemoDirector>();
        RoomChallengeSystem challenges = genGo.AddComponent<RoomChallengeSystem>();

        SerializedObject cso = new SerializedObject(challenges);
        cso.FindProperty("enemyPrefab").objectReferenceValue = enemy;
        cso.FindProperty("spikePrefab").objectReferenceValue = spike;
        cso.FindProperty("turretPrefab").objectReferenceValue = turret;
        cso.FindProperty("chestPrefab").objectReferenceValue = chest;
        cso.FindProperty("floorMaterial").objectReferenceValue = m.floor;
        cso.FindProperty("bossMaterial").objectReferenceValue = m.boss;
        cso.ApplyModifiedPropertiesWithoutUndo();
        DungeonGameManager manager = genGo.AddComponent<DungeonGameManager>();
        genGo.AddComponent<DungeonHUD>();

        SerializedObject so = new SerializedObject(gen);
        so.FindProperty("mapWidth").intValue = 60;
        so.FindProperty("mapHeight").intValue = 40;
        so.FindProperty("tileSize").floatValue = 1f;
        so.FindProperty("minLeafSize").intValue = 12;
        so.FindProperty("maxLeafSize").intValue = 20;
        so.FindProperty("minRoomSize").intValue = 5;
        so.FindProperty("roomMargin").intValue = 2;
        so.FindProperty("seed").intValue = 12345;
        so.FindProperty("randomSeed").boolValue = false;
        so.FindProperty("enemyCount").intValue = 8;
        so.FindProperty("itemCount").intValue = 5;
        so.FindProperty("floorPrefab").objectReferenceValue = floor;
        so.FindProperty("wallPrefab").objectReferenceValue = wall;
        so.FindProperty("goalPrefab").objectReferenceValue = goal;
        so.FindProperty("startPrefab").objectReferenceValue = start;
        so.FindProperty("playerPrefab").objectReferenceValue = player;
        so.FindProperty("enemyPrefab").objectReferenceValue = enemy;
        so.FindProperty("itemPrefab").objectReferenceValue = item;
        so.FindProperty("corridorMaterial").objectReferenceValue = m.corridor;
        so.FindProperty("roomChallenges").objectReferenceValue = challenges;
        so.FindProperty("levelParent").objectReferenceValue = levelRoot.transform;
        so.FindProperty("logAsciiMap").boolValue = !demo;
        so.FindProperty("enableRegenerateKey").boolValue = false; // R ditangani DungeonGameManager
        so.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject dso = new SerializedObject(director);
        dso.FindProperty("revealSequence").boolValue = true;
        dso.FindProperty("autopilot").boolValue = demo;
        dso.FindProperty("lineMaterial").objectReferenceValue = lineMat;
        dso.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject gso = new SerializedObject(manager);
        gso.FindProperty("generator").objectReferenceValue = gen;
        gso.FindProperty("director").objectReferenceValue = director;
        gso.FindProperty("demoMode").boolValue = demo;
        gso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, scenePath);
    }
}
