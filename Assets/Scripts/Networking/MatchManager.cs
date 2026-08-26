using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RobotWars.Networking
{
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Match")]
        [Tooltip("Round Wins needed to become the Roomba King.")]
        [SerializeField] private int winsToWin = 3;

        [Tooltip("Arena scenes cycled through between rounds.")]
        [SerializeField] private string[] arenaScenes = new string[] { "SampleScene" };

        [Header("Round")]
        [SerializeField] private float countdownSeconds = 3f;
        [SerializeField] private float roundEndDelay = 3f;

        private readonly NetworkVariable<int> _roundNumber = new NetworkVariable<int>(0);
        private readonly NetworkVariable<bool> _roundActive = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _matchOver = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<int> _championClientId = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> _countdownLeft = new NetworkVariable<int>(0);
        private readonly NetworkList<int> _roundWins = new NetworkList<int>();

        // Fired everywhere (server + clients).
        public event Action<int> CountdownChanged;
        public event Action RoundStarted;
        public event Action<int> RoundEnded;         // winner clientId, -1 = draw
        public event Action<int> MatchEnded;         // champion clientId

        public bool IsRoundActive => _roundActive.Value;
        public int RoundNumber => _roundNumber.Value;
        public int WinsToWin => winsToWin;
        public bool IsMatchOver => _matchOver.Value;
        public int ChampionClientId => _championClientId.Value;
        public int CountdownLeft => _countdownLeft.Value;

        private Coroutine _roundFlow;
        private bool _awaitingPlayers = true;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;

            // Wait for the arena's SpawnManager to place players before round 1.
            _awaitingPlayers = true;

            if (IsServer)
                _roundWins.Clear(); // fresh match

            _countdownLeft.OnValueChanged += (prev, next) => CountdownChanged?.Invoke(next);
            _roundActive.OnValueChanged += (prev, next) => { if (next) RoundStarted?.Invoke(); };
            _matchOver.OnValueChanged += (prev, next) =>
            {
                if (next) MatchEnded?.Invoke(_championClientId.Value);
            };
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        // Server-only: start the round flow once the arena's SpawnManager has
        // placed the players. The arena scene signals readiness via
        // NotifyPlayersPlaced(), so we never start a round before the new map
        // is loaded and players are at their spawn points.
        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (_matchOver.Value) return;
            if (_roundFlow != null) return;
            if (_awaitingPlayers) return;
            if (ActiveRoombaCount() == 0) return;

            _roundFlow = StartCoroutine(RoundFlow());
        }

        // Called by the arena scene's SpawnManager after players are placed.
        public void NotifyPlayersPlaced()
        {
            if (!IsServer) return;
            _awaitingPlayers = false;
        }

        private IEnumerator RoundFlow()
        {
            // Countdown before players gain control.
            _roundActive.Value = false;
            for (int i = Mathf.CeilToInt(countdownSeconds); i > 0; i--)
            {
                _countdownLeft.Value = i;
                yield return new WaitForSeconds(1f);
            }
            _countdownLeft.Value = 0;
            _roundActive.Value = true;

            // Wait until one or zero players remain.
            // Solo test mode: if only one player is connected, never end the
            // round automatically — let the player drive around freely.
            int totalPlayers = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ConnectedClientsList.Count : 0;
            int alive;
            do
            {
                yield return new WaitForSeconds(0.25f);
                alive = ActiveRoombaCount();
            } while (alive > 1 || totalPlayers <= 1);

            // Round over.
            _roundActive.Value = false;
            yield return new WaitForSeconds(roundEndDelay);

            if (alive == 1)
                AwardWin(LastStandingClientId());
            else
                RoundEnded?.Invoke(-1); // draw

            _roundNumber.Value++;
            _roundFlow = null;

            if (_matchOver.Value) yield break;

            // Next round: load the arena first, then wait for it to place us.
            _awaitingPlayers = true;
            yield return new WaitForSeconds(1f);
            LoadNextArena();
        }

        private void AwardWin(ulong winnerClientId)
        {
            int index = (int)winnerClientId;
            while (_roundWins.Count <= index) _roundWins.Add(0);
            _roundWins[index] = _roundWins[index] + 1;
            RoundEnded?.Invoke((int)winnerClientId);

            if (_roundWins[index] >= winsToWin)
            {
                _matchOver.Value = true;
                _championClientId.Value = index;
            }
        }

        private void LoadNextArena()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SceneManager == null || arenaScenes == null || arenaScenes.Length == 0)
                return;

            string scene = arenaScenes[_roundNumber.Value % arenaScenes.Length];
            nm.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        // Server-only helpers.

        private int ActiveRoombaCount()
        {
            int count = 0;
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;
            foreach (var client in nm.ConnectedClients)
            {
                if (client.Value.PlayerObject == null) continue;
                var health = client.Value.PlayerObject.GetComponent<RobotWars.Combat.RoombaHealth>();
                if (health == null || health.IsEliminated) continue;
                count++;
            }
            return count;
        }

        private ulong LastStandingClientId()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;
            foreach (var client in nm.ConnectedClients)
            {
                if (client.Value.PlayerObject == null) continue;
                var health = client.Value.PlayerObject.GetComponent<RobotWars.Combat.RoombaHealth>();
                if (health != null && !health.IsEliminated)
                    return client.Key;
            }
            return 0;
        }

        public int GetRoundWins(ulong clientId)
        {
            int index = (int)clientId;
            return index >= 0 && index < _roundWins.Count ? _roundWins[index] : 0;
        }

        // Server-only: reset for a fresh match (Play Again).
        public void ResetMatch()
        {
            if (!IsServer) return;
            _matchOver.Value = false;
            _championClientId.Value = -1;
            _roundNumber.Value = 0;
            _roundWins.Clear();
        }

        public void PlayAgain()
        {
            if (!IsServer) return;
            ResetMatch();
            LoadNextArena();
        }

        public void ReturnToLobby()
        {
            if (!IsServer) return;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
                nm.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
            // Despawn so a fresh match starts cleanly next time instead of
            // reusing this manager's matchOver=true state.
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}
