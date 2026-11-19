using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared airstrike/car explosion VFX and enemy damage implementation.</summary>
public static class ExplosionUtility
{
    public static void Explode(GameObject effectPrefab, Vector3 damageCentre,
        Vector3 visualPosition, float radius, float damage, float knockback,
        float visualScale, float lifetime)
    {
        if (effectPrefab != null && visualScale > 0f)
        {
            GameObject explosion = Object.Instantiate(effectPrefab, visualPosition, Quaternion.identity);
            explosion.transform.localScale *= visualScale;

            foreach (ParticleSystem ps in explosion.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            Object.Destroy(explosion, Mathf.Max(0.1f, lifetime));
        }

        if (damage <= 0f || radius <= 0f)
            return;

        Collider[] hits = Physics.OverlapSphere(damageCentre, radius, ~0, QueryTriggerInteraction.Ignore);
        // Local set prevents multi-collider enemies from taking damage twice,
        // including if damage callbacks trigger another explosion synchronously.
        HashSet<HordeEnemy> hitEnemies = new HashSet<HordeEnemy>();
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            HordeEnemy enemy = hit.GetComponentInParent<HordeEnemy>();
            if (enemy == null || enemy.IsDead || !hitEnemies.Add(enemy))
                continue;

            enemy.TakeDamage(damage, damageCentre, Mathf.Max(0f, knockback), 0);
        }
    }
}
