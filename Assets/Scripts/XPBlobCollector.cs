
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class XPBlobCollector : MonoBehaviour
{
    [Header("Collection Settings")]
    public float collectionRange = 5f;
    public float pollingRate = 0.2f;

    [SerializeField] private float minCollectionDuration = 0.08f;
    [SerializeField] private float maxCollectionDuration = 0.25f;

    [Header("Audio")]
    public AudioClip collectionStartSound;
    public AudioClip collectionFinishSound;

    [SerializeField] private float soundCooldown = 0.12f;

    public static float collectedXP;

    // All active collectors (player and vehicle).
    private static readonly List<XPBlobCollector> ActiveCollectors =
        new List<XPBlobCollector>();

    // Blobs currently travelling towards THIS collector.
    private readonly HashSet<XPBlob> collecting =
        new HashSet<XPBlob>();

    // Shared audio throttling between player and vehicle.
    private static float nextStartSoundTime;
    private static float nextFinishSoundTime;

    private void OnEnable()
    {
        if (!ActiveCollectors.Contains(this))
            ActiveCollectors.Add(this);

        float interval = Mathf.Max(0.02f, pollingRate);

        InvokeRepeating(
            nameof(PollForXP),
            interval,
            interval
        );
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(PollForXP));
        ActiveCollectors.Remove(this);

        // Release blobs travelling towards this collector.
        // They remain active and can be claimed by another collector.
        foreach (XPBlob blob in collecting)
        {
            if (blob == null)
                continue;

            blob.CancelMoveTween();
            blob.ClearCollector(this);
        }

        collecting.Clear();
    }

    // Called by XPBlobPool immediately after spawning a blob.
    public static void NotifyBlobSpawned(XPBlob blob)
    {
        if (blob == null || blob.IsBeingCollected)
            return;

        XPBlobCollector closestCollector = null;
        float closestDistanceSquared = float.MaxValue;

        // Find the closest collector that can reach this blob.
        for (int i = ActiveCollectors.Count - 1; i >= 0; i--)
        {
            XPBlobCollector collector = ActiveCollectors[i];

            if (collector == null || !collector.isActiveAndEnabled)
            {
                ActiveCollectors.RemoveAt(i);
                continue;
            }

            Vector3 offset =
                blob.transform.position - collector.transform.position;

            float distanceSquared = offset.sqrMagnitude;
            float rangeSquared =
                collector.collectionRange * collector.collectionRange;

            if (distanceSquared > rangeSquared)
                continue;

            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closestCollector = collector;
            }
        }

        if (closestCollector == null)
            return;

        if (!blob.TryClaim(closestCollector))
            return;

        closestCollector.CollectBlob(blob);
    }

    // Fallback polling for blobs that enter collection range after spawning.
    private void PollForXP()
    {
        for (int i = XPBlob.ActiveBlobs.Count - 1; i >= 0; i--)
        {
            XPBlob blob = XPBlob.ActiveBlobs[i];

            if (blob == null)
            {
                XPBlob.ActiveBlobs.RemoveAt(i);
                continue;
            }

            TryCollectBlob(blob);
        }
    }

    private bool TryCollectBlob(XPBlob blob)
    {
        if (blob == null || blob.IsBeingCollected)
            return false;

        Vector3 offset =
            blob.transform.position - transform.position;

        float rangeSquared = collectionRange * collectionRange;

        if (offset.sqrMagnitude > rangeSquared)
            return false;

        if (!blob.TryClaim(this))
            return false;

        CollectBlob(blob);
        return true;
    }

    private void CollectBlob(XPBlob blob)
    {
        bool startingNewBatch = collecting.Count == 0;

        collecting.Add(blob);

        if (startingNewBatch)
            PlayCollectionStartSound();

        // Calculate collection duration based on distance.
        float distance = Vector3.Distance(
            blob.transform.position,
            transform.position
        );

        float safeRange = Mathf.Max(0.001f, collectionRange);

        float normalizedDistance = Mathf.Clamp01(
            distance / safeRange
        );

        float minDuration = Mathf.Max(0.01f, minCollectionDuration);
        float maxDuration = Mathf.Max(minDuration, maxCollectionDuration);

        float duration = Mathf.Lerp(
            minDuration,
            maxDuration,
            normalizedDistance
        );

        Tween tween = blob.transform.DOMove(
                transform.position,
                duration
            )
            .SetEase(Ease.InCubic)
            .SetTarget(blob);

        blob.SetMoveTween(tween);

        tween.OnComplete(() =>
        {
            if (blob == null)
                return;

            CompleteCollection(blob);
        });
    }

    private void CompleteCollection(XPBlob blob)
    {
        if (!collecting.Remove(blob))
            return;

        collectedXP += blob.XPValue;

        blob.ClearMoveTween();
        blob.ReturnToPool();

        // Only play finish sound when the entire batch is done.
        if (collecting.Count == 0)
            PlayCollectionFinishSound();
    }

    internal void NotifyBlobDisabled(XPBlob blob)
    {
        if (collecting.Remove(blob) && collecting.Count == 0)
            PlayCollectionFinishSound();
    }

    private void PlayCollectionStartSound()
    {
        if (collectionStartSound == null)
            return;

        if (Time.time < nextStartSoundTime)
            return;

        AudioManager.Play3D(
            collectionStartSound,
            transform.position
        );

        nextStartSoundTime = Time.time + soundCooldown;
    }

    private void PlayCollectionFinishSound()
    {
        if (collectionFinishSound == null)
            return;

        if (Time.time < nextFinishSoundTime)
            return;

        AudioManager.Play3D(
            collectionFinishSound,
            transform.position
        );

        nextFinishSoundTime = Time.time + soundCooldown;
    }
}
