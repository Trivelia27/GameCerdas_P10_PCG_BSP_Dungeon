using UnityEngine;

/// <summary>Proyektil turret: bergerak lurus, hancur saat mengenai Wall atau Player.</summary>
public class Projectile : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<Projectile> All = new System.Collections.Generic.List<Projectile>();

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    /// <summary>Tile yang akan dilewati proyektil dalam <paramref name="seconds"/> detik ke depan.</summary>
    public void CollectPathTiles(System.Collections.Generic.HashSet<Vector2Int> into, float seconds)
    {
        if (gen == null) return;
        for (float t = 0f; t <= seconds; t += 0.1f)
        {
            Vector2Int g = gen.WorldToGrid(transform.position + velocity * t);
            if (gen.GetTile(g.x, g.y) == TileType.Wall) break;
            into.Add(g);
        }
    }

    private DungeonGenerator gen;
    private Vector3 velocity;
    private float life = 3.5f;

    public void Init(DungeonGenerator generator, Vector3 v)
    {
        gen = generator;
        velocity = v;
    }

    private void Update()
    {
        if (!DungeonGameManager.IsLive)
        {
            if (DungeonGameManager.Instance != null && DungeonGameManager.Instance.State != DungeonState.Paused)
                Destroy(gameObject);
            return;
        }

        transform.position += velocity * Time.deltaTime;
        life -= Time.deltaTime;

        Vector2Int tile = gen.WorldToGrid(transform.position);
        if (life <= 0f || gen.GetTile(tile.x, tile.y) == TileType.Wall)
        {
            Fx.Burst(transform.position, new Color(0.75f, 0.45f, 1f), 8, 2f, 0.1f, 0.3f, 0f);
            Destroy(gameObject);
            return;
        }

        DungeonGameManager gm = DungeonGameManager.Instance;
        if (gm != null && gm.Player != null)
        {
            Vector3 d = gm.Player.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude < 0.45f)
            {
                gm.PlayerHit(transform.position - velocity.normalized);
                Destroy(gameObject);
            }
        }
    }
}
