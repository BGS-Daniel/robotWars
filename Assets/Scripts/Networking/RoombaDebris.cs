using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RobotWars.Networking
{
    public class RoombaDebris : NetworkBehaviour
    {
        [System.Serializable]
        public class Visual
        {
            public Mesh mesh;
            public Material material;
        }

        [SerializeField] private Visual[] visuals;
        [SerializeField] private float lifetime = 6f;

        private readonly NetworkVariable<int> _visualIndex = new NetworkVariable<int>(-1);
        private int _pendingVisualIndex = -1;
        private Rigidbody _body;

        public Visual[] Visuals => visuals;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _body = GetComponent<Rigidbody>();

            var netRb = GetComponent<NetworkRigidbody>();
            if (netRb != null) netRb.AutoUpdateKinematicState = false;

            if (!IsServer)
            {
                _body.isKinematic = true;
                _body.useGravity = false;
            }

            // Push the pre-spawn choice into the replicated value, then apply it.
            if (IsServer && _pendingVisualIndex >= 0)
                _visualIndex.Value = _pendingVisualIndex;

            ApplyVisual(_visualIndex.Value);
            _visualIndex.OnValueChanged += (prev, next) => ApplyVisual(next);

            if (IsServer)
                StartCoroutine(DespawnAfterLifetime());
        }

        // Server-only, call before Spawn(). The index is stored locally and
        // pushed into the NetworkVariable on spawn so it replicates to clients.
        public void Configure(int visualIndex, Vector3 scale)
        {
            if (!IsServer) return;
            _pendingVisualIndex = visualIndex;
            _visualIndex.Value = visualIndex;
            transform.localScale = scale;
        }

        // Matches a part's mesh+material to a visuals index so the spawning
        // roomba can replicate the same look. Falls back to 0.
        public int FindVisualIndex(Mesh mesh, Material material)
        {
            if (visuals == null || visuals.Length == 0) return 0;
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i].mesh == mesh && visuals[i].material == material)
                    return i;
            return 0;
        }

        public void Launch(Vector3 velocity, Vector3 angularVelocity)
        {
            if (_body != null)
            {
                _body.linearVelocity = velocity;
                _body.angularVelocity = angularVelocity;
            }
        }

        private void ApplyVisual(int index)
        {
            if (visuals == null || index < 0 || index >= visuals.Length) return;

            var mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = visuals[index].mesh;

            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = visuals[index].material;

            var mc = GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = visuals[index].mesh;
        }

        private IEnumerator DespawnAfterLifetime()
        {
            yield return new WaitForSeconds(lifetime);
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
