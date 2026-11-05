using UnityEngine;

/// <summary>
/// Item: melayang, berputar, berkilau, dan dapat diambil Player.
/// Varian Treasure (peti harta di Treasure Room) bernilai lebih besar.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    [SerializeField] private float bobAmplitude = 0.12f;
    [SerializeField] private float bobSpeed = 3f;
    [SerializeField] private float spinSpeed = 120f;
    [SerializeField] private bool isTreasure = false;
    [SerializeField] private int value = 100;
    [SerializeField] private Color sparkleColor = new Color(0.4f, 0.95f, 1f, 0.9f);

    private Vector3 basePosition;
    private bool collected;

    public bool IsTreasure => isTreasure;
    public int Value => value;

    private void Start()
    {
        basePosition = transform.position;
        Fx.AttachSparkle(transform, Vector3.zero, sparkleColor, isTreasure ? 12f : 5f);
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
