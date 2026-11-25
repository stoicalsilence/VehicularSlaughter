using UnityEngine;

[DisallowMultipleComponent]
public class PropImpactSound : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip[] impactSounds;
    [SerializeField]
    private Vector2 pitchRange =
        new Vector2(0.85f, 1.15f);

    [Header("Impact")]
    [SerializeField] private float minImpactSpeed = 1.5f;
    [SerializeField] private float maxImpactSpeed = 12f;
    [SerializeField] private float minVolume = 0.15f;
    [SerializeField] private float maxVolume = 1f;
    [SerializeField] private float impactCooldown = 0.15f;

    private AudioSource audioSource;
    private float nextImpactTime;

    private void OnCollisionEnter(Collision collision)
    {
        if (!enabled || Time.time < nextImpactTime)
            return;

        if (impactSounds == null || impactSounds.Length == 0)
            return;

        float speedSqr =
            collision.relativeVelocity.sqrMagnitude;

        if (speedSqr < minImpactSpeed * minImpactSpeed)
            return;

        AudioClip clip =
            impactSounds[Random.Range(0, impactSounds.Length)];

        if (clip == null)
            return;

        float speed = Mathf.Sqrt(speedSqr);

        float strength = Mathf.InverseLerp(
            minImpactSpeed, maxImpactSpeed, speed);

        float volume = Mathf.Lerp(
            minVolume, maxVolume, strength);

        // Only initialize audio when actually needed.
        if (audioSource == null)
            InitializeAudioSource();

        audioSource.pitch = Random.Range(
            pitchRange.x, pitchRange.y);

        audioSource.PlayOneShot(clip, volume);

        nextImpactTime = Time.time + impactCooldown;
    }

    private void InitializeAudioSource()
    {
        if (!TryGetComponent(out audioSource))
        {
            audioSource =
                gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.loop = false;

        audioSource.minDistance = 1f;
        audioSource.maxDistance = 30f;

        audioSource.rolloffMode =
            AudioRolloffMode.Linear;
    }
}