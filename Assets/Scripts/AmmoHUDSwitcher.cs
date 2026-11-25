using UnityEngine;

/// <summary>Owns the two HUD roots and samples the actual active weapon after gameplay updates.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
[AddComponentMenu("UI/Ammo HUD Switcher")]
public sealed class AmmoHUDSwitcher : MonoBehaviour
{
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;
    [SerializeField] private PlayerGun playerGun;
    [Header("Separate sibling roots (keep this switcher outside both)")]
    [SerializeField] private GameObject playerAmmoRoot;
    [SerializeField] private GameObject vehicleAmmoRoot;
    [SerializeField] private AmmoCounterMeter playerMeter;
    [SerializeField] private AmmoCounterMeter vehicleMeter;

    private Object previousSource;
    private bool hasMode;
    private bool previousInVehicle;

    private void Awake()
    {
        if (playerGun == null && vehicleInteractor != null)
            playerGun = vehicleInteractor.GetComponentInChildren<PlayerGun>(true);
        if (vehicleInteractor == null && playerGun != null)
            vehicleInteractor = playerGun.GetComponentInParent<PlayerVehicleInteractor>();
        if (playerMeter == null && playerAmmoRoot != null)
            playerMeter = playerAmmoRoot.GetComponentInChildren<AmmoCounterMeter>(true);
        if (vehicleMeter == null && vehicleAmmoRoot != null)
            vehicleMeter = vehicleAmmoRoot.GetComponentInChildren<AmmoCounterMeter>(true);
        if (vehicleInteractor == null || playerGun == null || playerAmmoRoot == null ||
            vehicleAmmoRoot == null || playerMeter == null || vehicleMeter == null ||
            playerAmmoRoot == vehicleAmmoRoot || playerMeter == vehicleMeter ||
            !Inside(playerMeter.transform, playerAmmoRoot.transform) ||
            !Inside(vehicleMeter.transform, vehicleAmmoRoot.transform) ||
            Inside(playerAmmoRoot.transform, vehicleAmmoRoot.transform) ||
            Inside(vehicleAmmoRoot.transform, playerAmmoRoot.transform) ||
            Inside(transform, playerAmmoRoot.transform) || Inside(transform, vehicleAmmoRoot.transform))
        {
            Debug.LogError("AmmoHUDSwitcher: assign the interactor, gun and two separate HUD roots/meters. Keep the switcher outside both roots.", this);
            enabled = false;
        }
    }
    private static bool Inside(Transform child, Transform root) => child == root || child.IsChildOf(root);
    private void OnEnable() { hasMode = false; previousSource = null; }
    private void OnDisable() { hasMode = false; previousSource = null; }
    private void LateUpdate()
    {
        if (vehicleInteractor == null) return;
        bool inVehicle = vehicleInteractor.IsInVehicle;
        CarController car = inVehicle ? vehicleInteractor.currentCarController : null;
        TurretGun turret = car != null ? car.VehicleTurret : null;
        Object source = inVehicle ? (Object)turret : playerGun;
        bool changed = !hasMode || previousInVehicle != inVehicle || !ReferenceEquals(previousSource, source);
        GameObject show = inVehicle ? vehicleAmmoRoot : playerAmmoRoot;
        GameObject hide = inVehicle ? playerAmmoRoot : vehicleAmmoRoot;
        AmmoCounterMeter meter = inVehicle ? vehicleMeter : playerMeter;
        if (hide != null && hide.activeSelf) hide.SetActive(false);
        if (show != null && !show.activeSelf) { show.SetActive(true); changed = true; }
        if (meter == null) return;
        if (changed) meter.ResetFeedback();
        previousSource = source;
        previousInVehicle = inVehicle;
        hasMode = true;

        if (inVehicle)
        {
            if (turret == null || !turret.AmmoInitialized)
                meter.Present(0, 0, false, 0f, available: false);
            else
                meter.Present(turret.CurrentAmmo, turret.MagazineSize, turret.IsReloading,
                    turret.ReloadProgress, turret.IsEmpDisabled || car.IsEmpDisabled,
                    !turret.isActiveAndEnabled || car.IsVehicleWrecked);
        }
        else if (playerGun == null || !playerGun.AmmoInitialized)
            meter.Present(0, 0, false, 0f, available: false);
        else
            meter.Present(playerGun.CurrentAmmo, playerGun.MagazineSize, playerGun.IsReloading,
                playerGun.ReloadProgress, offline: !playerGun.isActiveAndEnabled);
    }
}
