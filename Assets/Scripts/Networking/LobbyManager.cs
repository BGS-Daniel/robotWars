using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace RobotWars.Networking
{
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        public delegate void LobbyChangedHandler(Lobby lobby);
        public event LobbyChangedHandler LobbyUpdated;

        private const float SyncInterval = 5f;
        private const float HeartbeatInterval = 15f;

        private Lobby _lobby;
        private Coroutine _syncLoop;
        private bool _shouldSync;

        public Lobby CurrentLobby => _lobby;
        public bool InLobby => _lobby != null;
        public bool IsHost => _lobby != null && _lobby.HostId == AuthenticationService.Instance.PlayerId;
        public string LobbyCode => _lobby?.LobbyCode;

        private List<Lobby> _cachedPublicLobbies = new List<Lobby>();
        public List<Lobby> CachedPublicLobbies => _cachedPublicLobbies;

        // NGO clientId -> Authentication playerId, so the host can remove a member who disconnected.
        private readonly Dictionary<ulong, string> _clientPlayerIds = new Dictionary<ulong, string>();

        public void RegisterPlayerId(ulong clientId, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _clientPlayerIds[clientId] = playerId;
        }

        public void UnregisterPlayerId(ulong clientId)
        {
            _clientPlayerIds.Remove(clientId);
        }

        public string GetPlayerIdForClient(ulong clientId)
        {
            return _clientPlayerIds.TryGetValue(clientId, out string p) ? p : null;
        }

        public async Task RemoveSelfAsync()
        {
            if (_lobby == null || IsHost) return;

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(_lobby.Id, AuthenticationService.Instance.PlayerId);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Lobby] remove self: " + e.Message);
            }
        }

        public async Task RemovePlayerByIdAsync(string playerId)
        {
            if (_lobby == null || string.IsNullOrEmpty(playerId)) return;
            if (playerId == AuthenticationService.Instance.PlayerId) return; // never self-remove

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(_lobby.Id, playerId);
                Debug.Log("[LobbyManager] Removed disconnected player " + playerId);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LobbyManager] disconnect-remove FAILED for " + playerId + ": " + e.Message);
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public async Task<bool> CreateLobbyAsync(string lobbyName, int maxPlayers, string relayJoinCode)
        {
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                IsLocked = false,
                Player = new Player { Data = MakePlayerData() },
                Data = new Dictionary<string, DataObject>
                {
                    { RelayCodeKey, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };

            _lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            if (_lobby != null) StartSync();
            return _lobby != null;
        }

        public async Task<bool> JoinLobbyByCodeAsync(string joinCode)
        {
            var options = new JoinLobbyByCodeOptions { Player = new Player { Data = MakePlayerData() } };

            _lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode.Trim().ToUpper(), options);
            return await FinishJoinAsync();
        }

        public async Task<bool> JoinLobbyByIdAsync(string lobbyId)
        {
            var options = new JoinLobbyByIdOptions { Player = new Player { Data = MakePlayerData() } };

            _lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
            return await FinishJoinAsync();
        }

        private async Task<bool> FinishJoinAsync()
        {
            if (_lobby == null) return false;

            if (!_lobby.Data.TryGetValue(RelayCodeKey, out DataObject relayData) ||
                string.IsNullOrEmpty(relayData?.Value))
            {
                _lobby = null;
                return false;
            }

            await RelayManager.Instance.JoinRelayAsync(relayData.Value);
            StartSync();
            return true;
        }

        public async Task RefreshLobbiesAsync()
        {
            var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 12 });
            _cachedPublicLobbies = response.Results;
        }

        public async void LeaveLobby()
        {
            StopSync();
            if (_lobby == null) return;

            try
            {
                if (IsHost)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_lobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(_lobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Lobby] leave error: " + e.Message);
            }

            _lobby = null;
            LobbyUpdated?.Invoke(null);
        }

        public void ForceLeaveLocal()
        {
            StopSync();
            _lobby = null;
            LobbyUpdated?.Invoke(null);
        }

        private const string RelayCodeKey = "RelayJoinCode";

        private static Dictionary<string, PlayerDataObject> MakePlayerData()
        {
            return new Dictionary<string, PlayerDataObject>
            {
                { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, LocalPlayer.Name) }
            };
        }

        private void StartSync()
        {
            if (_shouldSync) return;
            _shouldSync = true;
            _syncLoop = StartCoroutine(SyncLoop());
        }

        private void StopSync()
        {
            _shouldSync = false;
            if (_syncLoop != null)
            {
                StopCoroutine(_syncLoop);
                _syncLoop = null;
            }
        }

        private IEnumerator SyncLoop()
        {
            while (_shouldSync && _lobby != null)
            {
                yield return new WaitForSecondsRealtime(SyncInterval);

                if (IsHost)
                    yield return HeartbeatThenRefresh();
                else
                    yield return RefreshOnly();
            }
        }

        private IEnumerator HeartbeatThenRefresh()
        {
            var ping = LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            yield return new WaitUntil(() => ping.IsCompleted);
            if (ping.IsFaulted) Debug.LogWarning("[Lobby] heartbeat: " + ping.Exception?.Message);
            yield return RefreshOnly();
        }

        private IEnumerator RefreshOnly()
        {
            var task = LobbyService.Instance.GetLobbyAsync(_lobby.Id);
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.IsFaulted)
            {
                Debug.LogWarning("[Lobby] refresh: " + task.Exception?.Message);
                yield break;
            }

            _lobby = task.Result;
            LobbyUpdated?.Invoke(_lobby);
        }
    }
}