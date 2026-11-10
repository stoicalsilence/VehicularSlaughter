using UnityEngine;

public interface ITurretDamageable
{
    void TakeDamage(
        float damage,
        int criticalHits);

    void TakeDamage(
        float damage,
        Vector3 impactPosition,
        float knockbackForce,
        int criticalHits);
}