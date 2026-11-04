using UnityEngine;

/// <summary>
/// Goal: beacon berdenyut. Player menyentuh Goal → level selesai.
/// </summary>
public class GoalTrigger : MonoBehaviour
{
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float ringInterval = 1.4f;

    private bool triggered;
    private float ringTimer;
    private Renderer[] renderers;

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        ringTimer -= Time.deltaTime;
        if (ringTimer <= 0f)
        {
            ringTimer = ringInterval;

            // hanya berdenyut bila sudah terlihat (setelah animasi reveal)
            if (renderers != null && renderers.Length > 0 && renderers[0].enabled)
            {
                Fx.Ring(transform.position, new Color(1f, 0.8f, 0.25f), 1.8f, 1.1f);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered || !other.CompareTag("Player"))
            return;

        DungeonGameManager manager = DungeonGameManager.Instance;

        if (manager != null)
        {
            if (manager.State != DungeonState.Playing)
                return;

            triggered = true;
            manager.ReachGoal(transform.position);
            return;
        }

        triggered = true;
        DungeonGenerator generator = FindFirstObjectByType<DungeonGenerator>();
        if (generator != null) generator.OnGoalReached();
    }
}
