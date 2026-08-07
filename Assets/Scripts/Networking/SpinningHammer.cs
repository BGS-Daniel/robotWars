using UnityEngine;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class SpinningHammer : MonoBehaviour
    {
        [SerializeField] private Transform arm;
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private float direction = 1f; // +1 CCW from above, -1 CW

        [SerializeField] private float outwardSpeed = 5f;
        [SerializeField] private float upwardSpeed = 2f;
        [SerializeField] private float reHitCooldown = 0.75f;

        private float _cooldownTimer;

        private void Update()
        {
            if (arm != null)
                arm.Rotate(0f, direction * spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void FixedUpdate()
        {
            if (_cooldownTimer > 0f) _cooldownTimer -= Time.fixedDeltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_cooldownTimer > 0f) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return; // host computes hits
            var rb = other.attachedRigidbody;
            if (rb == null) return; // walls, floor, props without a rigidbody

            _cooldownTimer = reHitCooldown;

            Vector3 contactPoint = other.ClosestPoint(transform.position);

            Vector3 away = rb.position - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            away.Normalize();

            Vector3 impulse = (away * outwardSpeed + Vector3.up * upwardSpeed) * rb.mass;

            var drive = rb.GetComponent<RobotDrive>();
            if (drive != null)
                drive.ApplyKnockback(impulse, contactPoint);
            else
                rb.AddForceAtPosition(impulse, contactPoint, ForceMode.Impulse);
        }
    }
}
