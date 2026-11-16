
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

    [Header("XP Progression")]
    [SerializeField] private PlayerLevelSystem levelSystem;

    [Header("Audio")]
    public AudioClip collectionStartSound;
    public AudioClip collectionFinishSound;

    [SerializeField] private float soundCooldown = 0.12f;

    // Kept for compatibility with older systems.
    // This now tracks XP awarded after applying the multiplier.
    public static float collectedXP;

    private static readonly List<XPBlobCollector> ActiveCollectors =
        new List<XPBlobCollector>();

    private readonly HashSet<XPBlob> collecting =
        new HashSet<XPBlob>();

    private static float nextStartSoundTime;
    private static float nextFinishSoundTime;

    private void Awake()
    {
        // Automatically finds the progression system for the player collector.
        // For the vehicle collector, assign the player's system in the Inspector.
        if (levelSystem == null)
            levelSystem = GetComponentInParent<PlayerLevelSystem>();

        if (levelSystem == null)
        {
            Debug.LogError(
                "XPBlobCollector: No PlayerLevelSystem assigned. " +
                "Assign the player's PlayerLevelSystem in the Inspector.",
                this
            );
        }
    }

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

        foreach (XPBlob blob in collecting)
        {
            if (blob == null)
                continue;

            blob.CancelMoveTween();
            blob.ClearCollector(this);
        }

        collecting.Clear();
    }

    public static void NotifyBlobSpawned(XPBlob blob)
    {
        if (blob == null || blob.IsBeingCollected)
            return;

        XPBlobCollector closestCollector = null;
        float closestDistanceSquared = float.MaxValue;

        for (int i = ActiveCollectors.Count - 1; i >= 0; i--)
        {
            XPBlobCollector collector = ActiveCollectors[i];

            if (collector == null || !collector.isActiveAndEnabled)
            {
                ActiveCollectors.RemoveAt(i);
                continue;
            }

            // Collectors without a progression target cannot collect XP.
            if (collector.levelSystem == null)
                continue;

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
        if (levelSystem == null || blob == null || blob.IsBeingCollected)
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

        if (levelSystem != null)
        {
            // Apply the PLAYER'S XP multiplier, regardless of
            // whether this blob was collected by the person or car.
            float xpAwarded =
                blob.XPValue * levelSystem.XPGainMultiplier;

            collectedXP += xpAwarded;

            levelSystem.AddXP(xpAwarded);
        }

        blob.ClearMoveTween();
        blob.ReturnToPool();

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
