using UnityEngine;

/// <summary>
/// A thrown grenade's independent, timed life. Uses the same explosion service
/// as PlayerRadio; the launcher provides an immutable snapshot of the throw stats.
/// Can be attached to the grenade prefab in advance, or added at spawn time.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrenadeProjectile : MonoBehaviour
{
    private GameObject explosionPrefab;
    private AudioClip bounceSound;
    private float damage;
    private float radius;
    private float knockback;
    private float visualScale;
    private float explosionLifetime;
    private float fuseRemaining;
    private float nextBounceSoundTime;
    private bool live;

    public void Arm(
        GameObject explosion,
        float explosionDamage,
        float explosionRadius,
        float explosionKnockback,
        float explosionVisualScale,
        float visualLifetime,
        float fuseSeconds,
        AudioClip bounceAudio)
    {
        explosionPrefab = explosion;
        damage = Mathf.Max(0f, explosionDamage);
        radius = Mathf.Max(0f, explosionRadius);
        knockback = Mathf.Max(0f, explosionKnockback);
        visualScale = Mathf.Max(0.01f, explosionVisualScale);
        explosionLifetime = Mathf.Max(0.1f, visualLifetime);
        fuseRemaining = Mathf.Max(0.1f, fuseSeconds);
        bounceSound = bounceAudio;
        nextBounceSoundTime = 0f;
        live = true;
    }

    private void Update()
    {
        if (!live)
            return;

        // Scaled delta time means pause/time-scale controls affect the fuse.
        fuseRemaining -= Time.deltaTime;
        if (fuseRemaining <= 0f)
            Detonate();
    }


    private void OnCollisionEnter(Collision collision)
    {
        if (!live)
            return;

        // Explode instantly when hitting an enemy.
        // GetComponentInParent also detects child hitbox colliders.
        HordeEnemy enemy =
            collision.collider.GetComponentInParent<HordeEnemy>();

        if (enemy != null && !enemy.IsDead)
        {
            Detonate();
            return;
        }

        // Otherwise, bounce normally and play the bounce sound.
        if (bounceSound == null ||
            Time.time < nextBounceSoundTime ||
            collision.relativeVelocity.sqrMagnitude < 4f)
            return;

        nextBounceSoundTime = Time.time + 0.15f;

        AudioManager.Play3D(
            bounceSound,
            transform.position,
            0.6f,
            1,
            12,
            30);
    }


    public void Detonate()
    {
        if (!live)
            return;

        live = false; // Never apply multiple explosions on the same projectile.
        Vector3 blastPoint = transform.position;

        // Match PlayerRadio.DoAirStrikeExplosion's ExplosionUtility call.
        // The second point is raised slightly for grenade-sized explosions.
        ExplosionUtility.Explode(
            explosionPrefab,
            blastPoint,
            blastPoint + Vector3.up * 0.3f,
            radius,
            damage,
            knockback,
            visualScale,
            explosionLifetime);

        Destroy(gameObject);
    }
}
