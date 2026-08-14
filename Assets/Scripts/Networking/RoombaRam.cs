using UnityEngine;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class RoombaRam : NetworkBehaviour
    {
        [SerializeField] private ImpactSource ramSource;

        private Rigidbody _body;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _body = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer) return;
            if (ramSource == null) return;
            var rb = collision.rigidbody;
            if (rb == null) return;
            if (rb == _body) return;

            var health = rb.GetComponent<RoombaHealth>();
            if (health == null) return;

            Vector3 contactPoint = collision.contacts.Length > 0
                ? collision.contacts[0].point
                : rb.position;

            CombatResolver.Resolve(new ImpactRequest
            {
                source = ramSource,
                attacker = gameObject,
                targetBody = rb,
                targetHealth = health,
                contactPoint = contactPoint,
                attackerVelocityAtContact = CombatResolver.BodyPointVelocity(_body, contactPoint)
            });
        }
    }
}
