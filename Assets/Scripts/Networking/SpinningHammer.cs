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

            var health = rb.GetComponent<RoombaHealth>();
            if (health == null) return;

            Vector3 contactPoint = other.ClosestPoint(transform.position);

            // The arm spins around world Y; its tip carries the impact even when
            // the hazard body itself is static.
            Vector3 attackerVel = CombatResolver.SpinPointVelocity(arm, Vector3.up,
                direction * spinSpeed * Mathf.Deg2Rad, contactPoint);

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
