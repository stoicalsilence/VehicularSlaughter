using UnityEngine;

namespace JuicyMetalHit
{
    /// <summary>Spawns a one-shot impact. Local +Z points away from the hit surface.</summary>
    [DisallowMultipleComponent]
    public sealed class MetalHitFX : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float cleanupDelay = 2.5f;
        [SerializeField] private bool destroyAfterPlayback = true;
        private ParticleSystem rootSystem;

        private void Awake()
        {
            rootSystem = GetComponent<ParticleSystem>();
        }

        private void Start()
        {
            if (destroyAfterPlayback) Destroy(gameObject, cleanupDelay);
        }

        /// <summary>Use with RaycastHit.point and RaycastHit.normal. Returns the new instance.</summary>
        public static MetalHitFX Spawn(MetalHitFX prefab, Vector3 point, Vector3 normal, float scale = 1f)
        {
            if (prefab == null) return null;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.forward;
            var instance = Instantiate(prefab, point + n * 0.025f,
                Quaternion.FromToRotation(Vector3.forward, n));
            instance.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scale);
            instance.Replay();
            return instance;
        }

        /// <summary>Clears and restarts the entire effect. Disable auto-destroy for pooled instances.</summary>
        public void Replay()
        {
            if (rootSystem == null) rootSystem = GetComponent<ParticleSystem>();
            if (rootSystem == null) return;
            rootSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rootSystem.Play(true);
        }
    }
}
