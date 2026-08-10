using UnityEngine;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class ChargeMeter : NetworkBehaviour
    {
        [SerializeField] private float maxCharge = 200f;

        // Knockback multiplier added per charge point (e.g. 0.015 = +1.5% per point,
        // so 100 charge = ~2.5x knockback).
        [SerializeField] private float knockbackScale = 0.015f;

        private readonly NetworkVariable<float> _charge = new NetworkVariable<float>(0f);

        public float Charge => _charge.Value;

        public void AddCharge(float amount)
        {
            if (!IsServer) return;
            _charge.Value = Mathf.Min(maxCharge, _charge.Value + amount);
        }

        public void ResetCharge()
        {
            if (!IsServer) return;
            _charge.Value = 0f;
        }

        public float KnockbackMultiplier => 1f + _charge.Value * knockbackScale;
    }
}
