using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace RobotWars.Networking
{
    public class RobotDrive : NetworkBehaviour
    {
        [Header("Speed")]
        [SerializeField] private float maxSpeed = 12f;
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float braking = 22f;
        [SerializeField] private float coastDrag = 4f;

        [Header("Steering")]
        [SerializeField] private float turnSpeed = 90f;
        [SerializeField] private float turnSpeedAtMaxSpeed = 55f;
        [SerializeField] private float steerSmoothing = 6f;

        [Header("Drift")]
        [SerializeField] private float lateralFriction = 8f;
        [SerializeField] private float driftLateralFriction = 3f;

        [Header("Ground Check")]
        [SerializeField] private float groundCheckRadius = 0.4f;
        [SerializeField] private Vector3 groundCheckOffset = new Vector3(0f, -0.5f, 0f);

        private Rigidbody _rb;
        private float _throttle;
        private float _steer;
        private float _smoothedThrottle;
        private float _smoothedSteer;
        private float _knockbackTimer;
        private readonly Collider[] _overlapBuffer = new Collider[16];

        public bool IsInKnockback => _knockbackTimer > 0f;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
        }

        private void FixedUpdate()
        {
            if (IsOwner)
                SendInputServerRpc(_throttle, _steer);
            if (IsServer)
                ApplyDrive();
        }

        public void ApplyKnockback(Vector3 impulse, Vector3 contactPoint)
        {
            _knockbackTimer = 0.35f;
            if (_rb != null)
                _rb.AddForceAtPosition(impulse, contactPoint, ForceMode.Impulse);
        }

        private void Update()
        {
            if (!IsOwner) return;
            ReadInput();
        }

        [ServerRpc]
        private void SendInputServerRpc(float throttle, float steer)
        {
            _throttle = throttle;
            _steer = steer;
        }

        private void ReadInput()
        {
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                _throttle = gamepad.leftStick.ReadValue().y;
                _steer = gamepad.leftStick.ReadValue().x;
            }
            else
            {
                var kb = Keyboard.current;
                _throttle = 0f;
                _steer = 0f;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) _throttle += 1f;
                    if (kb.sKey.isPressed) _throttle -= 1f;
                    if (kb.aKey.isPressed) _steer -= 1f;
                    if (kb.dKey.isPressed) _steer += 1f;
                }
            }
        }

        private bool IsGrounded()
        {
            Vector3 checkPos = transform.position + groundCheckOffset;
            int count = Physics.OverlapSphereNonAlloc(checkPos, groundCheckRadius,
                _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_overlapBuffer[i].transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        private void ApplyDrive()
        {
            if (_rb != null && _rb.isKinematic) return;

            if (MatchManager.Instance != null && !MatchManager.Instance.IsRoundActive) return;

            if (_knockbackTimer > 0f)
            {
                _knockbackTimer -= Time.fixedDeltaTime;
                return;
            }

            if (!IsGrounded()) return;

            float dt = Time.fixedDeltaTime;

            float lerpFactor = 1f - Mathf.Exp(-steerSmoothing * dt);
            _smoothedThrottle = Mathf.Lerp(_smoothedThrottle, _throttle, lerpFactor);
            _smoothedSteer = Mathf.Lerp(_smoothedSteer, _steer, lerpFactor);

            Vector3 vel = _rb.linearVelocity;
            Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);
            float currentSpeed = flatVel.magnitude;

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            float forwardSpeed = Vector3.Dot(flatVel, forward);
            float lateralSpeed = Vector3.Dot(flatVel, right);

            bool isDrifting = Mathf.Abs(_smoothedSteer) > 0.7f && currentSpeed > maxSpeed * 0.4f;
            float latFriction = isDrifting ? driftLateralFriction : lateralFriction;
            float newLateral = Mathf.MoveTowards(lateralSpeed, 0f, latFriction * dt);

            float targetSpeed = _smoothedThrottle * maxSpeed;
            float accel;
            if (_smoothedThrottle > 0.01f)
                accel = forwardSpeed < targetSpeed ? acceleration : braking;
            else if (_smoothedThrottle < -0.01f)
                accel = forwardSpeed > targetSpeed ? acceleration : braking;
            else
                accel = coastDrag;

            float newForward = Mathf.MoveTowards(forwardSpeed, targetSpeed, accel * dt);

            Vector3 newFlatVel = forward * newForward + right * newLateral;
            vel.x = newFlatVel.x;
            vel.z = newFlatVel.z;
            _rb.linearVelocity = vel;

            float speedFactor = Mathf.InverseLerp(0f, maxSpeed, currentSpeed);
            float effectiveTurnSpeed = Mathf.Lerp(turnSpeed, turnSpeedAtMaxSpeed, speedFactor);
            float turnAmount = _smoothedSteer * effectiveTurnSpeed * dt;
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, turnAmount, 0f));
        }
    }
}
