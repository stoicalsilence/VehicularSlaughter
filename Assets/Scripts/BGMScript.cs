
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BGMScript : MonoBehaviour
{
    [Header("Soundtracks")]
    [SerializeField] private List<AudioClip> soundtracks = new List<AudioClip>();

    [Header("Settings")]
    [SerializeField, Min(0f)] private float pauseBetweenTracks = 3f;
    [SerializeField] private bool playOnStart = true;

    private AudioSource audioSource;
    private readonly List<AudioClip> shuffledBag = new List<AudioClip>();

    private AudioClip lastPlayed;
    private Coroutine playbackRoutine;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    private void Start()
    {
        if (playOnStart)
        {
            StartPlaylist();
        }
    }

    public void StartPlaylist()
    {
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
        }

        playbackRoutine = StartCoroutine(PlaybackLoop());
    }

    public void StopPlaylist()
    {
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
            playbackRoutine = null;
        }

        audioSource.Stop();
    }

    private IEnumerator PlaybackLoop()
    {
        while (true)
        {
            AudioClip nextTrack = GetNextTrack();

            if (nextTrack == null)
            {
                Debug.LogWarning("BGMScript: No valid soundtracks found.");
                playbackRoutine = null;
                yield break;
            }

            lastPlayed = nextTrack;

            audioSource.clip = nextTrack;
            audioSource.Play();

            // Wait until the current track has finished.
            while (audioSource.isPlaying)
            {
                yield return null;
            }

            // Wait before starting the next track.
            if (pauseBetweenTracks > 0f)
            {
                yield return new WaitForSeconds(pauseBetweenTracks);
            }
        }
    }

    private AudioClip GetNextTrack()
    {
        // Refill the bag when all tracks have been played.
        if (shuffledBag.Count == 0)
        {
            RefillBag();
        }

        if (shuffledBag.Count == 0)
        {
            return null;
        }

        // Take the next track from the shuffled bag.
        int lastIndex = shuffledBag.Count - 1;
        AudioClip selectedTrack = shuffledBag[lastIndex];

        shuffledBag.RemoveAt(lastIndex);

        return selectedTrack;
    }

    private void RefillBag()
    {
        shuffledBag.Clear();

        // Add all valid, unique soundtracks.
        foreach (AudioClip track in soundtracks)
        {
            if (track != null && !shuffledBag.Contains(track))
            {
                shuffledBag.Add(track);
            }
        }

        // Fisher-Yates shuffle.
        for (int i = shuffledBag.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            AudioClip temp = shuffledBag[i];
            shuffledBag[i] = shuffledBag[randomIndex];
            shuffledBag[randomIndex] = temp;
        }

        // Prevent repeating the previous cycle's final track.
        // Tracks are taken from the end of the list.
        if (shuffledBag.Count > 1 &&
            shuffledBag[shuffledBag.Count - 1] == lastPlayed)
        {
            AudioClip temp = shuffledBag[0];
            shuffledBag[0] = shuffledBag[shuffledBag.Count - 1];
            shuffledBag[shuffledBag.Count - 1] = temp;
        }
    }
}
