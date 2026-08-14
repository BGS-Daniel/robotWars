using UnityEngine;
using TMPro;
using Unity.Netcode;
using RobotWars.Combat;

namespace RobotWars.Networking
{
    public class ChargeHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private RoombaHealth _localHealth;

        private void Update()
        {
            if (_localHealth == null)
                FindLocalHealth();

            if (label != null)
                label.text = _localHealth != null
                    ? "HP " + Mathf.RoundToInt(_localHealth.CurrentHP) + "/" + Mathf.RoundToInt(_localHealth.MaxHP) +
                      "  GAUGE " + Mathf.RoundToInt(_localHealth.Gauge)
                    : "";
        }

        private void FindLocalHealth()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return;
            var po = nm.LocalClient.PlayerObject;
            if (po != null)
                _localHealth = po.GetComponent<RoombaHealth>();
        }
    }
}
