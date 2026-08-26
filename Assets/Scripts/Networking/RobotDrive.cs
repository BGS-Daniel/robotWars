using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace RobotWars.Networking
{
    public class RobotDrive : NetworkBehaviour
    {
        [Header("Drive")]
        [SerializeField] private float maxSpeed = 10f;
        [SerializeField] private float acceleration = 40f;
        [SerializeField] private float braking = 30f;
        [SerializeField] private float steerSpeed = 120f;
        [SerializeField] private float steerBoostAtSpeed = 1.5f;
        [SerializeField] private float driveSmoothing = 8f;

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

            float lerp = 1f - Mathf.Exp(-driveSmoothing * Time.fixedDeltaTime);
            _smoothedThrottle = Mathf.Lerp(_smoothedThrottle, _throttle, lerp);
            _smoothedSteer = Mathf.Lerp(_smoothedSteer, _steer, lerp);

            float currentSpeed = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;

            float targetSpeed = _smoothedThrottle * maxSpeed;

            float accel = _smoothedThrottle > 0f ? acceleration : braking;
            Vector3 desiredVelocity = transform.forward * targetSpeed;

            Vector3 vel = _rb.linearVelocity;
            Vector3 horizontalVel = Vector3.MoveTowards(
                new Vector3(vel.x, 0f, vel.z),
                new Vector3(desiredVelocity.x, 0f, desiredVelocity.z),
                accel * Time.fixedDeltaTime);
            vel.x = horizontalVel.x;
            vel.z = horizontalVel.z;
            _rb.linearVelocity = vel;

            float speedFactor = Mathf.InverseLerp(0f, maxSpeed * 0.5f, currentSpeed);
            float effectiveSteerSpeed = steerSpeed * Mathf.Lerp(0.3f, steerBoostAtSpeed, speedFactor);
            float yawDelta = _smoothedSteer * effectiveSteerSpeed * Time.fixedDeltaTime;
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
        }
    }
}
