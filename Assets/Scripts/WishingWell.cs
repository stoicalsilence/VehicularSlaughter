
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WishingWell : MonoBehaviour
{
    private static readonly List<WishingWell> activeWells =
        new List<WishingWell>();

    public static IReadOnlyList<WishingWell> ActiveWells =>
        activeWells;

    [Header("Interaction")]
    [SerializeField, Min(0.1f)]
    private float interactionDistance = 3f;

    [SerializeField]
    private Transform interactionPoint;

    [Header("XP Transfer Visuals")]
    [Tooltip("Visual-only prefab. Do not attach XPBlob.")]
    [SerializeField]
    private GameObject xpBlobVisualPrefab;

    [SerializeField]
    private Transform visualTargetPoint;

    [SerializeField, Min(1)]
    private int visualsPerInteraction = 5;

    [SerializeField, Min(0.05f)]
    private float minimumFlightDuration = 0.45f;

    [SerializeField, Min(0.05f)]
    private float maximumFlightDuration = 0.8f;

    [SerializeField, Min(0f)]
    private float arcHeight = 2f;

    [SerializeField, Min(0f)]
    private float randomStartDelay = 0.3f;

    [SerializeField, Min(0f)]
    private float sourceHeight = 1f;

    [Header("Audio")]
    [SerializeField] private AudioClip interactionSound;
    [SerializeField] private AudioClip transferFinishedSound;

    private readonly HashSet<GameObject> activeVisuals =
        new HashSet<GameObject>();

    public float InteractionDistance => interactionDistance;

    public Vector3 InteractionPosition =>
        interactionPoint != null
            ? interactionPoint.position
            : transform.position;

    private void OnEnable()
    {
        if (!activeWells.Contains(this))
            activeWells.Add(this);
    }

    private void OnDisable()
    {
        activeWells.Remove(this);

        DOTween.Kill(this);

        foreach (GameObject visual in activeVisuals)
        {
            if (visual != null)
                Destroy(visual);
        }

        activeVisuals.Clear();
    }

    public void PlayTransfer(Vector3 sourcePosition)
    {
        if (interactionSound != null)
        {
            AudioManager.Play3D(
                interactionSound,
                transform.position
            );
        }

        if (xpBlobVisualPrefab == null)
            return;

        int visualCount = Mathf.Clamp(
            visualsPerInteraction,
            1,
            32
        );

        int remainingVisuals = visualCount;

        Vector3 targetPosition = visualTargetPoint != null
            ? visualTargetPoint.position
            : transform.position + Vector3.up * 0.75f;

        for (int i = 0; i < visualCount; i++)
        {
            Vector3 startPosition =
                sourcePosition
                + Vector3.up * sourceHeight
                + Random.insideUnitSphere * 0.35f;

            Vector3 endPosition =
                targetPosition
                + Random.insideUnitSphere * 0.2f;

            Vector3 controlPoint =
                Vector3.Lerp(
                    startPosition,
                    endPosition,
                    0.5f
                )
                + Vector3.up * arcHeight
                + Random.insideUnitSphere * 0.2f;

            GameObject visual = Instantiate(
                xpBlobVisualPrefab,
                startPosition,
                Random.rotation
            );

            activeVisuals.Add(visual);

            float duration = Random.Range(
                minimumFlightDuration,
                Mathf.Max(
                    minimumFlightDuration,
                    maximumFlightDuration
                )
            );

            float delay = Random.Range(
                0f,
                randomStartDelay
            );

            AnimateSingleBlob(
                visual,
                startPosition,
                controlPoint,
                endPosition,
                duration,
                delay,
                () =>
                {
                    activeVisuals.Remove(visual);

                    remainingVisuals--;

                    if (remainingVisuals == 0)
                        CompleteTransfer();
                }
            );
        }
    }

    private void AnimateSingleBlob(
        GameObject visual,
        Vector3 start,
        Vector3 control,
        Vector3 end,
        float duration,
        float delay,
        System.Action onComplete)
    {
        Sequence sequence = DOTween.Sequence()
            .SetTarget(this);

        sequence.AppendInterval(delay);

        Tween movement = DOVirtual.Float(
            0f,
            1f,
            duration,
            progress =>
            {
                if (visual == null)
                    return;

                float inverse = 1f - progress;

                visual.transform.position =
                    inverse * inverse * start
                    + 2f * inverse * progress * control
                    + progress * progress * end;

                visual.transform.Rotate(
                    Vector3.up,
                    360f * Time.deltaTime,
                    Space.World
                );
            }
        ).SetEase(Ease.InOutSine);

        sequence.Append(movement);

        sequence.Insert(
            delay + duration * 0.75f,
            visual.transform
                .DOScale(
                    Vector3.zero,
                    duration * 0.25f
                )
                .SetEase(Ease.InBack)
        );

        sequence.OnComplete(() =>
        {
            if (visual != null)
                Destroy(visual);

            onComplete?.Invoke();
        });
    }

    private void CompleteTransfer()
    {
        if (transferFinishedSound != null)
        {
            AudioManager.Play3D(
                transferFinishedSound,
                transform.position
            );
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;

        Gizmos.DrawWireSphere(
            InteractionPosition,
            interactionDistance
        );
    }
}
