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
using UnityEngine.UI;

public class CarController : MonoBehaviour
{

    //CAR SETUP

    [Space(20)]
    //[Header("CAR SETUP")]
    [Space(10)]
    [Range(20, 190)]
    public int maxSpeed = 90; //The maximum speed that the car can reach in km/h.
    [Range(10, 120)]
    public int maxReverseSpeed = 45; //The maximum speed that the car can reach while going on reverse in km/h.
    [Range(1, 10)]
    public int accelerationMultiplier = 2; // How fast the car can accelerate. 1 is a slow acceleration and 10 is the fastest.
    [Space(10)]
    [Range(10, 45)]
    public int maxSteeringAngle = 27; // The maximum angle that the tires can reach while rotating the steering wheel.
    [Range(0.1f, 1f)]
    public float steeringSpeed = 0.5f; // How fast the steering wheel turns.
    [Space(10)]
    [Range(100, 600)]
    public int brakeForce = 350; // The strength of the wheel brakes.
    [Range(1, 10)]
    public int decelerationMultiplier = 2; // How fast the car decelerates when the user is not using the throttle.
    [Range(1, 10)]
    public int handbrakeDriftMultiplier = 5; // How much grip the car loses when the user hit the handbrake.
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
    //The following variable lets you to set up a UI text to display the speed of your car.
    public bool useUI = false;
    public Text carSpeedText; // Used to store the UI object that is going to show the speed of the car.

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

    [SerializeField, Min(1f)]
    [Tooltip("The car is wrecked when this reaches zero.")]
    public float maxVehicleHealth = 100f;

    [SerializeField, Min(0f)]
    [Tooltip("The car must be travelling toward an enemy at least this fast to damage or push it. In km/h.")]
    private float minimumRunOverSpeedKmh = 18f;

    [SerializeField, Min(1f)]
    [Tooltip("Speed at which enemy damage, knockback and car damage reach their maximum values. In km/h.")]
    private float maximumImpactSpeedKmh = 70f;

    [SerializeField, Min(0f)]
    private float minimumEnemyDamage = 10f;

    [SerializeField, Min(0f)]
    private float maximumEnemyDamage = 36f;

    [SerializeField, Min(0f)]
    [Tooltip("Knockback is a Rigidbody velocity change in metres per second.")]
    private float minimumEnemyKnockback = 2f;

    [SerializeField, Min(0f)]
    private float maximumEnemyKnockback = 9f;

    [SerializeField, Min(0f)]
    private float minimumVehicleDamagePerHit = 1f;

    [SerializeField, Min(0f)]
    private float maximumVehicleDamagePerHit = 10f;

    public float currentVehicleHealth;
    public bool vehicleWrecked;

    public TextMeshProUGUI lastDamageTakenText;

    [Header("Enemy Contact Damage")]
    [SerializeField, Min(0.05f)]
    [Tooltip("Time between contact damage ticks for each individual enemy pressing against the car. The first hit is immediate.")]
    private float enemyContactDamageCooldown = 1f;

    [Header("Car Thorns")]
    [SerializeField, Min(0f)]
    private float vehicleThornsDamage = 5f;

    public AudioClip damageSound;
    public ParticleSystem damageParticles;

    // Contact damage is tracked independently for each enemy. This prevents one
    // enemy's cooldown from suppressing damage from the rest of the horde.
    private sealed class EnemyContactState
    {
        public float nextDamageTime;
        public float lastContactTime;
    }

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

    // Subscribe from a HUD or game manager if you want to display vehicle health.
    public event Action<float, float> VehicleHealthChanged;

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
    public float carSpeed; // Used to store the speed of the car.
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

    public bool PlayerDrivingEnabled => playerDrivingEnabled;
    public void SetPlayerDrivingEnabled(bool value)
    {
        playerDrivingEnabled = value;
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

    // Start is called before the first frame update
    void Start()
    {
        //In this part, we set the 'carRigidbody' value with the Rigidbody attached to this
        //gameObject. Also, we define the center of mass of the car with the Vector3 given
        //in the inspector.
        carRigidbody = gameObject.GetComponent<Rigidbody>();
        carRigidbody.centerOfMass = bodyMassCenter;

        maxVehicleHealth = Mathf.Max(1f, maxVehicleHealth);
        currentVehicleHealth = maxVehicleHealth;
        vehicleWrecked = false;
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

        // We invoke 2 methods inside this script. CarSpeedUI() changes the text of the UI object that stores
        // the speed of the car and CarSounds() controls the engine and drifting sounds. Both methods are invoked
        // in 0 seconds, and repeatedly called every 0.1 seconds.
        if (useUI)
        {
            InvokeRepeating("CarSpeedUI", 0f, 0.1f);
        }
        else if (!useUI)
        {
            if (carSpeedText != null)
            {
                carSpeedText.text = "0";
            }
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

        lastDamageTakenText.text = "";
    }

    // Handle enemy gameplay impacts separately from optional crash VFX/audio.
    void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        HandleEnemyImpact(collision);
        HandleEnemyContactDamage(collision);

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

        HandleEnemyImpact(collision);
        HandleEnemyContactDamage(collision);
    }


    private void HandleEnemyImpact(Collision collision)
    {
        if (!enableVehicleImpactSystem || carRigidbody == null)
            return;

        HordeEnemy enemy = collision.collider != null
            ? collision.collider.GetComponentInParent<HordeEnemy>()
            : null;

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

        float enemyDamage = Mathf.Lerp(
            Mathf.Max(0f, minimumEnemyDamage),
            Mathf.Max(0f, maximumEnemyDamage),
            impact01
        );

        float enemyKnockback = Mathf.Lerp(
            Mathf.Max(0f, minimumEnemyKnockback),
            Mathf.Max(0f, maximumEnemyKnockback),
            impact01
        );

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
            Mathf.Max(0f, minimumVehicleDamagePerHit),
            Mathf.Max(0f, maximumVehicleDamagePerHit),
            impact01
        );

        vehicleDamage *= Mathf.Max(0f, enemy.VehicleImpactCarDamageMultiplier);

        // Run-over damage has its own per-enemy cooldown in HordeEnemy.
        // Do not modify contact-damage timers here.
        TakeVehicleDamage(vehicleDamage);
    }



    private void HandleEnemyContactDamage(Collision collision)
    {
        if (!enableVehicleImpactSystem ||
            carRigidbody == null ||
            vehicleWrecked ||
            currentVehicleHealth <= 0f ||
            collision.collider == null)
        {
            return;
        }

        HordeEnemy enemy =
            collision.collider.GetComponentInParent<HordeEnemy>();

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
        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);

        PlayDamageFeedback();

        CancelInvoke(nameof(HideLastDamageText));
        int damageInt = (int)damage;
        lastDamageTakenText.text = "-" + damageInt.ToString();
        Invoke(nameof(HideLastDamageText), 5);

        if (currentVehicleHealth <= 0f && !vehicleWrecked)
        {
            vehicleWrecked = true;
            OnVehicleHealthDepleted();
        }
    }

    private void HideLastDamageText()
    {
        lastDamageTakenText.text = "";
    }

    /// <summary>Restores vehicle health. A positive repair can bring a wrecked car back into service.</summary>
    public void RepairVehicle(float amount)
    {
        if (!enableVehicleImpactSystem || amount <= 0f)
            return;

        currentVehicleHealth = Mathf.Min(maxVehicleHealth, currentVehicleHealth + amount);
        if (currentVehicleHealth > 0f)
            vehicleWrecked = false;

        VehicleHealthChanged?.Invoke(currentVehicleHealth, maxVehicleHealth);
    }

    /// <summary>
    /// Called once when vehicle health first reaches zero.
    /// Intentionally empty: reaching zero health does not currently change driving.
    /// </summary>
    private void OnVehicleHealthDepleted()
    {
        // TODO: Add vehicle destruction behaviour here if desired in the future.
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
    }

    // Update is called once per frame
    void Update()
    {

        //CAR DATA

        // We determine the speed of the car.
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

    // This method converts the car speed data from float to string, and then set the text of the UI carSpeedText with this value.
    public void CarSpeedUI()
    {

        if (useUI)
        {
            try
            {
                float absoluteCarSpeed = Mathf.Abs(carSpeed);
                carSpeedText.text = Mathf.RoundToInt(absoluteCarSpeed).ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
        }

    }

    // This method controls the car sounds. For example, the car engine will sound slow when the car speed is low because the
    // pitch of the sound will be at its lowest point. On the other hand, it will sound fast when the car speed is high because
    // the pitch of the sound will be the sum of the initial pitch + the car speed divided by 100f.
    // Apart from that, the tireScreechSound will play whenever the car starts drifting or losing traction.
    public void CarSounds()
    {

        if (useSounds)
        {
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
                float motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
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
        throttleAxis = throttleAxis - (Time.deltaTime * 3f);
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
        ReleaseBrakes();
    }

    // Clears service-brake torque when the controller is no longer braking.
    void ReleaseBrakes()
    {
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

    /// <summary>
    /// Applies external damage to the vehicle.
    /// Returns false when the vehicle cannot accept any more damage.
    /// </summary>
    public bool TakeVehicleDamage(float damage, HordeEnemy attacker = null)
    {
        if (!enableVehicleImpactSystem ||
            vehicleWrecked ||
            currentVehicleHealth <= 0f ||
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
        AudioManager.Play3D(damageSound, transform.position, 1, 1, 1, 100);

        // Play damage particles
        damageParticles.Stop();
        damageParticles.Play();
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
}
