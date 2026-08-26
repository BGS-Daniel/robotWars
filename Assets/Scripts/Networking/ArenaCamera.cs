using UnityEngine;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class ArenaCamera : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private float followHeight = 5f;
        [SerializeField] private float followDistance = 10f;

        [Header("Movement")]
        [SerializeField] private float catchUpSpeed = 20f;
        [SerializeField] private float lookAtSmoothing = 6f;
        [SerializeField] private float respawnDelay = 1f;

        private Transform _followTarget;
        private RobotDrive _drive;
        private RoombaHealth _health;
        private bool _wasEliminated;
        private float _respawnTimer;

        private void LateUpdate()
        {
            if (_followTarget == null)
                TryFindPlayer();

            if (_followTarget == null) return;

            bool inKnockback = _drive != null && _drive.IsInKnockback;
            bool eliminated = _health != null && _health.IsEliminated;
            bool justRespawned = _wasEliminated && !eliminated;

            if (justRespawned)
                _respawnTimer = respawnDelay;

            if (_respawnTimer > 0f)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    Vector3 dp = _followTarget.position
                        + Vector3.up * followHeight
                        - _followTarget.forward * followDistance;
                    transform.position = dp;
                    Vector3 ld = _followTarget.position + Vector3.up * 1.5f;
                    transform.rotation = Quaternion.LookRotation(ld - transform.position);
                }
            }
            else if (eliminated)
            {
                // Fully frozen.
            }
            else
            {
                Vector3 desiredPos = _followTarget.position
                    + Vector3.up * followHeight
                    - _followTarget.forward * followDistance;

                if (inKnockback)
                {
                    Vector3 delta = desiredPos - transform.position;
                    transform.position += delta * 0.05f;
                }
                else
                {
                    Vector3 delta = desiredPos - transform.position;
                    float maxStep = catchUpSpeed * Time.deltaTime;
                    if (delta.sqrMagnitude > maxStep * maxStep)
                        delta = delta.normalized * maxStep;
                    transform.position += delta;
                }

                Vector3 ld = _followTarget.position + Vector3.up * 1.5f;
                Quaternion targetRot = Quaternion.LookRotation(ld - transform.position);
                float t = inKnockback ? 2f * Time.deltaTime : lookAtSmoothing * Time.deltaTime;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
            }

            _wasEliminated = eliminated;
        }

        private void TryFindPlayer()
        {
            var players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            foreach (var p in players)
            {
                if (p.IsOwner)
                {
                    _followTarget = p.transform;
                    _drive = p.GetComponent<RobotDrive>();
                    _health = p.GetComponent<RoombaHealth>();
                    return;
                }
            }
        }
    }
}
