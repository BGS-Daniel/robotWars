using UnityEngine;
using TMPro;
using Unity.Netcode;

namespace RobotWars.Networking
{
    public class ChargeHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private ChargeMeter _localMeter;

        private void Update()
        {
            if (_localMeter == null)
                FindLocalMeter();

            if (label != null)
                label.text = _localMeter != null
                    ? "CHARGE " + Mathf.RoundToInt(_localMeter.Charge)
                    : "";
        }

        private void FindLocalMeter()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return;
            var po = nm.LocalClient.PlayerObject;
            if (po != null)
                _localMeter = po.GetComponent<ChargeMeter>();
        }
    }
}
