using System;
using System.Collections;
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
        [SerializeField] private RobotWars.Networking.RoombaDebris debrisPrefab;
        [SerializeField] private GameObject impactBurstPrefab;
        [SerializeField] private float respawnDelay = 5f;

        private readonly NetworkVariable<float> _currentHP = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<float> _gauge = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<EliminationCause> _eliminationCause = new NetworkVariable<EliminationCause>(EliminationCause.None);

        // Server-only combat flow events.
        public event Action<float, float> DamageTaken;   // (damage, gaugeGain)
        public event Action<EliminationCause> Eliminated;

        // Client-facing events (fired everywhere, including the server).
        public event Action<float, float> HpChanged;     // (currentHP, maxHP)
        public event Action<float, float> GaugeChanged;  // (gauge, gaugeMax)
        public event Action<EliminationCause> EliminatedClient;

        public float CurrentHP => _currentHP.Value;
        public float MaxHP => settings != null ? settings.maxHP : 100f;
        public float Gauge => _gauge.Value;
        public float GaugeMax => settings != null ? settings.gaugeMax : 100f;
        public bool IsEliminated => _eliminationCause.Value != EliminationCause.None;
        public EliminationCause EliminationCause => _eliminationCause.Value;
        public CombatSettings Settings => settings;

        private RobotWars.Networking.RobotDrive _drive;
        private RobotWars.Networking.Weapon _weapon;
        private Renderer[] _renderers;
        private Collider[] _colliders;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _drive = GetComponent<RobotWars.Networking.RobotDrive>();
            _weapon = GetComponentInChildren<RobotWars.Networking.Weapon>();
            _renderers = GetComponentsInChildren<Renderer>();
            _colliders = GetComponentsInChildren<Collider>();

            if (IsServer)
                _currentHP.Value = MaxHP;

            _currentHP.OnValueChanged += OnHpValueChanged;
            _gauge.OnValueChanged += OnGaugeValueChanged;
            _eliminationCause.OnValueChanged += OnCauseValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _currentHP.OnValueChanged -= OnHpValueChanged;
            _gauge.OnValueChanged -= OnGaugeValueChanged;
            _eliminationCause.OnValueChanged -= OnCauseValueChanged;
        }

        // Server-only: ring-out check. Falling below the arena or leaving its
        // horizontal bounds eliminates the Roomba.
        private void FixedUpdate()
        {
            if (!IsServer || IsEliminated) return;
            if (settings == null) return;

            Vector3 pos = transform.position;
            if (pos.y < settings.killPlaneY ||
                Mathf.Abs(pos.x) > settings.arenaHalfExtents.x ||
                Mathf.Abs(pos.z) > settings.arenaHalfExtents.y)
            {
                Eliminate(EliminationCause.RingOut);
            }
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
            SetBodyVisible(true);
            SetControlsEnabled(true);
        }

        public void Eliminate(EliminationCause cause)
        {
            if (!IsServer || IsEliminated) return;
            _eliminationCause.Value = cause;
            _currentHP.Value = 0f;
            SetControlsEnabled(false);
            Eliminated?.Invoke(cause);

            SpawnDebris();
            if (respawnDelay > 0f)
                StartCoroutine(RespawnAfterDelay());
        }

        // Server-only: blow the Roomba apart into rigidbody debris that keeps
        // flying with its current velocity and can collide with the arena.
        private void SpawnDebris()
        {
            if (debrisPrefab == null || NetworkManager.Singleton == null) return;
            var rb = GetComponent<Rigidbody>();
            var parts = GetComponentsInChildren<Transform>();

            foreach (var part in parts)
            {
                if (part == null || part == transform) continue;

                var mf = part.GetComponent<MeshFilter>();
                var mr = part.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null) continue;

                var go = Instantiate(debrisPrefab.gameObject, part.position, part.rotation);
                var debris = go.GetComponent<RobotWars.Networking.RoombaDebris>();
                debris.Configure(debris.FindVisualIndex(mf.sharedMesh, mr.sharedMaterial),
                    part.lossyScale);

                var netObj = go.GetComponent<NetworkObject>();
                netObj.Spawn();

                // Launch with the part's current velocity (from the whole-body
                // motion) plus a random blast outward and upward.
                Vector3 pointVel = rb != null ? rb.GetPointVelocity(part.position) : Vector3.zero;
                Vector3 outward = (part.position - transform.position).normalized;
                if (outward.sqrMagnitude < 0.001f) outward = UnityEngine.Random.onUnitSphere;
                outward.y = Mathf.Abs(outward.y);
                debris.Launch(pointVel + outward * UnityEngine.Random.Range(4f, 8f) + Vector3.up * UnityEngine.Random.Range(1f, 3f),
                    UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(3f, 8f));
            }
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            if (!IsServer || !IsEliminated) yield break;

            // Back to a spawn point, then full round reset (HP, gauge, body, controls).
            var player = GetComponent<RobotWars.Networking.NetworkPlayer>();
            if (player != null)
                player.Respawn();
            else
                ResetForRound();
        }

        // Server-only: ask all clients to show the small impact puff.
        public void NotifyImpactFx(Vector3 contactPoint)
        {
            if (!IsServer) return;
            ImpactFxClientRpc(contactPoint);
        }

        [ClientRpc]
        private void ImpactFxClientRpc(Vector3 point)
        {
            SpawnImpactBurst(point);
        }

        // Local particle burst at the contact point so valid hits are readable.
        private void SpawnImpactBurst(Vector3 point)
        {
            if (impactBurstPrefab == null) return;
            var go = Instantiate(impactBurstPrefab, point, Quaternion.identity);
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play();
            Destroy(go, 2f);
        }

        private void OnHpValueChanged(float previousValue, float newValue)
        {
            HpChanged?.Invoke(newValue, MaxHP);
        }

        private void OnGaugeValueChanged(float previousValue, float newValue)
        {
            GaugeChanged?.Invoke(newValue, GaugeMax);
        }

        private void OnCauseValueChanged(EliminationCause previousValue, EliminationCause newValue)
        {
            if (newValue == EliminationCause.None)
            {
                // Respawn: restore the body that was hidden on elimination.
                SetBodyVisible(true);
                SetControlsEnabled(true);
                return;
            }
            SetBodyVisible(false);
            EliminatedClient?.Invoke(newValue);
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (_drive != null) _drive.enabled = enabled;
            if (_weapon != null) _weapon.enabled = enabled;
        }

        private void SetBodyVisible(bool visible)
        {
            if (_renderers != null)
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _renderers[i].enabled = visible;
            if (_colliders != null)
                for (int i = 0; i < _colliders.Length; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = visible;
        }

        private float ClampGauge(float value)
        {
            return settings != null ? settings.ClampGauge(value) : Mathf.Clamp(value, 0f, 100f);
        }
    }
}
