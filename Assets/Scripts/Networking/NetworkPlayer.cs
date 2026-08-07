using UnityEngine;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class NetworkPlayer : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var nrb = GetComponent<Unity.Netcode.Components.NetworkRigidbody>();
            if (nrb != null)
                nrb.AutoUpdateKinematicState = false;

            // Server simulates physics; clients stay kinematic and receive the
            // result over the network. The server copy stays frozen until placed.
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            DontDestroyOnLoad(gameObject);
        }

        /// Server-side: teleport the robot to its spawn point and enable physics.
        public void PlaceInArena(Vector3 position)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) return;

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            Physics.SyncTransforms();
        }
    }
}
