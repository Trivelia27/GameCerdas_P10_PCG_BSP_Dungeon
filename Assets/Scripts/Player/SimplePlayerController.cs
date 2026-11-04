using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimplePlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;

    // Dipakai Autopilot demo: bila aktif, input keyboard diganti input eksternal
    public bool ExternalControl { get; set; }
    public Vector2 ExternalInput { get; set; }

    // Dikunci oleh DungeonGameManager saat title / reveal / pause / level selesai
    public bool InputLocked { get; set; }

    public float MoveSpeed => moveSpeed;

    private CharacterController controller;
    private Vector3 velocity;
    private Vector3 knockback;

    private void Awake()
    {
        controller =
            GetComponent<CharacterController>();
    }

    public void ApplyKnockback(Vector3 impulse)
    {
        impulse.y = 0f;
        knockback = impulse;
    }

    private void Update()
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (!InputLocked)
        {
            horizontal =
                ExternalControl ? ExternalInput.x : Input.GetAxisRaw("Horizontal");

            vertical =
                ExternalControl ? ExternalInput.y : Input.GetAxisRaw("Vertical");
        }

        // ClampMagnitude (bukan normalized) agar autopilot dapat melambat tanpa overshoot waypoint
        Vector3 move =
            Vector3.ClampMagnitude(
                new Vector3(
                    horizontal,
                    0f,
                    vertical
                ),
                1f
            );

        controller.Move(
            (move * moveSpeed + knockback) *
            Time.deltaTime
        );

        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 30f * Time.deltaTime);

        if (move.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(move),
                12f * Time.deltaTime);
        }

        if (controller.isGrounded &&
            velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        velocity.y +=
            gravity *
            Time.deltaTime;

        controller.Move(
            velocity *
            Time.deltaTime
        );
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        SimpleEnemy enemy = hit.collider.GetComponentInParent<SimpleEnemy>();

        if (enemy != null && DungeonGameManager.Instance != null)
        {
            DungeonGameManager.Instance.PlayerHit(enemy.transform.position);
        }
    }
}
