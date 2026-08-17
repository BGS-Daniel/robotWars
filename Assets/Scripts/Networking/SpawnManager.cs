using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class SpawnManager : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints = new Transform[0];

        [SerializeField] private float spawnHeight = 0.1f;

        [Tooltip("Arena scene names that trigger placement via the load event fallback.")]
        [SerializeField] private string[] arenaSceneNames = new string[] { "SampleScene", "Arena2" };

        private int _placementFrames;
        private bool _awaitingArena;
        private bool _hasPlaced;

        private void Start()
        {
            // This object only exists in the arena scene, so on the server its
            // Start() running means the arena is loaded. OnLoadEventCompleted is
            // not reliably raised (host-only sessions); the event is only a fallback.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                _awaitingArena = true;
                _placementFrames = 2;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        private void FixedUpdate()
        {
            if (!_awaitingArena) return;

            // Let the arena colliders register before dropping robots in,
            // otherwise a rigidbody can tunnel through them.
            if (_placementFrames > 0)
            {
                _placementFrames--;
                if (_placementFrames == 0)
                    PlaceAllPlayers();
            }
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (System.Array.IndexOf(arenaSceneNames, sceneName) < 0) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (_hasPlaced) return;

            _awaitingArena = true;
            _placementFrames = 2;
        }

        private void PlaceAllPlayers()
        {
            _awaitingArena = false;
            _hasPlaced = true;
            if (NetworkManager.Singleton == null) return;

            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                var playerObject = client.Value.PlayerObject;
                if (playerObject == null || spawnPoints.Length == 0) continue;

                int index = (int)(client.Key % (ulong)spawnPoints.Length);
                Vector3 position = spawnPoints[index].position;
                position.y += spawnHeight;

                var networkPlayer = playerObject.GetComponent<NetworkPlayer>();
                if (networkPlayer != null)
                    networkPlayer.PlaceInArena(position);
            }

            // The map is loaded and players are placed: let MatchManager start
            // the countdown now (map first, then spawn/round).
            if (MatchManager.Instance != null)
                MatchManager.Instance.NotifyPlayersPlaced();
        }

        /// Server-only: re-place every player at its spawn point for a fresh round.
        public void ReplaceAllPlayers()
        {
            _hasPlaced = false;
            PlaceAllPlayers();
        }
    }
}
