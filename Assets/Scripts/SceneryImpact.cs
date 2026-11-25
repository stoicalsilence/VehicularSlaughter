using UnityEngine;

[DisallowMultipleComponent]
public class SceneryImpact : MonoBehaviour
{
    [Header("Impact Requirements")]

    [SerializeField, Min(0f)]
    private float minimumImpactSpeedKmh = 25f;

    [SerializeField, Min(0f)]
    private float minimumImpactImpulse = 500f;

    [SerializeField]
    private LayerMask impactLayers = ~0;

    [Header("Impact Response")]

    [SerializeField, Min(0f)]
    private float impactVelocityBoost = 5f;

    [SerializeField, Min(0f)]
    private float upwardVelocityBoost = 1.5f;

    [SerializeField, Min(0f)]
    private float angularVelocityBoost = 3f;

    [Header("Rigidbody Settings")]

    [SerializeField, Min(0.1f)]
    private float mass = 150f;

    [SerializeField, Min(0f)]
    private float impulseMultiplier = 0.5f;

    [SerializeField, Min(0f)]
    private float maximumTransferredImpulse = 2000f;

    private bool activated;

    public bool IsActivated => activated;

    public AudioClip optionalSound;

    private void OnCollisionEnter(Collision collision)
    {
        if (activated)
            return;

        if (collision.rigidbody == null)
            return;

        // Only accept impacts from allowed layers.
        if ((impactLayers.value &
            (1 << collision.gameObject.layer)) == 0)
            return;

        float impactSpeedKmh =
            collision.relativeVelocity.magnitude * 3.6f;

        float impactImpulse = collision.impulse.magnitude;

        if (impactSpeedKmh < minimumImpactSpeedKmh)
            return;

        if (impactImpulse < minimumImpactImpulse)
            return;

        if (collision.contactCount == 0)
            return;

        ContactPoint contact = collision.GetContact(0);

        Vector3 direction = contact.normal;

        Vector3 awayFromVehicle =
            contact.point - collision.rigidbody.worldCenterOfMass;

        if (Vector3.Dot(direction, awayFromVehicle) < 0f)
            direction = -direction;

        float transferredImpulse = Mathf.Min(
            impactImpulse * impulseMultiplier,
            maximumTransferredImpulse
        );

        ActivatePhysics(
            contact.point,
            direction * transferredImpulse
        );
    }

    public void ActivatePhysics(
    Vector3 impactPoint,
    Vector3 impulse)
    {
        if (activated)
            return;

        activated = true;

        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = mass;
        rb.isKinematic = false;
        rb.useGravity = true;

        rb.interpolation =
            RigidbodyInterpolation.Interpolate;

        rb.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;

        rb.maxAngularVelocity = 15f;

        if (optionalSound != null)
        {
            AudioManager.Play3D(
                optionalSound,
                transform.position,
                0.7f, 1, 1, 30
            );
        }

        rb.WakeUp();

        // Apply the original physical impact.
        rb.AddForceAtPosition(
            impulse,
            impactPoint,
            ForceMode.Impulse
        );

        // Calculate horizontal impact direction.
        Vector3 direction = Vector3.ProjectOnPlane(
            impulse,
            Vector3.up
        ).normalized;

        if (direction.sqrMagnitude < 0.001f)
            return;

        // Scale the additional response by impact strength.
        float strength = Mathf.Clamp01(
            impulse.magnitude /
            Mathf.Max(maximumTransferredImpulse, 1f)
        );

        // Give the object an immediate directional jolt.
        rb.AddForce(
            (direction * impactVelocityBoost +
             Vector3.up * upwardVelocityBoost) * strength,
            ForceMode.VelocityChange
        );

        // Rotate the tree in the direction of the impact.
        Vector3 rotationAxis = Vector3.Cross(
            Vector3.up,
            direction
        ).normalized;

        rb.angularVelocity +=
            rotationAxis * angularVelocityBoost * strength;
    }
}