using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// COMPETITION MANAGER - TURNUVA MOTORU (DÜZELTİLMİŞ)
// ==============================================================
public class CompetitionManager : MonoBehaviour
{
    public static CompetitionManager Instance;

    [Header("Turnuva Ayarları")]
    [Tooltip("5 Yıl = 1800 Gün")]
    public int maxDays = 1800;
    public int currentDay = 0;
    public bool isMatchActive = false;

    [Header("Sahnede Yarışan Agentlar")]
    public List<MerchantAgent> allAgents = new List<MerchantAgent>();

    [Header("Debug Bilgileri")]
    public int aliveAgentsCount = 0;
    public float topAgentMoney = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("<color=cyan>[Competition] Instance oluşturuldu.</color>");
        }
        else
        {
            Debug.LogWarning("[Competition] Duplicate instance bulundu, yok ediliyor.");
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Sahnedeki tüm agentları bul
        allAgents = FindObjectsOfType<MerchantAgent>().ToList();

        if (allAgents.Count == 0)
        {
            Debug.LogError("<color=red>[Competition] HATA: Sahnede hiç MerchantAgent bulunamadı!</color>");
            return;
        }

        // Agentlara isim ver
        for (int i = 0; i < allAgents.Count; i++)
        {
            allAgents[i].gameObject.name = $"Kervan_{i + 1}";
            Debug.Log($"[Competition] Agent kaydedildi: {allAgents[i].gameObject.name}");
        }

        // TimeManager kontrolü
        if (TimeManager.Instance == null)
        {
            Debug.LogError("<color=red>[Competition] FATAL: TimeManager bulunamadı! Zaman sistemi çalışmıyor.</color>");
            return;
        }

        // Zaman eventlerine abone ol
        TimeManager.Instance.OnNewDay += HandleNewDay;
        Debug.Log($"<color=green>[Competition] TimeManager'a abone olundu.</color>");

        // Turnuvayı başlat
        isMatchActive = true;
        currentDay = 0;

        Debug.Log($"<color=cyan>╔══════════════════════════════════════════════════╗</color>");
        Debug.Log($"<color=cyan>║   TURNUVA BAŞLADI - ALTIN YOLU                  ║</color>");
        Debug.Log($"<color=cyan>║   Toplam Agent: {allAgents.Count,-30} ║</color>");
        Debug.Log($"<color=cyan>║   Hedef Süre: {maxDays} gün (5 yıl)              ║</color>");
        Debug.Log($"<color=cyan>╚══════════════════════════════════════════════════╝</color>");
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    private void HandleNewDay()
    {
        if (!isMatchActive) return;

        currentDay++;

        // Hayatta kalanları say
        var aliveAgents = allAgents.Where(a =>
            a != null &&
            a.gameObject.activeSelf &&
            a.currentMoney > 0
        ).ToList();

        aliveAgentsCount = aliveAgents.Count;

        // En zengin agentı bul
        if (aliveAgents.Count > 0)
        {
            topAgentMoney = aliveAgents.Max(a => a.currentMoney);
        }

        // Her 30 günde bir rapor
        if (currentDay % 30 == 0)
        {
            Debug.Log($"<color=yellow>📊 [Gün {currentDay}] Hayatta: {aliveAgentsCount}/{allAgents.Count} | En Zengin: {topAgentMoney:F0} Altın</color>");
        }

        // TEK HAYATTA KALMA KONTROLÜ
        if (aliveAgents.Count <= 1 && currentDay > 30) // İlk ayı geç
        {
            if (aliveAgents.Count == 1)
            {
                Debug.Log($"<color=green>[Competition] 🏆 TEK KALAN ŞAMPIYON: {aliveAgents[0].gameObject.name}!</color>");
            }
            else
            {
                Debug.Log($"<color=red>[Competition] Tüm agentlar elendi!</color>");
            }
            EndMatch();
            return;
        }

        // SÜRE KONTROLÜ
        if (currentDay >= maxDays)
        {
            Debug.Log($"<color=yellow>[Competition] Süre doldu! ({maxDays} gün tamamlandı)</color>");
            EndMatch();
        }
    }

    private void EndMatch()
    {
        isMatchActive = false;
        Time.timeScale = 0f; // Oyunu durdur

        Debug.Log($"<color=yellow>═══════════════════════════════════════════════════</color>");
        Debug.Log($"<color=yellow>          TURNUVA SONA ERDİ!</color>");
        Debug.Log($"<color=yellow>          Toplam Süre: {currentDay} Gün</color>");
        Debug.Log($"<color=yellow>═══════════════════════════════════════════════════</color>");

        CalculateAltinYoluLeaderboard();
    }

    private void CalculateAltinYoluLeaderboard()
    {
        // Tüm agentları para durumlarına göre sırala
        var sortedAgents = allAgents
            .OrderByDescending(a =>
            {
                if (a == null) return -999f;
                if (!a.gameObject.activeSelf) return -999f;
                return a.currentMoney;
            })
            .ToList();

        Debug.Log("╔═══════════════════════════════════════════════════╗");
        Debug.Log("║          🏆 NİHAİ SKOR TABLOSU 🏆                ║");
        Debug.Log("╠═══════════════════════════════════════════════════╣");

        for (int i = 0; i < sortedAgents.Count; i++)
        {
            var agent = sortedAgents[i];

            if (agent == null)
            {
                Debug.Log($"║ #{i + 1,-2} │ NULL AGENT                              ║");
                continue;
            }

            bool isAlive = agent.gameObject.activeSelf && agent.currentMoney > 0;
            string rank;
            string status;

            if (i == 0 && isAlive)
            {
                rank = "🥇";
                status = $"<color=yellow><b>{agent.currentMoney:F0} ALTIN</b></color>";
            }
            else if (i == 1 && isAlive)
            {
                rank = "🥈";
                status = $"<color=white>{agent.currentMoney:F0} Altın</color>";
            }
            else if (i == 2 && isAlive)
            {
                rank = "🥉";
                status = $"<color=orange>{agent.currentMoney:F0} Altın</color>";
            }
            else if (isAlive)
            {
                rank = $"#{i + 1}";
                status = $"{agent.currentMoney:F0} Altın";
            }
            else
            {
                rank = $"#{i + 1}";
                status = "<color=red>İFLAS ETTİ</color>";
            }

            string agentName = agent.gameObject.name.PadRight(15);
            Debug.Log($"║ {rank,-3} │ {agentName} │ {status,-20} ║");
        }

        Debug.Log("╚═══════════════════════════════════════════════════╝");

        // Kazananı özel olarak vurgula
        var winner = sortedAgents.FirstOrDefault(a => a != null && a.gameObject.activeSelf && a.currentMoney > 0);
        if (winner != null)
        {
            Debug.Log($"<color=cyan>🎊 KAZANAN: {winner.gameObject.name} - {winner.currentMoney:F0} ALTIN 🎊</color>");
        }
    }

    // Manuel turnuva bitirme (test için)
    [ContextMenu("Turnuvayı Bitir")]
    public void ForceEndMatch()
    {
        if (!isMatchActive)
        {
            Debug.LogWarning("[Competition] Turnuva zaten sona erdi.");
            return;
        }

        Debug.Log("[Competition] Manuel olarak turnuva sonlandırıldı.");
        EndMatch();
    }

    // Turnuvayı yeniden başlat
    [ContextMenu("Turnuvayı Yeniden Başlat")]
    public void RestartMatch()
    {
        Time.timeScale = 1f;
        currentDay = 0;
        isMatchActive = true;

        foreach (var agent in allAgents)
        {
            if (agent != null)
            {
                agent.gameObject.SetActive(true);
                // Agent'ın kendi resetini yapması için EndEpisode yerine 
                // manuel reset fonksiyonu çağırılmalı (MerchantAgent'da olmalı)
            }
        }

        Debug.Log("<color=green>[Competition] Turnuva yeniden başlatıldı!</color>");
    }
}