using UnityEngine;

/// <summary>
/// Add only to a Chunky prefab, next to HordeEnemy.
/// The normal HordeEnemy/Manager still own chasing, climbing and animation.
/// PlayerHealth and CarController call the two methods on actual contact.
/// No Update(), FixedUpdate(), allocations or repeated physics queries.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HordeEnemy))]
public sealed class HordeChunkyImpact : MonoBehaviour
{
    [Header("Launch Player (velocity in metres / second)")]
    [SerializeField, Min(0f)] private float playerHorizontalSpeed = 19f;
    [SerializeField, Min(0f)] private float playerUpwardSpeed = 10f;
    [SerializeField, Min(0.1f)] private float playerCooldown = 2.25f;

    [Header("Launch Car (mass-independent velocity change)")]
    [SerializeField, Min(0f)] private float carHorizontalVelocityChange = 18f;
    [SerializeField, Min(0f)] private float carUpwardVelocityChange = 4.5f;
    [Tooltip("Angular velocity added around the sideways push axis, in rad/s.")]
    [SerializeField, Min(0f)] private float carRollAngularVelocityChange = 1.6f;
    [SerializeField, Min(0.1f)] private float carCooldown = 2.75f;

    [Header("Modifiers")]
    [Tooltip("Elite Chunkies throw harder. Does not alter the normal Elite health/speed/damage scaling.")]
    [SerializeField, Min(1f)] private float eliteLaunchMultiplier = 1.3f;

    private HordeEnemy enemy;
    private float nextPlayerLaunch;
    private float nextCarLaunch;

    private void Awake()
    {
        enemy = GetComponent<HordeEnemy>();
    }

    private void OnEnable()
    {
        // Called whenever a pooled Chunky re-enters the scene.
        nextPlayerLaunch = float.NegativeInfinity;
        nextCarLaunch = float.NegativeInfinity;
    }

    /// <summary>Only called from PlayerHealth's existing contact handler.</summary>
    public bool TryLaunchPlayer(PlayerHealth player)
    {
        if (!isActiveAndEnabled || enemy == null || enemy.IsDead || enemy.IsEMP ||
            player == null || !player.isActiveAndEnabled ||
            Time.time < nextPlayerLaunch)
            return false;

        Vector3 direction = HorizontalAwayFrom(player.transform.position);
        float multiplier = enemy.IsElite ? eliteLaunchMultiplier : 1f;
        Vector3 velocity = direction * (playerHorizontalSpeed * multiplier) +
                           Vector3.up * (playerUpwardSpeed * multiplier);

        // PlayerHealth checks for vehicle occupancy, dead player and a short
        // shared grace period; Q3PlayerController.Launch owns actual movement.
        if (!player.TryApplyChunkyLaunch(velocity))
            return false;

        nextPlayerLaunch = Time.time + Mathf.Max(0.1f, playerCooldown);
        return true;
    }

    /// <summary>Only called from CarController's existing collision callbacks.</summary>
    public bool TryLaunchVehicle(CarController car)
    {
        if (!isActiveAndEnabled || enemy == null || enemy.IsDead || enemy.IsEMP ||
            car == null || !car.isActiveAndEnabled || Time.time < nextCarLaunch)
            return false;

        Vector3 direction = HorizontalAwayFrom(car.transform.position);
        float multiplier = enemy.IsElite ? eliteLaunchMultiplier : 1f;
        Vector3 linearVelocityChange = direction * (carHorizontalVelocityChange * multiplier) +
                                       Vector3.up * (carUpwardVelocityChange * multiplier);
        // Add a little roll: not an enormous off-centre impulse that could
        // destabilize the vehicle's joints or wheel suspension.
        Vector3 rollAxis = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 angularVelocityChange = rollAxis * (carRollAngularVelocityChange * multiplier);

        if (!car.TryApplyChunkyLaunch(linearVelocityChange, angularVelocityChange))
            return false;

        nextCarLaunch = Time.time + Mathf.Max(0.1f, carCooldown);
        return true;
    }

    private Vector3 HorizontalAwayFrom(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;
        return direction.normalized;
    }
}
