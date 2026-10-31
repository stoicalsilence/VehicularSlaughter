using UnityEngine;

namespace Q3Movement
{
    /// <summary>
    /// Quake III CPM-style movement with terrain-aware ground movement, ground probing,
    /// ground snapping, jump buffering, coyote time, and controlled steep-slope sliding.
    /// Requires a CharacterController and the existing MouseLook class.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Q3PlayerController : MonoBehaviour
    {
        [System.Serializable]
        public class MovementSettings
        {
            [Min(0f)] public float MaxSpeed;
            [Min(0f)] public float Acceleration;
            [Min(0f)] public float Deceleration;

            public MovementSettings(float maxSpeed, float accel, float decel)
            {
                MaxSpeed = maxSpeed;
                Acceleration = accel;
                Deceleration = decel;
            }
        }

        [Header("Aiming")]
        [SerializeField] private Camera m_Camera;
        [SerializeField] private MouseLook m_MouseLook = new MouseLook();

        [Header("Movement")]
        [SerializeField, Min(0f)] private float m_Friction = 6f;
        [SerializeField, Min(0f)] private float m_Gravity = 20f;
        [SerializeField, Min(0f)] private float m_JumpForce = 8f;
        [Tooltip("Automatically jump again while the jump button remains held.")]
        [SerializeField] private bool m_AutoBunnyHop = false;
        [Tooltip("Strength of Quake-style directional air control.")]
        [SerializeField, Min(0f)] private float m_AirControl = 0.3f;
        [SerializeField] private MovementSettings m_GroundSettings = new MovementSettings(7f, 14f, 10f);
        [SerializeField] private MovementSettings m_AirSettings = new MovementSettings(7f, 2f, 2f);
        [SerializeField] private MovementSettings m_StrafeSettings = new MovementSettings(1f, 50f, 50f);

        [Header("Terrain Grounding")]
        [Tooltip("Include terrain and walkable ground layers. For best results, use only ground/terrain layers.")]
        [SerializeField] private LayerMask m_GroundMask = ~0;
        [Tooltip("How far below the feet the controller probes for terrain.")]
        [SerializeField, Min(0.05f)] private float m_GroundProbeDistance = 0.65f;
        [Tooltip("Maximum gap over which the player can snap down to walkable ground.")]
        [SerializeField, Min(0f)] private float m_GroundSnapDistance = 0.35f;
        [Tooltip("Sphere-cast radius as a fraction of the CharacterController radius.")]
        [SerializeField, Range(0.1f, 0.95f)] private float m_GroundProbeRadius = 0.8f;
        [Tooltip("Briefly prevents a ground probe from cancelling a jump immediately after take-off.")]
        [SerializeField, Min(0f)] private float m_JumpGroundLockTime = 0.12f;

        [Header("Jump Forgiveness")]
        [Tooltip("Time after leaving a walkable surface during which a jump can still be started.")]
        [SerializeField, Min(0f)] private float m_CoyoteTime = 0.08f;
        [Tooltip("A jump press is remembered this long while waiting to land.")]
        [SerializeField, Min(0f)] private float m_JumpBufferTime = 0.12f;

        [Header("Steep Slopes")]
        [Tooltip("Maximum speed while sliding on slopes steeper than CharacterController.slopeLimit.")]
        [SerializeField, Min(0f)] private float m_MaxSteepSlopeSpeed = 10f;
        [Tooltip("Fraction of normal ground speed available for steering on an unwalkable slope.")]
        [SerializeField, Range(0f, 1f)] private float m_SteepSlopeControl = 0.35f;

        [Header("Movement Audio")]
        [Tooltip("Optional dedicated AudioSource. If empty, one is created automatically.")]
        [SerializeField] private AudioSource m_MovementAudioSource;
        [Tooltip("Footstep clips selected at random. Fill this with one or more clips.")]
        [SerializeField] private AudioClip[] m_FootstepClips = new AudioClip[0];
        [Tooltip("Sound played whenever a jump is actually performed.")]
        [SerializeField] private AudioClip m_JumpClip;
        [Tooltip("Distance travelled along walkable ground between footstep sounds, in world units.")]
        [SerializeField, Min(0.1f)] private float m_FootstepDistance = 2.4f;
        [Tooltip("Minimum surface speed required to play footsteps.")]
        [SerializeField, Min(0f)] private float m_MinFootstepSpeed = 0.5f;
        [SerializeField, Range(0f, 1f)] private float m_FootstepVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float m_JumpVolume = 0.8f;

        /// <summary>Magnitude of the CharacterController's measured velocity.</summary>
        public float Speed { get { return m_Character != null ? m_Character.velocity.magnitude : 0f; } }

        /// <summary>True when standing on a slope within CharacterController.slopeLimit.</summary>
        public bool IsGrounded { get { return m_IsGrounded; } }

        private CharacterController m_Character;
        private readonly RaycastHit[] m_GroundHits = new RaycastHit[32];
        private Transform m_Tran;
        private Transform m_CamTran;

        // World-space velocity. On walkable ground it is kept tangent to the ground plane.
        private Vector3 m_PlayerVelocity = Vector3.zero;
        private Vector3 m_MoveDirectionNorm = Vector3.zero;
        private Vector3 m_MoveInput = Vector3.zero;
        private Vector3 m_GroundNormal = Vector3.up;

        private bool m_IsGrounded;
        private bool m_OnSteepSlope;
        private float m_GroundGap;
        private float m_JumpBufferTimer;
        private float m_TimeSinceGrounded = 999f;
        private float m_JumpGroundLockTimer;
        private float m_PlayerFriction;
        private float m_FootstepDistanceTravelled;
        private int m_LastFootstepClipIndex = -1;

        private void Start()
        {
            m_Tran = transform;
            m_Character = GetComponent<CharacterController>();
            InitializeMovementAudio();

            if (m_Camera == null)
                m_Camera = Camera.main;

            if (m_Camera != null)
            {
                m_CamTran = m_Camera.transform;
                m_MouseLook.Init(m_Tran, m_CamTran);
            }
        }

        private void Update()
        {
            if (m_Character == null)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            ReadMovementInput();

            m_MouseLook.UpdateCursorLock();
            if (m_CamTran != null)
                m_MouseLook.LookRotation(m_Tran, m_CamTran);

            UpdateJumpBuffer(dt);
            UpdateGrounding(dt);

            bool jumpRequested = m_AutoBunnyHop
                ? Input.GetButton("Jump")
                : m_JumpBufferTimer > 0f;

            bool canJump = m_IsGrounded || m_TimeSinceGrounded <= m_CoyoteTime;
            bool jumpThisFrame = jumpRequested && canJump;

            if (m_IsGrounded)
            {
                // Avoid losing momentum on a buffered jump, as in classic bunny hopping.
                GroundMove(jumpRequested, dt);
            }
            else if (m_OnSteepSlope)
            {
                SteepSlopeMove(dt);
            }
            else
            {
                AirMove(dt);
            }

            if (jumpThisFrame)
                PerformJump();

            Vector3 displacement = m_PlayerVelocity * dt;

            // If descending over a small terrain dip, add a bounded downward correction.
            // Do not apply it during take-off; the jump lock also protects the next few frames.
            if (!jumpThisFrame && m_IsGrounded && !m_Character.isGrounded &&
                m_JumpGroundLockTimer <= 0f && m_GroundGap > m_Character.skinWidth)
            {
                float snapAmount = Mathf.Min(
                    m_GroundGap - m_Character.skinWidth,
                    m_GroundSnapDistance);
                if (snapAmount > 0f)
                    displacement += Vector3.down * snapAmount;
            }

            bool wasAirborne = !m_IsGrounded && !m_OnSteepSlope;
            CollisionFlags flags = m_Character.Move(displacement);

            // Only cancel vertical velocity for airborne ceiling/floor impacts. Ground movement
            // can legitimately have a vertical component while following a slope.
            if (wasAirborne && (flags & CollisionFlags.Above) != 0 && m_PlayerVelocity.y > 0f)
                m_PlayerVelocity.y = 0f;

            if (wasAirborne && (flags & CollisionFlags.Below) != 0 && m_PlayerVelocity.y < 0f)
                m_PlayerVelocity.y = 0f;

            UpdateFootstepSounds(dt);
        }

        private void ReadMovementInput()
        {
            m_MoveInput = new Vector3(
                Input.GetAxisRaw("Horizontal"),
                0f,
                Input.GetAxisRaw("Vertical"));

            // Raw keyboard axes can have a magnitude of sqrt(2) diagonally. Clamp to prevent
            // diagonal movement from being faster than forward movement.
            if (m_MoveInput.sqrMagnitude > 1f)
                m_MoveInput.Normalize();
        }

        private void UpdateJumpBuffer(float dt)
        {
            if (Input.GetButtonDown("Jump"))
                m_JumpBufferTimer = m_JumpBufferTime;
            else
                m_JumpBufferTimer = Mathf.Max(0f, m_JumpBufferTimer - dt);

            if (m_AutoBunnyHop && Input.GetButton("Jump"))
                m_JumpBufferTimer = m_JumpBufferTime;

            m_JumpGroundLockTimer = Mathf.Max(0f, m_JumpGroundLockTimer - dt);
        }

        /// <summary>
        /// Uses both CharacterController contact information and a terrain probe. The probe
        /// supplies a stable surface normal, which CharacterController.isGrounded alone lacks.
        /// </summary>
        private void UpdateGrounding(float dt)
        {
            bool controllerGrounded = m_Character.isGrounded;
            RaycastHit hit;
            bool foundGround = ProbeGround(out hit);

            m_OnSteepSlope = false;
            m_GroundNormal = Vector3.up;
            m_GroundGap = float.PositiveInfinity;

            if (foundGround && hit.normal.y > 0.01f)
            {
                Bounds bounds = m_Character.bounds;
                Vector3 feetCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                float gap = Vector3.Dot(feetCenter - hit.point, hit.normal);

                // Ignore intersections that are too far away or indicate that the probe hit a
                // surface significantly above the controller's feet.
                float allowedPenetration = Mathf.Max(0.15f, m_Character.skinWidth * 2f);
                if (gap >= -allowedPenetration && gap <= m_GroundProbeDistance)
                {
                    m_GroundNormal = hit.normal.normalized;
                    m_GroundGap = Mathf.Max(0f, gap);

                    float slopeAngle = Vector3.Angle(m_GroundNormal, Vector3.up);
                    bool steepSurface = slopeAngle > m_Character.slopeLimit;
                    m_OnSteepSlope = steepSurface && m_JumpGroundLockTimer <= 0f;

                    // A steep face never becomes walkable merely because the take-off lock is
                    // active. The lock suppresses re-grounding, not the slope-limit rule itself.
                    if (steepSurface)
                    {
                        m_IsGrounded = false;
                    }
                    else
                    {
                        bool almostTouching = gap <= Mathf.Max(0.04f, m_Character.skinWidth * 1.5f);
                        bool descendingOrTouching = m_PlayerVelocity.y <= 0.05f || almostTouching;
                        bool probeCanGround = m_JumpGroundLockTimer <= 0f &&
                                              gap <= m_GroundSnapDistance &&
                                              descendingOrTouching;

                        m_IsGrounded = controllerGrounded || probeCanGround;
                    }
                }
                else
                {
                    m_IsGrounded = controllerGrounded;
                }
            }
            else
            {
                // The controller contact is a useful fallback on steps and tight geometry where
                // a downward probe may miss the exact contact point.
                m_IsGrounded = controllerGrounded;
            }

            if (m_IsGrounded)
                m_TimeSinceGrounded = 0f;
            else
                m_TimeSinceGrounded += dt;
        }

        private bool ProbeGround(out RaycastHit hit)
        {
            Bounds bounds = m_Character.bounds;
            float radius = Mathf.Max(0.02f, m_Character.radius * m_GroundProbeRadius);
            Vector3 origin = bounds.center + Vector3.up * 0.05f;
            float castDistance = bounds.extents.y + m_GroundProbeDistance + 0.1f;

            // NonAlloc avoids a per-frame garbage allocation. Filter the player's own colliders
            // explicitly instead of excluding its whole layer (ground may share that layer).
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                radius,
                Vector3.down,
                m_GroundHits,
                castDistance,
                m_GroundMask,
                QueryTriggerInteraction.Ignore);

            hit = new RaycastHit();
            bool found = false;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = m_GroundHits[i];
                if (candidate.collider == null ||
                    candidate.collider == m_Character ||
                    candidate.collider.transform == m_Tran ||
                    candidate.collider.transform.IsChildOf(m_Tran))
                {
                    continue;
                }

                if (candidate.distance < nearestDistance)
                {
                    nearestDistance = candidate.distance;
                    hit = candidate;
                    found = true;
                }
            }

            return found;
        }

        private void GroundMove(bool jumpQueued, float dt)
        {
            // Remove the component pointing into/out of the supporting surface. The remaining
            // velocity follows the terrain instead of fighting it with a horizontal-only move.
            m_PlayerVelocity = Vector3.ProjectOnPlane(m_PlayerVelocity, m_GroundNormal);

            if (!jumpQueued)
                ApplyGroundFriction(1f, dt);
            else
                ApplyGroundFriction(0f, dt);

            Vector3 wishDir = GetHorizontalWishDirection();
            wishDir = Vector3.ProjectOnPlane(wishDir, m_GroundNormal);

            if (wishDir.sqrMagnitude > 0.0001f)
                wishDir.Normalize();
            else
                wishDir = Vector3.zero;

            m_MoveDirectionNorm = wishDir;
            float wishSpeed = m_MoveInput.magnitude * m_GroundSettings.MaxSpeed;

            Accelerate(wishDir, wishSpeed, m_GroundSettings.Acceleration, dt);

            // Keep accumulated ground velocity tangent as terrain normals change frame-to-frame.
            m_PlayerVelocity = Vector3.ProjectOnPlane(m_PlayerVelocity, m_GroundNormal);
        }

        private void SteepSlopeMove(float dt)
        {
            // Slopes beyond CharacterController.slopeLimit are not treated as walkable. Apply
            // gravity along the slope plane, but cap the slide speed and retain limited steering.
            m_PlayerVelocity = Vector3.ProjectOnPlane(m_PlayerVelocity, m_GroundNormal);

            Vector3 gravityAlongSlope = Vector3.ProjectOnPlane(Vector3.down * m_Gravity, m_GroundNormal);
            m_PlayerVelocity += gravityAlongSlope * dt;

            Vector3 wishDir = Vector3.ProjectOnPlane(
                GetHorizontalWishDirection(),
                m_GroundNormal);

            if (wishDir.sqrMagnitude > 0.0001f && m_SteepSlopeControl > 0f)
            {
                wishDir.Normalize();
                float wishSpeed = m_MoveInput.magnitude * m_GroundSettings.MaxSpeed * m_SteepSlopeControl;
                Accelerate(wishDir, wishSpeed, m_GroundSettings.Acceleration * m_SteepSlopeControl, dt);
            }

            m_PlayerVelocity = Vector3.ProjectOnPlane(m_PlayerVelocity, m_GroundNormal);

            if (m_MaxSteepSlopeSpeed > 0f && m_PlayerVelocity.magnitude > m_MaxSteepSlopeSpeed)
                m_PlayerVelocity = m_PlayerVelocity.normalized * m_MaxSteepSlopeSpeed;

            m_MoveDirectionNorm = wishDir.sqrMagnitude > 0.0001f ? wishDir.normalized : Vector3.zero;
        }

        private void AirMove(float dt)
        {
            Vector3 wishDir = GetHorizontalWishDirection();
            float inputMagnitude = m_MoveInput.magnitude;
            float wishSpeed = inputMagnitude * m_AirSettings.MaxSpeed;
            float airControlTargetSpeed = wishSpeed;
            float accel;

            if (wishDir.sqrMagnitude > 0.0001f)
                wishDir.Normalize();

            m_MoveDirectionNorm = wishDir;

            if (Vector3.Dot(m_PlayerVelocity, wishDir) < 0f)
                accel = m_AirSettings.Deceleration;
            else
                accel = m_AirSettings.Acceleration;

            // Retain the original CPM-style strafe speed/acceleration settings.
            if (Mathf.Abs(m_MoveInput.z) < 0.001f && Mathf.Abs(m_MoveInput.x) > 0.001f)
            {
                wishSpeed = Mathf.Min(wishSpeed, m_StrafeSettings.MaxSpeed);
                accel = m_StrafeSettings.Acceleration;
            }

            Accelerate(wishDir, wishSpeed, accel, dt);

            if (m_AirControl > 0f)
                AirControl(wishDir, airControlTargetSpeed, dt);

            m_PlayerVelocity.y -= m_Gravity * dt;
        }

        /// <summary>Quake-style air steering that changes horizontal direction without altering vertical velocity.</summary>
        private void AirControl(Vector3 targetDir, float targetSpeed, float dt)
        {
            if (Mathf.Abs(m_MoveInput.z) < 0.001f || targetSpeed < 0.001f || targetDir.sqrMagnitude < 0.0001f)
                return;

            Vector3 horizontalVelocity = new Vector3(m_PlayerVelocity.x, 0f, m_PlayerVelocity.z);
            float speed = horizontalVelocity.magnitude;
            if (speed < 0.001f)
                return;

            Vector3 velocityDir = horizontalVelocity / speed;
            float dot = Vector3.Dot(velocityDir, targetDir);
            if (dot <= 0f)
                return;

            float steer = 32f * m_AirControl * dot * dot * dt;
            Vector3 steeredVelocity = velocityDir * speed + targetDir * steer;
            if (steeredVelocity.sqrMagnitude > 0.0001f)
                steeredVelocity = steeredVelocity.normalized * speed;

            m_PlayerVelocity.x = steeredVelocity.x;
            m_PlayerVelocity.z = steeredVelocity.z;
        }

        private void PerformJump()
        {
            PlayJumpSound();

            // Preserve horizontal momentum but replace the slope-following vertical component.
            m_PlayerVelocity.y = m_JumpForce;
            m_IsGrounded = false;
            m_OnSteepSlope = false;
            m_TimeSinceGrounded = m_CoyoteTime + 1f;
            m_JumpBufferTimer = 0f;
            m_JumpGroundLockTimer = m_JumpGroundLockTime;
        }

        private void InitializeMovementAudio()
        {
            // Keep movement audio separate from any other AudioSource already on the player.
            if (m_MovementAudioSource == null)
            {
                m_MovementAudioSource = gameObject.AddComponent<AudioSource>();
                m_MovementAudioSource.playOnAwake = false;
                m_MovementAudioSource.spatialBlend = 1f;
            }
        }

        private void UpdateFootstepSounds(float dt)
        {
            if (m_MovementAudioSource == null ||
                m_FootstepClips == null ||
                m_FootstepClips.Length == 0 ||
                !m_IsGrounded ||
                m_OnSteepSlope)
            {
                return;
            }

            // Use actual CharacterController movement, projected onto the supporting surface.
            // This stays stable on slopes and avoids counting vertical snap/gravity as footsteps.
            Vector3 surfaceVelocity = Vector3.ProjectOnPlane(m_Character.velocity, m_GroundNormal);
            float surfaceSpeed = surfaceVelocity.magnitude;
            if (surfaceSpeed < m_MinFootstepSpeed)
                return;

            float stepDistance = Mathf.Max(0.1f, m_FootstepDistance);
            m_FootstepDistanceTravelled += surfaceSpeed * dt;

            if (m_FootstepDistanceTravelled >= stepDistance)
            {
                // Do not play a burst of sounds if the frame rate stutters and several steps elapse.
                m_FootstepDistanceTravelled %= stepDistance;
                PlayFootstepSound();
            }
        }

        private void PlayFootstepSound()
        {
            int validClipCount = 0;
            for (int i = 0; i < m_FootstepClips.Length; i++)
            {
                if (m_FootstepClips[i] != null)
                    validClipCount++;
            }

            if (validClipCount == 0)
                return;

            // Avoid repeating the previous clip when there is more than one valid clip.
            bool avoidRepeat = validClipCount > 1;
            int candidateCount = 0;
            for (int i = 0; i < m_FootstepClips.Length; i++)
            {
                if (m_FootstepClips[i] != null && (!avoidRepeat || i != m_LastFootstepClipIndex))
                    candidateCount++;
            }

            int selectedCandidate = Random.Range(0, candidateCount);
            for (int i = 0; i < m_FootstepClips.Length; i++)
            {
                if (m_FootstepClips[i] == null || (avoidRepeat && i == m_LastFootstepClipIndex))
                    continue;

                if (selectedCandidate == 0)
                {
                    m_LastFootstepClipIndex = i;
                    m_MovementAudioSource.PlayOneShot(m_FootstepClips[i], m_FootstepVolume);
                    return;
                }

                selectedCandidate--;
            }
        }

        private void PlayJumpSound()
        {
            if (m_MovementAudioSource != null && m_JumpClip != null)
                m_MovementAudioSource.PlayOneShot(m_JumpClip, m_JumpVolume);
        }

        public void ResetMovementState()
        {
            m_PlayerVelocity = Vector3.zero;
            m_MoveDirectionNorm = Vector3.zero;
            m_MoveInput = Vector3.zero;
            m_GroundNormal = Vector3.up;
            m_IsGrounded = false;
            m_OnSteepSlope = false;
            m_GroundGap = float.PositiveInfinity;
            m_JumpBufferTimer = 0f;
            m_TimeSinceGrounded = 999f;
            m_JumpGroundLockTimer = 0f;
            m_FootstepDistanceTravelled = 0f;
        }


        private void ApplyGroundFriction(float frictionScale, float dt)
        {
            Vector3 surfaceVelocity = Vector3.ProjectOnPlane(m_PlayerVelocity, m_GroundNormal);
            float speed = surfaceVelocity.magnitude;

            if (speed < 0.0001f)
            {
                m_PlayerVelocity = Vector3.zero;
                m_PlayerFriction = 0f;
                return;
            }

            float control = speed < m_GroundSettings.Deceleration
                ? m_GroundSettings.Deceleration
                : speed;
            float drop = control * m_Friction * dt * frictionScale;
            float newSpeed = Mathf.Max(0f, speed - drop);

            m_PlayerFriction = newSpeed;
            m_PlayerVelocity = surfaceVelocity * (newSpeed / speed);
        }

        /// <summary>Accelerates along a normalized direction, preserving Quake-style speed gain.</summary>
        private void Accelerate(Vector3 targetDir, float targetSpeed, float accel, float dt)
        {
            if (targetSpeed <= 0f || accel <= 0f || targetDir.sqrMagnitude < 0.0001f)
                return;

            float currentSpeed = Vector3.Dot(m_PlayerVelocity, targetDir);
            float addSpeed = targetSpeed - currentSpeed;
            if (addSpeed <= 0f)
                return;

            float accelSpeed = accel * dt * targetSpeed;
            if (accelSpeed > addSpeed)
                accelSpeed = addSpeed;

            m_PlayerVelocity += targetDir * accelSpeed;
        }

        private Vector3 GetHorizontalWishDirection()
        {
            // Use yaw-only forward/right vectors, so camera pitch never tilts the movement input.
            Vector3 forward = Vector3.ProjectOnPlane(m_Tran.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 wishDir = right * m_MoveInput.x + forward * m_MoveInput.z;

            if (wishDir.sqrMagnitude > 1f)
                wishDir.Normalize();

            return wishDir;
        }
    }
}
