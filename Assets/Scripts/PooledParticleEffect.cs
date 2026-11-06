
using UnityEngine;

public sealed class PooledParticleEffect : MonoBehaviour
{
    private ParticleSystem[] particleSystems;

    public bool IsInPool { get; private set; }

    public bool IsAlive
    {
        get
        {
            if (particleSystems == null)
                return false;

            foreach (ParticleSystem ps in particleSystems)
            {
                if (ps != null && ps.IsAlive(false))
                    return true;
            }

            return false;
        }
    }

    public void Initialize()
    {
        particleSystems =
            GetComponentsInChildren<ParticleSystem>(true);

        if (particleSystems.Length == 0)
        {
            Debug.LogError(
                "PooledParticleEffect: No ParticleSystems found!",
                this
            );
        }

        // We control playback manually through the pool.
        foreach (ParticleSystem ps in particleSystems)
        {
            var main = ps.main;
            main.playOnAwake = false;
        }

        IsInPool = true;
    }

    public void SetInPool(bool value)
    {
        IsInPool = value;
    }

    public void PlayEffect()
    {
        foreach (ParticleSystem ps in particleSystems)
        {
            if (ps == null)
                continue;

            ps.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );

            ps.Play(false);
        }
    }

    public void StopAndClear()
    {
        foreach (ParticleSystem ps in particleSystems)
        {
            if (ps == null)
                continue;

            ps.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }
    }
}
