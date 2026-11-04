using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menjalankan Eksperimen 1–6 dan Tugas Eksperimen (§102–108, §126) tanpa Play Mode,
/// lalu menulis hasilnya ke Hasil_Eksperimen_P10.md di root project.
/// Generator dijalankan dengan RunDataOnly() sehingga urutan random identik dengan saat Play.
/// </summary>
public static class DungeonExperimentRunner
{
    private const string OutputFile = "Hasil_Eksperimen_P10.md";

    private class Config
    {
        public int mapWidth = 60, mapHeight = 40;
        public int minLeafSize = 12, maxLeafSize = 20;
        public int minRoomSize = 5, roomMargin = 2;
        public int enemyCount = 8, itemCount = 5;
        public GoalStrategy goal = GoalStrategy.FarthestEuclidean;

        public Config Clone() { return (Config)MemberwiseClone(); }
    }

    [MenuItem("Tools/GameCerdas P10/2. Run Experiments")]
    public static void Run()
    {
        GameObject temp = new GameObject("__ExperimentGenerator");
        temp.hideFlags = HideFlags.HideAndDontSave;
        DungeonGenerator gen = temp.AddComponent<DungeonGenerator>();

        StringBuilder md = new StringBuilder();
        md.AppendLine("# Hasil Eksperimen — Praktikum 10: Procedural Dungeon Generation dengan BSP");
        md.AppendLine();
        md.AppendLine("Dihasilkan otomatis oleh `Tools > GameCerdas P10 > Run Experiments` (" +
                      System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ").");
        md.AppendLine("Parameter default: Map 60×40, Min Leaf 12, Max Leaf 20, Min Room 5, Room Margin 2, " +
                      "Enemy 8, Item 5, Goal = Farthest Room (Euclidean), corridor width 1.");
        md.AppendLine();
        md.AppendLine("*StartGoal (path)* = jarak BFS dalam langkah grid; *Euclid* = jarak garis lurus antar center room.");
        md.AppendLine();

        Config baseCfg = new Config();

        // ------------------------------------------------------------------
        md.AppendLine("## Tugas Eksperimen (§126) — 5 seed berbeda");
        md.AppendLine();
        SeedTable(md, gen, baseCfg, new[] { 101, 202, 303, 404, 505 });

        // ------------------------------------------------------------------
        md.AppendLine("## Eksperimen 1 — Seed (§102)");
        md.AppendLine();
        SeedTable(md, gen, baseCfg, new[] { 100, 200, 300, 400, 500 });

        md.AppendLine("Uji reproducibility: seed yang sama dijalankan dua kali.");
        md.AppendLine();
        md.AppendLine("| Seed | Run 1 hash | Run 2 hash | Identik |");
        md.AppendLine("|---|---|---|---|");
        foreach (int s in new[] { 12345, 999 })
        {
            Apply(gen, baseCfg);
            gen.RunDataOnly(s, out string a1);
            gen.RunDataOnly(s, out string a2);
            md.AppendLine("| " + s + " | " + Hash(a1) + " | " + Hash(a2) + " | " + (a1 == a2 ? "Ya" : "TIDAK") + " |");
        }
        md.AppendLine();

        // ------------------------------------------------------------------
        int[] avgSeeds = { 101, 202, 303, 404, 505 };

        md.AppendLine("## Eksperimen 2 — Min Leaf Size (§103)");
        md.AppendLine();
        md.AppendLine("Rata-rata dari seed 101, 202, 303, 404, 505.");
        md.AppendLine();
        md.AppendLine("| minLeafSize | BSP Leaves | Rooms | Rata-rata luas room (tile) | Floor Tiles | Valid |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (int v in new[] { 8, 12, 16 })
        {
            Config c = baseCfg.Clone();
            c.minLeafSize = v;
            Averages a = Average(gen, c, avgSeeds);
            md.AppendLine("| " + v + " | " + a.leaves.ToString("0.0") + " | " + a.rooms.ToString("0.0") + " | " +
                          a.roomArea.ToString("0.0") + " | " + a.floor.ToString("0") + " | " + a.validCount + "/5 |");
        }
        md.AppendLine();

        // ------------------------------------------------------------------
        md.AppendLine("## Eksperimen 3 — Room Margin (§104)");
        md.AppendLine();
        md.AppendLine("| roomMargin | BSP Leaves | Rooms | Rata-rata luas room | Floor Tiles | Valid |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (int v in new[] { 1, 2, 4 })
        {
            Config c = baseCfg.Clone();
            c.roomMargin = v;
            Averages a = Average(gen, c, avgSeeds);
            md.AppendLine("| " + v + " | " + a.leaves.ToString("0.0") + " | " + a.rooms.ToString("0.0") + " | " +
                          a.roomArea.ToString("0.0") + " | " + a.floor.ToString("0") + " | " + a.validCount + "/5 |");
        }
        md.AppendLine();

        // ------------------------------------------------------------------
        md.AppendLine("## Eksperimen 4 — Ukuran Dungeon (§105)");
        md.AppendLine();
        md.AppendLine("Jumlah GameObject = Floor + Wall (visible) + Goal + Player + Enemy + Item (tanpa parent kosong). " +
                      "Wall tanpa optimasi = seluruh cell Wall. Waktu = algoritma data saja (tanpa Instantiate).");
        md.AppendLine();
        md.AppendLine("| Ukuran | Total Tile | Rooms | Floor Tiles | Wall Objects | Wall tanpa optimasi | Total GameObject | Waktu gen (ms) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (Vector2Int size in new[] { new Vector2Int(40, 30), new Vector2Int(60, 40), new Vector2Int(80, 60) })
        {
            Config c = baseCfg.Clone();
            c.mapWidth = size.x;
            c.mapHeight = size.y;
            Averages a = Average(gen, c, avgSeeds);
            int total = size.x * size.y;
            float objects = a.floor + a.walls + 1 + 1 + a.enemies + a.items;
            md.AppendLine("| " + size.x + "×" + size.y + " | " + total + " | " + a.rooms.ToString("0.0") + " | " +
                          a.floor.ToString("0") + " | " + a.walls.ToString("0") + " | " + (total - a.floor).ToString("0") + " | " +
                          objects.ToString("0") + " | " + a.ms.ToString("0.00") + " |");
        }
        md.AppendLine();

        // ------------------------------------------------------------------
        md.AppendLine("## Eksperimen 5 — Enemy Density (§106)");
        md.AppendLine();
        md.AppendLine("| Enemy Count | Enemy berhasil ditempatkan | Floor Tiles | Floor tile per Enemy |");
        md.AppendLine("|---|---|---|---|");
        foreach (int v in new[] { 5, 10, 20 })
        {
            Config c = baseCfg.Clone();
            c.enemyCount = v;
            Averages a = Average(gen, c, avgSeeds);
            md.AppendLine("| " + v + " | " + a.enemies.ToString("0.0") + " | " + a.floor.ToString("0") + " | " +
                          (a.floor / Mathf.Max(1f, a.enemies)).ToString("0.0") + " |");
        }
        md.AppendLine();

        // ------------------------------------------------------------------
        md.AppendLine("## Eksperimen 6 — Strategi Start/Goal (§108)");
        md.AppendLine();
        md.AppendLine("Nilai = jarak Start→Goal dalam langkah BFS (euclid dalam kurung).");
        md.AppendLine();
        md.AppendLine("| Seed | A: Last Room | B: Farthest Euclidean | C: Farthest Path (BFS) |");
        md.AppendLine("|---|---|---|---|");
        float[] sums = new float[3];
        foreach (int s in avgSeeds)
        {
            StringBuilder row = new StringBuilder("| " + s + " |");
            int idx = 0;
            foreach (GoalStrategy g in new[] { GoalStrategy.LastRoom, GoalStrategy.FarthestEuclidean, GoalStrategy.FarthestPath })
            {
                Config c = baseCfg.Clone();
                c.goal = g;
                Apply(gen, c);
                DungeonMetrics m = gen.RunDataOnly(s, out _);
                row.Append(" " + m.startGoalPath + " (" + m.startGoalEuclidean.ToString("0.0") + ") |");
                sums[idx++] += m.startGoalPath;
            }
            md.AppendLine(row.ToString());
        }
        md.AppendLine("| **Rata-rata** | **" + (sums[0] / 5f).ToString("0.0") + "** | **" + (sums[1] / 5f).ToString("0.0") +
                      "** | **" + (sums[2] / 5f).ToString("0.0") + "** |");
        md.AppendLine();

        // ------------------------------------------------------------------
        md.AppendLine("## Contoh Peta ASCII — Seed 12345 (default)");
        md.AppendLine();
        md.AppendLine("`#` Wall, `.` Floor, `S` Start, `G` Goal, `E` Enemy, `I` Item");
        md.AppendLine();
        Apply(gen, baseCfg);
        DungeonMetrics dm = gen.RunDataOnly(12345, out string ascii);
        md.AppendLine("```");
        md.Append(ascii);
        md.AppendLine("```");
        md.AppendLine();
        md.AppendLine("```");
        md.AppendLine(gen.MetricsToString());
        md.AppendLine("```");

        Object.DestroyImmediate(temp);

        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFile);
        File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
        Debug.Log("[P10] Hasil eksperimen ditulis ke: " + path);
    }

    // ----------------------------------------------------------------------
    private static void SeedTable(StringBuilder md, DungeonGenerator gen, Config cfg, int[] seeds)
    {
        md.AppendLine("| Seed | Leaves | Rooms | Floor Tiles | Corridor Tiles | Enemy | Item | StartGoal (path) | Euclid | Wall Objects | Valid |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (int s in seeds)
        {
            Apply(gen, cfg);
            DungeonMetrics m = gen.RunDataOnly(s, out _);
            string seedText = m.seed == s ? s.ToString() : s + " → " + m.seed;
            md.AppendLine("| " + seedText + " | " + m.leafCount + " | " + m.roomCount + " | " + m.floorTiles + " | " +
                          m.corridorTiles + " | " + m.enemyCount + " | " + m.itemCount + " | " + m.startGoalPath + " | " +
                          m.startGoalEuclidean.ToString("0.0") + " | " + m.wallObjects + " | " + (m.valid ? "Yes" : "No") + " |");
        }
        md.AppendLine();
    }

    private struct Averages
    {
        public float leaves, rooms, roomArea, floor, walls, enemies, items, ms;
        public int validCount;
    }

    private static Averages Average(DungeonGenerator gen, Config cfg, int[] seeds)
    {
        Averages a = new Averages();
        foreach (int s in seeds)
        {
            Apply(gen, cfg);
            DungeonMetrics m = gen.RunDataOnly(s, out _);
            a.leaves += m.leafCount;
            a.rooms += m.roomCount;
            a.roomArea += m.roomCount > 0 ? (m.floorTiles - m.corridorTiles) / (float)m.roomCount : 0f;
            a.floor += m.floorTiles;
            a.walls += m.wallObjects;
            a.enemies += m.enemyCount;
            a.items += m.itemCount;
            a.ms += m.generationMs;
            if (m.valid) a.validCount++;
        }

        float n = seeds.Length;
        a.leaves /= n; a.rooms /= n; a.roomArea /= n; a.floor /= n;
        a.walls /= n; a.enemies /= n; a.items /= n; a.ms /= n;
        return a;
    }

    private static void Apply(DungeonGenerator gen, Config c)
    {
        SerializedObject so = new SerializedObject(gen);
        so.FindProperty("mapWidth").intValue = c.mapWidth;
        so.FindProperty("mapHeight").intValue = c.mapHeight;
        so.FindProperty("minLeafSize").intValue = c.minLeafSize;
        so.FindProperty("maxLeafSize").intValue = c.maxLeafSize;
        so.FindProperty("minRoomSize").intValue = c.minRoomSize;
        so.FindProperty("roomMargin").intValue = c.roomMargin;
        so.FindProperty("enemyCount").intValue = c.enemyCount;
        so.FindProperty("itemCount").intValue = c.itemCount;
        so.FindProperty("goalStrategy").enumValueIndex = (int)c.goal;
        so.FindProperty("randomSeed").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h.ToString("X8");
        }
    }
}
