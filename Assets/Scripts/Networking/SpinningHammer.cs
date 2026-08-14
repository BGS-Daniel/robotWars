using UnityEngine;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class SpinningHammer : MonoBehaviour
    {
        [SerializeField] private Transform arm;
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private float direction = 1f; // +1 CCW from above, -1 CW
        [SerializeField] private ImpactSource impactSource;

        [Header("Crate / Prop Push")]
        [Tooltip("Outward launch speed applied to non-Roomba rigidbodies (crates, props).")]
        [SerializeField] private float outwardSpeed = 5f;
        [Tooltip("Upward launch speed applied to non-Roomba rigidbodies.")]
        [SerializeField] private float upwardSpeed = 4f;

        private void Update()
        {
            if (arm != null)
                arm.Rotate(0f, direction * spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (impactSource == null) return;
            var rb = other.attachedRigidbody;
            if (rb == null) return; // walls, floor, props without a rigidbody

            Vector3 contactPoint = other.ClosestPoint(transform.position);

            // The arm spins around world Y; its tip carries the impact even when
            // the hazard body itself is static.
            Vector3 attackerVel = CombatResolver.SpinPointVelocity(arm, Vector3.up,
                direction * spinSpeed * Mathf.Deg2Rad, contactPoint);

            var health = rb.GetComponent<RoombaHealth>();
            if (health != null)
            {
                CombatResolver.Resolve(new ImpactRequest
                {
                    source = impactSource,
                    attacker = gameObject,
                    targetBody = rb,
                    targetHealth = health,
                    contactPoint = contactPoint,
                    attackerVelocityAtContact = attackerVel
                });
                return;
            }

            // Crates and props: fling them with the hammer arm's motion.
            PushProp(rb, contactPoint, attackerVel);
        }

        private void PushProp(Rigidbody rb, Vector3 contactPoint, Vector3 attackerVel)
        {
            Vector3 away = attackerVel;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = rb.position - transform.position;
                away.y = 0f;
            }
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            away.Normalize();

            // Scale by mass so launch speed is the same regardless of prop weight.
            Vector3 impulse = (away * outwardSpeed + Vector3.up * upwardSpeed) * rb.mass;
            rb.AddForceAtPosition(impulse, contactPoint, ForceMode.Impulse);
        }
    }
}
