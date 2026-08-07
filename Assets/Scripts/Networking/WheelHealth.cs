using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class WheelHealth : NetworkBehaviour
    {
        [SerializeField] private Transform[] wheels = new Transform[4];

        private readonly NetworkVariable<int> _destroyedMask = new NetworkVariable<int>(0);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _destroyedMask.OnValueChanged += OnDestroyedMaskChanged;
            ApplyMask(0, _destroyedMask.Value); // late joiners already have a mask
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _destroyedMask.OnValueChanged -= OnDestroyedMaskChanged;
        }

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) RequestDestroyServerRpc(0);
            if (kb.digit2Key.wasPressedThisFrame) RequestDestroyServerRpc(1);
            if (kb.digit3Key.wasPressedThisFrame) RequestDestroyServerRpc(2);
            if (kb.digit4Key.wasPressedThisFrame) RequestDestroyServerRpc(3);
        }

        [ServerRpc]
        private void RequestDestroyServerRpc(int index)
        {
            if (index < 0 || index >= wheels.Length) return;
            int mask = _destroyedMask.Value | (1 << index);
            if (mask != _destroyedMask.Value)
                _destroyedMask.Value = mask;
        }

        private void OnDestroyedMaskChanged(int previousValue, int newValue)
        {
            ApplyMask(previousValue, newValue);
        }

        private void ApplyMask(int previousValue, int newValue)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                bool wasDestroyed = (previousValue & (1 << i)) != 0;
                bool isDestroyed = (newValue & (1 << i)) != 0;
                if (isDestroyed && !wasDestroyed)
                    wheels[i].gameObject.SetActive(false);
            }
        }

        public bool IsDestroyed(int index)
        {
            return (_destroyedMask.Value & (1 << index)) != 0;
        }

        // Fraction (0..1) of intact wheels per side; losing one halves that side's power.
        public float LeftPower
        {
            get
            {
                int intact = 0;
                if (!IsDestroyed(0)) intact++; // FL
                if (!IsDestroyed(2)) intact++; // BL
                return intact / 2f;
            }
        }

        public float RightPower
        {
            get
            {
                int intact = 0;
                if (!IsDestroyed(1)) intact++; // FR
                if (!IsDestroyed(3)) intact++; // BR
                return intact / 2f;
            }
        }
    }
}
