using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace RobotWars.Networking
{
    public class RobotDrive : NetworkBehaviour
    {
        [Header("Wheels (visual only)")]
        [SerializeField] private Transform[] leftWheelVisuals = new Transform[0];
        [SerializeField] private Transform[] rightWheelVisuals = new Transform[0];
        [SerializeField] private float wheelRadius = 0.35f;
        [SerializeField] private float wheelBase = 1.8f;

        [Header("Wheels (physics)")]
        [SerializeField] private Transform[] leftWheels = new Transform[0];
        [SerializeField] private Transform[] rightWheels = new Transform[0];
        [SerializeField] private float groundClearance = 0.06f;

        [Header("Drive")]
        [SerializeField] private float maxSpeed = 6f;
        [SerializeField] private float maxAngularSpeed = 120f;
        [SerializeField] private float driveSmoothing = 8f;
        [SerializeField] private float driveAcceleration = 60f;

        private Rigidbody _rb;
        private WheelHealth _wheelHealth;
        private float _leftInput;
        private float _rightInput;
        private float _smoothedLeft;
        private float _smoothedRight;
        private float _knockbackTimer;
        private float _leftWheelSpeed;
        private float _rightWheelSpeed;
        private readonly Collider[] _overlapBuffer = new Collider[16];

        // True while the body is being flung by a hit (drive is suppressed).
        public bool IsInKnockback => _knockbackTimer > 0f;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _rb = GetComponent<Rigidbody>();
            _wheelHealth = GetComponent<WheelHealth>();
            _rb.centerOfMass = new Vector3(0f, 0.15f, 0f); // low, so turning never tips us over
        }

        private void FixedUpdate()
        {
            if (IsOwner)
                SendInputServerRpc(_leftInput, _rightInput);
            if (IsServer)
                ApplyDrive();
            SpinWheelVisuals();
        }

        // Disables the drive briefly so the flung body can travel instead of the
        // next FixedUpdate cancelling its horizontal velocity. Applied at the
        // contact point so the hit imparts torque.
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
        private void SendInputServerRpc(float left, float right)
        {
            _leftInput = left;
            _rightInput = right;
        }

        private void ReadInput()
        {
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                _leftInput = gamepad.leftStick.ReadValue().y;
                _rightInput = gamepad.rightStick.ReadValue().y;
            }
            else
            {
                var kb = Keyboard.current;
                _leftInput = 0f;
                _rightInput = 0f;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) _leftInput += 1f;
                    if (kb.sKey.isPressed) _leftInput -= 1f;
                    if (kb.upArrowKey.isPressed) _rightInput += 1f;
                    if (kb.downArrowKey.isPressed) _rightInput -= 1f;
                }
            }
        }

        // Caps yaw, scaling forward speed with it so the pivot geometry is preserved.
        private void CapDrive(ref float forwardSpeed, ref float yawRate)
        {
            float maxYaw = maxAngularSpeed * Mathf.Deg2Rad;
            if (Mathf.Abs(yawRate) > maxYaw)
            {
                float scale = maxYaw / Mathf.Abs(yawRate);
                yawRate *= scale;
                forwardSpeed *= scale;
            }
        }

        // Fraction (0..1) of the given wheels whose tires are touching something.
        private float SideGrip(Transform[] wheels)
        {
            if (wheels == null || wheels.Length == 0) return 1f;
            int grounded = 0;
            foreach (var w in wheels)
                if (w != null && IsWheelGrounded(w)) grounded++;
            return (float)grounded / wheels.Length;
        }

        // A sphere overlap in any direction beats a raycast: it catches floors,
        // slopes, walls pushed against the side, even the ceiling when flipped
        // (the tires stick out past the chassis).
        private bool IsWheelGrounded(Transform wheel)
        {
            float radius = wheelRadius + groundClearance;
            int count = Physics.OverlapSphereNonAlloc(wheel.position, radius,
                _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_overlapBuffer[i].transform.IsChildOf(transform)) continue; // own body doesn't count
                return true;
            }
            return false;
        }

        private void ApplyDrive()
        {
            // Kinematic bodies (frozen in lobby / client copies) reject velocity writes.
            if (_rb != null && _rb.isKinematic) return;

            if (_knockbackTimer > 0f) // stunned: let the flung body travel freely
            {
                _knockbackTimer -= Time.fixedDeltaTime;
                return;
            }

            float leftTraction = SideGrip(leftWheels);
            float rightTraction = SideGrip(rightWheels);

            // Fully airborne: no tire can push, so leave velocity and wheel speeds
            // alone. Persisting the wheel speeds means landing resumes the pursuit
            // instead of re-accelerating from zero (a visible lurch).
            if (leftTraction <= 0f && rightTraction <= 0f)
                return;

            // Ramp both inputs at the same rate so the forward/yaw ratio -- and
            // therefore the pivot point -- stays exact throughout the ramp.
            float driveLerp = 1f - Mathf.Exp(-driveSmoothing * Time.fixedDeltaTime);
            _smoothedLeft = Mathf.Lerp(_smoothedLeft, _leftInput, driveLerp);
            _smoothedRight = Mathf.Lerp(_smoothedRight, _rightInput, driveLerp);

            // Wheels pursue their target speed, capped by how quickly traction
            // lets them change. Traction never lowers the target itself, so a
            // wheel that briefly loses contact keeps its momentum instead of
            // snapping to a stop. Targets are health-scaled.
            float targetLeft, targetRight, targetForward, targetYaw;
            float healthLeft = _wheelHealth != null ? _wheelHealth.LeftPower : 1f;
            float healthRight = _wheelHealth != null ? _wheelHealth.RightPower : 1f;
            targetLeft = _smoothedLeft * maxSpeed * healthLeft;
            targetRight = _smoothedRight * maxSpeed * healthRight;

            float maxChange = driveAcceleration * Time.fixedDeltaTime;
            _leftWheelSpeed = Mathf.MoveTowards(_leftWheelSpeed, targetLeft, maxChange * leftTraction);
            _rightWheelSpeed = Mathf.MoveTowards(_rightWheelSpeed, targetRight, maxChange * rightTraction);

            // Pivot math runs on the pursued wheel speeds so the stationary
            // side really stops (and the moving side keeps its momentum).
            targetForward = (_leftWheelSpeed + _rightWheelSpeed) * 0.5f;
            targetYaw = (_leftWheelSpeed - _rightWheelSpeed) / wheelBase;
            CapDrive(ref targetForward, ref targetYaw);

            // Only touch horizontal velocity; leave Y so gravity keeps working.
            Vector3 targetVel = transform.forward * targetForward;
            Vector3 vel = _rb.linearVelocity;
            vel.x = targetVel.x;
            vel.z = targetVel.z;
            _rb.linearVelocity = vel;

            Vector3 ang = _rb.angularVelocity;
            ang.y = targetYaw;
            _rb.angularVelocity = ang;
        }

        private void SpinWheelVisuals()
        {
            // Use the pursued wheel speeds so visuals match actual motion,
            // including momentum carried through a bump.
            float leftAngle = (_leftWheelSpeed / wheelRadius) * Mathf.Rad2Deg * Time.fixedDeltaTime;
            float rightAngle = (_rightWheelSpeed / wheelRadius) * Mathf.Rad2Deg * Time.fixedDeltaTime;
            for (int i = 0; i < leftWheelVisuals.Length; i++)
            {
                var v = leftWheelVisuals[i];
                bool grounded = leftWheels != null && leftWheels.Length > 0
                    ? IsWheelGrounded(leftWheels[Mathf.Min(i, leftWheels.Length - 1)])
                    : true;
                if (v != null && v.parent != null && grounded)
                    v.Rotate(v.parent.right, leftAngle, Space.World);
            }
            for (int i = 0; i < rightWheelVisuals.Length; i++)
            {
                var v = rightWheelVisuals[i];
                bool grounded = rightWheels != null && rightWheels.Length > 0
                    ? IsWheelGrounded(rightWheels[Mathf.Min(i, rightWheels.Length - 1)])
                    : true;
                if (v != null && v.parent != null && grounded)
                    v.Rotate(v.parent.right, rightAngle, Space.World);
            }
        }
    }
}
