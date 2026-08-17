using Unity.Netcode;
using UnityEngine;

namespace RobotWars.Networking
{
    public class RoombaTint : NetworkBehaviour
    {
        [Tooltip("Palette shared with the lobby colour picker, indexed by colour.")]
        [SerializeField] private Color[] palette =
        {
            new Color(0.35f, 0.6f, 1f),   // blue
            new Color(1f, 0.35f, 0.3f),   // red
            new Color(0.4f, 0.9f, 0.4f),  // green
            new Color(1f, 0.8f, 0.3f)     // yellow
        };

        private readonly NetworkVariable<int> _colorIndex = new NetworkVariable<int>(0);
        private Renderer[] _renderers;

        public int ColorIndex => _colorIndex.Value;
        public event System.Action<int> ColorIndexChanged;

        // Colour for a palette index (used by the HUD to tint the player label).
        public Color GetColor(int index)
        {
            return palette[Mathf.Clamp(index, 0, palette.Length - 1)];
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _renderers = GetComponentsInChildren<Renderer>();

            if (IsOwner)
                ReportColorServerRpc(LocalPlayer.ColorIndex);

            Apply(_colorIndex.Value);
            _colorIndex.OnValueChanged += (prev, next) =>
            {
                Apply(next);
                ColorIndexChanged?.Invoke(next);
            };
        }

        // Owner-side: push the currently selected colour to the server. Safe to
        // call again if the player changes colour after the Roomba already spawned.
        public void ReportColor()
        {
            if (!IsOwner) return;
            ReportColorServerRpc(LocalPlayer.ColorIndex);
        }

        [ServerRpc]
        private void ReportColorServerRpc(int index)
        {
            _colorIndex.Value = index;
        }

        private void Apply(int index)
        {
            if (_renderers == null) return;
            Color color = palette[Mathf.Clamp(index, 0, palette.Length - 1)];

            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);

            foreach (var r in _renderers)
                if (r != null)
                    r.SetPropertyBlock(block);
        }
    }
}
