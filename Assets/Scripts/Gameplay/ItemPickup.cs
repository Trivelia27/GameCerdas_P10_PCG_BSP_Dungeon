using UnityEngine;

/// <summary>
/// Item: melayang, berputar, berkilau, dan dapat diambil Player.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    [SerializeField] private float bobAmplitude = 0.12f;
    [SerializeField] private float bobSpeed = 3f;
    [SerializeField] private float spinSpeed = 120f;

    private Vector3 basePosition;
    private bool collected;

    private void Start()
    {
        basePosition = transform.position;
        Fx.AttachSparkle(transform, Vector3.zero, new Color(0.4f, 0.95f, 1f, 0.9f), 5f);
    }

    private void Update()
    {
        transform.position = basePosition +
            Vector3.up * Mathf.Sin(Time.time * bobSpeed + basePosition.x) * bobAmplitude;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || !other.CompareTag("Player"))
            return;

        DungeonGameManager manager = DungeonGameManager.Instance;

        if (manager != null)
        {
            if (manager.State != DungeonState.Playing)
                return;

            manager.CollectItem(this);
        }
        else
        {
            DungeonGenerator generator = FindFirstObjectByType<DungeonGenerator>();
            if (generator != null) generator.ItemsCollected++;
        }

        collected = true;
        Destroy(gameObject);
    }
}
