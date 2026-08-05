using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace RobotWars.Networking
{
    public class Bootstrapper : MonoBehaviour
    {
        public static Bootstrapper Instance { get; private set; }

        [SerializeField] private string presetProfile;
        [Tooltip("Auto-assign a unique identity per running process (required for host + join on the same PC). Disable to share a fixed device identity.")]
        [SerializeField] private bool autoUniqueProfile = true;

        public bool Initialized { get; private set; }
        public string Error { get; private set; }

        public string ActiveProfile => presetProfile;

        public string PresetProfile
        {
            set => presetProfile = value;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Give each running instance its own identity so two instances on the
            // same PC (host + client) are seen as different players by Lobby/Relay.
            if (autoUniqueProfile && string.IsNullOrEmpty(presetProfile))
                presetProfile = "local-" + Process.GetCurrentProcess().Id;
        }

        public async Task<bool> EnsureInitializedAsync()
        {
            if (Initialized) return true;

            try
            {
                await UnityServices.InitializeAsync();

                if (!string.IsNullOrEmpty(presetProfile))
                    AuthenticationService.Instance.SwitchProfile(presetProfile);

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                Initialized = true;
                Error = null;
                UnityEngine.Debug.Log($"[Services] Ready. Profile: {AuthenticationService.Instance.Profile}  PlayerId: {AuthenticationService.Instance.PlayerId}");
                return true;
            }
            catch (System.Exception e)
            {
                Initialized = false;
                var detail = e;
                while (detail.InnerException != null) detail = detail.InnerException;
                Error = detail.Message;
                UnityEngine.Debug.LogError("[Services] Initialization failed: " + detail.Message +
                    "\nInnerException chain: " + e + "\n" + e.StackTrace);
                return false;
            }
        }
    }
}
