using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using GameData.Player.Manager;

namespace GameData.Player.Script
{
    public class PlayerController1 : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, HideInInspector, Min(0f)] private float WalkSpeed = 2f;
        [SerializeField, HideInInspector, Min(0f)] private float RunSpeed = 6f;
        [SerializeField] private float AnimBlendSpeed = 8.9f;

        [SerializeField] private Transform CameraRoot;

        [SerializeField] private Transform Camera;

        [SerializeField] private float UpperLimit = -40f;

        [SerializeField] private float BottomLimit = 70f;

        [SerializeField] private float MouseSensitivity = 21.9f;
        [SerializeField, Range(.1f,4f)] private float mouseSensitivityMultiplier = .5f;
        public const string SensitivityPreferenceKey = "Player.Input.MouseSensitivityMultiplier.v1";
        public float MouseSensitivityMultiplier => mouseSensitivityMultiplier;
        public float EffectiveMouseSensitivity => MouseSensitivity * mouseSensitivityMultiplier;
        public void SetMouseSensitivityMultiplier(float value, bool persist = true)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))return;
            mouseSensitivityMultiplier=Mathf.Clamp(value,.1f,4f);
            if(persist)PlayerPrefs.SetFloat(SensitivityPreferenceKey,mouseSensitivityMultiplier);
        }
        private void Awake()
        {
            SetMouseSensitivityMultiplier(PlayerPrefs.GetFloat(SensitivityPreferenceKey,mouseSensitivityMultiplier),false);
        }
        private void OnApplicationQuit()=>PlayerPrefs.Save();

        [Header("Jump")]
        [SerializeField, HideInInspector, Min(0f)]
        private float JumpFactor = 260f;
        [SerializeField, Min(1f), Tooltip("Extra gravity only while falling. 1 = normal gravity.")]
        private float FallGravityMultiplier = 1.4f;

        [SerializeField] private float Dis2Ground = 0.8f;

        [SerializeField] private LayerMask GroundCheck;

        [SerializeField] private float AirResistance = 0.8f;

        [SerializeField, Min(0f), Tooltip("Horizontal acceleration while jumping or falling. AirResistance scales this control strength.")]
        private float AirControlAcceleration = 20f;
        [SerializeField, Min(0f), Tooltip("Horizontal braking in the air when movement is released. 0 preserves all momentum.")]
        private float AirBraking = 1.5f;

        private Rigidbody _playerRigidbody;
        private InputManager _inputManager;

        private Animator _animator;
        private PlayerRagdoll _ragdoll;
        private MiningSimulator.Ores.SlopeVisualLean _slopeLean;
        private MiningSimulator.Ores.PlayerStats _playerStats;
        private AnimationRunMana _runMana;
        private PistolShooting _shooting;
        private FullBodyFirstPerson _fullBodyLook;
        private TopDownSurvivor _topDown;
        private MegabonkPlayerMovement _slide;
        private MiningSimulator.Ores.AlunePlayerEffect _alune;
        private SurvivorCombatData CoreData => _playerStats != null ? _playerStats.CoreData : null;

        [SerializeField, Min(0)] private float environmentColliderRadius = .78f;
        [SerializeField, Min(0)] private float environmentColliderHeight = 2.55f;
        private CapsuleCollider _bodyCapsule;
        private int EnvironmentGroundMask => GroundCheck.value | 1;
        private PhysicsMaterial _bodyPhysicsMaterial;
        private readonly List<Vector3> _wallNormals = new(4);
        private readonly RaycastHit[] _groundHits = new RaycastHit[12];
        private const float MinimumGroundNormalY = .7071068f;
        private Vector3 _groundNormal = Vector3.up;
        private bool _grounded;
        public bool IsGrounded => _grounded;
        public event Action Jumped;
        public event Action<float> Landed;
        private bool groundSampleInitialized;
        private MiningSimulator.Ores.UiPanelCoordinator gameplayUi;
        private MiningSimulator.Ores.GameMainMenu mainMenu;

        private bool _jumpHeldLastStep;
        private bool _jumpPending;
        private bool _jumpRequested;
        private int _takeOffGroundChecks;

        private bool _hasAnimator;
        private static readonly int HiEmoteState = Animator.StringToHash("Base Layer.Emote Hi");
        private static readonly int LocomotionState = Animator.StringToHash("Base Layer.Locomotion");

        private int _xVelHash;

        private int _yVelHash;

        private int _jumpHash;

        private int _groundHash;

        private int _fallingHash;

        private int _zVelHash;

        private float _xRotation;

        // The existing blend tree was authored at these values, independently of world speed.
        private const float WalkAnimationSpeed = 2f;
        private const float RunAnimationSpeed = 6f;

        private Vector2 _currentVelocity;


        private void Start()
        {
            _hasAnimator = TryGetComponent<Animator>(out _animator);
            _ragdoll = GetComponent<PlayerRagdoll>();
            _playerRigidbody = GetComponent<Rigidbody>();
            _bodyCapsule = GetComponent<CapsuleCollider>();
            if (_bodyCapsule)
            {
                float foot = _bodyCapsule.center.y - _bodyCapsule.height * .5f;
                float radius = Mathf.Max(_bodyCapsule.radius, environmentColliderRadius);
                float height = Mathf.Max(_bodyCapsule.height, environmentColliderHeight, radius * 2f);
                _bodyCapsule.height = height;
                _bodyCapsule.radius = radius;
                Vector3 center = _bodyCapsule.center;
                center.y = foot + height * .5f;
                _bodyCapsule.center = center;
                _bodyPhysicsMaterial = new PhysicsMaterial("Player ZeroFriction")
                {
                    staticFriction = 0f,
                    dynamicFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
                _bodyCapsule.sharedMaterial = _bodyPhysicsMaterial;
            }
            _inputManager = GetComponent<InputManager>();
            _playerStats = GetComponent<MiningSimulator.Ores.PlayerStats>();
            _runMana = GetComponent<AnimationRunMana>();
            _shooting = GetComponent<PistolShooting>();
            _fullBodyLook = GetComponent<FullBodyFirstPerson>();
            _topDown=GetComponent<TopDownSurvivor>();
            _slide = GetComponent<MegabonkPlayerMovement>();
            if (!_slide && _topDown && _topDown.enabled && _bodyCapsule &&
                FindAnyObjectByType<MiningSimulator.Ores.TerrainGenerator>())
                _slide = gameObject.AddComponent<MegabonkPlayerMovement>();
            _alune=GetComponent<MiningSimulator.Ores.AlunePlayerEffect>();if(!_alune)_alune=gameObject.AddComponent<MiningSimulator.Ores.AlunePlayerEffect>();
            gameplayUi=FindAnyObjectByType<MiningSimulator.Ores.UiPanelCoordinator>();
            mainMenu=FindAnyObjectByType<MiningSimulator.Ores.GameMainMenu>();

            _xVelHash = Animator.StringToHash("X_velocity");
            _yVelHash = Animator.StringToHash("Y_velocity");
            _jumpHash = Animator.StringToHash("Jump");
            _groundHash = Animator.StringToHash("Grounded");
            _fallingHash = Animator.StringToHash("Falling");
            _zVelHash = Animator.StringToHash("Z_velocity");
            _inputManager.JumpPressed += OnJumpPressed;
            SampleGround();
        }

        private void OnJumpPressed()
        {
            if (isActiveAndEnabled && Time.timeScale > 0f) _jumpRequested = true;
        }

        private void Update()
        {
            HandleHiEmote();
            HandleJump();
        }

        private void HandleHiEmote()
        {
            if (!_hasAnimator || !_animator.isActiveAndEnabled || !_topDown || !_topDown.enabled ||
                Time.timeScale <= 0f || (gameplayUi && gameplayUi.BlocksGameplay) || (mainMenu && mainMenu.IsOpen))
                return;

            bool moving = _inputManager.Move.sqrMagnitude > .01f;
            bool transitioning = _animator.IsInTransition(0);
            bool emoting = transitioning
                ? _animator.GetNextAnimatorStateInfo(0).fullPathHash == HiEmoteState
                : _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == HiEmoteState;

            if (emoting && (moving || !_grounded || (_alune && _alune.IsStunned)))
                _animator.CrossFadeInFixedTime(LocomotionState, .08f, 0);
            else if (!emoting && !moving && _grounded && !_inputManager.Jump &&
                     (!_alune || !_alune.IsStunned) && _inputManager.HiEmotePressed &&
                     _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == LocomotionState &&
                     _animator.HasState(0, HiEmoteState))
                _animator.CrossFadeInFixedTime(HiEmoteState, .08f, 0);
        }

        private void OnDisable()
        {
            _slopeLean?.Reset();
            if (_slide) _slide.ResetSlide();
            _jumpRequested = false;
            _jumpHeldLastStep = false;
            _wallNormals.Clear();
        }
        private Vector3 knockbackVelocity;
        private float knockbackRemaining, knockbackDuration;
        public void ApplyKnockback(Vector3 velocity, float seconds)
        {
            var ragdoll=GetComponent<PlayerRagdoll>();
            if(ragdoll&&ragdoll.IsRagdoll){ragdoll.AddVelocity(velocity);return;}
            knockbackVelocity = new Vector3(velocity.x, 0, velocity.z);
            knockbackDuration = knockbackRemaining = Mathf.Max(.01f, seconds);
            if(_playerRigidbody&&!_playerRigidbody.isKinematic)_playerRigidbody.linearVelocity+=velocity;
        }
        public void ResetMotion()
        {
            knockbackVelocity = Vector3.zero; knockbackRemaining = 0;
            _currentVelocity = Vector2.zero;
            _jumpRequested = _jumpPending = _jumpHeldLastStep = false;
            _takeOffGroundChecks = 0;
            if (_slide) _slide.ResetSlide();
            if(_fullBodyLook)_fullBodyLook.ResetLook(transform.eulerAngles.y,0f);
            if (_animator) { _animator.ResetTrigger(_jumpHash); _animator.SetFloat(_xVelHash, 0f); _animator.SetFloat(_yVelHash, 0f); }
            SampleGround();
        }
        private void OnDestroy()
        {
            if (_inputManager != null) _inputManager.JumpPressed -= OnJumpPressed;
            if (_bodyPhysicsMaterial) Destroy(_bodyPhysicsMaterial);
        }

        private void OnCollisionEnter(Collision collision) => RecordWallContacts(collision);
        private void OnCollisionStay(Collision collision) => RecordWallContacts(collision);

        private void RecordWallContacts(Collision collision)
        {
            if (!isActiveAndEnabled || collision.rigidbody) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (contact.thisCollider != _bodyCapsule || Mathf.Abs(contact.normal.y) >= .5f) continue;
                Vector3 normal = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
                if (!_wallNormals.Contains(normal)) _wallNormals.Add(normal);
            }
        }

        private Vector3 SlideAlongWalls(Vector3 movement)
        {
            foreach (Vector3 normal in _wallNormals)
                if (Vector3.Dot(movement, normal) < 0f)
                    movement = Vector3.ProjectOnPlane(movement, normal);
            return movement;
        }

        private void FixedUpdate()
        {
            SampleGround();
            if (!_grounded && _slide) _slide.LeaveGround();
            Move();
            _wallNormals.Clear();
            if (!_grounded && _playerRigidbody.useGravity && _playerRigidbody.linearVelocity.y < 0f)
                _playerRigidbody.AddForce(Physics.gravity * (Mathf.Max(1f, FallGravityMultiplier) - 1f), ForceMode.Acceleration);
        }

        private void LateUpdate()
        {
            if (mainMenu && mainMenu.IsOpen) return;
            CamMovements();
            if (!_topDown || !_topDown.enabled || !_grounded || !_animator || !_animator.isActiveAndEnabled ||
                (_ragdoll && _ragdoll.IsRagdoll))
            {
                _slopeLean?.Reset();
                return;
            }

            _slopeLean ??= new MiningSimulator.Ores.SlopeVisualLean(transform);
            _slopeLean.Update(_groundNormal);
        }

        private void OnAnimatorMove()
        {
            if((_topDown&&_topDown.enabled)||(_fullBodyLook && _fullBodyLook.enabled))return; // Rigidbody Move() owns movement; the new look owns yaw.
            // In-place jump/fall root motion otherwise overwrites Rigidbody horizontal velocity.
            // Keep the existing grounded animation behaviour; physics owns the whole jump.
            if (_hasAnimator && _grounded && !_jumpPending && knockbackRemaining <= 0 && _playerRigidbody.linearVelocity.y <= 0.1f)
                _animator.ApplyBuiltinRootMotion();
        }

        private void Move()
        {
            if (!_hasAnimator) return;

            var coreData = CoreData;
            float walkSpeed = _playerStats != null ? _playerStats.WalkSpeed : coreData != null ? coreData.walkSpeed : WalkSpeed;
            float runSpeed = _playerStats != null ? _playerStats.RunSpeed : coreData != null ? coreData.runSpeed : RunSpeed;
            bool running = _inputManager.Run && (_runMana == null || _runMana.CanRun);
            float targetSpeed = Mathf.Max(0f, running ? runSpeed : walkSpeed)*(_alune?_alune.MovementMultiplier:1);
            float animationSpeed = running ? RunAnimationSpeed : WalkAnimationSpeed;
            Vector2 movementInput = _alune&&_alune.IsStunned?Vector2.zero:Vector2.ClampMagnitude(_inputManager.Move, 1f);

            // Camera-relative world input is independent of which enemy the actor faces.
            Vector3 worldMove=_topDown&&_topDown.enabled?_topDown.MovementDirection(movementInput):transform.TransformDirection(new Vector3(movementInput.x,0,movementInput.y));
            if (_slide)
                _slide.UpdatePosture(_inputManager.Slide && !JumpInputBlocked && !(_alune && _alune.IsStunned),
                    _grounded, worldMove, targetSpeed);
            Vector3 localMove=transform.InverseTransformDirection(worldMove);
            movementInput=new Vector2(localMove.x,localMove.z);
            _currentVelocity = Vector2.Lerp(_currentVelocity, movementInput * animationSpeed, AnimBlendSpeed * Time.fixedDeltaTime);
            // Lerp otherwise retains subnormal values such as -2.802597e-45 at rest.
            if (movementInput.x == 0f && Mathf.Abs(_currentVelocity.x) < .01f) _currentVelocity.x = 0f;
            if (movementInput.y == 0f && Mathf.Abs(_currentVelocity.y) < .01f) _currentVelocity.y = 0f;
            // Physics responds to input immediately; only the visual blend is smoothed.
            Vector2 desiredVelocity = movementInput * targetSpeed;
            if (knockbackRemaining > 0)
            {
                var impulse = transform.InverseTransformDirection(knockbackVelocity * (knockbackRemaining / knockbackDuration));
                desiredVelocity += new Vector2(impulse.x, impulse.z);
                knockbackRemaining = Mathf.Max(0, knockbackRemaining - Time.fixedDeltaTime);
            }

            if (_grounded && _slide && _slide.IsSliding && knockbackRemaining <= 0f)
            {
                Vector3 desired = SlideAlongWalls(_slide.CalculateSlideVelocity(worldMove, _groundNormal, Time.fixedDeltaTime));
                // Wall contacts change the horizontal direction. Keep the resulting slide tangent to the ground.
                desired.y = -(desired.x * _groundNormal.x + desired.z * _groundNormal.z) / _groundNormal.y;
                _playerRigidbody.AddForce(desired - _playerRigidbody.linearVelocity, ForceMode.VelocityChange);
            }
            else if (_grounded)
            {
                Vector3 velocity = _playerRigidbody.linearVelocity;
                Vector3 desired = SlideAlongWalls(transform.TransformDirection(new Vector3(desiredVelocity.x, 0f, desiredVelocity.y)));
                // Follow the slope at the same horizontal speed in either direction.
                desired.y = _groundNormal.y < .9999f
                    ? -(desired.x * _groundNormal.x + desired.z * _groundNormal.z) / _groundNormal.y
                    : velocity.y;
                _playerRigidbody.AddForce(desired - velocity, ForceMode.VelocityChange);

            }
            else if (!_slide || !_slide.KeepsAirMomentum || knockbackRemaining > 0f)
            {
                // Steer horizontal velocity without changing the vertical jump/fall trajectory.
                var localVelocity = transform.InverseTransformDirection(_playerRigidbody.linearVelocity);
                bool steering = movementInput.sqrMagnitude > 0.0001f;
                var airTarget = steering || knockbackRemaining > 0 ? desiredVelocity : Vector2.zero;
                var correction = new Vector3(airTarget.x - localVelocity.x, 0f, airTarget.y - localVelocity.z);
                float acceleration = steering ? Mathf.Max(0f, AirControlAcceleration) * Mathf.Max(0f, AirResistance) : Mathf.Max(0f, AirBraking);
                float maxCorrection = acceleration * Time.fixedDeltaTime;
                correction = Vector3.ClampMagnitude(correction, maxCorrection);
                _playerRigidbody.AddForce(SlideAlongWalls(transform.TransformDirection(correction)), ForceMode.VelocityChange);
            }

            _animator.SetFloat(_xVelHash, _currentVelocity.x);
            _animator.SetFloat(_yVelHash, _currentVelocity.y);


        }

        private void CamMovements()
        {
            if (_topDown && _topDown.enabled) return;
            if (!_hasAnimator) return;

            var Mouse_x = Cursor.lockState == CursorLockMode.Locked ? _inputManager.Look.x : 0f;
            var Mouse_y = Cursor.lockState == CursorLockMode.Locked ? _inputManager.Look.y : 0f;
            if(_fullBodyLook && _fullBodyLook.enabled){
                float scale=_inputManager.LookIsMouse?1f/60f:Time.deltaTime;
                _fullBodyLook.Look(new Vector2(Mouse_x,Mouse_y)*EffectiveMouseSensitivity*scale);
                return; // FullBodyFirstPerson alone owns camera and body yaw.
            }
            Camera.position = CameraRoot.position;

            // Preserve the old 60 Hz tuning, without amplifying mouse delta on slow frames.
            float lookScale = _inputManager.LookIsMouse ? 1f / 60f : Time.deltaTime;
            _xRotation -= Mouse_y * EffectiveMouseSensitivity * lookScale;
            _xRotation = Mathf.Clamp(_xRotation, UpperLimit, BottomLimit);

            // Visual shot kick returns to zero; it never changes the stored mouse aim.
            Vector3 shotKick = _shooting != null ? _shooting.CameraShotKick : Vector3.zero;
            Camera.localRotation = Quaternion.Euler(new Vector3(_xRotation, 0, 0) + shotKick);
            _playerRigidbody.MoveRotation(_playerRigidbody.rotation * Quaternion.Euler(0, Mouse_x * EffectiveMouseSensitivity * lookScale, 0));
        }

        private bool JumpInputBlocked => _topDown && _topDown.enabled
            ? gameplayUi && gameplayUi.BlocksGameplay || mainMenu && mainMenu.IsOpen
            : Cursor.lockState != CursorLockMode.Locked;

        private void HandleJump()
        {
            if (!_hasAnimator) return;
            bool jumpPressed = _jumpRequested || (_inputManager.Jump && !_jumpHeldLastStep);
            _jumpRequested = false;
            _jumpHeldLastStep = _inputManager.Jump;
            if (_alune&&_alune.IsStunned || !jumpPressed || !_grounded || _jumpPending || Time.timeScale <= 0f || JumpInputBlocked) return;

            _jumpPending = true;
            _animator.ResetTrigger(_jumpHash);
            _animator.SetTrigger(_jumpHash);
            // Take off now. The legacy clip event becomes an idempotent no-op.
            JumpAddForce();
        }
        public void JumpAddForce()
        {
            if (!_jumpPending || _playerRigidbody == null) return;
            _jumpPending = false;
            if (_slide) _slide.LeaveGround();
            float jumpStrength = CoreData != null ? CoreData.jumpStrength : JumpFactor;
            Vector3 velocity = _playerRigidbody.linearVelocity;
            velocity.y = Mathf.Max(0f, jumpStrength) / Mathf.Max(.001f, _playerRigidbody.mass);
            _playerRigidbody.linearVelocity = velocity;
            // Avoid re-grounding on the take-off frame.
            _takeOffGroundChecks = 2;
            _grounded = false;
            SetAnimationGrounding();
            Jumped?.Invoke();
        }

        private void SampleGround()
        {
            if(!_hasAnimator) return;


            bool wasGrounded=_grounded;
            bool canNotify=groundSampleInitialized;
            groundSampleInitialized=true;
            Vector3 velocity = _playerRigidbody.linearVelocity;
            _animator.SetFloat(_zVelHash, velocity.y);

            bool takingOff = _takeOffGroundChecks > 0;
            if (takingOff) _takeOffGroundChecks--;

            float size = _playerStats ? _playerStats.BodySizeMultiplier : 1f;
            Bounds capsuleBounds = _bodyCapsule ? _bodyCapsule.bounds : default;
            bool hasCapsule = capsuleBounds.extents.y > 0f;
            Vector3 origin = hasCapsule ? capsuleBounds.center : _playerRigidbody.worldCenterOfMass;
            float halfHeight = hasCapsule ? capsuleBounds.extents.y : Dis2Ground * size;
            float radius = hasCapsule ? Mathf.Min(capsuleBounds.extents.x, capsuleBounds.extents.z) : 0f;
            float tolerance = (wasGrounded ? .08f : .035f) * size;
            _grounded = false;
            _groundNormal = Vector3.up;
            if (!takingOff && TryFindGround(origin, halfHeight, radius, tolerance, velocity, out var normal))
            {
                _grounded = true;
                _groundNormal = normal;
                if (canNotify && !wasGrounded && velocity.y < -.6f)
                    Landed?.Invoke(-velocity.y);
            }
            SetAnimationGrounding();
        }

        private bool TryFindGround(Vector3 origin, float halfHeight, float radius, float tolerance,
            Vector3 velocity, out Vector3 normal)
        {
            normal = Vector3.up;
            float slopeAllowance = radius * (1f / MinimumGroundNormalY - 1f);
            if (Physics.Raycast(origin, Vector3.down, out var centerHit, halfHeight + slopeAllowance + tolerance,
                EnvironmentGroundMask, QueryTriggerInteraction.Ignore) &&
                (centerHit.collider.gameObject.layer != 0 || (GroundCheck.value & 1) != 0 ||
                    MiningSimulator.Ores.WorldNavigationGrid.IsLevelSupport(centerHit.collider)) &&
                centerHit.normal.y >= MinimumGroundNormalY &&
                centerHit.distance <= halfHeight + radius * (1f / centerHit.normal.y - 1f) + tolerance &&
                Vector3.Dot(velocity, centerHit.normal) <= .1f)
            {
                normal = centerHit.normal;
                return true;
            }

            if (radius <= 0f || velocity.y > .1f) return false;

            float probeRadius = radius * .9f;
            const float lift = .1f;
            var foot = origin + Vector3.up * (probeRadius - halfHeight + lift);
            int count = Physics.SphereCastNonAlloc(foot, probeRadius, Vector3.down, _groundHits,
                lift + slopeAllowance + tolerance, EnvironmentGroundMask, QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.rigidbody == _playerRigidbody || hit.distance >= bestDistance ||
                    (hit.collider.gameObject.layer == 0 && (GroundCheck.value & 1) == 0 &&
                        !MiningSimulator.Ores.WorldNavigationGrid.IsLevelSupport(hit.collider)) ||
                    hit.normal.y < MinimumGroundNormalY) continue;

                // A sphere touching a mesh edge can report an interpolated normal.
                Vector3 verifyOrigin = hit.point + Vector3.up * lift;
                if (!hit.collider.Raycast(new Ray(verifyOrigin, Vector3.down), out var surface, lift * 2f) ||
                    surface.normal.y < MinimumGroundNormalY || Vector3.Dot(velocity, surface.normal) > .1f ||
                    hit.distance > lift + (radius - probeRadius) * (1f / surface.normal.y - 1f) + tolerance)
                    continue;
                bestDistance = hit.distance;
                normal = surface.normal;
            }
            return bestDistance < float.PositiveInfinity;
        }

        private void SetAnimationGrounding()
        {
            _animator.SetBool(_groundHash, _grounded);
            _animator.SetBool(_fallingHash, !_grounded);
        }


    }
}
