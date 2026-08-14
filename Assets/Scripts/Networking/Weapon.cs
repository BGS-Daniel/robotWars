using UnityEngine;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class Weapon : NetworkBehaviour
    {
        [SerializeField] private Transform pivot;
        [SerializeField] private float spinSpeed = 240f;   // deg/s
        [SerializeField] private ImpactSource impactSource;

        [Header("Crate / Prop Push")]
        [Tooltip("Outward launch speed applied to non-Roomba rigidbodies (crates, props).")]
        [SerializeField] private float outwardSpeed = 3f;
        [Tooltip("Upward launch speed applied to non-Roomba rigidbodies.")]
        [SerializeField] private float upwardSpeed = 6f;

        private Rigidbody _body;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _body = GetComponentInParent<Rigidbody>();
        }

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
            if (impactSource == null) return;
            var rb = other.attachedRigidbody;
            if (rb == null) return; // walls, floor, props without a rigidbody
            if (rb == GetComponentInParent<Rigidbody>()) return; // don't hit ourselves
            if (pivot == null) return;

            Vector3 contactPoint = other.ClosestPoint(pivot.position);

            Vector3 attackerVel = CombatResolver.BodyPointVelocity(_body, contactPoint);
            // Outer edge of the spinning blade produces a strong impact even when
            // the Roomba itself is stationary.
            attackerVel += CombatResolver.SpinPointVelocity(pivot, pivot.right,
                -spinSpeed * Mathf.Deg2Rad, contactPoint);

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

            // Crates and props: push them with the blade's motion instead of combat.
            PushProp(rb, contactPoint, attackerVel);
        }

        // Physical fling for non-Roomba rigidbodies (crates, props). Impulse at
        // the contact point imparts torque so the object tumbles.
        private void PushProp(Rigidbody rb, Vector3 contactPoint, Vector3 attackerVel)
        {
            Vector3 away = attackerVel;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = rb.position - pivot.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = pivot.forward;
            away.Normalize();

            // Scale by mass so launch speed is the same regardless of prop weight.
            Vector3 impulse = (away * outwardSpeed + Vector3.up * upwardSpeed) * rb.mass;
            rb.AddForceAtPosition(impulse, contactPoint, ForceMode.Impulse);
        }
    }
}
