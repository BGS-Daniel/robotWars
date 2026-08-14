using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class NetworkPlayer : NetworkBehaviour
    {
        private Vector3 _spawnPosition;

        private void Update()
        {
            // Debug cheat: F10 to respawn instantly.
            if (!IsOwner) return;
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                RespawnServerRpc();
        }

        [ServerRpc]
        private void RespawnServerRpc()
        {
            Respawn();
        }
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

            _spawnPosition = position;

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = Quaternion.identity;
            Physics.SyncTransforms();

            GetComponent<RoombaHealth>()?.ResetForRound();
        }

        /// Server-side: return to the stored spawn point with a full round reset.
        public void Respawn()
        {
            PlaceInArena(_spawnPosition);
        }
    }
}
