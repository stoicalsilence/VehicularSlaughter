using UnityEngine;

/// <summary>
/// Attach to the car's Rigidbody GameObject (alongside CarController).
/// Add this to other heavy Rigidbody objects too, with Car Only unchecked.
/// No changes to CarController are needed.
/// </summary>
[DisallowMultipleComponent]
public class HologramImpactEmitter : MonoBehaviour
{
    [Header("Which objects can trigger?")]
    [Tooltip("Only allow collisions from an object with the supplied CarController in its hierarchy.")]
    [SerializeField] private bool carOnly = true;

    [Tooltip("When Car Only is disabled, the emitter's Rigidbody must meet this mass.")]
    [Min(0f)] [SerializeField] private float minimumMassKg = 100f;

    [Header("Impact speed")]
    [Tooltip("Minimum relative collision speed (km/h) needed to create a hologram.")]
    [Min(0f)] [SerializeField] private float minimumImpactSpeedKmh = 12f;

    [Tooltip("Impacts at this speed or faster create the strongest effect.")]
    [Min(1f)] [SerializeField] private float fullStrengthSpeedKmh = 75f;

    private Rigidbody sourceBody;
    private CarController sourceCar;

    private void Awake()
    {
        sourceBody = GetComponentInParent<Rigidbody>();
        sourceCar = GetComponentInParent<CarController>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        if (carOnly)
        {
            if (sourceCar == null)
                return;
        }
        else if (sourceBody == null || sourceBody.mass < minimumMassKg)
        {
            return;
        }

        float speedKmh = collision.relativeVelocity.magnitude * 3.6f;
        if (speedKmh < minimumImpactSpeedKmh)
            return;

        // Works with colliders on children of a tree prefab: the receiver can live on its root.
        HologramImpactReceiver receiver = collision.collider.GetComponentInParent<HologramImpactReceiver>();
        if (receiver == null || receiver.transform.IsChildOf(transform))
            return;

        Vector3 point = collision.contactCount > 0
            ? collision.GetContact(0).point
            : collision.collider.ClosestPoint(transform.position);

        float strength = Mathf.InverseLerp(
            minimumImpactSpeedKmh,
            Mathf.Max(minimumImpactSpeedKmh + 0.01f, fullStrengthSpeedKmh),
            speedKmh);

        receiver.TriggerHologram(point, Mathf.Lerp(0.5f, 1f, strength));
    }
}
