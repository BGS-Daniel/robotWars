using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;

namespace RobotWars.Combat
{
    // Reference-equality for GameObjects so dictionary keys survive instance ID reuse.
    internal class GameObjectPairComparer : IEqualityComparer<(GameObject, GameObject)>
    {
        public static readonly GameObjectPairComparer Instance = new GameObjectPairComparer();

        public bool Equals((GameObject, GameObject) a, (GameObject, GameObject) b)
        {
            return ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2);
        }

        public int GetHashCode((GameObject, GameObject) pair)
        {
            unchecked
            {
                int h1 = RuntimeHelpers.GetHashCode(pair.Item1);
                int h2 = RuntimeHelpers.GetHashCode(pair.Item2);
                return (h1 * 397) ^ h2;
            }
        }
    }

    public struct ImpactRequest
    {
        public ImpactSource source;
        public GameObject attacker;
        public Rigidbody targetBody;
        public RoombaHealth targetHealth;
        public Vector3 contactPoint;
        public Vector3 attackerVelocityAtContact;
    }

    public struct ImpactResult
    {
        public bool valid;
        public float relativeSpeed;
        public float impactStrength;
        public float impactMultiplier;
        public float finalDamage;
        public float gaugeGain;
        public float finalKnockback;
        public Vector3 impactDirection;
    }

    public static class CombatResolver
    {
        // Impact Cooldown per source-target pair so a resting weapon can't deal
        // damage every physics frame.
        private static readonly Dictionary<(GameObject, GameObject), float> _lastHitTimes =
            new Dictionary<(GameObject, GameObject), float>(GameObjectPairComparer.Instance);

        // Attacker velocity at a point: body motion plus optional spin velocity.
        public static Vector3 BodyPointVelocity(Rigidbody body, Vector3 contactPoint)
        {
            return body != null ? body.GetPointVelocity(contactPoint) : Vector3.zero;
        }

        // Tangential velocity from a rotating part (e.g. spinning blade/hammer arm).
        public static Vector3 SpinPointVelocity(Transform pivot, Vector3 spinAxis, float spinRadPerSec, Vector3 contactPoint)
        {
            if (pivot == null || spinAxis.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector3 toContact = contactPoint - pivot.position;
            return Vector3.Cross(spinAxis.normalized, toContact) * spinRadPerSec;
        }

        public static ImpactResult Resolve(ImpactRequest request)
        {
            var result = default(ImpactResult);
            if (request.source == null) return result;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return result;
            if (request.targetHealth == null || request.targetHealth.IsEliminated) return result;
            if (request.targetBody == null) return result;

            Vector3 targetVelocity = request.targetBody.GetPointVelocity(request.contactPoint);
            Vector3 relativeVelocity = request.attackerVelocityAtContact - targetVelocity;
            float relativeSpeed = relativeVelocity.magnitude;

            // Validate impact.
            if (relativeSpeed < request.source.minImpactSpeed) return result;
            if (!IsCooldownReady(request)) return result;

            // Impact Strength = relative speed * impact mass * machinery power.
            float impactStrength = request.source.ComputeImpactStrength(relativeSpeed);
            float reference = request.targetHealth.Settings != null
                ? request.targetHealth.Settings.referenceImpactStrength
                : 30f;
            float impactMultiplier = reference > 0f ? impactStrength / reference : 1f;
            if (request.targetHealth.Settings != null)
                impactMultiplier = request.targetHealth.Settings.ClampImpactMultiplier(impactMultiplier);

            float finalDamage = request.source.baseDamage * impactMultiplier * request.source.damageMultiplier;

            float gainMultiplier = request.source.baseGaugeIncrease;
            if (gainMultiplier <= 0f && request.targetHealth.Settings != null)
                gainMultiplier = request.targetHealth.Settings.gaugeGainMultiplier;
            float gaugeGain = finalDamage * gainMultiplier;

            result.valid = true;
            result.relativeSpeed = relativeSpeed;
            result.impactStrength = impactStrength;
            result.impactMultiplier = impactMultiplier;
            result.finalDamage = finalDamage;
            result.gaugeGain = gaugeGain;
            result.impactDirection = ComputeImpactDirection(request);

            // Damage first (raises the gauge), so this hit contributes to its own knockback.
            request.targetHealth.ApplyDamage(finalDamage, gaugeGain);

            float gaugeMultiplier = request.targetHealth.EvaluateGaugeMultiplier();
            result.finalKnockback = request.source.baseKnockback * impactMultiplier
                                    * request.source.knockbackMultiplier * gaugeMultiplier;

            RegisterHit(request);
            ApplyKnockback(request, result);
            request.targetHealth.NotifyImpactFx(request.contactPoint);

            return result;
        }

        private static Vector3 ComputeImpactDirection(ImpactRequest request)
        {
            Vector3 dir = request.attackerVelocityAtContact;
            if (dir.sqrMagnitude < 0.0001f)
                dir = request.targetBody.position - request.contactPoint;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            return dir.normalized;
        }

        private static void ApplyKnockback(ImpactRequest request, ImpactResult result)
        {
            if (result.finalKnockback <= 0f) return;

            // Horizontal push from the impact direction, plus an upward fraction
            // so the Roomba lifts off the ground even at low gauge.
            Vector3 impulse = result.impactDirection * result.finalKnockback * request.targetBody.mass;
            float upward = request.targetHealth.Settings != null
                ? request.targetHealth.Settings.knockbackUpwardRatio
                : 0.5f;
            impulse.y += result.finalKnockback * upward * request.targetBody.mass;

            var drive = request.targetBody.GetComponent<RobotWars.Networking.RobotDrive>();
            if (drive != null)
                drive.ApplyKnockback(impulse, request.contactPoint);
            else
                request.targetBody.AddForceAtPosition(impulse, request.contactPoint, ForceMode.Impulse);
        }

        private static bool IsCooldownReady(ImpactRequest request)
        {
            if (request.attacker == null || request.targetHealth == null) return true;
            if (!_lastHitTimes.TryGetValue((request.attacker, request.targetHealth.gameObject), out float last))
                return true;
            return Time.time - last >= request.source.impactCooldown;
        }

        private static void RegisterHit(ImpactRequest request)
        {
            if (request.attacker == null || request.targetHealth == null) return;
            _lastHitTimes[(request.attacker, request.targetHealth.gameObject)] = Time.time;
        }
    }
}
