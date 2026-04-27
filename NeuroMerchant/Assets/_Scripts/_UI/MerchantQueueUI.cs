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
    [SerializeField] private int maxVisibleMerchants = 12;
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
        if (CompetitionManager.Instance == null || CompetitionManager.Instance.allAgents == null) return;

        bool isDiplomatMode = SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi;
        bool isMonopolyMode = SessionData.CurrentMode == SessionData.GameMode.TekelSavaslari;

        StringBuilder sb = new StringBuilder();

        if (isMonopolyMode)
        {
            // TEKEL SAVAŞLARI İÇİN ÖZEL UI (Ürün Kralları Listesi)
            string[] allItems = { "Wheat", "Wood", "Fish", "Cotton", "Meat", "Coal", "Leather", "Iron", "Clothes", "Tools", "Spices", "Jewelry" };
            string[] trItems = { "Buğday", "Odun", "Balık", "Pamuk", "Et", "Kömür", "Deri", "Demir", "Kıyafet", "Alet", "Baharat", "Mücevher" };

            for (int i = 0; i < allItems.Length; i++)
            {
                string item = allItems[i];
                string trName = trItems[i];
                MerchantAgent bestAgent = null;
                int maxSold = 0;

                foreach (var agent in CompetitionManager.Instance.allAgents)
                {
                    if (agent.soldItemsTracker != null && agent.soldItemsTracker.TryGetValue(item, out int sold) && sold > maxSold)
                    {
                        maxSold = sold;
                        bestAgent = agent;
                    }
                }

                if (bestAgent != null)
                {
                    // 1-) Buğday Kralı - Kervan_5 - 86 tane sattı
                    sb.AppendLine($"<color=white>{i + 1}-)</color> <color=orange>{trName} Kralı</color> - <color=yellow>{bestAgent.gameObject.name}</color> - <color=green>{maxSold} tane sattı</color>");
                }
                else
                {
                    sb.AppendLine($"<color=white>{i + 1}-)</color> <color=orange>{trName} Kralı</color> - <color=grey>Yok</color> - <color=green>0 tane sattı</color>");
                }
            }
        }
        else
        {
            // DİĞER MODLAR İÇİN NORMAL AJAN SIRALAMASI
            List<MerchantAgent> sortedAgents;

            if (isDiplomatMode)
            {
                sortedAgents = CompetitionManager.Instance.allAgents
                    .OrderByDescending(a => a.gameObject.activeSelf && a.currentMoney > 0 ? a.contractPoints : -1)
                    .ThenByDescending(a => a.gameObject.activeSelf && a.currentMoney > 0 ? a.currentMoney : -1f)
                    .Take(maxVisibleMerchants)
                    .ToList();
            }
            else
            {
                sortedAgents = CompetitionManager.Instance.allAgents
                    .OrderByDescending(a => a.gameObject.activeSelf && a.currentMoney > 0 ? a.currentMoney : -1f)
                    .Take(maxVisibleMerchants)
                    .ToList();
            }

            for (int i = 0; i < sortedAgents.Count; i++)
            {
                var agent = sortedAgents[i];
                bool isAlive = agent.gameObject.activeSelf && agent.currentMoney > 0;

                string rankColor = i == 0 ? "yellow" : (isAlive ? "white" : "red");
                sb.Append($"<color={rankColor}>{i + 1}- {agent.gameObject.name}</color> ");

                if (isDiplomatMode)
                {
                    sb.Append($"<color=cyan>🏆 {agent.contractPoints} Puan ({agent.completedContractsCount} İhale)</color> | <color=yellow>{agent.currentMoney:F0}$</color> ");
                }
                else
                {
                    sb.Append($"<color=yellow>{agent.currentMoney:F0}$</color> ");
                }

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
        }

        leaderboardBodyText.text = sb.ToString();
    }
}