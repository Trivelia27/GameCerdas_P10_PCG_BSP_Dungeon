using UnityEngine;

public class SimpleEnemy : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 60f;

    private void Update()
    {
        transform.Rotate(
            Vector3.up,
            rotationSpeed * Time.deltaTime
        );
    }
}
