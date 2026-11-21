
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ForbiddenTome : MonoBehaviour
{
    private static readonly List<ForbiddenTome> activeTomes =
        new List<ForbiddenTome>();

    public static IReadOnlyList<ForbiddenTome> ActiveTomes =>
        activeTomes;

    [Header("Interaction")]
    [SerializeField, Min(0.1f)]
    private float interactionDistance = 3f;

    [SerializeField]
    private Transform interactionPoint;

    [Header("Forbidden Knowledge")]
    [Tooltip("0.5 = +50 percentage points of XP gain.")]
    [SerializeField, Min(0f)]
    private float xpGainIncrease = 0.5f;

    [Tooltip("0.25 = +25 percentage points of difficulty.")]
    [SerializeField, Min(0f)]
    private float difficultyIncrease = 0.25f;

    [Header("Activation Limits")]
    [SerializeField, Min(1)]
    private int maximumActivations = 1;

    [Header("Visuals")]
    [SerializeField]
    private Transform animatedObject;

    [SerializeField, Min(0f)]
    private float activationPunchScale = 0.25f;

    [SerializeField, Min(0.05f)]
    private float activationDuration = 0.5f;

    [SerializeField]
    private ParticleSystem activationParticles;

    [SerializeField]
    private ParticleSystem ambientParticles;

    [Header("Audio")]
    [SerializeField]
    private AudioClip activationSound;

    [SerializeField]
    private AudioClip exhaustedSound;

    private int activationCount;

    private Tween activationTween;
    private Vector3 originalScale;

    public float InteractionDistance => interactionDistance;

    public Vector3 InteractionPosition =>
        interactionPoint != null
            ? interactionPoint.position
            : transform.position;

    public float XPGainIncrease => xpGainIncrease;
    public float DifficultyIncrease => difficultyIncrease;

    public int ActivationCount => activationCount;

    public bool CanActivate =>
        isActiveAndEnabled &&
        activationCount < maximumActivations;

    private void Awake()
    {
        if (animatedObject == null)
            animatedObject = transform;

        originalScale = animatedObject.localScale;
    }

    private void OnEnable()
    {
        if (!activeTomes.Contains(this))
            activeTomes.Add(this);
    }

    private void OnDisable()
    {
        activeTomes.Remove(this);

        activationTween?.Kill();
        activationTween = null;

        if (animatedObject != null)
            animatedObject.localScale = originalScale;
    }

    // ==========================================
    // ACTIVATION
    // ==========================================

    public bool TryActivate(PlayerStats playerStats)
    {
        if (playerStats == null || !CanActivate)
            return false;

        // Additively increase the player's XP multiplier.
        // 1.0 -> 1.5 -> 2.0 -> 2.5
        playerStats.xpGain = Mathf.Max(
            1f,
            playerStats.xpGain + xpGainIncrease
        );

        // Increase the global difficulty consumed
        // by HordeManager.
        // 1.0 -> 1.25 -> 1.5 -> 1.75
        playerStats.difficulty = Mathf.Max(
            1f,
            playerStats.difficulty + difficultyIncrease
        );

        activationCount++;

        PlayActivationEffects();

        if (!CanActivate)
            ExhaustTome();

        return true;
    }

    // ==========================================
    // EFFECTS
    // ==========================================

    private void PlayActivationEffects()
    {
        if (activationSound != null)
        {
            AudioManager.Play3D(
                activationSound,
                transform.position
            );
        }

        if (activationParticles != null)
            activationParticles.Play(true);

        if (animatedObject == null)
            return;

        activationTween?.Kill();

        animatedObject.localScale = originalScale;

        activationTween = animatedObject
            .DOPunchScale(
                Vector3.one * activationPunchScale,
                activationDuration,
                8,
                0.7f
            )
            .SetTarget(this)
            .OnComplete(() =>
            {
                if (animatedObject != null)
                    animatedObject.localScale = originalScale;

                activationTween = null;
            });
    }

    private void ExhaustTome()
    {
        // Keep the Tome in the world, but prevent
        // further activations.

        if (ambientParticles != null)
            ambientParticles.Stop(true);

        if (exhaustedSound != null)
        {
            AudioManager.Play3D(
                exhaustedSound,
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
