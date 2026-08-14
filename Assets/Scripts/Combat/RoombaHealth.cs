using System;
using Unity.Netcode;
using UnityEngine;

namespace RobotWars.Combat
{
    public enum EliminationCause
    {
        None,
        Destroyed,
        RingOut,
        WallSlam,
        ArenaHazard
    }

    public class RoombaHealth : NetworkBehaviour
    {
        [SerializeField] private CombatSettings settings;

        private readonly NetworkVariable<float> _currentHP = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<float> _gauge = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<EliminationCause> _eliminationCause = new NetworkVariable<EliminationCause>(EliminationCause.None);

        public event Action<float, float> DamageTaken;   // (damage, gaugeGain) - server only
        public event Action<EliminationCause> Eliminated; // server only

        public float CurrentHP => _currentHP.Value;
        public float MaxHP => settings != null ? settings.maxHP : 100f;
        public float Gauge => _gauge.Value;
        public float GaugeMax => settings != null ? settings.gaugeMax : 100f;
        public bool IsEliminated => _eliminationCause.Value != EliminationCause.None;
        public EliminationCause EliminationCause => _eliminationCause.Value;
        public CombatSettings Settings => settings;

        private RobotWars.Networking.RobotDrive _drive;
        private RobotWars.Networking.Weapon _weapon;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _drive = GetComponent<RobotWars.Networking.RobotDrive>();
            _weapon = GetComponentInChildren<RobotWars.Networking.Weapon>();
            if (IsServer)
                _currentHP.Value = MaxHP;
        }

        // Gauge is updated before knockback so this hit contributes to its own launch.
        public void ApplyDamage(float damage, float gaugeGain)
        {
            if (!IsServer || IsEliminated) return;

            _currentHP.Value = Mathf.Max(0f, _currentHP.Value - damage);
            _gauge.Value = ClampGauge(_gauge.Value + gaugeGain);

            DamageTaken?.Invoke(damage, gaugeGain);

            if (_currentHP.Value <= 0f)
                Eliminate(EliminationCause.Destroyed);
        }

        public float EvaluateGaugeMultiplier()
        {
            return settings != null ? settings.EvaluateGaugeMultiplier(_gauge.Value) : 1f;
        }

        public void ResetForRound()
        {
            if (!IsServer) return;
            _currentHP.Value = MaxHP;
            _gauge.Value = 0f;
            _eliminationCause.Value = EliminationCause.None;
            SetControlsEnabled(true);
        }

        public void Eliminate(EliminationCause cause)
        {
            if (!IsServer || IsEliminated) return;
            _eliminationCause.Value = cause;
            _currentHP.Value = 0f;
            SetControlsEnabled(false);
            Eliminated?.Invoke(cause);
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (_drive != null) _drive.enabled = enabled;
            if (_weapon != null) _weapon.enabled = enabled;
        }

        private float ClampGauge(float value)
        {
            return settings != null ? settings.ClampGauge(value) : Mathf.Clamp(value, 0f, 100f);
        }
    }
}
