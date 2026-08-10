using UnityEngine;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class Weapon : NetworkBehaviour
    {
        [SerializeField] private Transform pivot;
        [SerializeField] private float spinSpeed = 240f;   // deg/s
        [SerializeField] private float outwardSpeed = 3f;
        [SerializeField] private float upwardSpeed = 6f;
        [SerializeField] private float damagePerHit = 8f;

        private void Update()
        {
            if (pivot == null) return;
            // Cosmetic spin, runs everywhere; local space keeps the axis locked
            // to the robot body when it is knocked around.
            pivot.Rotate(-spinSpeed * Time.deltaTime, 0f, 0f, Space.Self);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            var rb = other.attachedRigidbody;
            if (rb == null) return; // walls, floor, props without a rigidbody
            if (rb == GetComponentInParent<Rigidbody>()) return; // don't hit ourselves
            if (pivot == null) return;

            // Impulse at the contact point (not center of mass) imparts torque,
            // so the victim spins/tumbles from the hit instead of just sliding.
            Vector3 contactPoint = other.ClosestPoint(pivot.position);

            Vector3 away = rb.position - pivot.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = pivot.forward;
            away.Normalize();

            // Scale by mass so launch speed is the same regardless of robot weight.
            Vector3 impulse = (away * outwardSpeed + Vector3.up * upwardSpeed) * rb.mass;

            var drive = rb.GetComponent<RobotDrive>();
            if (drive != null)
            {
                // Charge the victim before applying knockback, so the hit that
                // lands also contributes to how far it is flung.
                var meter = rb.GetComponent<ChargeMeter>();
                if (meter != null)
                {
                    meter.AddCharge(damagePerHit);
                    impulse *= meter.KnockbackMultiplier;
                }
                drive.ApplyKnockback(impulse, contactPoint);
            }
            else
                rb.AddForceAtPosition(impulse, contactPoint, ForceMode.Impulse);
        }
    }
}
