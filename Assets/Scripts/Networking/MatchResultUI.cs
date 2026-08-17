using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWars.Networking
{
    public class MatchResultUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI championText;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button returnToLobbyButton;

        private bool _bound;

        private void Start()
        {
            if (panel != null)
                panel.SetActive(false);

            if (playAgainButton != null)
                playAgainButton.onClick.AddListener(OnPlayAgain);
            if (returnToLobbyButton != null)
                returnToLobbyButton.onClick.AddListener(OnReturnToLobby);
        }

        private void OnDestroy()
        {
            if (_bound && MatchManager.Instance != null)
                MatchManager.Instance.MatchEnded -= OnMatchEnded;
        }

        private void Update()
        {
            var mm = MatchManager.Instance;
            if (mm == null) return;
            if (!_bound)
            {
                _bound = true;
                mm.MatchEnded += OnMatchEnded;
            }

            if (mm.IsMatchOver && panel != null && !panel.activeSelf)
                Show(mm);
        }

        private void OnMatchEnded(int championClientId)
        {
            Show(MatchManager.Instance);
        }

        private void Show(MatchManager mm)
        {
            if (panel == null) return;
            panel.SetActive(true);
            if (championText != null)
                championText.text = "Player " + (mm.ChampionClientId + 1) + " is the Roomba King!";
        }

        private void OnPlayAgain()
        {
            if (panel != null) panel.SetActive(false);
            MatchManager.Instance?.PlayAgain();
        }

        private void OnReturnToLobby()
        {
            if (panel != null) panel.SetActive(false);
            MatchManager.Instance?.ReturnToLobby();
        }
    }
}
