using UnityEngine;

/// <summary>
/// Receives Unity Animation Events from the first-person arms Animator and forwards
/// them to PlayerGun. Attach this component to the same GameObject as the Animator.
/// The PlayerGun reference is auto-found in the parent hierarchy when possible.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class WeaponAnimationEvents : MonoBehaviour
{
    [Tooltip("PlayerGun on the player root. Auto-found in parent objects when left empty.")]
    [SerializeField] private PlayerGun playerGun;

    private void Awake()
    {
        if (playerGun == null)
            playerGun = GetComponentInParent<PlayerGun>();

        if (playerGun == null)
        {
            Debug.LogError(
                "WeaponAnimationEvents: Could not find PlayerGun. Assign it in the Inspector.",
                this);
        }
    }

    // Add these exact function names as Animation Events on the reload clip.
    public void PlayReloadSound()
    {
        playerGun?.AnimationEvent_PlayReloadSound();
    }

    public void PlayMagazineOutSound()
    {
        playerGun?.AnimationEvent_PlayMagazineOutSound();
    }

    public void PlayMagazineInSound()
    {
        playerGun?.AnimationEvent_PlayMagazineInSound();
    }

    public void PlayBoltActionSound()
    {
        playerGun?.AnimationEvent_PlayBoltActionSound();
    }

    // This can be added more than once to a shell-by-shell shotgun reload clip.
    public void PlayShellInsertSound()
    {
        playerGun?.AnimationEvent_PlayShellInsertSound();
    }
}
