using UnityEngine;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class WallImpactDetector : NetworkBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private float wallImpactMass = 2f;

        private RoombaHealth _health;
        private RobotDrive _drive;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (body == null) body = GetComponent<Rigidbody>();
            _health = GetComponent<RoombaHealth>();
            _drive = GetComponent<RobotDrive>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer) return;
            if (_health == null || _health.IsEliminated) return;
            if (body == null) return;
            if (collision.collider.CompareTag("Wall") == false) return;

            // Normal driving, scraping or low-speed bumps must never destroy.
            if (_drive == null || !_drive.IsInKnockback) return;

            var settings = _health.Settings;
            if (settings == null) return;

            // Relative speed against the wall = how fast we were flung at it.
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < settings.minWallImpactSpeed) return;

            Vector3 contactPoint = collision.contacts.Length > 0
                ? collision.contacts[0].point
                : transform.position;

            // Show the impact burst at the contact point on all clients.
            _health.NotifyImpactFx(contactPoint);

            float strength = impactSpeed * wallImpactMass;

            if (strength >= settings.wallDestructionStrength)
            {
                _health.Eliminate(EliminationCause.WallSlam);
            }
            else
            {
                // Weak impact: minor wall impact damage + continued instability.
                float gain = settings.wallImpactDamage * settings.gaugeGainMultiplier;
                _health.ApplyDamage(settings.wallImpactDamage, gain);
            }
        }
    }
}
