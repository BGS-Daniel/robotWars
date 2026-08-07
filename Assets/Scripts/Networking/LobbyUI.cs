using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RobotWars.Networking
{
    public class LobbyUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject lobbyPanel;

        [Header("Menu")]
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI lobbyListText;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button refreshButton;

        [Header("Lobby list (dynamic rows)")]
        [SerializeField] private Button lobbyRowPrefab;
        [SerializeField] private RectTransform lobbyListContainer;

        [Header("Lobby")]
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private TextMeshProUGUI playerListText;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button startButton;

        private const string GameSceneName = "SampleScene";

        private bool _busy;
        private bool _intentionalLeave;
        private bool _quitConfirmed;
        private System.Collections.Generic.List<Unity.Services.Lobbies.Models.Lobby> _lastPublic;
        private readonly System.Collections.Generic.List<Button> _spawnedRows =
            new System.Collections.Generic.List<Button>();

        private void Awake()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        private void Start()
        {
            if (playerNameInput != null)
                playerNameInput.text = LocalPlayer.Name;

            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.HostDisconnected += OnHostDisconnected;

            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.ClientDisconnected += OnClientDisconnected;

            LobbyManager.Instance.LobbyUpdated += OnLobbyUpdated;
            RenderFromLobby(LobbyManager.Instance.CurrentLobby);
            UpdateButtons();
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;

            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.HostDisconnected -= OnHostDisconnected;

            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.ClientDisconnected -= OnClientDisconnected;

            if (LobbyManager.Instance != null)
                LobbyManager.Instance.LobbyUpdated -= OnLobbyUpdated;
        }

        private bool OnWantsToQuit()
        {
            if (_quitConfirmed) return true; // allow the real quit
            _intentionalLeave = true; // suppress the "host left" UI path
            StartCoroutine(QuitAfterCleanup());
            return false; // cancel this first quit
        }

        private System.Collections.IEnumerator QuitAfterCleanup()
        {
            if (LobbyManager.Instance != null && LobbyManager.Instance.InLobby)
            {
                if (LobbyManager.Instance.IsHost)
                {
                    LobbyManager.Instance.LeaveLobby(); // deletes the lobby
                }
                else
                {
                    // Wait ~5s for the remote RemovePlayerAsync so the host no longer lists us.
                    var task = LobbyManager.Instance.RemoveSelfAsync();
                    float waited = 0f;
                    while (!task.IsCompleted && waited < 5f)
                    {
                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }

            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.Shutdown();

            yield return null;
            _quitConfirmed = true;
            Application.Quit();
        }

        private void OnHostDisconnected()
        {
            if (_intentionalLeave) return;

            _intentionalLeave = true;
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.ForceLeaveLocal();
            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.Shutdown();
            SetBusy(false);
            ShowMenuView();
            if (lobbyListText != null) { lobbyListText.text = ""; lobbyListText.gameObject.SetActive(true); }
            SetStatus("The host left or closed the lobby. You returned to the main menu.");
            _intentionalLeave = false;
        }

        private async void OnClientDisconnected(ulong clientId)
        {
            if (_intentionalLeave) return;
            if (LobbyManager.Instance == null || !LobbyManager.Instance.IsHost) return;

            string playerId = LobbyManager.Instance.GetPlayerIdForClient(clientId);

            Debug.Log("[LobbyUI] OnClientDisconnected host=" + LobbyManager.Instance.IsHost +
                      " clientId=" + clientId + " playerId=" + (playerId ?? "<null>"));

            LobbyManager.Instance.UnregisterPlayerId(clientId);
            if (string.IsNullOrEmpty(playerId)) return;

            await LobbyManager.Instance.RemovePlayerByIdAsync(playerId);
        }

        #region Public button handlers

        public async void OnHostPressed()
        {
            SaveName();
            SetBusy(true);
            SetStatus("Signing in & allocating relay...");
            try
            {
                if (!await Bootstrapper.Instance.EnsureInitializedAsync())
                {
                    SetStatus("Setup needed: " + Bootstrapper.Instance.Error +
                              "\nLink your project in Window > General > Services and enable Authentication, Lobby and Relay.", true);
                    return;
                }

                string relayCode = await RelayManager.Instance.AllocateRelayAsync(4);
                SetStatus("Creating lobby...");

                bool created = await LobbyManager.Instance.CreateLobbyAsync(
                    LocalPlayer.Name + "'s Arena", 4, relayCode);

                if (!created)
                {
                    SetStatus("Could not create lobby.", true);
                    return;
                }

                NetworkRunner.Instance.StartHost();
                ShowLobbyView();
                SetStatus("Lobby created. Share the code so friends can join.");
            }
            catch (System.Exception e)
            {
                SetStatus("Host error: " + e.Message, true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async void OnJoinByCodePressed()
        {
            string code = joinCodeInput != null ? joinCodeInput.text.Trim() : "";
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Type a lobby code to join.", true);
                return;
            }

            await JoinByCodeAsync(code);
        }

        public async void OnLobbyRow(int index)
        {
            if (_lastPublic == null || index < 0 || index >= _lastPublic.Count)
                return;
            var l = _lastPublic[index];
            if (l == null) return;
            await JoinByIdAsync(l.Id);
        }

        private async System.Threading.Tasks.Task JoinByIdAsync(string id)
        {
            SaveName();

            if (LobbyManager.Instance != null && LobbyManager.Instance.InLobby &&
                LobbyManager.Instance.CurrentLobby != null &&
                string.Equals(LobbyManager.Instance.CurrentLobby.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("You are already in this lobby.", true);
                return;
            }

            SetBusy(true);
            SetStatus("Joining lobby...");
            try
            {
                if (!await Bootstrapper.Instance.EnsureInitializedAsync())
                {
                    SetStatus("Setup needed: " + Bootstrapper.Instance.Error +
                              "\nLink your project in Window > General > Services and enable Authentication, Lobby and Relay.", true);
                    return;
                }

                bool joined = await LobbyManager.Instance.JoinLobbyByIdAsync(id);
                if (!joined)
                {
                    SetStatus("Lobby not found, or it has no relay code.", true);
                    return;
                }

                NetworkRunner.Instance.StartClient();
                ShowLobbyView();
                if (joinCodeInput != null) joinCodeInput.text = "";
                SetStatus("Joined. Waiting for host...");
            }
            catch (System.Exception e)
            {
                SetStatus("Join error: " + e.Message, true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task JoinByCodeAsync(string code)
        {
            SaveName();
            code = code == null ? "" : code.Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Type a lobby code to join.", true);
                return;
            }

            // Can't join a lobby you are already in (e.g. the host clicking their own row).
            if (LobbyManager.Instance != null && LobbyManager.Instance.InLobby &&
                LobbyManager.Instance.LobbyCode != null &&
                string.Equals(LobbyManager.Instance.LobbyCode, code.ToUpper(), System.StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("You are already in this lobby.", true);
                return;
            }

            SetBusy(true);
            SetStatus("Signing in & joining lobby " + code.ToUpper() + "...");
            try
            {
                if (!await Bootstrapper.Instance.EnsureInitializedAsync())
                {
                    SetStatus("Setup needed: " + Bootstrapper.Instance.Error +
                              "\nLink your project in Window > General > Services and enable Authentication, Lobby and Relay.", true);
                    return;
                }

                bool joined = await LobbyManager.Instance.JoinLobbyByCodeAsync(code);
                if (!joined)
                {
                    SetStatus("Lobby not found, or it has no relay code.", true);
                    return;
                }

                NetworkRunner.Instance.StartClient();
                ShowLobbyView();
                if (joinCodeInput != null) joinCodeInput.text = "";
                SetStatus("Joined. Waiting for host...");
            }
            catch (System.Exception e)
            {
                SetStatus("Join error: " + e.Message, true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async void OnRefreshLobbiesPressed()
        {
            try
            {
                if (!await Bootstrapper.Instance.EnsureInitializedAsync())
                {
                    SetStatus("Setup needed: " + Bootstrapper.Instance.Error, true);
                    return;
                }
                await LobbyManager.Instance.RefreshLobbiesAsync();
                RenderLobbyList(LobbyManager.Instance.CachedPublicLobbies);
                SetStatus(lobbyListText != null && LobbyManager.Instance.CachedPublicLobbies.Count == 0
                    ? "No public lobbies found. Start one by Hosting!"
                    : "Click a lobby below to join.");
            }
            catch (System.Exception e)
            {
                SetStatus("Refresh error: " + e.Message, true);
            }
        }

        private void RenderLobbyList(System.Collections.Generic.List<Unity.Services.Lobbies.Models.Lobby> list)
        {
            _lastPublic = list;
            ClearSpawnedRows();

            int n = list == null ? 0 : list.Count;

            if (n > 0 && lobbyRowPrefab != null && lobbyListContainer != null)
            {
                for (int i = 0; i < n; i++)
                {
                    var l = list[i];
                    if (l == null) continue;

                    var row = Instantiate(lobbyRowPrefab, lobbyListContainer);
                    var rt = (UnityEngine.RectTransform)row.transform;
                    rt.anchoredPosition = new Vector2(0, -i * 52f);

                    var lbl = row.GetComponentInChildren<TMP_Text>();
                    if (lbl != null)
                        lbl.text = l.Name + "   (" + l.Players.Count + "/" + l.MaxPlayers + ")";

                    int index = i;
                    row.onClick.AddListener(() => OnLobbyRow(index));
                    _spawnedRows.Add(row);
                }
            }

            if (lobbyListText != null)
                lobbyListText.gameObject.SetActive(n == 0);
        }

        private void ClearSpawnedRows()
        {
            foreach (var r in _spawnedRows)
                if (r != null) Destroy(r.gameObject);
            _spawnedRows.Clear();
        }

        public void OnLeavePressed()
        {
            _intentionalLeave = true;
            SetBusy(true);
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.LeaveLobby();
            if (NetworkRunner.Instance != null)
                NetworkRunner.Instance.Shutdown();
            SetBusy(false);
            ShowMenuView();
            if (lobbyListText != null) { lobbyListText.text = ""; lobbyListText.gameObject.SetActive(true); }
            SetStatus("Left the lobby.");
            _intentionalLeave = false;
        }

        public void OnStartGamePressed()
        {
            if (_busy) return;
            if (LobbyManager.Instance == null || !LobbyManager.Instance.IsHost)
            {
                SetStatus("Only the host can start the game.", true);
                return;
            }

            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || nm.SceneManager == null || !nm.IsListening)
            {
                SetStatus("Network is not ready to start the game.", true);
                return;
            }

            SetStatus("Starting game...");
            var status = nm.SceneManager.LoadScene(GameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != Unity.Netcode.SceneEventProgressStatus.Started)
                SetStatus("Could not start the game: " + status, true);
        }

        #endregion

        #region View switching / rendering

        private void ShowLobbyView()
        {
            if (menuPanel != null) menuPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
            if (startButton != null)
                startButton.gameObject.SetActive(LobbyManager.Instance != null && LobbyManager.Instance.IsHost);
            RenderFromLobby(LobbyManager.Instance.CurrentLobby);
        }

        private void ShowMenuView()
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (menuPanel != null) menuPanel.SetActive(true);
        }

        private void OnLobbyUpdated(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            if (LobbyManager.Instance != null && LobbyManager.Instance.InLobby)
                RenderFromLobby(lobby);
        }

        private void RenderFromLobby(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            if (lobby == null)
            {
                if (lobbyCodeText != null) lobbyCodeText.text = "------";
                if (playerListText != null) playerListText.text = "";
                return;
            }

            if (lobbyCodeText != null) lobbyCodeText.text = lobby.LobbyCode;

            var sb = new System.Text.StringBuilder();
            foreach (var p in lobby.Players)
            {
                string name = p.Data != null && p.Data.TryGetValue("PlayerName", out var sn)
                    ? sn.Value
                    : (p.Id ?? "?");
                bool host = lobby.HostId == p.Id;
                sb.Append(name);
                if (host) sb.Append("  (Host)");
                sb.Append("\n");
            }
            if (playerListText != null) playerListText.text = sb.ToString();
        }

        private void SaveName()
        {
            string s = playerNameInput != null ? playerNameInput.text : "";
            LocalPlayer.Name = string.IsNullOrWhiteSpace(s) ? "Player" : s.Trim();
            if (playerNameInput != null) playerNameInput.text = LocalPlayer.Name;
        }

        private void SetStatus(string message, bool isError = false)
        {
            if (statusText != null) statusText.text = message;
            if (isError) Debug.LogError("[LobbyUI] " + message);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (hostButton != null) hostButton.interactable = !_busy;
            if (joinButton != null) joinButton.interactable = !_busy;
            if (refreshButton != null) refreshButton.interactable = !_busy;
            if (leaveButton != null) leaveButton.interactable = !_busy;
            if (startButton != null) startButton.interactable = !_busy;
        }

        #endregion
    }
}