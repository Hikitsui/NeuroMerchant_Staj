using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class MerchantQueueUI : MonoBehaviour
{
    public static MerchantQueueUI Instance;

    [Header("UI Bileşenleri")]
    [SerializeField] private TextMeshProUGUI queueTitleText;
    [SerializeField] private TextMeshProUGUI leaderboardBodyText;

    [Header("Ayarlar")]
    [SerializeField] private int maxVisibleMerchants = 10;
    [SerializeField] private bool autoUpdateOnNewDay = true;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // TimeManager'a abone ol ki her gün sonunda liste güncellensin
        if (autoUpdateOnNewDay && TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += RefreshQueue;
        }

        if (queueTitleText != null) queueTitleText.text = "🏆 MERCHANT QUEUE";
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= RefreshQueue;
        }
    }

    [ContextMenu("Refresh Queue")]
    public void RefreshQueue()
    {
        // CompetitionManager'daki ajan listesini al
        if (CompetitionManager.Instance == null || CompetitionManager.Instance.allAgents == null) return;

        // Ajanları paralarına göre sırala (İflas edenler en alta)
        var sortedAgents = CompetitionManager.Instance.allAgents
            .OrderByDescending(a => a.gameObject.activeSelf && a.currentMoney > 0 ? a.currentMoney : -1f)
            .Take(maxVisibleMerchants)
            .ToList();

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < sortedAgents.Count; i++)
        {
            var agent = sortedAgents[i];
            bool isAlive = agent.gameObject.activeSelf && agent.currentMoney > 0;

            // Sıra ve İsim
            string rankColor = i == 0 ? "yellow" : (isAlive ? "white" : "red");
            sb.Append($"<color={rankColor}>{i + 1}- {agent.gameObject.name}</color> ");

            // Para Durumu
            sb.Append($"<color=yellow>{agent.currentMoney:F0}$</color> ");

            // Yük Durumu
            if (isAlive)
            {
                if (agent.carriedItemData != null && agent.carriedAmount > 0)
                {
                    sb.Append($"| <color=#00FF00>{agent.carriedAmount}x {agent.carriedItemData.itemName}</color>");
                }
                else
                {
                    sb.Append("| <color=#AAAAAA>Boş</color>");
                }
            }
            else
            {
                sb.Append("| <color=red>ELENDİ</color>");
            }

            sb.AppendLine();
        }

        leaderboardBodyText.text = sb.ToString();
    }
}