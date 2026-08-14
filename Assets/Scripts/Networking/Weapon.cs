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

            var health = rb.GetComponent<RoombaHealth>();
            if (health == null) return;

            Vector3 contactPoint = other.ClosestPoint(pivot.position);

            Vector3 attackerVel = CombatResolver.BodyPointVelocity(_body, contactPoint);
            // Outer edge of the spinning blade produces a strong impact even when
            // the Roomba itself is stationary.
            attackerVel += CombatResolver.SpinPointVelocity(pivot, pivot.right,
                -spinSpeed * Mathf.Deg2Rad, contactPoint);

            CombatResolver.Resolve(new ImpactRequest
            {
                source = impactSource,
                attacker = gameObject,
                targetBody = rb,
                targetHealth = health,
                contactPoint = contactPoint,
                attackerVelocityAtContact = attackerVel
            });
        }
    }
}
