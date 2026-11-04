using UnityEngine;

/// <summary>Membuat partikel debu cahaya melayang di arena saat game mulai.</summary>
public class AmbientMotes : MonoBehaviour
{
    [SerializeField] private Vector3 area = new Vector3(32f, 7f, 32f);
    [SerializeField] private Color color = new Color(0.45f, 0.8f, 1f, 0.55f);

    private void Start()
    {
        Fx.CreateAmbientMotes(transform, area, color);
    }
}
