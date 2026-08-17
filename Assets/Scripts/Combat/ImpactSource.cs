using UnityEngine;

namespace RobotWars.Combat
{
    [CreateAssetMenu(fileName = "ImpactSource", menuName = "Roomba Wars/Impact Source", order = 0)]
    public class ImpactSource : ScriptableObject
    {
        [Header("Valid Impact")]
        [Tooltip("Minimum relative impact speed (m/s) required to deal damage.")]
        public float minImpactSpeed = 2f;

        [Tooltip("Minimum time before this source can damage the same target again.")]
        public float impactCooldown = 0.35f;

        [Header("Base Values")]
        [Tooltip("Damage produced before impact modifiers.")]
        public float baseDamage = 10f;

        [Tooltip("Physical knockback force produced before impact modifiers.")]
        public float baseKnockback = 6f;

        [Tooltip("Knockback Gauge gain per unit of Final Damage for this source.")]
        public float baseGaugeIncrease = 0.5f;

        [Header("Modifiers")]
        [Tooltip("Controls how strongly impact speed affects Damage.")]
        public float damageMultiplier = 1f;

        [Tooltip("Controls how strongly impact speed affects Knockback.")]
        public float knockbackMultiplier = 1f;

        [Header("Impact Mass")]
        [Tooltip("Effective impact mass used by the combat system (independent of Rigidbody mass).")]
        public float impactMass = 1f;

        [Header("Knockback Direction")]
        [Tooltip("Fraction of knockback applied upward. 0 = pure horizontal, 1+ = launches the target up. Overrides the global CombatSettings value when > 0.")]
        public float knockbackUpwardRatio = 0f;

        [Header("Machinery Power")]
        [Tooltip("Multiplier applied when the source is actively powered. Inactive/Disabled = 1.")]
        public float machineryPowerMultiplier = 1f;

        // Active = allowed to deal damage (e.g. weapon powered).
        public bool IsActive = true;

        public float ComputeImpactStrength(float relativeSpeed)
        {
            float power = IsActive ? machineryPowerMultiplier : 1f;
            return relativeSpeed * impactMass * power;
        }
    }
}
