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
    [ContextMenu("Refresh Queue")]
    public void RefreshQueue()
    {
        if (CompetitionManager.Instance == null || CompetitionManager.Instance.allAgents == null) return;

        bool isDiplomatMode = SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi;
        bool isMonopolyMode = SessionData.CurrentMode == SessionData.GameMode.TekelSavaslari;
        bool isGuildMode = SessionData.CurrentMode == SessionData.GameMode.LoncalarIttifaki; // YENİ: Lonca kontrolü eklendi

        StringBuilder sb = new StringBuilder();

        if (isMonopolyMode)
        {
            // ========================================================
            // TEKEL SAVAŞLARI İÇİN ÖZEL UI (Ürün Kralları Listesi)
            // ========================================================
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
                    sb.AppendLine($"<color=white>{i + 1}-)</color> <color=orange>{trName} Kralı</color> - <color=yellow>{bestAgent.gameObject.name}</color> - <color=green>{maxSold} tane sattı</color>");
                }
                else
                {
                    sb.AppendLine($"<color=white>{i + 1}-)</color> <color=orange>{trName} Kralı</color> - <color=grey>Yok</color> - <color=green>0 tane sattı</color>");
                }
            }
        }
        else if (isGuildMode)
        {
            // ========================================================
            // YENİ: LONCALAR İTTİFAKI CANLI EKRANI
            // ========================================================
            string[] guildNames = { "Yakut Loncası", "Safir Loncası", "Zümrüt Loncası", "Kehribar Loncası", "Obsidyen Loncası" };
            string[] guildColors = { "#FF4444", "#4488FF", "#44FF44", "#FFBB44", "#CC44FF" };

            // Dinamik Lonca Sayısı (Menüden gelen sayı)
            int numGuilds = SessionData.GuildCount > 0 ? SessionData.GuildCount : 5;
            int agentsPerGuild = Mathf.Max(1, Mathf.CeilToInt((float)CompetitionManager.Instance.allAgents.Count / (float)numGuilds));

            var guildTotals = new Dictionary<int, float>();
            var guildAgents = new Dictionary<int, List<string>>();

            for (int i = 0; i < numGuilds; i++)
            {
                guildTotals[i] = 0f;
                guildAgents[i] = new List<string>();
            }

            // Loncaların kasasını ve yaşayan üyelerini topla
            for (int i = 0; i < CompetitionManager.Instance.allAgents.Count; i++)
            {
                var agent = CompetitionManager.Instance.allAgents[i];
                if (agent.gameObject.activeSelf && agent.currentMoney > 0)
                {
                    int gIndex = i / agentsPerGuild;
                    if (gIndex < numGuilds)
                    {
                        guildTotals[gIndex] += agent.currentMoney;
                        guildAgents[gIndex].Add(agent.gameObject.name);
                    }
                }
            }

            // Kasası en dolu olan loncayı en başa al
            var sortedGuilds = guildTotals.OrderByDescending(kv => kv.Value).ToList();

            int rank = 1;
            foreach (var kv in sortedGuilds)
            {
                int gIndex = kv.Key;
                float money = kv.Value;

                if (guildAgents[gIndex].Count > 0) // Sadece yaşayan loncaları göster
                {
                    // Eğer 5'ten fazla lonca ayarlandıysa renk sınırını aşmamak için modulo kullanılır
                    string color = guildColors[gIndex % guildColors.Length];
                    string name = gIndex < guildNames.Length ? guildNames[gIndex] : $"Lonca {gIndex + 1}";
                    string members = string.Join(", ", guildAgents[gIndex]);

                    sb.AppendLine($"<color=white>{rank}-)</color> <color={color}><b>{name}</b></color> - <color=yellow>{money:F0} G</color>");
                    sb.AppendLine($"   <size=80%><color=grey>Üyeler: {members}</color></size>");
                    rank++;
                }
            }
        }
        else
        {
            // ========================================================
            // DİĞER MODLAR İÇİN NORMAL AJAN SIRALAMASI
            // ========================================================
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