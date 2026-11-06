
using System.Collections.Generic;
using UnityEngine;

public sealed class DeathParticlePool : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private PooledParticleEffect prefab;

    [Header("Pool Settings")]
    [SerializeField] private int prewarmCount = 32;
    [SerializeField] private int maxPoolSize = 128;

    private readonly Stack<PooledParticleEffect> pool =
        new Stack<PooledParticleEffect>();

    private readonly List<PooledParticleEffect> activeEffects =
        new List<PooledParticleEffect>();

    private Transform poolRoot;

    public int PooledCount => pool.Count;
    public int ActiveCount => activeEffects.Count;

    private void Awake()
    {
        if (prefab == null)
        {
            Debug.LogError(
                "DeathParticlePool: No prefab assigned!",
                this
            );

            enabled = false;
            return;
        }

        CreatePoolRoot();
        PrewarmPool();
    }

    private void CreatePoolRoot()
    {
        GameObject root = new GameObject("Pooled Death Effects");

        root.transform.SetParent(transform, false);
        root.SetActive(false);

        poolRoot = root.transform;
    }

    private PooledParticleEffect CreateEffect()
    {
        PooledParticleEffect effect = Instantiate(
            prefab,
            poolRoot
        );

        effect.Initialize();
        effect.gameObject.SetActive(false);

        return effect;
    }

    private void PrewarmPool()
    {
        int count = Mathf.Min(
            Mathf.Max(0, prewarmCount),
            Mathf.Max(0, maxPoolSize)
        );

        for (int i = 0; i < count; i++)
        {
            PooledParticleEffect effect = CreateEffect();

            pool.Push(effect);
        }
    }

    // ------------------------------------------------
    // SPAWNING
    // ------------------------------------------------

    public void Play(Vector3 position)
    {
        Play(position, Quaternion.identity);
    }

    public void Play(Vector3 position, Quaternion rotation)
    {
        PooledParticleEffect effect;

        if (pool.Count > 0)
        {
            effect = pool.Pop();
        }
        else
        {
            effect = CreateEffect();
        }

        effect.SetInPool(false);

        effect.transform.SetParent(transform, false);

        effect.transform.SetPositionAndRotation(
            position,
            rotation
        );

        activeEffects.Add(effect);

        effect.gameObject.SetActive(true);

        effect.PlayEffect();
    }

    // ------------------------------------------------
    // CENTRALIZED LIFETIME MANAGEMENT
    // ------------------------------------------------

    private void Update()
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            PooledParticleEffect effect = activeEffects[i];

            if (effect == null)
            {
                RemoveAt(i);
                continue;
            }

            if (effect.IsAlive)
                continue;

            ReturnAt(i);
        }
    }

    private void RemoveAt(int index)
    {
        int lastIndex = activeEffects.Count - 1;

        activeEffects[index] = activeEffects[lastIndex];
        activeEffects.RemoveAt(lastIndex);
    }

    private void ReturnAt(int index)
    {
        int lastIndex = activeEffects.Count - 1;

        PooledParticleEffect effect = activeEffects[index];

        activeEffects[index] = activeEffects[lastIndex];
        activeEffects.RemoveAt(lastIndex);

        if (effect == null)
            return;

        effect.StopAndClear();
        effect.SetInPool(true);

        effect.gameObject.SetActive(false);

        if (pool.Count >= maxPoolSize)
        {
            Destroy(effect.gameObject);
            return;
        }

        effect.transform.SetParent(poolRoot, false);

        pool.Push(effect);
    }
}
