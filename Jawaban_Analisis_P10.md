# Jawaban Analisis — Praktikum 10: Procedural Dungeon Generation dengan BSP

Angka yang dikutip berasal dari `Hasil_Eksperimen_P10.md` (parameter default: 60×40, minLeaf 12, maxLeaf 20, minRoom 5, margin 2).

## A. Pembahasan Tugas Eksperimen (§126)

| Seed | Rooms | Enemy | Item | Start–Goal (BFS) | Valid |
|---|---|---|---|---|---|
| 101 | 10 | 8 | 5 | 73 | Yes |
| 202 | 8 | 8 | 5 | 82 | Yes |
| 303 | 7 | 8 | 5 | 87 | Yes |
| 404 | 11 | 8 | 5 | 62 | Yes |
| 505 | 7 | 8 | 5 | 62 | Yes |

**Pengaruh seed.** Seed menentukan seluruh urutan `Random.value` / `Random.Range`. Karena setiap keputusan (arah split, posisi split, ukuran dan posisi room, arah belokan corridor, posisi enemy/item) mengambil angka dari urutan yang sama, mengganti satu angka seed mengubah keputusan pertama, dan perubahan itu merambat ke seluruh tree BSP. Hasilnya, jumlah room berkisar 7–11 dan jarak Start–Goal 62–87 langkah. Seed yang sama dijalankan dua kali menghasilkan hash peta yang identik (12345 → `260A4AB8` dua kali).

**Pengaruh parameter BSP.** `minLeafSize` 8 → 12 → 16 menurunkan jumlah leaf rata-rata 13.6 → 8.6 → 5.4 dan menaikkan luas room rata-rata 48.5 → 75.9 → 83.9 tile. `roomMargin` 1 → 2 → 4 tidak mengubah jumlah leaf (8.6) karena margin baru dipakai setelah BSP, tetapi menurunkan jumlah room menjadi 6.0 pada margin 4. Leaf 12 tile dikurangi 2×4 menyisakan 4 tile, lebih kecil dari `minRoomSize` 5, sehingga leaf tersebut tidak diberi room.

**Kualitas struktur.** Semua seed menghasilkan pola room → corridor → room yang jelas. Floor sekitar 25–35% dari map, dan corridor hanya 45–108 tile, sehingga ruang didominasi room yang cocok untuk combat/exploration.

**Konektivitas.** Room dihubungkan berurutan sesuai urutan leaf DFS, sehingga room *i* selalu terhubung ke room *i+1* dan seluruh room membentuk satu komponen. BFS memastikannya: ke-10 seed pada tabel Eksperimen 1 dan Tugas valid pada percobaan pertama (tanpa penggantian seed), dan semua konfigurasi Eksperimen 2–4 valid 5/5.

**Variasi antar-level.** Variasinya tinggi pada tata letak tetapi rendah pada topologi. Hubungan antar-room selalu berbentuk rantai, sehingga dungeon cenderung linear dan tidak memiliki loop. Ini dapat dikembangkan dengan corridor tambahan antar-sibling acak.

## B. Pertanyaan Analisis (§127)

**1. Apa perbedaan random dan procedural generation?**
Random hanya menghasilkan nilai acak tanpa struktur, misalnya mengisi tiap cell dengan Wall/Floor secara acak. Procedural generation memakai algoritma dan aturan (BSP, room di leaf, corridor, constraint) dengan randomness sebagai salah satu input, sehingga hasilnya bervariasi tetapi tetap terstruktur dan dapat dimainkan. PCG = randomness + algorithm + rule + constraint + validation + design goal.

**2. Mengapa level procedural tidak cukup hanya menggunakan random?**
Random murni tidak menjamin level dapat dimainkan. Region bisa terputus, Goal tidak terjangkau, Enemy muncul tepat di Start, atau room terlalu kecil. Diperlukan aturan (struktur BSP, corridor) dan constraint/validasi (BFS, jarak minimum enemy) agar setiap hasil memenuhi tujuan desain.

**3. Mengapa diperlukan seed?**
Agar generation *reproducible*: seed sama menghasilkan urutan random sama dan dungeon sama. Ini penting untuk debugging (bug pada seed 64521 dapat diulang persis), untuk eksperimen yang adil antar-parameter, dan untuk berbagi level (cukup bagikan satu angka).

**4. Apa fungsi grid pada dungeon generator?**
Grid (`TileType[,]`) adalah representasi data level yang terpisah dari visual. Semua algoritma (BSP, carving, BFS, spawn) bekerja pada grid yang murah dan mudah divalidasi. Baru setelah valid, grid dikonversi ke GameObject melalui `GridToWorld(x, y) → (x, 0, z)`.

**5. Mengapa BSP cocok untuk room-based dungeon?**
BSP membagi area menjadi rectangle yang tidak saling tumpang-tindih, sehingga setiap leaf dapat memuat satu room tanpa bertabrakan. Struktur tree juga memberi urutan alami untuk menghubungkan room yang berdekatan. Hasilnya terstruktur (room → corridor → room) dan konektivitas mudah dikontrol.

**6. Apa yang dimaksud Leaf dalam BSP?**
Leaf adalah node pada tree BSP yang tidak dibagi lagi (`left == null && right == null`), karena ukurannya sudah ≤ `maxLeafSize` atau terlalu kecil (< 2 × `minLeafSize`) untuk dibagi. Room dibuat di dalam setiap leaf.

**7. Mengapa Room tidak dibuat memenuhi seluruh Leaf?**
Jika room memenuhi leaf, room yang bertetangga akan menempel tanpa dinding pemisah, sehingga struktur room–corridor hilang dan dungeon menjadi satu ruangan besar. `roomMargin` menjamin ada dinding antar-room, dan ukuran/posisi acak di dalam leaf menambah variasi bentuk.

**8. Apa fungsi corridor?**
Menghubungkan room yang terpisah sehingga seluruh dungeon menjadi satu area walkable yang terhubung. Corridor juga berfungsi sebagai elemen desain: chokepoint, jalur transisi, dan pengatur ritme eksplorasi.

**9. Mengapa corridor berbentuk L mudah digunakan?**
Cukup dua segmen lurus (horizontal lalu vertical, atau sebaliknya) yang selalu menghubungkan dua titik, tetap mengikuti grid, dan sederhana diimplementasikan dengan dua loop. Pemilihan urutan secara acak menambah variasi tanpa algoritma pathfinding.

**10. Apa yang dimaksud hard constraint?**
Aturan yang wajib terpenuhi; jika gagal, dungeon dinyatakan invalid. Contoh di proyek ini: minimal 2 room, Start dan Goal ada di dalam grid dan di floor, Start ≠ Goal, Goal dapat dicapai dari Start (BFS), Enemy/Item hanya di Floor, dan Enemy tidak berada di/bersebelahan dengan corridor. Constraint terakhir ditambahkan setelah ditemukan kasus nyata pada seed 12345: Enemy spawn di corridor selebar 1 tile yang merupakan satu-satunya jalan keluar dari Start room, sehingga collider-nya menutup jalan dan level tidak dapat diselesaikan meskipun BFS pada grid menyatakan valid.

**11. Apa yang dimaksud soft constraint?**
Aturan yang sebaiknya terpenuhi untuk kualitas, tetapi tidak membatalkan dungeon. Contoh: Goal jauh dari Start (strategi farthest room), enemy tersebar, item tidak menumpuk, room tidak terlalu kecil, dan dungeon tidak terlalu linear.

**12. Mengapa Enemy tidak boleh spawn dekat Start?**
Agar player tidak langsung diserang atau kalah sebelum sempat bergerak (*spawn kill*), dan agar ada momen aman untuk orientasi. Proyek ini menerapkan tiga constraint: jarak ≥ 8 tile dari Start, `safeRoomCount = 1` (room Start bebas Enemy), dan `keepCorridorsClear` (Enemy tidak menutup corridor).

**13. Mengapa level perlu divalidasi?**
Karena dungeon yang "terlihat benar" belum tentu dapat dimainkan (lihat kasus Enemy menutup corridor pada jawaban no. 10 — validasi juga harus memperhitungkan object yang di-spawn, bukan hanya grid). Satu seed buruk bisa menghasilkan Goal yang tidak terjangkau atau room terlalu sedikit. Validasi sebelum build (Generate → Validate → Build) mencegah level rusak sampai ke player, dan pola generate-and-test (`maxGenerationAttempts = 10`) otomatis mencoba seed berikutnya.

**14. Bagaimana BFS dapat memvalidasi dungeon?**
BFS dimulai dari Start dan menjelajahi semua tile non-Wall yang terhubung 4 arah. Jika Goal pernah dikunjungi, Start–Goal terhubung. Jika antrian habis sebelum Goal ditemukan, dungeon invalid. Variasi BFS (`ComputeDistances`) juga menghasilkan jarak langkah, yang dipakai untuk strategi Goal C dan metrics *Start–Goal Distance*. Pada Eksperimen 6 rata-rata jarak path: A (Last Room) 67.0, B (Farthest Euclidean) 73.2, C (Farthest Path) 84.0 langkah. Strategi C terbaik karena yang dirasakan player adalah jarak jalan, bukan garis lurus (seed 101: B = euclid 47.4 tetapi path 73, sedangkan C = euclid 39.1 tetapi path 86).

**15. Apa keuntungan menggabungkan PCG dengan NavMesh dan AI?** *(lihat juga bagian C: Enemy FSM per peran room)*
Level yang berbeda setiap kali tidak bisa diberi waypoint atau NavMesh secara manual. Dengan NavMeshSurface yang di-bake runtime setelah generation, Enemy (FSM, Behavior Tree, Utility AI dari pertemuan sebelumnya) langsung dapat bernavigasi di dungeon apa pun. Room–corridor BSP juga memberi struktur taktis yang jelas (ruang terbuka vs chokepoint) bagi AI. Parameter PCG (enemyCount, itemCount, ukuran) dapat diatur oleh Dynamic Difficulty Adjustment di pertemuan berikutnya.

## C. Pengembangan: Room Challenges & Enemy FSM

**Room Role Assignment sebagai Constraint-Based Generation.** Layout BSP hanya menjawab *di mana* room berada. Lapisan kedua menentukan *apa isi* tiap room dengan aturan berikut:

| Jenis | Aturan |
|---|---|
| Hard | Room pertama = **Start** (safe room). Room Goal = **Boss** (modul §116: Boss hanya boleh di Goal Room). |
| Rule | **Treasure** = room samping terjauh yang **tidak** dilalui jalur BFS Start→Goal (modul §117: treasure di room tanpa Start/Goal, idealnya dead-end), sehingga eksplorasi diberi hadiah. |
| Rule | Room lain diurutkan menurut *difficulty* (jarak BFS dari Start). Room termudah selalu **Patrol**, lalu **Guard / Trap / Sentry** bergilir dengan urutan diacak per seed, sehingga kesulitan naik bertahap dan setiap tipe muncul bila jumlah room cukup. |
| Constraint ukuran | **Sentry** butuh room ≥ 6×6; bila tidak terpenuhi diganti Guard. |

Hasil Eksperimen 7 (seed 1–50): seluruh 50 dungeon punya Boss tepat di room Goal dan punya Treasure Room. 48/50 Treasure berada di room samping, dan 2 sisanya fallback ketika semua room berada di jalur utama. 30/50 dungeon memuat keempat tantangan sekaligus; yang lain terlalu kecil (sedikit room). Assignment memakai RNG terpisah, sehingga tabel 1–6 identik dengan sebelum fitur ditambahkan: reproducibility modul tetap terjaga.

**Enemy FSM.** Enemy tidak lagi sekadar berputar. Perilakunya mengikuti peran room:
- **Patrol:** berkeliling sudut room.
- **Wander:** berjalan acak (di Trap/Sentry room).
- **Guard:** Watch → Chase saat player masuk radius deteksi 4,5 → Return ke pos bila player menjauh/keluar room.
- **Boss:** menjaga area sekitar Goal (radius 4,5).

Ini menghubungkan PCG dengan materi FSM sebelumnya. Semua gerakan dibatasi rectangle room asal, sehingga constraint spawn modul (tidak di Wall, tidak dekat Start, tidak di corridor) tetap berlaku sepanjang permainan. Checklist *"Enemy & hazard tetap di room-nya"* di HUD memverifikasinya secara live.

**Pelajaran dari pengujian.** Saat diuji dengan autopilot, ditemukan bahwa Boss yang mengejar ke seluruh room membuat dungeon seed 12345 hampir tidak bisa diselesaikan, karena Boss room berada di tengah rantai room dan Treasure/Trap hanya bisa dicapai lewat room itu. Solusinya, Boss diikat ke area Goal: tepi room tetap bisa dilintasi. Ini contoh bahwa validasi *playability* tidak cukup hanya pada grid (BFS), tetapi juga harus memperhitungkan perilaku objek dinamis.

## D. Kesimpulan Eksperimen Lain
- **Ukuran dungeon (Eks. 4):** 40×30 → 80×60 menaikkan total GameObject ±638 → ±2312. Optimasi `IsWallVisible` memangkas Wall dari 3340 menjadi 837 cell pada 80×60 (−75%). Waktu algoritma data < 0.1 ms; biaya utama ada pada Instantiate/render, bukan algoritma.
- **Enemy density (Eks. 5):** Enemy count absolut 20 pada 702 floor tile = 1 enemy per 35 tile (padat), 5 = 1 per 140 tile (sepi). Density berbasis luas floor (`useEnemyDensity`) lebih konsisten antar ukuran map.
