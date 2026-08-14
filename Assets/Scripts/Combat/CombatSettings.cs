using UnityEngine;

namespace RobotWars.Combat
{
    [CreateAssetMenu(fileName = "CombatSettings", menuName = "Roomba Wars/Combat Settings", order = 0)]
    public class CombatSettings : ScriptableObject
    {
        [Header("Roomba HP")]
        [Tooltip("Max HP for every Roomba at the start of a Round.")]
        public float maxHP = 100f;

        [Header("Knockback Gauge")]
        [Tooltip("Maximum Knockback Gauge value (%).")]
        public float gaugeMax = 100f;

        [Tooltip("Global Gauge Gain multiplier: GaugeGain = FinalDamage * gaugeGainMultiplier.")]
        public float gaugeGainMultiplier = 0.5f;

        [Tooltip("Maps Knockback Gauge (%) to the incoming Knockback multiplier.")]
        public AnimationCurve gaugeKnockbackCurve = new AnimationCurve(
            new Keyframe(0f, 1.00f),
            new Keyframe(25f, 1.15f),
            new Keyframe(50f, 1.40f),
            new Keyframe(75f, 1.75f),
            new Keyframe(100f, 2.25f));

        [Header("Impact")]
        [Tooltip("Reference Impact Strength that yields an Impact Multiplier of 1.")]
        public float referenceImpactStrength = 10f;

        [Header("Ring Out")]
        [Tooltip("Kill plane height; a Roomba below this Y is eliminated as Ring Out.")]
        public float killPlaneY = -4f;

        [Tooltip("Horizontal arena half-extents; a Roomba outside these is a Ring Out.")]
        public Vector2 arenaHalfExtents = new Vector2(26f, 26f);

        [Header("Wall Impact")]
        [Tooltip("Minimum impact speed (m/s) against a wall for a destructive Wall Slam.")]
        public float minWallImpactSpeed = 8f;

        [Tooltip("Wall Impact Strength required to instantly destroy the Roomba.")]
        public float wallDestructionStrength = 40f;

        [Tooltip("Damage dealt by a weak wall impact (below destruction threshold).")]
        public float wallImpactDamage = 5f;

        public float EvaluateGaugeMultiplier(float gauge)
        {
            if (gaugeKnockbackCurve == null || gaugeKnockbackCurve.length == 0) return 1f;
            return gaugeKnockbackCurve.Evaluate(gauge);
        }

        public float ClampGauge(float value)
        {
            return Mathf.Clamp(value, 0f, gaugeMax);
        }
    }
}
