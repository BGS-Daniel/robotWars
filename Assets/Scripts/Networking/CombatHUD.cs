using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    [System.Serializable]
    public class PlayerSlot
    {
        public GameObject row;
        public TextMeshProUGUI label;
        public Image hpFill;
        public Image gaugeFill;
    }

    public class CombatHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI banner;
        [SerializeField] private PlayerSlot[] slots;

        [Header("Player Colors")]
        [Tooltip("Label color for each player slot, by client id.")]
        [SerializeField] private Color[] playerColors =
        {
            new Color(0.35f, 0.6f, 1f),
            new Color(1f, 0.35f, 0.3f),
            new Color(0.4f, 0.9f, 0.4f),
            new Color(1f, 0.8f, 0.3f)
        };

        [Header("Knockback Gauge Colors")]
        [SerializeField] private Color gaugeLowColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color gaugeHighColor = new Color(1f, 0.6f, 0.1f, 1f);
        [SerializeField] private Color gaugeMaxColor = new Color(0.9f, 0.15f, 0.12f, 1f);

        private readonly Dictionary<ulong, RoombaHealth> _bound = new Dictionary<ulong, RoombaHealth>();
        private Coroutine _bannerRoutine;

        private void Start()
        {
            if (banner != null)
                banner.gameObject.SetActive(false);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || slots[i].row == null) continue;
                slots[i].row.SetActive(false);
            }
        }

        private void Update()
        {
            SyncPlayers();
        }

        private void SyncPlayers()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;

            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (_bound.ContainsKey(client.ClientId)) continue;

                var health = client.PlayerObject.GetComponent<RoombaHealth>();
                if (health == null) continue;

                Bind(client.ClientId, health);
            }
        }

        private void Bind(ulong clientId, RoombaHealth health)
        {
            _bound[clientId] = health;

            // Map each client to a fixed slot by its client id (host=0, next=1, ...).
            int slotIndex = (int)clientId;
            if (slotIndex < 0 || slotIndex >= slots.Length) return;

            var slot = slots[slotIndex];
            if (slot == null || slot.row == null) return;

            slot.row.SetActive(true);

            Color color = playerColors[slotIndex % playerColors.Length];
            if (slot.label != null)
            {
                slot.label.text = "Player " + (clientId + 1);
                slot.label.color = color;
            }

            SetHp(slot, health.CurrentHP, health.MaxHP);
            SetGauge(slot, health.Gauge, health.GaugeMax);

            health.HpChanged += (hp, max) => SetHp(slot, hp, max);
            health.GaugeChanged += (gauge, max) => SetGauge(slot, gauge, max);
            health.EliminatedClient += cause => ShowElimination(clientId, cause);
        }

        private void SetHp(PlayerSlot slot, float hp, float max)
        {
            if (slot.hpFill == null) return;
            slot.hpFill.fillAmount = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
        }

        private void SetGauge(PlayerSlot slot, float gauge, float max)
        {
            if (slot.gaugeFill == null) return;
            float frac = max > 0f ? Mathf.Clamp01(gauge / max) : 0f;
            slot.gaugeFill.fillAmount = frac;

            if (frac >= 1f) slot.gaugeFill.color = gaugeMaxColor;
            else if (frac >= 0.5f) slot.gaugeFill.color = gaugeHighColor;
            else slot.gaugeFill.color = gaugeLowColor;
        }

        private void ShowElimination(ulong clientId, EliminationCause cause)
        {
            string text = "Player " + (clientId + 1) + " Eliminated: " + CauseText(cause);
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(ShowBanner(text));
        }

        private IEnumerator ShowBanner(string text)
        {
            if (banner != null)
            {
                banner.gameObject.SetActive(true);
                banner.text = text;
            }
            yield return new WaitForSeconds(2f);
            if (banner != null)
                banner.gameObject.SetActive(false);
            _bannerRoutine = null;
        }

        private static string CauseText(EliminationCause cause)
        {
            switch (cause)
            {
                case EliminationCause.RingOut: return "Ring Out";
                case EliminationCause.WallSlam: return "Wall Slam";
                case EliminationCause.ArenaHazard: return "Arena Hazard";
                default: return "Destroyed";
            }
        }
    }
}
