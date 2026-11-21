using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static void Play2D(
        AudioClip clip,
        float volume = 1f,
        float pitch = 1f,
        int priority = 128)
    {
        GameObject tempGO = new GameObject("TempAudio2D");
        AudioSource audioSource = tempGO.AddComponent<AudioSource>();

        audioSource.clip = clip;
        audioSource.volume = volume;
        audioSource.spatialBlend = 0f;
        audioSource.pitch = pitch;
        audioSource.priority = Mathf.Clamp(priority, 0, 256);

        audioSource.Play();

        Object.Destroy(tempGO, clip.length);
    }

    public static void Play3D(
        AudioClip clip,
        Vector3 position,
        float volume = 1f,
        float pitch = 1f,
        float minDistance = 1f,
        float maxDistance = 20f,
        int priority = 128)
    {
        GameObject tempGO = new GameObject("TempAudio3D");
        tempGO.transform.position = position;

        AudioSource audioSource = tempGO.AddComponent<AudioSource>();

        audioSource.clip = clip;
        audioSource.volume = volume;
        audioSource.pitch = pitch;
        audioSource.priority = Mathf.Clamp(priority, 0, 256);

        audioSource.spatialBlend = 1f;

        audioSource.minDistance = minDistance;
        audioSource.maxDistance = maxDistance;

        audioSource.rolloffMode = AudioRolloffMode.Linear;

        audioSource.Play();

        Object.Destroy(tempGO, clip.length);
    }
}