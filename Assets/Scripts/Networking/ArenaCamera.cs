using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class ArenaCamera : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private float followHeight = 5f;
        [SerializeField] private float followDistance = 10f;

        [Header("Orbit")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float stickSensitivity = 120f;
        [SerializeField] private float stickDeadzone = 0.15f;
        [SerializeField] private float pitchMin = -10f;
        [SerializeField] private float pitchMax = 60f;

        [Header("Smoothing")]
        [SerializeField] private float catchUpSpeed = 20f;
        [SerializeField] private float lookAtSmoothing = 6f;
        [SerializeField] private float respawnDelay = 1f;

        [Header("Collision")]
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private LayerMask collisionLayers = ~0;

        private Transform _followTarget;
        private RobotDrive _drive;
        private RoombaHealth _health;
        private bool _wasEliminated;
        private float _respawnTimer;
        private float _orbitYaw;
        private float _orbitPitch = 20f;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (_followTarget == null)
                TryFindPlayer();

            if (_followTarget == null) return;

            bool inKnockback = _drive != null && _drive.IsInKnockback;
            bool eliminated = _health != null && _health.IsEliminated;
            bool justRespawned = _wasEliminated && !eliminated;

            if (justRespawned)
                _respawnTimer = respawnDelay;

            if (_respawnTimer > 0f)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    _orbitYaw = _followTarget.eulerAngles.y + 180f;
                    _orbitPitch = 20f;
                    SnapToOrbit();
                }
            }
            else if (eliminated)
            {
                // Fully frozen.
            }
            else
            {
                ReadOrbitInput();

                Vector3 desiredPos = GetDesiredPosition();

                if (inKnockback)
                {
                    Vector3 delta = desiredPos - transform.position;
                    transform.position += delta * 0.05f;
                }
                else
                {
                    Vector3 delta = desiredPos - transform.position;
                    float maxStep = catchUpSpeed * Time.deltaTime;
                    if (delta.sqrMagnitude > maxStep * maxStep)
                        delta = delta.normalized * maxStep;
                    transform.position += delta;
                }

                Vector3 lookDir = _followTarget.position + Vector3.up * 1.5f;
                Quaternion targetRot = Quaternion.LookRotation(lookDir - transform.position);
                float t = inKnockback ? 2f * Time.deltaTime : lookAtSmoothing * Time.deltaTime;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
            }

            _wasEliminated = eliminated;
        }

        private Vector3 GetDesiredPosition()
        {
            Quaternion rotation = Quaternion.Euler(-_orbitPitch, _orbitYaw, 0f);
            Vector3 offset = rotation * (Vector3.back * followDistance);
            Vector3 pivot = _followTarget.position + Vector3.up * followHeight;
            Vector3 desired = pivot + offset;

            Vector3 dir = desired - pivot;
            float dist = dir.magnitude;
            if (dist < 0.01f) return desired;

            if (Physics.SphereCast(pivot, collisionRadius, dir.normalized, out RaycastHit hit,
                dist, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                return pivot + dir.normalized * (hit.distance - collisionRadius);
            }
            return desired;
        }

        private void SnapToOrbit()
        {
            transform.position = GetDesiredPosition();
            Vector3 lookDir = _followTarget.position + Vector3.up * 1.5f;
            transform.rotation = Quaternion.LookRotation(lookDir - transform.position);
        }

        private void ReadOrbitInput()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = !locked;
            }

            float inputX = 0f;
            float inputY = 0f;

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.magnitude > stickDeadzone)
                {
                    inputX = stick.x * stickSensitivity;
                    inputY = stick.y * stickSensitivity;
                }
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    inputX = delta.x * mouseSensitivity;
                    inputY = delta.y * mouseSensitivity;
                }
            }

            _orbitYaw += inputX;
            _orbitPitch += inputY;
            _orbitPitch = Mathf.Clamp(_orbitPitch, pitchMin, pitchMax);
        }

        private void TryFindPlayer()
        {
            var players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            foreach (var p in players)
            {
                if (p.IsOwner)
                {
                    _followTarget = p.transform;
                    _drive = p.GetComponent<RobotDrive>();
                    _health = p.GetComponent<RoombaHealth>();
                    return;
                }
            }
        }
    }
}
