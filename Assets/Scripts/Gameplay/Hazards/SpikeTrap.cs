using UnityEngine;

/// <summary>
/// Spike trap pada Trap Room. Siklus: Down (aman) → Warning (plate berkedip) → Up (damage).
/// Offset fase per lajur membuat pola gelombang yang bisa dibaca dan dilewati dengan timing.
/// </summary>
public class SpikeTrap : MonoBehaviour
{
    public enum SpikeState { Down, Warning, Up }

    [SerializeField] private Transform spikes;
    [SerializeField] private Renderer plate;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private float down, warning, up, offset;
    private MaterialPropertyBlock block;
    private float spikesLowY, spikesHighY;
    private SpikeState lastState = SpikeState.Down;

    public Vector2Int Tile { get; private set; }
    public SpikeState State { get; private set; }

    private float Cycle => down + warning + up;
    private float Phase => Mathf.Repeat(Time.time + offset, Cycle);

    public void Init(Vector2Int tile, float downTime, float warningTime, float upTime, float phaseOffset)
    {
        Tile = tile;
        down = downTime;
        warning = warningTime;
        up = upTime;
        offset = phaseOffset;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (spikes != null)
        {
            spikesHighY = spikes.localPosition.y;
            spikesLowY = spikesHighY - 0.32f;
        }
    }

    /// <summary>True bila spike sedang berbahaya atau akan naik dalam <paramref name="lookahead"/> detik.</summary>
    public bool DangerWithin(float lookahead)
    {
        float p = Phase;
        if (p >= down) return true;                 // Warning atau Up
        return p + lookahead >= down;               // sebentar lagi Warning
    }

    private void Update()
    {
        float p = Phase;
        State = p < down ? SpikeState.Down : p < down + warning ? SpikeState.Warning : SpikeState.Up;

        // animasi spike
        float target = State == SpikeState.Up ? spikesHighY : spikesLowY;
        if (spikes != null)
        {
            Vector3 lp = spikes.localPosition;
            lp.y = Mathf.MoveTowards(lp.y, target, Time.deltaTime * (State == SpikeState.Up ? 6f : 1.5f));
            spikes.localPosition = lp;
        }

        // warna plate
        Color c;
        if (State == SpikeState.Up) c = new Color(1f, 0.15f, 0.2f) * 2.2f;
        else if (State == SpikeState.Warning) c = new Color(1f, 0.55f, 0.1f) * (1f + 1.5f * Mathf.Abs(Mathf.Sin(Time.time * 14f)));
        else c = new Color(0.5f, 0.08f, 0.12f) * 0.5f;

        if (plate != null)
        {
            plate.GetPropertyBlock(block);
            block.SetColor(EmissionId, c);
            plate.SetPropertyBlock(block);
        }

        if (State == SpikeState.Up && lastState != SpikeState.Up && plate != null && plate.enabled && DungeonGameManager.IsLive)
        {
            // suara hanya untuk spike yang dekat player (hindari spam)
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null && (player.transform.position - transform.position).sqrMagnitude < 9f)
                AudioManager.Play(Sfx.Hit, 0.12f, 1.8f);
        }
        lastState = State;

        if (State == SpikeState.Up && DungeonGameManager.IsLive)
        {
            DungeonGameManager gm = DungeonGameManager.Instance;
            if (gm != null && gm.Player != null)
            {
                Vector3 d = gm.Player.transform.position - transform.position;
                d.y = 0f;
                if (Mathf.Abs(d.x) < 0.45f && Mathf.Abs(d.z) < 0.45f)
                    gm.PlayerHit(transform.position);
            }
        }
    }
}
