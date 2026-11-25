/*
MESSAGE FROM CREATOR: This script was coded by Mena. You can use it in your games either these are commercial or
personal projects. You can even add or remove functions as you wish. However, you cannot sell copies of this
script by itself, since it is originally distributed as a free product.
I wish you the best for your project. Good luck!

P.S: If you need more cars, you can check my other vehicle assets on the Unity Asset Store, perhaps you could find
something useful for your game. Best regards, Mena.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CarController : MonoBehaviour
{

    [Header("Stats")]
    [SerializeField] private CarStats carStats;

    //CAR SETUP
    public int maxSpeed => carStats.maxSpeed; //The maximum speed that the car can reach in km/h.

    public int maxReverseSpeed => carStats.maxReverseSpeed; //The maximum speed that the car can reach while going on reverse in km/h.

    public int accelerationMultiplier => carStats.accelerationMultiplier; // How fast the car can accelerate. 1 is a slow acceleration and 10 is the fastest.

    public int maxSteeringAngle => carStats.maxSteeringAngle; // The maximum angle that the tires can reach while rotating the steering wheel.

    public float steeringSpeed => carStats.steeringSpeed; // How fast the steering wheel turns.

    public int brakeForce => carStats.brakeForce; // The strength of the wheel brakes.

    public int decelerationMultiplier => carStats.decelerationMultiplier; // How fast the car decelerates when the user is not using the throttle.

    public int handbrakeDriftMultiplier => carStats.handbrakeDriftMultiplier; // How much grip the car loses when the user hit the handbrake.
    [Space(10)]
    public Vector3 bodyMassCenter; // This is a vector that contains the center of mass of the car. I recommend to set this value
                                   // in the points x = 0 and z = 0 of your car. You can select the value that you want in the y axis,
                                   // however, you must notice that the higher this value is, the more unstable the car becomes.
                                   // Usually the y value goes from 0 to 1.5.

    //WHEELS

    //[Header("WHEELS")]

    /*
    The following variables are used to store the wheels' data of the car. We need both the mesh-only game objects and wheel
    collider components of the wheels. The wheel collider components and 3D meshes of the wheels cannot come from the same
    game object; they must be separate game objects.
    */
    public GameObject frontLeftMesh;
    public WheelCollider frontLeftCollider;
    [Space(10)]
    public GameObject frontRightMesh;
    public WheelCollider frontRightCollider;
    [Space(10)]
    public GameObject rearLeftMesh;
    public WheelCollider rearLeftCollider;
    [Space(10)]
    public GameObject rearRightMesh;
    public WheelCollider rearRightCollider;

    //PARTICLE SYSTEMS

    [Space(20)]
    //[Header("EFFECTS")]
    [Space(10)]
    //The following variable lets you to set up particle systems in your car
    public bool useEffects = false;

    // The following particle systems are used as tire smoke when the car drifts.
    public ParticleSystem RLWParticleSystem;
    public ParticleSystem RRWParticleSystem;

    [Space(10)]
    // The following trail renderers are used as tire skids when the car loses traction.
    public TrailRenderer RLWTireSkid;
    public TrailRenderer RRWTireSkid;

    [Space(10)]
    [Tooltip("Exhaust particles. Emission runs only while forward throttle is being applied. Use a looping particle system for continuous exhaust.")]
    public ParticleSystem exhaustParticles;

    [Tooltip("Assign dedicated brake-light meshes/lights here. They are enabled only while the car is actively braking; do not assign tail lamps that should stay visible all the time.")]
    public GameObject[] rearBrakeLights;

    //SPEED TEXT (UI)

    [Space(20)]
    //[Header("UI")]
    [Space(10)]
    // Optional standalone TMP output. Leave disabled when using SpeedometerMeter.
    public bool useUI = false;
    public TextMeshProUGUI carSpeedText;
    public GameObject parkBrakeIcon;

    //SOUNDS

    [Space(20)]
    //[Header("Sounds")]
    [Space(10)]
    //The following variable lets you to set up sounds for your car such as the car engine or tire screech sounds.
    public bool useSounds = false;
    public AudioSource carEngineSound; // This variable stores the sound of the car engine.
    public AudioSource tireScreechSound; // This variable stores the sound of the tire screech (when the car is drifting).
    float initialCarEngineSoundPitch; // Used to store the initial pitch of the car engine sound.

    [Min(0.1f)]
    [Tooltip("How quickly the engine pitch rises when throttle is applied.")]
    public float enginePitchRiseSpeed = 10f;

    [Min(0.1f)]
    [Tooltip("How quickly the engine pitch returns to its normal pitch after throttle is released.")]
    public float enginePitchReturnSpeed = 3f;

    // CRASH EFFECTS
    [Space(20)]
    [Header("CRASH EFFECTS")]
    public bool useCrashEffects = true;

    [Tooltip("Collisions below this impact speed will not trigger crash effects. Speeds are in km/h.")]
    [Min(0f)]
    public float minimumCrashSpeedKmh = 8f;

    [Tooltip("Impacts at or above this speed use the medium crash effects.")]
    [Min(0f)]
    public float mediumCrashSpeedKmh = 25f;

    [Tooltip("Impacts at or above this speed use the heavy crash effects.")]
    [Min(0f)]
    public float heavyCrashSpeedKmh = 50f;

    [Tooltip("Minimum time between crash effects, in seconds.")]
    [Min(0f)]
    public float crashCooldown = 0.25f;

    [Tooltip("Only collisions with objects on these layers can trigger crash effects. Set this to Everything to allow all layers.")]
    public LayerMask crashLayerMask = ~0;

    [Space(10)]
    [Tooltip("Non-looping particle prefab for light impacts. It is spawned at the collision point.")]
    public ParticleSystem lightCrashParticlesPrefab;
    public AudioClip lightCrashSound;

    [Space(5)]
    [Tooltip("Non-looping particle prefab for medium impacts. It is spawned at the collision point.")]
    public ParticleSystem mediumCrashParticlesPrefab;
    public AudioClip mediumCrashSound;

    [Space(5)]
    [Tooltip("Non-looping particle prefab for heavy impacts. It is spawned at the collision point.")]
    public ParticleSystem heavyCrashParticlesPrefab;
    public AudioClip heavyCrashSound;

    [Range(0f, 1f)]
    [Tooltip("Volume multiplier for crash sounds played at the collision point.")]
    public float crashSoundVolume = 1f;

    float nextCrashEffectTime;

    // VEHICLE DURABILITY AND HORDE IMPACTS
    [Space(20)]
    [Header("VEHICLE DURABILITY / ENEMY IMPACTS")]
    [SerializeField] private bool enableVehicleImpactSystem = true;

    public ParticleSystem severeDamageParticleEffect;
    public AudioClip[] severeDamageSounds;
    public bool severeDamageActive;

    private float maxVehicleHealth => carStats.maxVehicleHealth;

    // Read-only HUD access to the authoritative stats and initialization state.
    public float MaxVehicleHealth => carStats != null ? maxVehicleHealth : 0f;
    public bool IsVehicleHealthInitialized => vehicleHealthInitialized;

    private float minimumRunOverSpeedKmh => carStats.minimumRunOverSpeedKmh;

    private float maximumImpactSpeedKmh => carStats.maximumImpactSpeedKmh;

    private float minimumEnemyDamage => carStats.minimumEnemyDamage;

    private float maximumEnemyDamage => carStats.maximumEnemyDamage;

    private float minimumEnemyKnockback => carStats.minimumEnemyKnockback;

    private float maximumEnemyKnockback => carStats.maximumEnemyKnockback;

    private float minimumVehicleDamagePerHit => carStats.minimumVehicleDamagePerHit;

    private float maximumVehicleDamagePerHit => carStats.maximumVehicleDamagePerHit;

    public float currentVehicleHealth;
    public bool vehicleWrecked;

    [Header("Vehicle Destruction")]
    [SerializeField] private PlayerRadio playerRadio;
    [Tooltip("Optional override. If empty, reuse the radio's airstrike explosion prefab.")]
    [SerializeField] private GameObject carExplosionPrefab;
    [Tooltip("Optional blast centre. Defaults to the car's centre of mass.")]
    [SerializeField] private Transform carExplosionPoint;
    [SerializeField, Min(0.01f)] private float carExplosionBaseVisualRadius = 5f;
    [SerializeField, Min(0f)] private float carExplosionKnockback = 8f;
    [SerializeField, Min(0.1f)] private float carExplosionLifetime = 3f;
    [SerializeField] private ParticleSystem wreckFire;
    [SerializeField] private TurretGun vehicleTurret;
    [SerializeField] private XPBlobCollector vehicleXPCollector;
    [SerializeField, Min(0f)] private float ejectionHorizontalSpeed = 12f;
    [SerializeField, Min(0f)] private float ejectionVerticalSpeed = 10f;

    [Header("EMP / Battery")]
    [SerializeField, Min(0.1f)] private float empDisableDuration = 15f;
    [Tooltip("Dedicated looping blue electrical sparks on the car; not the wreck fire.")]
    [SerializeField] private ParticleSystem empDisabledParticles;
    [Tooltip("Optional HUD text: battery health or countdown. Leave unassigned if unused.")]
    [SerializeField] private TextMeshProUGUI batteryStatusText;
    private const int MaxBatteryHP = 2;
    private int batteryHP = MaxBatteryHP;
    private float empDisabledUntil;
    public int BatteryHP => batteryHP;
    public int MaxBatteryHealth => MaxBatteryHP;
    public bool IsEmpDisabled => empDisabledUntil > 0f;
    public float EmpSecondsRemaining => IsEmpDisabled ? Mathf.Max(0f, empDisabledUntil - Time.time) : 0f;
    public event Action<int, int> BatteryHealthChanged;
    private int lastBatteryDisplayHP = -1;
    private int lastBatteryDisplaySeconds = -1;
    private bool lastBatteryDisplayWrecked;

    private bool turretWasEnabled;
    private bool collectorWasEnabled;

    // Legacy scene reference only; no damage-number updates are performed.
    [HideInInspector] public TextMeshProUGUI lastDamageTakenText;
    private float enemyContactDamageCooldown => carStats.enemyContactDamageCooldown;

    private float vehicleThornsDamage => carStats.vehicleThornsDamage;

    public AudioClip damageSound;
    public ParticleSystem damageParticles;

    // Contact damage is tracked independently for each enemy. This prevents one
    // enemy's cooldown from suppressing damage from the rest of the horde.
    private sealed class EnemyContactState
    {
        public float nextDamageTime;
        public float lastContactTime;
    }

    private bool vehicleHealthInitialized;

    private readonly Dictionary<HordeEnemy, EnemyContactState> enemyContactStates =
        new Dictionary<HordeEnemy, EnemyContactState>();

    // Reused buffer so stale contact entries can be removed without allocating
    // a new list every physics frame.
    private readonly List<HordeEnemy> staleEnemyContacts =
        new List<HordeEnemy>();

    // Forget enemies that have not generated a collision callback recently.
    // This also handles pooled/despawned enemies and avoids stale dictionary entries.
    private const float EnemyContactForgetDelay = 0.25f;

    public bool IsVehicleWrecked => vehicleWrecked;
    public TurretGun VehicleTurret => vehicleTurret;

    // Subscribe from a HUD or game manager if you want to display vehicle health.
    public event Action<float, float> VehicleHealthChanged;
    // A reset is not ordinary healing; HUDs can snap instead of animating.
    public event Action VehicleHealthReset;

    //CONTROLS

    [Space(20)]
    //[Header("CONTROLS")]
    [Space(10)]
    //The following variables lets you to set up touch controls for mobile devices.
    public bool useTouchControls = false;
    public GameObject throttleButton;
    PrometeoTouchInput throttlePTI;
    public GameObject reverseButton;
    PrometeoTouchInput reversePTI;
    public GameObject turnRightButton;
    PrometeoTouchInput turnRightPTI;
    public GameObject turnLeftButton;
    PrometeoTouchInput turnLeftPTI;
    public GameObject handbrakeButton;
    PrometeoTouchInput handbrakePTI;

    //CAR DATA

    [HideInInspector]
    public float carSpeed; // Legacy drivetrain value: keep existing torque/FX behavior.

    /// <summary>Horizontal chassis speed in km/h, independent of wheel slip.</summary>
    public float ActualSpeedKmh
    {
        get
        {
            if (carRigidbody == null) return 0f;
            Vector3 velocity = carRigidbody.velocity;
            velocity.y = 0f;
            return velocity.magnitude * 3.6f;
        }
    }

    /// <summary>Signed horizontal speed along the vehicle heading, in km/h.</summary>
    public float SignedForwardSpeedKmh
    {
        get
        {
            if (carRigidbody == null) return 0f;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return 0f;
            return Vector3.Dot(carRigidbody.velocity, forward.normalized) * 3.6f;
        }
    }

    public float SpeedometerMaximumKmh => carStats != null
        ? Mathf.Max(1f, carStats.maxSpeed) : 1f;
    [HideInInspector]
    public bool isDrifting; // Used to know whether the car is drifting or not.
    [HideInInspector]
    public bool isTractionLocked; // Used to know whether the traction of the car is locked or not.

    //PRIVATE VARIABLES

    /*
    IMPORTANT: The following variables should not be modified manually since their values are automatically given via script.
    */
    Rigidbody carRigidbody; // Stores the car's rigidbody.
    Vector3 velocityBeforePhysicsStep;
    float steeringAxis; // Used to know whether the steering wheel has reached the maximum value. It goes from -1 to 1.
    float throttleAxis; // Used to know whether the throttle has reached the maximum value. It goes from -1 to 1.
    float driftingAxis;
    float localVelocityZ;
    float localVelocityX;
    bool deceleratingCar;
    bool touchControlsSetup = false;

    // The vehicle can remain active physically while player input is disabled (for example,
    // when the player exits it). VehicleSeat owns this switch.
    [SerializeField, Tooltip("When false, keyboard/touch driving input is ignored. Vehicle physics and effects continue.")]
    private bool playerDrivingEnabled = true;

    [Header("Parking Brake")]
    [SerializeField] private KeyCode parkingBrakeKey = KeyCode.V;
    [SerializeField, Min(1000f)] private float parkingBrakeTorque = 15000f;
    [SerializeField, Min(0.01f)] private float parkingLockSpeed = 0.3f;
    [SerializeField, Tooltip("Played when the parking brake is engaged or released.")]
    private AudioClip parkingBrakeToggleSound;
    [SerializeField, Min(0f)]
    private float parkingBrakeToggleCooldown = 1f;

    private float nextParkingBrakeToggleTime;

    private bool parkingBrakeEngaged;
    private bool parkingPositionLocked;
    private RigidbodyConstraints constraintsBeforeParking;
    public bool IsParkingBrakeEngaged => parkingBrakeEngaged;

    // Only an occupant of this specific car may engage or release its parking brake.
    private bool HasPlayerInDriverSeat()
    {
        foreach (PlayerVehicleInteractor occupant in GetComponentsInChildren<PlayerVehicleInteractor>(true))
        {
            if (occupant.IsInVehicle && occupant.currentCarController == this)
                return true;
        }
        return false;
    }

    public void SetParkingBrake(bool engaged)
    {
        if (Time.time < nextParkingBrakeToggleTime) return;

        if (!HasPlayerInDriverSeat() || parkingBrakeEngaged == engaged || vehicleWrecked) return;
        parkingBrakeEngaged = engaged;
        nextParkingBrakeToggleTime = Time.time + parkingBrakeToggleCooldown;
        if (parkingBrakeToggleSound != null)
            AudioManager.Play2D(parkingBrakeToggleSound, 1f);

        if (engaged)
        {
            CancelInvoke(nameof(DecelerateCar));
            deceleratingCar = false;
            throttleAxis = 0f;
            // Keep the normal drifting handbrake independent of parking.
            ApplyParkingBrake();
        }
        else
        {
            UnlockParkingPosition();
            if (!IsEmpDisabled) ReleaseBrakes();
        }
    }

    private void UnlockParkingPosition()
    {
        if (!parkingPositionLocked || carRigidbody == null) return;
        carRigidbody.constraints = constraintsBeforeParking;
        parkingPositionLocked = false;
        carRigidbody.WakeUp();
    }

    private void ApplyParkingBrake()
    {
        if (!parkingBrakeEngaged) return;
        // A parking brake is not a drifting handbrake: lock wheel rotation without removing grip.
        frontLeftCollider.motorTorque = frontRightCollider.motorTorque = 0f;
        rearLeftCollider.motorTorque = rearRightCollider.motorTorque = 0f;
        frontLeftCollider.brakeTorque = frontRightCollider.brakeTorque = parkingBrakeTorque;
        rearLeftCollider.brakeTorque = rearRightCollider.brakeTorque = parkingBrakeTorque;
    }

    private void FixedUpdateParkingBrake()
    {
        if (!parkingBrakeEngaged || carRigidbody == null || vehicleWrecked) return;
        ApplyParkingBrake();
        if (parkingPositionLocked || carRigidbody.velocity.sqrMagnitude > parkingLockSpeed * parkingLockSpeed)
            return;

        // Never suspend a car in mid-air: at least two wheels must touch the ground.
        WheelHit hit;
        int grounded = 0;
        if (frontLeftCollider.GetGroundHit(out hit)) grounded++;
        if (frontRightCollider.GetGroundHit(out hit)) grounded++;
        if (rearLeftCollider.GetGroundHit(out hit)) grounded++;
        if (rearRightCollider.GetGroundHit(out hit)) grounded++;
        if (grounded < 2) return;

        constraintsBeforeParking = carRigidbody.constraints;
        parkingPositionLocked = true;
        carRigidbody.velocity = Vector3.zero;
        carRigidbody.angularVelocity = Vector3.zero;
        carRigidbody.constraints = RigidbodyConstraints.FreezeAll;
    }

    public bool PlayerDrivingEnabled => playerDrivingEnabled && !vehicleWrecked && !IsEmpDisabled;
    public void SetPlayerDrivingEnabled(bool value)
    {
        playerDrivingEnabled = value && !vehicleWrecked;
    }
    /*
    The following variables are used to store information about sideways friction of the wheels (such as
    extremumSlip,extremumValue, asymptoteSlip, asymptoteValue and stiffness). We change this values to
    make the car to start drifting.
    */
    WheelFrictionCurve FLwheelFriction;
    float FLWextremumSlip;
    WheelFrictionCurve FRwheelFriction;
    float FRWextremumSlip;
    WheelFrictionCurve RLwheelFriction;
    float RLWextremumSlip;
    WheelFrictionCurve RRwheelFriction;
    float RRWextremumSlip;


    private void Awake()
    {
        if (carStats == null)
            carStats = GetComponent<CarStats>();

        if (carStats == null)
        {
            Debug.LogError(
                "CarController requires a CarStats component on the same GameObject.",
                this
            );

            enabled = false;
            return;
        }
        // Cache these before the player is parented to the vehicle.
        if (vehicleTurret == null)
            vehicleTurret = GetComponentInChildren<TurretGun>(true);

        if (vehicleXPCollector == null)
        {
            foreach (XPBlobCollector collector in GetComponentsInChildren<XPBlobCollector>(true))
            {
                if (collector.GetComponentInParent<PlayerVehicleInteractor>() != null)
                    continue;

                vehicleXPCollector = collector;
                break;
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        //In this part, we set the 'carRigidbody' value with the Rigidbody attached to this
        //gameObject. Also, we define the center of mass of the car with the Vector3 given
        //in the inspector.
        carRigidbody = gameObject.GetComponent<Rigidbody>();
        carRigidbody.centerOfMass = bodyMassCenter;

        currentVehicleHealth = maxVehicleHealth;
        vehicleWrecked = false;
        vehicleHealthInitialized = true;
        if (wreckFire != null)
            wreckFire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);

        //Initial setup to calculate the drift value of the car. This part could look a bit
        //complicated, but do not be afraid, the only thing we're doing here is to save the default
        //friction values of the car wheels so we can set an appropiate drifting value later.
        FLwheelFriction = new WheelFrictionCurve();
        FLwheelFriction.extremumSlip = frontLeftCollider.sidewaysFriction.extremumSlip;
        FLWextremumSlip = frontLeftCollider.sidewaysFriction.extremumSlip;
        FLwheelFriction.extremumValue = frontLeftCollider.sidewaysFriction.extremumValue;
        FLwheelFriction.asymptoteSlip = frontLeftCollider.sidewaysFriction.asymptoteSlip;
        FLwheelFriction.asymptoteValue = frontLeftCollider.sidewaysFriction.asymptoteValue;
        FLwheelFriction.stiffness = frontLeftCollider.sidewaysFriction.stiffness;
        FRwheelFriction = new WheelFrictionCurve();
        FRwheelFriction.extremumSlip = frontRightCollider.sidewaysFriction.extremumSlip;
        FRWextremumSlip = frontRightCollider.sidewaysFriction.extremumSlip;
        FRwheelFriction.extremumValue = frontRightCollider.sidewaysFriction.extremumValue;
        FRwheelFriction.asymptoteSlip = frontRightCollider.sidewaysFriction.asymptoteSlip;
        FRwheelFriction.asymptoteValue = frontRightCollider.sidewaysFriction.asymptoteValue;
        FRwheelFriction.stiffness = frontRightCollider.sidewaysFriction.stiffness;
        RLwheelFriction = new WheelFrictionCurve();
        RLwheelFriction.extremumSlip = rearLeftCollider.sidewaysFriction.extremumSlip;
        RLWextremumSlip = rearLeftCollider.sidewaysFriction.extremumSlip;
        RLwheelFriction.extremumValue = rearLeftCollider.sidewaysFriction.extremumValue;
        RLwheelFriction.asymptoteSlip = rearLeftCollider.sidewaysFriction.asymptoteSlip;
        RLwheelFriction.asymptoteValue = rearLeftCollider.sidewaysFriction.asymptoteValue;
        RLwheelFriction.stiffness = rearLeftCollider.sidewaysFriction.stiffness;
        RRwheelFriction = new WheelFrictionCurve();
        RRwheelFriction.extremumSlip = rearRightCollider.sidewaysFriction.extremumSlip;
        RRWextremumSlip = rearRightCollider.sidewaysFriction.extremumSlip;
        RRwheelFriction.extremumValue = rearRightCollider.sidewaysFriction.extremumValue;
        RRwheelFriction.asymptoteSlip = rearRightCollider.sidewaysFriction.asymptoteSlip;
        RRwheelFriction.asymptoteValue = rearRightCollider.sidewaysFriction.asymptoteValue;
        RRwheelFriction.stiffness = rearRightCollider.sidewaysFriction.stiffness;

        // We save the initial pitch of the car engine sound.
        if (carEngineSound != null)
        {
            initialCarEngineSoundPitch = carEngineSound.pitch;
        }

        // Keep exhaust off and brake lights off until their matching input is active.
        if (exhaustParticles != null)
        {
            exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        SetBrakeLights(false);

        // Optional standalone TMP display; the procedural HUD owns its own text.
        if (useUI && carSpeedText != null)
        {
            CarSpeedUI();
            InvokeRepeating(nameof(CarSpeedUI), 0.1f, 0.1f);
        }

        if (useSounds)
        {
            InvokeRepeating("CarSounds", 0f, 0.1f);
        }
        else if (!useSounds)
        {
            if (carEngineSound != null)
            {
                carEngineSound.Stop();
            }
            if (tireScreechSound != null)
            {
                tireScreechSound.Stop();
            }
        }

        if (!useEffects)
        {
            if (RLWParticleSystem != null)
            {
                RLWParticleSystem.Stop();
            }
            if (RRWParticleSystem != null)
            {
                RRWParticleSystem.Stop();
            }
            if (RLWTireSkid != null)
            {
                RLWTireSkid.emitting = false;
            }
            if (RRWTireSkid != null)
            {
                RRWTireSkid.emitting = false;
            }
        }

        if (useTouchControls)
        {
            if (throttleButton != null && reverseButton != null &&
            turnRightButton != null && turnLeftButton != null
            && handbrakeButton != null)
            {

                throttlePTI = throttleButton.GetComponent<PrometeoTouchInput>();
                reversePTI = reverseButton.GetComponent<PrometeoTouchInput>();
                turnLeftPTI = turnLeftButton.GetComponent<PrometeoTouchInput>();
                turnRightPTI = turnRightButton.GetComponent<PrometeoTouchInput>();
                handbrakePTI = handbrakeButton.GetComponent<PrometeoTouchInput>();
                touchControlsSetup = true;

            }
            else
            {
                String ex = "Touch controls are not completely set up. You must drag and drop your scene buttons in the" +
                " PrometeoCarController component.";
                Debug.LogWarning(ex);
            }
        }

        // Hide the old indicator once, if a legacy scene still assigns it.
        if (lastDamageTakenText != null)
        {
            lastDamageTakenText.text = string.Empty;
            lastDamageTakenText.enabled = false;
        }
        ResetBattery();
    }

    // Handle enemy gameplay impacts separately from optional crash VFX/audio.
    void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        HordeEnemy enemy = collision.collider != null
            ? collision.collider.GetComponentInParent<HordeEnemy>() : null;
        if (HandleEMPContact(collision, enemy)) return;
        // Reuses the existing car collision event; no extra overlap scan.
        // EMP detonation deliberately takes priority over Chunky launching.
        if (enemy != null && !enemy.IsDead && enemy.ChunkyImpact != null)
            enemy.ChunkyImpact.TryLaunchVehicle(this);
        HandleEnemyImpact(collision, enemy);
        HandleEnemyContactDamage(collision, enemy);

        // Crash effects are cosmetic and must not control whether gameplay damage runs.
        if (!useCrashEffects)
            return;

        // Ignore layers that are not included in the crash mask.
        if ((crashLayerMask.value & (1 << collision.gameObject.layer)) == 0)
        {
            return;
        }

        if (Time.time < nextCrashEffectTime)
        {
            return;
        }

        // Unity reports relative velocity in metres per second; inspector thresholds use km/h.
        float impactSpeedKmh = collision.relativeVelocity.magnitude * 3.6f;
        float minimumThreshold = Mathf.Max(0f, minimumCrashSpeedKmh);
        float mediumThreshold = Mathf.Max(minimumThreshold, mediumCrashSpeedKmh);
        float heavyThreshold = Mathf.Max(mediumThreshold, heavyCrashSpeedKmh);

        if (impactSpeedKmh < minimumThreshold)
        {
            return;
        }

        Vector3 impactPoint = transform.position;
        Vector3 impactNormal = -transform.forward;
        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            impactPoint = contact.point;
            impactNormal = contact.normal;
        }

        ParticleSystem particlesToPlay;
        AudioClip soundToPlay;

        if (impactSpeedKmh >= heavyThreshold)
        {
            particlesToPlay = heavyCrashParticlesPrefab;
            soundToPlay = heavyCrashSound;
        }
        else if (impactSpeedKmh >= mediumThreshold)
        {
            particlesToPlay = mediumCrashParticlesPrefab;
            soundToPlay = mediumCrashSound;
        }
        else
        {
            particlesToPlay = lightCrashParticlesPrefab;
            soundToPlay = lightCrashSound;
        }

        PlayCrashEffects(particlesToPlay, soundToPlay, impactPoint, impactNormal);
        nextCrashEffectTime = Time.time + Mathf.Max(0f, crashCooldown);
    }

    // Also evaluate ongoing contact. This catches the case where the car first
    // touches an enemy below the run-over threshold, then accelerates while still
    // in contact. The enemy's vehicleHitCooldown prevents per-frame damage.
    void OnCollisionStay(Collision collision)
    {
        if (collision == null)
            return;

        HordeEnemy enemy = collision.collider != null
            ? collision.collider.GetComponentInParent<HordeEnemy>() : null;
        if (HandleEMPContact(collision, enemy)) return;
        // Reuses the existing car collision event; no extra overlap scan.
        // EMP detonation deliberately takes priority over Chunky launching.
        if (enemy != null && !enemy.IsDead && enemy.ChunkyImpact != null)
            enemy.ChunkyImpact.TryLaunchVehicle(this);
        HandleEnemyImpact(collision, enemy);
        HandleEnemyContactDamage(collision, enemy);
    }


    // EMP contact has priority over run-over damage and thorns, regardless of speed.
    private bool HandleEMPContact(Collision collision, HordeEnemy enemy)
    {
        if (enemy == null || !enemy.IsEMP) return false;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : enemy.transform.position;
        enemy.DetonateEMP(this, point);
        // Also swallow stale callbacks from the same enemy after it has detonated.
        return true;
    }

    /// <summary>One battery point per EMP. No hull damage and no stacked shutdowns.</summary>
    public bool TakeEMPBatteryHit()
    {
        if (!vehicleHealthInitialized || vehicleWrecked || currentVehicleHealth <= 0f || IsEmpDisabled) return false;
        batteryHP = Mathf.Max(0, batteryHP - 1);
        if (batteryHP == 0)
        {
            empDisabledUntil = Time.time + Mathf.Max(0.1f, empDisableDuration);
            // Preserve seat occupancy and playerDrivingEnabled. Reuse the existing
            // wheel/audio shutdown without invoking wreck, explosion or ejection.
            StopWreckDriving();
            if (vehicleTurret != null) vehicleTurret.SetEmpDisabled(true);
            if (empDisabledParticles != null) empDisabledParticles.Play(true);
        }
        BatteryHealthChanged?.Invoke(batteryHP, MaxBatteryHP);
        RefreshBatteryHUD();
        return true;
    }

    private void ClearEMPShutdown()
    {
        bool wasDisabled = IsEmpDisabled;
        empDisabledUntil = 0f;
        if (vehicleTurret != null) vehicleTurret.SetEmpDisabled(false);
        if (empDisabledParticles != null)
            empDisabledParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (wasDisabled && !vehicleWrecked) ReleaseBrakes();
    }

    private void ResetBattery()
    {
        ClearEMPShutdown();
        batteryHP = MaxBatteryHP;
        BatteryHealthChanged?.Invoke(batteryHP, MaxBatteryHP);
        RefreshBatteryHUD();
    }

    private void RefreshBatteryHUD()
    {
        if (batteryStatusText == null) return;
        int seconds = Mathf.CeilToInt(EmpSecondsRemaining);
        if (lastBatteryDisplayHP == batteryHP && lastBatteryDisplaySeconds == seconds &&
            lastBatteryDisplayWrecked == vehicleWrecked) return;
        lastBatteryDisplayHP = batteryHP;
        lastBatteryDisplaySeconds = seconds;
        lastBatteryDisplayWrecked = vehicleWrecked;
        batteryStatusText.text = vehicleWrecked ? "BATTERY OFFLINE" : IsEmpDisabled ?
            $"EMP - {seconds}s" : $"{batteryHP}/{MaxBatteryHP}";
    }

    private void HandleEnemyImpact(Collision collision, HordeEnemy enemy)
    {
        if (!enableVehicleImpactSystem || vehicleWrecked || carRigidbody == null)
            return;

        if (enemy == null || enemy.IsDead)
            return;

        float minimumSpeed = Mathf.Max(0f, minimumRunOverSpeedKmh);
        float speedTowardEnemyKmh = GetSpeedTowardEnemyKmh(enemy);

        // Only process run-over damage when the car is moving
        // toward the enemy fast enough.
        if (speedTowardEnemyKmh < minimumSpeed)
            return;

        float maximumSpeed = Mathf.Max(minimumSpeed + 0.01f, maximumImpactSpeedKmh);
        float impact01 = Mathf.InverseLerp(minimumSpeed, maximumSpeed, speedTowardEnemyKmh);

        float ramDamage = Mathf.Lerp(
            Mathf.Max(0f, minimumEnemyDamage),
            Mathf.Max(0f, maximumEnemyDamage),
            impact01
        );

        // Add the car's thorns damage to every successful ram.
        float enemyDamage = ramDamage + Mathf.Max(0f, vehicleThornsDamage);

        float enemyKnockback = Mathf.Max(0f, carStats.turretKnockbackForce);

        Vector3 impactPoint = transform.position;

        if (collision.contactCount > 0)
            impactPoint = collision.GetContact(0).point;

        bool registeredHit = enemy.ReceiveVehicleImpact(
            enemyDamage,
            impactPoint,
            enemyKnockback
        );

        if (!registeredHit)
            return;

        float vehicleDamage = Mathf.Lerp(
            Mathf.Max(0f, maximumVehicleDamagePerHit - 10),
            Mathf.Max(0f, maximumVehicleDamagePerHit),
            impact01
        );

        vehicleDamage *= Mathf.Max(0f, enemy.VehicleImpactCarDamageMultiplier);

        // Run-over damage has its own per-enemy cooldown in HordeEnemy.
        // Do not modify contact-damage timers here.
        TakeVehicleDamage(vehicleDamage);
    }



    private void HandleEnemyContactDamage(Collision collision, HordeEnemy enemy)
    {
        if (!enableVehicleImpactSystem ||
            carRigidbody == null ||
            vehicleWrecked ||
            currentVehicleHealth <= 0f ||
            collision.collider == null)
        {
            return;
        }

        if (enemy == null || enemy.IsDead)
            return;

        // Run-over impacts are handled separately by HandleEnemyImpact().
        // Do not trigger contact damage or its feedback during a run-over.
        float speedTowardEnemyKmh = GetSpeedTowardEnemyKmh(enemy);

        if (speedTowardEnemyKmh >= Mathf.Max(0f, minimumRunOverSpeedKmh))
            return;

        float now = Time.time;

        // The first low-speed contact applies damage immediately. Each enemy then
        // receives its own repeating cooldown, independent of other enemies.
        if (!enemyContactStates.TryGetValue(enemy, out EnemyContactState state))
        {
            state = new EnemyContactState();
            enemyContactStates.Add(enemy, state);
        }

        state.lastContactTime = now;

        if (now < state.nextDamageTime)
            return;

        // The enemy is pressing against the vehicle at low speed.
        float damage = enemy.contactDamage *
                       enemy.vehicleContactDamageMultiplier;

        if (TakeVehicleDamage(damage, enemy))
        {
            state.nextDamageTime =
                now + Mathf.Max(0.05f, enemyContactDamageCooldown);
        }
    }

    private void CleanupEnemyContactStates()
    {
        if (enemyContactStates.Count == 0)
            return;

        float now = Time.time;
        staleEnemyContacts.Clear();

        foreach (KeyValuePair<HordeEnemy, EnemyContactState> pair in enemyContactStates)
        {
            HordeEnemy enemy = pair.Key;
            EnemyContactState state = pair.Value;

            if (enemy == null ||
                enemy.IsDead ||
                now - state.lastContactTime > EnemyContactForgetDelay)
            {
                staleEnemyContacts.Add(enemy);
            }
        }

        for (int i = 0; i < staleEnemyContacts.Count; i++)
            enemyContactStates.Remove(staleEnemyContacts[i]);
    }


    private void ApplyVehicleDamage(float damage)
    {
        if (!enableVehicleImpactSystem || vehicleWrecked || damage <= 0f)
            return;

        currentVehicleHealth = Mathf.Max(0f, currentVehicleHealth - damage);

        PlayDamageFeedback();

        if (currentVehicleHealth <= 0f)
            OnVehicleHealthDepleted();

        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);
    }

    /// <summary>Repairs an operational vehicle. Only RespawnVehicle revives a wreck.</summary>
    public void RepairVehicle(float amount)
    {
        if (!enableVehicleImpactSystem || vehicleWrecked || amount <= 0f)
            return;

        currentVehicleHealth = Mathf.Min(maxVehicleHealth, currentVehicleHealth + amount);
        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);
    }

    /// <summary>Fully heals the car, including a wreck, without instantiating a new one.</summary>
    public void RespawnVehicle()
    {
        bool wasWrecked = vehicleWrecked;
        UnlockParkingPosition();
        parkingBrakeEngaged = false;
        currentVehicleHealth = maxVehicleHealth;
        vehicleWrecked = false;
        ResetBattery();
        enemyContactStates.Clear();
        staleEnemyContacts.Clear();

        if (wreckFire != null)
            wreckFire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (wasWrecked)
        {
            // The seat will enable driving again when the player enters.
            SetPlayerDrivingEnabled(false);
            ReleaseBrakes();
            if (vehicleTurret != null)
                vehicleTurret.enabled = turretWasEnabled;
            if (vehicleXPCollector != null)
                vehicleXPCollector.enabled = collectorWasEnabled;
        }

        VehicleHealthReset?.Invoke();
        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);
    }

    /// <summary>Single, guarded transition from operational car to wreck.</summary>
    private void OnVehicleHealthDepleted()
    {
        if (vehicleWrecked)
            return;

        vehicleWrecked = true;
        currentVehicleHealth = 0f;
        ClearEMPShutdown();
        RefreshBatteryHUD();
        SetPlayerDrivingEnabled(false);
        enemyContactStates.Clear();
        staleEnemyContacts.Clear();

        if (vehicleTurret != null)
        {
            turretWasEnabled = vehicleTurret.enabled;
            vehicleTurret.enabled = false;
        }
        if (vehicleXPCollector != null)
        {
            collectorWasEnabled = vehicleXPCollector.enabled;
            vehicleXPCollector.enabled = false;
        }

        Vector3 blastCentre = GetExplosionCentre();

        // The occupant is a child of the seat in the existing interactor.
        // Eject before dealing explosion damage so all player state is restored.
        foreach (PlayerVehicleInteractor occupant in GetComponentsInChildren<PlayerVehicleInteractor>(true))
        {
            if (occupant.currentCarController == this)
                EjectOccupant(occupant);
        }
        StopWreckDriving();

        if (wreckFire != null)
            wreckFire.Play(true);

        // Stats are read at destruction time, so runtime upgrades apply immediately.
        float damage = carStats != null ? carStats.carExplosionDamage : 100f;
        float radius = carStats != null ? carStats.carExplosionDamageRange : 10f;
        GameObject prefab = carExplosionPrefab != null ? carExplosionPrefab :
            (playerRadio != null ? playerRadio.airstrikeExplosion : null);

        ExplosionUtility.Explode(prefab, blastCentre, blastCentre,
            radius, damage, carExplosionKnockback,
            Mathf.Max(0f, radius) / Mathf.Max(0.01f, carExplosionBaseVisualRadius),
            carExplosionLifetime);
    }

    private Vector3 GetExplosionCentre()
    {
        return carExplosionPoint != null ? carExplosionPoint.position :
            (carRigidbody != null ? carRigidbody.worldCenterOfMass : transform.position);
    }

    public void EjectOccupant(PlayerVehicleInteractor occupant)
    {
        if (vehicleWrecked && occupant != null)
            occupant.ForceEjectFromVehicle(this, GetExplosionCentre(), ejectionHorizontalSpeed, ejectionVerticalSpeed);
    }

    private void StopWreckDriving()
    {
        // A wreck or EMP can still apply its own braking without inheriting the parking lock.
        UnlockParkingPosition();
        parkingBrakeEngaged = false;
        CancelInvoke(nameof(DecelerateCar));
        CancelInvoke(nameof(RecoverTraction));
        deceleratingCar = false;
        throttleAxis = steeringAxis = driftingAxis = 0f;
        localVelocityX = localVelocityZ = 0f;
        isDrifting = isTractionLocked = false;

        // Restore original tyre grip immediately, even if the car died mid-drift.
        FLwheelFriction.extremumSlip = FLWextremumSlip;
        FRwheelFriction.extremumSlip = FRWextremumSlip;
        RLwheelFriction.extremumSlip = RLWextremumSlip;
        RRwheelFriction.extremumSlip = RRWextremumSlip;
        frontLeftCollider.sidewaysFriction = FLwheelFriction;
        frontRightCollider.sidewaysFriction = FRwheelFriction;
        rearLeftCollider.sidewaysFriction = RLwheelFriction;
        rearRightCollider.sidewaysFriction = RRwheelFriction;

        frontLeftCollider.steerAngle = frontRightCollider.steerAngle = 0f;
        ThrottleOff();

        if (carEngineSound != null) carEngineSound.Stop();
        if (tireScreechSound != null) tireScreechSound.Stop();
        if (exhaustParticles != null)
            exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (RLWParticleSystem != null) RLWParticleSystem.Stop();
        if (RRWParticleSystem != null) RRWParticleSystem.Stop();
        if (RLWTireSkid != null) RLWTireSkid.emitting = false;
        if (RRWTireSkid != null) RRWTireSkid.emitting = false;
        SetBrakeLights(false);
    }

    void PlayCrashEffects(ParticleSystem particlePrefab, AudioClip sound, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (particlePrefab != null)
        {
            Quaternion effectRotation = impactNormal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(impactNormal.normalized)
                : Quaternion.identity;

            ParticleSystem spawnedParticles = Instantiate(particlePrefab, impactPoint, effectRotation);
            spawnedParticles.Play();

            // Crash particle prefabs should be non-looping. Keep the instance alive until its particles finish.
            ParticleSystem.MainModule main = spawnedParticles.main;
            float particleLifetime = main.duration + main.startLifetime.constantMax;
            Destroy(spawnedParticles.gameObject, Mathf.Max(0.1f, particleLifetime));
        }

        if (sound != null)
        {
            AudioManager.Play3D(sound, impactPoint, crashSoundVolume);
            //AudioSource.PlayClipAtPoint(sound, impactPoint, crashSoundVolume);
        }
    }

    // Cache the pre-solver velocity so OnCollisionEnter can evaluate the actual approach speed.
    void FixedUpdate()
    {
        if (carRigidbody != null)
            velocityBeforePhysicsStep = carRigidbody.velocity;

        CleanupEnemyContactStates();
        FixedUpdateParkingBrake();
    }

    // Update is called once per frame
    void Update()
    {
        if (Time.timeScale <= 0f)
        {
            if (carEngineSound != null && carEngineSound.isPlaying)
            {
                carEngineSound.Stop();
            }

            if (severeDamageParticleEffect.isPlaying)
            {
                if (currentVehicleHealth / carStats.maxVehicleHealth > 0.3f)
                {
                    severeDamageActive = false;
                    severeDamageParticleEffect.Stop();
                }
            }

            if (tireScreechSound != null && tireScreechSound.isPlaying)
            {
                tireScreechSound.Stop();
            }

            return;
        }

        if (vehicleHealthInitialized &&
    currentVehicleHealth > maxVehicleHealth)
        {
            currentVehicleHealth = maxVehicleHealth;

            VehicleHealthChanged?.Invoke(
                currentVehicleHealth,
                maxVehicleHealth
            );
        }

        if (!vehicleWrecked && currentVehicleHealth <= 0f)
        {
            OnVehicleHealthDepleted();
            VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);
        }

        if (IsEmpDisabled && Time.time >= empDisabledUntil && !vehicleWrecked)
            ResetBattery();
        RefreshBatteryHUD();

        // Toggle once per press, only while the player is actually driving.
        if (playerDrivingEnabled && !vehicleWrecked && !IsEmpDisabled &&
            Input.GetKeyDown(parkingBrakeKey) && HasPlayerInDriverSeat())
            SetParkingBrake(!parkingBrakeEngaged);

        // Keep the optional legacy parking indicator synchronized even during EMP/wreck.
        if (parkBrakeIcon != null && parkBrakeIcon.activeSelf != parkingBrakeEngaged)
            parkBrakeIcon.SetActive(parkingBrakeEngaged);

        if (vehicleWrecked || IsEmpDisabled)
        {
            // Keep the wreck/EMP car physically present and its wheel meshes synchronized.
            carSpeed = carRigidbody.velocity.magnitude * 3.6f;
            AnimateWheelMeshes();
            return;
        }

        //CAR DATA

        // Legacy wheel-derived drivetrain value. HUD uses ActualSpeedKmh instead.
        carSpeed = (2 * Mathf.PI * frontLeftCollider.radius * frontLeftCollider.rpm * 60) / 1000;
        // Save the local velocity of the car in the x axis. Used to know if the car is drifting.
        localVelocityX = transform.InverseTransformDirection(carRigidbody.velocity).x;
        // Save the local velocity of the car in the z axis. Used to know if the car is going forward or backwards.
        localVelocityZ = transform.InverseTransformDirection(carRigidbody.velocity).z;

        // Resolve the driving inputs once so audio and visual effects work with both keyboard and touch controls.
        bool useTouchInput = useTouchControls && touchControlsSetup;
        bool forwardInput = playerDrivingEnabled &&
                            (useTouchInput ? throttlePTI.buttonPressed : Input.GetKey(KeyCode.W));
        bool reverseInput = playerDrivingEnabled &&
                            (useTouchInput ? reversePTI.buttonPressed : Input.GetKey(KeyCode.S));
        bool handbrakeInput = playerDrivingEnabled &&
                              (useTouchInput ? handbrakePTI.buttonPressed : Input.GetKey(KeyCode.Space));

        // Starting to drive automatically disengages the parking brake.
        // The seat check prevents input from controlling an unoccupied car.
        if (parkingBrakeEngaged && (forwardInput || reverseInput) &&
            HasPlayerInDriverSeat())
            SetParkingBrake(false);

        // Pressing the opposite direction while moving first applies the brakes in this controller.
        bool engineUnderThrottle = (forwardInput && localVelocityZ >= -1f) ||
                                   (reverseInput && localVelocityZ <= 1f);
        bool gasInput = forwardInput && localVelocityZ >= -1f;
        bool brakingInput = handbrakeInput ||
                            (forwardInput && localVelocityZ < -1f) ||
                            (reverseInput && localVelocityZ > 1f);

        UpdateEngineSoundPitch(engineUnderThrottle);
        UpdateExhaustParticles(gasInput);
        SetBrakeLights(brakingInput);

        // Parking overrides throttle, steering and deceleration while engaged.
        // Keep wheel meshes and speed readout updating as the car slows down.
        if (parkingBrakeEngaged)
        {
            CancelInvoke(nameof(DecelerateCar));
            deceleratingCar = false;
            ApplyParkingBrake();
            UpdateEngineSoundPitch(false);
            UpdateExhaustParticles(false);
            SetBrakeLights(true);
            if (isTractionLocked) RecoverTraction();
            AnimateWheelMeshes();
            return;
        }

        //CAR PHYSICS

        /*
        The next part is regarding to the car controller. First, it checks if the user wants to use touch controls (for
        mobile devices) or analog input controls (WASD + Space).

        The following methods are called whenever a certain key is pressed. For example, in the first 'if' we call the
        method GoForward() if the user has pressed W.

        In this part of the code we specify what the car needs to do if the user presses W (throttle), S (reverse),
        A (turn left), D (turn right) or Space bar (handbrake).
        */
        if (useTouchControls && touchControlsSetup)
        {

            if (forwardInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                GoForward();
            }
            if (reverseInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                GoReverse();
            }

            if (playerDrivingEnabled && turnLeftPTI.buttonPressed)
            {
                TurnLeft();
            }
            if (playerDrivingEnabled && turnRightPTI.buttonPressed)
            {
                TurnRight();
            }
            if (handbrakeInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                Handbrake();
            }
            // Start traction recovery once when the touch handbrake is released.
            // RecoverTraction schedules any remaining recovery steps itself.
            if (!handbrakeInput && isTractionLocked)
            {
                RecoverTraction();
            }
            if ((!forwardInput && !reverseInput))
            {
                ThrottleOff();
            }
            if ((!reverseInput && !forwardInput) && !handbrakeInput && !deceleratingCar)
            {
                InvokeRepeating("DecelerateCar", 0f, 0.1f);
                deceleratingCar = true;
            }
            if (!turnLeftPTI.buttonPressed && !turnRightPTI.buttonPressed && steeringAxis != 0f)
            {
                ResetSteeringAngle();
            }

        }
        else
        {

            if (forwardInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                GoForward();
            }
            if (reverseInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                GoReverse();
            }

            if (playerDrivingEnabled && Input.GetKey(KeyCode.A))
            {
                TurnLeft();
            }
            if (playerDrivingEnabled && Input.GetKey(KeyCode.D))
            {
                TurnRight();
            }
            if (handbrakeInput)
            {
                CancelInvoke("DecelerateCar");
                deceleratingCar = false;
                Handbrake();
            }
            if (playerDrivingEnabled && Input.GetKeyUp(KeyCode.Space))
            {
                RecoverTraction();
            }
            if ((!reverseInput && !forwardInput))
            {
                ThrottleOff();
            }
            if ((!reverseInput && !forwardInput) && !handbrakeInput && !deceleratingCar)
            {
                InvokeRepeating("DecelerateCar", 0f, 0.1f);
                deceleratingCar = true;
            }
            if (!Input.GetKey(KeyCode.A) && !Input.GetKey(KeyCode.D) && steeringAxis != 0f)
            {
                ResetSteeringAngle();
            }

        }


        // We call the method AnimateWheelMeshes() in order to match the wheel collider movements with the 3D meshes of the wheels.
        AnimateWheelMeshes();

    }

    // Engine pitch responds to throttle input, not speed alone. This prevents coasting downhill from revving the engine.
    void UpdateEngineSoundPitch(bool engineUnderThrottle)
    {
        if (!useSounds || carEngineSound == null)
        {
            return;
        }

        float targetPitch = initialCarEngineSoundPitch;
        if (engineUnderThrottle && carRigidbody != null)
        {
            // Keep the existing speed-based pitch character, but only while the engine is being driven.
            targetPitch += carRigidbody.velocity.magnitude / 25f;
        }

        float responseSpeed = engineUnderThrottle ? enginePitchRiseSpeed : enginePitchReturnSpeed;
        carEngineSound.pitch = Mathf.MoveTowards(
            carEngineSound.pitch,
            targetPitch,
            Mathf.Max(0.1f, responseSpeed) * Time.deltaTime);
    }

    // Start/stop exhaust emission based on the forward throttle input.
    void UpdateExhaustParticles(bool shouldEmit)
    {
        if (exhaustParticles == null)
        {
            return;
        }

        if (shouldEmit)
        {
            if (!exhaustParticles.isEmitting)
            {
                var emission = exhaustParticles.emission;

                if (carSpeed < 50)
                {
                    emission.rateOverTime = 25f;
                }
                else if (carSpeed <= 75)
                {
                    emission.rateOverTime = 75f;
                }
                else
                {
                    emission.rateOverTime = 175f;
                }

                exhaustParticles.Play(true);
            }
        }
        else if (exhaustParticles.isEmitting)
        {
            // Stop new emission, allowing already spawned particles to fade naturally.
            exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // The assigned objects should contain only brake-specific visuals/lights.
    void SetBrakeLights(bool shouldBeOn)
    {
        if (rearBrakeLights == null)
        {
            return;
        }

        for (int i = 0; i < rearBrakeLights.Length; i++)
        {
            GameObject brakeLight = rearBrakeLights[i];
            if (brakeLight != null && brakeLight.activeSelf != shouldBeOn)
            {
                brakeLight.SetActive(shouldBeOn);
            }
        }
    }

    // Optional standalone TMP speed output. Do not assign the meter's text here.
    public void CarSpeedUI()
    {
        if (useUI && carSpeedText != null)
        {
            int speed = Mathf.RoundToInt(ActualSpeedKmh);
            carSpeedText.SetText("{0:0}", speed);
        }
    }

    // This method controls the car sounds. For example, the car engine will sound slow when the car speed is low because the
    // pitch of the sound will be at its lowest point. On the other hand, it will sound fast when the car speed is high because
    // the pitch of the sound will be the sum of the initial pitch + the car speed divided by 100f.
    // Apart from that, the tireScreechSound will play whenever the car starts drifting or losing traction.
    public void CarSounds()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        if (useSounds)
        {
            if (carEngineSound != null && !carEngineSound.isPlaying)
            {
                carEngineSound.Play();
            }

            try
            {
                if ((isDrifting) || (isTractionLocked && Mathf.Abs(carSpeed) > 12f))
                {
                    if (tireScreechSound != null && !tireScreechSound.isPlaying)
                    {
                        tireScreechSound.Play();
                    }
                }
                else if ((!isDrifting) && (!isTractionLocked || Mathf.Abs(carSpeed) < 12f))
                {
                    if (tireScreechSound != null && tireScreechSound.isPlaying)
                    {
                        tireScreechSound.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
        }
        else if (!useSounds)
        {
            if (carEngineSound != null && carEngineSound.isPlaying)
            {
                carEngineSound.Stop();
            }
            if (tireScreechSound != null && tireScreechSound.isPlaying)
            {
                tireScreechSound.Stop();
            }
        }

    }

    //
    //STEERING METHODS
    //

    //The following method turns the front car wheels to the left. The speed of this movement will depend on the steeringSpeed variable.
    public void TurnLeft()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        steeringAxis = steeringAxis - (Time.deltaTime * 10f * steeringSpeed);
        if (steeringAxis < -1f)
        {
            steeringAxis = -1f;
        }
        var steeringAngle = steeringAxis * maxSteeringAngle;
        frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steeringAngle, steeringSpeed);
        frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steeringAngle, steeringSpeed);
    }

    //The following method turns the front car wheels to the right. The speed of this movement will depend on the steeringSpeed variable.
    public void TurnRight()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        steeringAxis = steeringAxis + (Time.deltaTime * 10f * steeringSpeed);
        if (steeringAxis > 1f)
        {
            steeringAxis = 1f;
        }
        var steeringAngle = steeringAxis * maxSteeringAngle;
        frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steeringAngle, steeringSpeed);
        frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steeringAngle, steeringSpeed);
    }

    //The following method takes the front car wheels to their default position (rotation = 0). The speed of this movement will depend
    // on the steeringSpeed variable.
    public void ResetSteeringAngle()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        if (steeringAxis < 0f)
        {
            steeringAxis = steeringAxis + (Time.deltaTime * 10f * steeringSpeed);
        }
        else if (steeringAxis > 0f)
        {
            steeringAxis = steeringAxis - (Time.deltaTime * 10f * steeringSpeed);
        }
        if (Mathf.Abs(frontLeftCollider.steerAngle) < 1f)
        {
            steeringAxis = 0f;
        }
        var steeringAngle = steeringAxis * maxSteeringAngle;
        frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steeringAngle, steeringSpeed);
        frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steeringAngle, steeringSpeed);
    }

    // This method matches both the position and rotation of the WheelColliders with the WheelMeshes.
    void AnimateWheelMeshes()
    {
        try
        {
            Quaternion FLWRotation;
            Vector3 FLWPosition;
            frontLeftCollider.GetWorldPose(out FLWPosition, out FLWRotation);
            frontLeftMesh.transform.position = FLWPosition;
            frontLeftMesh.transform.rotation = FLWRotation;

            Quaternion FRWRotation;
            Vector3 FRWPosition;
            frontRightCollider.GetWorldPose(out FRWPosition, out FRWRotation);
            frontRightMesh.transform.position = FRWPosition;
            frontRightMesh.transform.rotation = FRWRotation;

            Quaternion RLWRotation;
            Vector3 RLWPosition;
            rearLeftCollider.GetWorldPose(out RLWPosition, out RLWRotation);
            rearLeftMesh.transform.position = RLWPosition;
            rearLeftMesh.transform.rotation = RLWRotation;

            Quaternion RRWRotation;
            Vector3 RRWPosition;
            rearRightCollider.GetWorldPose(out RRWPosition, out RRWRotation);
            rearRightMesh.transform.position = RRWPosition;
            rearRightMesh.transform.rotation = RRWRotation;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(ex);
        }
    }

    //
    //ENGINE AND BRAKING METHODS
    //

    // This method apply positive torque to the wheels in order to go forward.
    public void GoForward()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        //If the forces aplied to the rigidbody in the 'x' asis are greater than
        //3f, it means that the car is losing traction, then the car will start emitting particle systems.
        if (Mathf.Abs(localVelocityX) > 2.5f)
        {
            isDrifting = true;
            DriftCarPS();
        }
        else
        {
            isDrifting = false;
            DriftCarPS();
        }
        // The following part sets the throttle power to 1 smoothly.
        throttleAxis = throttleAxis + (Time.deltaTime * 3f);
        if (throttleAxis > 1f)
        {
            throttleAxis = 1f;
        }
        //If the car is going backwards, then apply brakes in order to avoid strange
        //behaviours. If the local velocity in the 'z' axis is less than -1f, then it
        //is safe to apply positive torque to go forward.
        if (localVelocityZ < -1f)
        {
            Brakes();
        }
        else
        {
            // Clear any brake torque left over from a direction change before driving
            // or limiting the motor torque at maximum speed.
            ReleaseBrakes();
            if (Mathf.RoundToInt(carSpeed) < maxSpeed)
            {
                // Vehicle health never modifies the driving response.
                float motorTorque = (accelerationMultiplier * 85f) * throttleAxis;
                frontLeftCollider.motorTorque = motorTorque;
                frontRightCollider.motorTorque = motorTorque;
                rearLeftCollider.motorTorque = motorTorque;
                rearRightCollider.motorTorque = motorTorque;
            }
            else
            {
                // If the maxSpeed has been reached, then stop applying torque to the wheels.
                // IMPORTANT: The maxSpeed variable should be considered as an approximation; the speed of the car
                // could be a bit higher than expected.
                frontLeftCollider.motorTorque = 0;
                frontRightCollider.motorTorque = 0;
                rearLeftCollider.motorTorque = 0;
                rearRightCollider.motorTorque = 0;
            }
        }
    }

    // This method apply negative torque to the wheels in order to go backwards.
    public void GoReverse()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        //If the forces aplied to the rigidbody in the 'x' asis are greater than
        //3f, it means that the car is losing traction, then the car will start emitting particle systems.
        if (Mathf.Abs(localVelocityX) > 2.5f)
        {
            isDrifting = true;
            DriftCarPS();
        }
        else
        {
            isDrifting = false;
            DriftCarPS();
        }
        // The following part sets the throttle power to -1 smoothly.
        throttleAxis = throttleAxis - (Time.deltaTime * 6f);
        if (throttleAxis < -1f)
        {
            throttleAxis = -1f;
        }
        //If the car is still going forward, then apply brakes in order to avoid strange
        //behaviours. If the local velocity in the 'z' axis is greater than 1f, then it
        //is safe to apply negative torque to go reverse.
        if (localVelocityZ > 1f)
        {
            Brakes();
        }
        else
        {
            // Clear any brake torque left over from a direction change before driving
            // or limiting the motor torque at maximum reverse speed.
            ReleaseBrakes();
            if (Mathf.Abs(Mathf.RoundToInt(carSpeed)) < maxReverseSpeed)
            {
                // Vehicle health never modifies the driving response.
                float motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
                frontLeftCollider.motorTorque = motorTorque;
                frontRightCollider.motorTorque = motorTorque;
                rearLeftCollider.motorTorque = motorTorque;
                rearRightCollider.motorTorque = motorTorque;
            }
            else
            {
                //If the maxReverseSpeed has been reached, then stop applying torque to the wheels.
                // IMPORTANT: The maxReverseSpeed variable should be considered as an approximation; the speed of the car
                // could be a bit higher than expected.
                frontLeftCollider.motorTorque = 0;
                frontRightCollider.motorTorque = 0;
                rearLeftCollider.motorTorque = 0;
                rearRightCollider.motorTorque = 0;
            }
        }
    }

    //The following function set the motor torque to 0 (in case the user is not pressing either W or S).
    public void ThrottleOff()
    {
        frontLeftCollider.motorTorque = 0f;
        frontRightCollider.motorTorque = 0f;
        rearLeftCollider.motorTorque = 0f;
        rearRightCollider.motorTorque = 0f;
        if (vehicleWrecked || IsEmpDisabled)
            Brakes();
        else if (parkingBrakeEngaged)
            ApplyParkingBrake();
        else
            ReleaseBrakes();
    }

    // Clears service-brake torque when the controller is no longer braking.
    void ReleaseBrakes()
    {
        if (parkingBrakeEngaged) { ApplyParkingBrake(); return; }
        frontLeftCollider.brakeTorque = 0f;
        frontRightCollider.brakeTorque = 0f;
        rearLeftCollider.brakeTorque = 0f;
        rearRightCollider.brakeTorque = 0f;
    }

    // The following method decelerates the speed of the car according to the decelerationMultiplier variable, where
    // 1 is the slowest and 10 is the fastest deceleration. This method is called by the function InvokeRepeating,
    // usually every 0.1f when the user is not pressing W (throttle), S (reverse) or Space bar (handbrake).
    public void DecelerateCar()
    {
        if (vehicleWrecked || IsEmpDisabled || parkingBrakeEngaged) return;
        if (Mathf.Abs(localVelocityX) > 2.5f)
        {
            isDrifting = true;
            DriftCarPS();
        }
        else
        {
            isDrifting = false;
            DriftCarPS();
        }
        // The following part resets the throttle power to 0 smoothly.
        if (throttleAxis != 0f)
        {
            if (throttleAxis > 0f)
            {
                throttleAxis = throttleAxis - (Time.deltaTime * 10f);
            }
            else if (throttleAxis < 0f)
            {
                throttleAxis = throttleAxis + (Time.deltaTime * 10f);
            }
            if (Mathf.Abs(throttleAxis) < 0.15f)
            {
                throttleAxis = 0f;
            }
        }
        carRigidbody.velocity = carRigidbody.velocity * (1f / (1f + (0.025f * decelerationMultiplier)));
        // Since we want to decelerate the car, we are going to remove the torque from the wheels of the car.
        frontLeftCollider.motorTorque = 0f;
        frontRightCollider.motorTorque = 0f;
        rearLeftCollider.motorTorque = 0f;
        rearRightCollider.motorTorque = 0f;
        ReleaseBrakes();
        // If the magnitude of the car's velocity is less than 0.25f (very slow velocity), then stop the car completely and
        // also cancel the invoke of this method.
        if (carRigidbody.velocity.magnitude < 0.25f)
        {
            carRigidbody.velocity = Vector3.zero;
            CancelInvoke("DecelerateCar");
        }
    }

    // This function applies brake torque to the wheels according to the brake force given by the user.
    public void Brakes()
    {
        // Do not apply drive torque and braking torque at the same time when
        // changing between forward and reverse.
        frontLeftCollider.motorTorque = 0f;
        frontRightCollider.motorTorque = 0f;
        rearLeftCollider.motorTorque = 0f;
        rearRightCollider.motorTorque = 0f;

        frontLeftCollider.brakeTorque = brakeForce;
        frontRightCollider.brakeTorque = brakeForce;
        rearLeftCollider.brakeTorque = brakeForce;
        rearRightCollider.brakeTorque = brakeForce;
    }

    // This function is used to make the car lose traction. By using this, the car will start drifting. The amount of traction lost
    // will depend on the handbrakeDriftMultiplier variable. If this value is small, then the car will not drift too much, but if
    // it is high, then you could make the car to feel like going on ice.
    public void Handbrake()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        CancelInvoke("RecoverTraction");
        // We are going to start losing traction smoothly, there is were our 'driftingAxis' variable takes
        // place. This variable will start from 0 and will reach a top value of 1, which means that the maximum
        // drifting value has been reached. It will increase smoothly by using the variable Time.deltaTime.
        driftingAxis = driftingAxis + (Time.deltaTime);
        float secureStartingPoint = driftingAxis * FLWextremumSlip * handbrakeDriftMultiplier;

        if (secureStartingPoint < FLWextremumSlip)
        {
            driftingAxis = FLWextremumSlip / (FLWextremumSlip * handbrakeDriftMultiplier);
        }
        if (driftingAxis > 1f)
        {
            driftingAxis = 1f;
        }
        //If the forces aplied to the rigidbody in the 'x' asis are greater than
        //3f, it means that the car lost its traction, then the car will start emitting particle systems.
        if (Mathf.Abs(localVelocityX) > 2.5f)
        {
            isDrifting = true;
        }
        else
        {
            isDrifting = false;
        }
        //If the 'driftingAxis' value is not 1f, it means that the wheels have not reach their maximum drifting
        //value, so, we are going to continue increasing the sideways friction of the wheels until driftingAxis
        // = 1f.
        if (driftingAxis < 1f)
        {
            FLwheelFriction.extremumSlip = FLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            frontLeftCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction.extremumSlip = FRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            frontRightCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction.extremumSlip = RLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            rearLeftCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction.extremumSlip = RRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            rearRightCollider.sidewaysFriction = RRwheelFriction;
        }

        // Whenever the player uses the handbrake, it means that the wheels are locked, so we set 'isTractionLocked = true'
        // and, as a consequense, the car starts to emit trails to simulate the wheel skids.
        isTractionLocked = true;
        DriftCarPS();

    }

    // This function is used to emit both the particle systems of the tires' smoke and the trail renderers of the tire skids
    // depending on the value of the bool variables 'isDrifting' and 'isTractionLocked'.
    public void DriftCarPS()
    {
        if (vehicleWrecked || IsEmpDisabled) return;

        if (useEffects)
        {
            try
            {
                if (isDrifting)
                {
                    RLWParticleSystem.Play();
                    RRWParticleSystem.Play();
                }
                else if (!isDrifting)
                {
                    RLWParticleSystem.Stop();
                    RRWParticleSystem.Stop();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }

            try
            {
                if ((isTractionLocked || Mathf.Abs(localVelocityX) > 5f) && Mathf.Abs(carSpeed) > 12f)
                {
                    RLWTireSkid.emitting = true;
                    RRWTireSkid.emitting = true;
                }
                else
                {
                    RLWTireSkid.emitting = false;
                    RRWTireSkid.emitting = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
        }
        else if (!useEffects)
        {
            if (RLWParticleSystem != null)
            {
                RLWParticleSystem.Stop();
            }
            if (RRWParticleSystem != null)
            {
                RRWParticleSystem.Stop();
            }
            if (RLWTireSkid != null)
            {
                RLWTireSkid.emitting = false;
            }
            if (RRWTireSkid != null)
            {
                RRWTireSkid.emitting = false;
            }
        }

    }

    // This function is used to recover the traction of the car when the user has stopped using the car's handbrake.
    public void RecoverTraction()
    {
        if (vehicleWrecked || IsEmpDisabled) return;
        // Keep only one scheduled recovery call, even if this method is triggered
        // from more than one input path.
        CancelInvoke(nameof(RecoverTraction));
        isTractionLocked = false;

        driftingAxis = Mathf.Max(0f, driftingAxis - (Time.deltaTime / 1.5f));
        float driftMultiplier = Mathf.Max(1f, handbrakeDriftMultiplier);
        float minimumDriftingAxis = 1f / driftMultiplier;

        // Reduce the slip toward its original value without ever dropping below
        // the car's normal grip during the final recovery step.
        if (driftingAxis > minimumDriftingAxis)
        {
            FLwheelFriction.extremumSlip = FLWextremumSlip * driftMultiplier * driftingAxis;
            frontLeftCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction.extremumSlip = FRWextremumSlip * driftMultiplier * driftingAxis;
            frontRightCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction.extremumSlip = RLWextremumSlip * driftMultiplier * driftingAxis;
            rearLeftCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction.extremumSlip = RRWextremumSlip * driftMultiplier * driftingAxis;
            rearRightCollider.sidewaysFriction = RRwheelFriction;

            // Schedule exactly one next step; the touch input does not call this
            // continuously while the handbrake is released.
            Invoke(nameof(RecoverTraction), Mathf.Max(Time.deltaTime, 0.01f));
        }
        else
        {
            // Restore each wheel's original slip exactly once recovery is complete.
            FLwheelFriction.extremumSlip = FLWextremumSlip;
            frontLeftCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction.extremumSlip = FRWextremumSlip;
            frontRightCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction.extremumSlip = RLWextremumSlip;
            rearLeftCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction.extremumSlip = RRWextremumSlip;
            rearRightCollider.sidewaysFriction = RRwheelFriction;

            driftingAxis = 0f;
        }
    }

    // Short car-wide immunity prevents several Chunkies hitting on the same
    // physics step from adding unbounded impulses. Per-Chunky cooldown is separate.
    private float nextChunkyLaunchTime;
    private const float ChunkyLaunchGraceSeconds = 0.75f;

    /// <summary>
    /// External one-shot launch, in world-space velocity changes (m/s and rad/s).
    /// Not a damage path. Leaves contact damage, car thorns, and EMP intact.
    /// </summary>
    public bool TryApplyChunkyLaunch(Vector3 velocityChange, Vector3 angularVelocityChange)
    {
        if (!isActiveAndEnabled || !enableVehicleImpactSystem || vehicleWrecked ||
            carRigidbody == null || carRigidbody.isKinematic ||
            Time.time < nextChunkyLaunchTime)
            return false;

        // A parked vehicle has FreezeAll constraints; remove only the parking
        // lock before applying the impact. This also works when nobody is driving.
        if (parkingBrakeEngaged || parkingPositionLocked)
        {
            parkingBrakeEngaged = false;
            UnlockParkingPosition();
            if (!IsEmpDisabled) ReleaseBrakes();
        }

        nextChunkyLaunchTime = Time.time + ChunkyLaunchGraceSeconds;
        carRigidbody.WakeUp();
        carRigidbody.AddForce(velocityChange, ForceMode.VelocityChange);
        carRigidbody.AddTorque(angularVelocityChange, ForceMode.VelocityChange);
        return true;
    }

    /// <summary>
    /// Applies external damage to the vehicle.
    /// Returns false when the vehicle cannot accept any more damage.
    /// </summary>
    public bool TakeVehicleDamage(float damage, HordeEnemy attacker = null)
    {
        if (!enableVehicleImpactSystem ||
            vehicleWrecked ||
            currentVehicleHealth <= 0f ||
            (attacker != null && attacker.IsEMP) ||
            damage <= 0f)
        {
            return false;
        }

        ApplyVehicleDamage(damage);

        // Car thorns: retaliate against the enemy that damaged the car.
        if (attacker != null &&
            !attacker.IsDead &&
            vehicleThornsDamage > 0f)
        {
            attacker.ReceiveThornsDamage(vehicleThornsDamage);
        }

        return true;
    }

    private void PlayDamageFeedback()
    {
        // Play hit sound
        if (damageSound != null)
            AudioManager.Play3D(damageSound, transform.position, 1, 1, 1, 100);

        // Play damage particles
        if (damageParticles != null)
        {
            damageParticles.Stop();
            damageParticles.Play();
        }

        if (!severeDamageActive)
        {
            if (currentVehicleHealth / carStats.maxVehicleHealth <= 0.3f)
            {
                severeDamageActive = true;
                severeDamageParticleEffect.Play();
                carEngineSound.PlayOneShot(severeDamageSounds[UnityEngine.Random.Range(0, severeDamageSounds.Length)]);
            }
        }
    }

    private float GetSpeedTowardEnemyKmh(HordeEnemy enemy)
    {
        Vector3 toEnemy = enemy.transform.position -
                          carRigidbody.worldCenterOfMass;

        toEnemy = Vector3.ProjectOnPlane(toEnemy, Vector3.up);

        // Use the car's movement direction if the enemy is
        // almost directly at the car's centre.
        if (toEnemy.sqrMagnitude < 0.001f)
        {
            toEnemy = Vector3.ProjectOnPlane(
                velocityBeforePhysicsStep,
                Vector3.up
            );

            if (toEnemy.sqrMagnitude < 0.001f)
            {
                toEnemy = Vector3.ProjectOnPlane(
                    transform.forward,
                    Vector3.up
                );
            }
        }

        toEnemy.Normalize();

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(
            velocityBeforePhysicsStep,
            Vector3.up
        );

        return Vector3.Dot(horizontalVelocity, toEnemy) * 3.6f;
    }

    private void OnEnable()
    {
        if (carStats != null)
            carStats.MaxVehicleHealthChanged += OnMaxVehicleHealthChanged;
    }

    private void OnDisable()
    {
        if (carStats != null)
            carStats.MaxVehicleHealthChanged -= OnMaxVehicleHealthChanged;
    }

    private void OnMaxVehicleHealthChanged(float oldMax, float newMax)
    {
        // Ignore changes before Start initializes the vehicle's health.
        if (!vehicleHealthInitialized)
            return;

        // Increasing maximum health does not automatically heal the vehicle.
        // Decreasing maximum health clamps current health to the new maximum.
        currentVehicleHealth = Mathf.Min(currentVehicleHealth, newMax);

        if (currentVehicleHealth <= 0f && !vehicleWrecked)
            OnVehicleHealthDepleted();

        VehicleHealthChanged?.Invoke(currentVehicleHealth, newMax);
    }
}
