using UnityEngine;
using Unity.Netcode;
using Unity.Services.Authentication;

namespace RobotWars.Networking
{
    public class NetworkRunner : MonoBehaviour
    {
        public static NetworkRunner Instance { get; private set; }

        public event System.Action HostDisconnected;
        public event System.Action<ulong> ClientDisconnected;

        public bool IsConnected => NetworkManager.Singleton != null &&
                                   (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Keep the network session alive across scene loads (lobby -> game).
            if (NetworkManager.Singleton != null)
                DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        public bool StartHost()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;

            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = OnConnectionApproval;

            bool ok = nm.StartHost();
            if (ok && LobbyManager.Instance != null &&
                AuthenticationService.Instance != null &&
                AuthenticationService.Instance.IsSignedIn)
            {
                // The host never runs OnConnectionApproval, so register its own
                // clientId -> playerId mapping here (used to look up lobby colour).
                LobbyManager.Instance.RegisterPlayerId(
                    Unity.Netcode.NetworkManager.ServerClientId,
                    AuthenticationService.Instance.PlayerId);
            }
            return ok;
        }

        public bool StartClient()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;

            // Approval must be enabled on both sides for ConnectionData to reach
            // the host, which reads it to learn this client's auth playerId.
            nm.NetworkConfig.ConnectionApproval = true;

            string playerId = AuthenticationService.Instance != null &&
                              AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : "";
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(playerId);

            return nm.StartClient();
        }

        public void Shutdown()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.Shutdown();
        }

        private void OnConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = true;

            string playerId = request.Payload != null
                ? System.Text.Encoding.UTF8.GetString(request.Payload)
                : "";
            if (!string.IsNullOrEmpty(playerId) && LobbyManager.Instance != null)
                LobbyManager.Instance.RegisterPlayerId(request.ClientNetworkId, playerId);
        }

        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            // On a non-server this is the loss of the host; on the host, a client dropping.
            if (nm != null && nm.IsServer)
                ClientDisconnected?.Invoke(clientId);
            else
                HostDisconnected?.Invoke();
        }
    }
}
