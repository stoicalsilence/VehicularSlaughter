using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Fixed-budget, spatialized footsteps for a very large horde.
/// Does not attach an AudioSource, Animator event, or Update() to each enemy.
/// Called by HordeManager's existing centralized FixedUpdate tick.
/// </summary>
[DisallowMultipleComponent]
public sealed class HordeFootstepAudio : MonoBehaviour
{
    private const int SectorCount = 8;
    private const float AngleToSector = SectorCount / (2f * Mathf.PI);

    [Header("References")]
    [Tooltip("Assign the AudioListener/camera Transform. If empty, uses HordeManager's player Transform.")]
    [SerializeField] private Transform listenerTransform;
    [SerializeField] private AudioMixerGroup outputMixerGroup;
    [Tooltip("Use several short, dry, mono footstep clips; leave no null elements.")]
    [SerializeField] private AudioClip[] footstepClips;

    [Header("Audio Budget")]
    [Tooltip("One new footstep maximum per scan. 0.1s means at most 10 new steps per second.")]
    [SerializeField, Min(0.05f)] private float scanInterval = 0.1f;
    [Tooltip("Minimum approximate interval between sounds from the same direction.")]
    [SerializeField, Min(0.15f)] private float stepIntervalPerSector = 0.65f;

    [Header("3D Spatialization")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
    [SerializeField, Min(0.1f)] private float minDistance = 2.5f;
    [SerializeField, Min(1f)] private float maxDistance = 20f;
    [Tooltip("Small boost to sounds in the rear half of the listener's view.")]
    [SerializeField, Range(1f, 2f)] private float rearVolumeMultiplier = 1.2f;

    private readonly AudioSource[] sources = new AudioSource[SectorCount];
    private readonly HordeEnemy[] closestMovingEnemy = new HordeEnemy[SectorCount];
    private readonly float[] closestDistanceSqr = new float[SectorCount];
    private readonly float[] nextStepTime = new float[SectorCount];
    private float accumulatedTime;
    private int lastPlayedSector = -1;
    private int lastClipIndex = -1;

    private void Awake()
    {
        // Create a constant eight sources ONCE, not eight sources per enemy.
        for (int i = 0; i < SectorCount; i++)
        {
            GameObject speaker = new GameObject("Footstep Sector " + i);
            speaker.transform.SetParent(transform, false);

            AudioSource source = speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = Mathf.Max(0.1f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
            source.priority = 90; // Relatively important; tune against gunfire/UI.
            source.outputAudioMixerGroup = outputMixerGroup;
            sources[i] = source;

            // Keep the different sectors from starting in perfect sync.
            nextStepTime[i] = Time.time + Random.Range(0f, 0.35f);
        }
    }

    /// <summary>
    /// Called from HordeManager at its existing ~0.05s cadence.
    /// Scans all enemies only at scanInterval; no allocations and no physics queries.
    /// </summary>
    public void ManagedTick(List<HordeEnemy> activeEnemies, Transform player, float elapsed)
    {
        if (activeEnemies == null || footstepClips == null || footstepClips.Length == 0)
            return;

        accumulatedTime += elapsed;
        if (accumulatedTime < Mathf.Max(0.05f, scanInterval))
            return;
        accumulatedTime = 0f;

        Transform listener = listenerTransform != null ? listenerTransform : player;
        if (listener == null)
            return;

        Vector3 origin = listener.position;
        Vector3 forward = listener.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        // Horizontal right vector corresponding to forward.
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        float audibleRange = Mathf.Max(minDistance + 0.1f, maxDistance);
        float rangeSqr = audibleRange * audibleRange;

        for (int i = 0; i < SectorCount; i++)
        {
            closestMovingEnemy[i] = null;
            closestDistanceSqr[i] = rangeSqr;
        }

        for (int i = 0; i < activeEnemies.Count; i++)
        {
            HordeEnemy enemy = activeEnemies[i];
            if (enemy == null || !enemy.IsMovingForFootsteps)
                continue;

            Vector3 offset = enemy.transform.position - origin;
            offset.y = 0f;
            float distanceSqr = offset.x * offset.x + offset.z * offset.z;
            if (distanceSqr >= rangeSqr || distanceSqr < 0.04f)
                continue;

            float front = offset.x * forward.x + offset.z * forward.z;
            float side = offset.x * right.x + offset.z * right.z;
            float angle = Mathf.Atan2(side, front);
            int sector = Mathf.Clamp(Mathf.FloorToInt((angle + Mathf.PI) * AngleToSector),
                                     0, SectorCount - 1);

            if (distanceSqr < closestDistanceSqr[sector])
            {
                closestDistanceSqr[sector] = distanceSqr;
                closestMovingEnemy[sector] = enemy;
            }
        }

        // Fair round-robin across directions. The front horde cannot consume
        // all audio opportunities while something approaches from behind.
        float now = Time.time;
        for (int offset = 1; offset <= SectorCount; offset++)
        {
            int sector = (lastPlayedSector + offset) % SectorCount;
            HordeEnemy enemy = closestMovingEnemy[sector];
            AudioSource source = sources[sector];
            if (enemy == null || now < nextStepTime[sector] || source.isPlaying)
                continue;

            AudioClip clip = ChooseClip();
            if (clip == null)
                return;

            source.transform.position = enemy.transform.position;
            // With this sector mapping, 0,1,6,7 are the rear half.
            bool isBehind = sector <= 1 || sector >= 6;
            source.volume = Mathf.Clamp01(volume * (isBehind ? rearVolumeMultiplier : 1f));
            source.pitch = Random.Range(0.95f, 1.05f);
            source.clip = clip;
            source.Play(); // NOT PlayOneShot: one overlapping clip at most per source.

            nextStepTime[sector] = now +
                Mathf.Max(0.15f, stepIntervalPerSector) * Random.Range(0.85f, 1.15f);
            lastPlayedSector = sector;
            break; // Hard budget: at most one new step per scan.
        }
    }

    private AudioClip ChooseClip()
    {
        int count = footstepClips.Length;
        if (count == 0)
            return null;

        // Prefer a different variation than the previous sound.
        int first = Random.Range(0, count);
        if (count > 1 && first == lastClipIndex)
            first = (first + 1) % count;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (first + offset) % count;
            if (footstepClips[index] != null)
            {
                lastClipIndex = index;
                return footstepClips[index];
            }
        }
        return null;
    }

    private void OnDisable()
    {
        for (int i = 0; i < SectorCount; i++)
        {
            if (sources[i] != null)
                sources[i].Stop();
        }
        accumulatedTime = 0f;
    }
}
