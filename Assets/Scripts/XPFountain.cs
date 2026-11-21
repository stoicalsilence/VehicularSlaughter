
using DG.Tweening;
using UnityEngine;

public sealed class XPFountain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public PlayerLevelSystem levelSystem;
    [SerializeField] public PlayerLevelRewardSystem rewardSystem;

    [Header("Distance Detection")]
    [SerializeField, Min(0.1f)] private float interactionDistance = 3f;

    [Header("Visual XP Collection")]
    [Tooltip("A visual-only XP blob prefab. Do not use a prefab with the XPBlob script.")]
    [SerializeField] private GameObject xpBlobVisualPrefab;

    [SerializeField] private Transform visualTargetPoint;

    [SerializeField, Min(0)] private int visualsPerLevel = 2;
    [SerializeField, Min(1)] private int maximumVisuals = 18;

    [SerializeField, Min(0.05f)] private float minimumFlightDuration = 0.45f;
    [SerializeField, Min(0.05f)] private float maximumFlightDuration = 0.8f;

    [SerializeField, Min(0f)] private float arcHeight = 2f;
    [SerializeField, Min(0f)] private float randomStartDelay = 0.3f;

    [Header("Audio")]
    [SerializeField] private AudioClip bankingSound;

    private Transform playerTransform;
    private Transform vehicleTransform;

    private bool isBanking;

    public Animator anim;
    public AnimationClip spawnAnim;
    public AnimationClip leaveAnim;

    public AudioClip thudSound;
    public AudioClip grabSound;
    public AudioClip enterSound;

    public ParticleSystem p1;
    public ParticleSystem p2;

    private void Update()
    {
        if (levelSystem == null || rewardSystem == null || isBanking)
            return;

        FindNearbyPlayerOrVehicle();

        Transform target = GetClosestTarget();

        if (target == null)
            return;

        float distance = Vector3.Distance(
            transform.position,
            target.position
        );

        if (distance > interactionDistance)
            return;

        TransferBankedLevels(target.position);
    }

    public void SpawnIn()
    {
        AudioManager.Play3D(enterSound, transform.position, 1, 1, 1, 100);
        anim.Play(spawnAnim.name);
        Invoke(nameof(PlayThudSound), 1);
    }

    public void Leave()
    {
        AudioManager.Play3D(enterSound, transform.position, 1, 1, 1, 100);
        anim.Play(leaveAnim.name);
        Invoke(nameof(PlayGrabSound), 4.1f);
        Destroy(this.gameObject, 2.45f);
    }

    public void PlayThudSound()
    {
        AudioManager.Play3D(thudSound, transform.position, 1, 1, 1, 100);
        p1.Play();
        p2.Play();
    }

    public void PlayGrabSound()
    {
        AudioManager.Play3D(grabSound, transform.position, 0.75f, 1, 1, 100);
        p1.Play();
        p2.Play();
    }

    private void FindNearbyPlayerOrVehicle()
    {
        if (playerTransform == null)
        {
            PlayerStats player = FindFirstObjectByType<PlayerStats>();

            if (player != null)
                playerTransform = player.transform;
        }

        if (vehicleTransform == null)
        {
            CarStats vehicle = FindFirstObjectByType<CarStats>();

            if (vehicle != null)
                vehicleTransform = vehicle.transform;
        }
    }

    private Transform GetClosestTarget()
    {
        bool playerValid = playerTransform != null;
        bool vehicleValid = vehicleTransform != null;

        if (!playerValid && !vehicleValid)
            return null;

        if (!playerValid)
            return vehicleTransform;

        if (!vehicleValid)
            return playerTransform;

        float playerDistance =
            (playerTransform.position - transform.position).sqrMagnitude;

        float vehicleDistance =
            (vehicleTransform.position - transform.position).sqrMagnitude;

        return playerDistance <= vehicleDistance
            ? playerTransform
            : vehicleTransform;
    }

    private void TransferBankedLevels(Vector3 sourcePosition)
    {
        int levelsToTransfer = levelSystem.BankedLevels;

        if (levelsToTransfer <= 0)
            return;

        if (!levelSystem.TrySpendLevels(levelsToTransfer))
            return;

        isBanking = true;

        PlayBankingSound();

        AnimateXPBanking(sourcePosition, levelsToTransfer);
    }

    // ==================================================
    // XP VISUALS
    // ==================================================

    private void AnimateXPBanking(Vector3 sourcePosition, int levels)
    {
        if (xpBlobVisualPrefab == null || visualsPerLevel <= 0)
        {
            CompleteBanking(levels);
            return;
        }

        int visualCount = (int)Mathf.Min(
            maximumVisuals,
            (long)levels * visualsPerLevel
        );

        if (visualCount <= 0)
        {
            CompleteBanking(levels);
            return;
        }

        Vector3 targetPosition = visualTargetPoint != null
            ? visualTargetPoint.position
            : transform.position + Vector3.up * 0.75f;

        int completedVisuals = 0;

        for (int i = 0; i < visualCount; i++)
        {
            Vector3 startPosition =
                sourcePosition + Random.insideUnitSphere * 0.35f;

            Vector3 endPosition =
                targetPosition + Random.insideUnitSphere * 0.2f;

            Vector3 controlPoint =
                Vector3.Lerp(startPosition, endPosition, 0.5f)
                + Vector3.up * arcHeight
                + Random.insideUnitSphere * 0.2f;

            GameObject visual = Instantiate(
                xpBlobVisualPrefab,
                startPosition,
                Random.rotation
            );

            float duration = Random.Range(
                minimumFlightDuration,
                Mathf.Max(minimumFlightDuration, maximumFlightDuration)
            );

            float delay = Random.Range(0f, randomStartDelay);

            AnimateSingleBlob(
                visual,
                startPosition,
                controlPoint,
                endPosition,
                duration,
                delay,
                () =>
                {
                    completedVisuals++;

                    if (completedVisuals >= visualCount)
                        CompleteBanking(levels);
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
        Sequence sequence = DOTween.Sequence().SetTarget(visual);

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
        ).SetEase(Ease.InOutSine).SetTarget(visual);

        sequence.Append(movement);

        sequence.Insert(
            delay + duration * 0.75f,
            visual.transform
                .DOScale(Vector3.zero, duration * 0.25f)
                .SetEase(Ease.InBack)
        );

        sequence.OnComplete(() =>
        {
            if (visual != null)
                Destroy(visual);

            onComplete?.Invoke();
        });
    }

    private void CompleteBanking(int levels)
    {
        // Start level-up selection only after every visual has arrived.
        rewardSystem.QueueLevelUps(levels);

        isBanking = false;
    }

    private void PlayBankingSound()
    {
        if (bankingSound == null)
            return;

        AudioManager.Play3D(
            bankingSound,
            transform.position
        );
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionDistance);
    }
}
