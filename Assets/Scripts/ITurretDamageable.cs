using UnityEngine;

public interface ITurretDamageable
{
    void TakeDamage(float damage);

    void TakeDamage(
        float damage,
        Vector3 impactPosition,
        float knockbackForce);
}