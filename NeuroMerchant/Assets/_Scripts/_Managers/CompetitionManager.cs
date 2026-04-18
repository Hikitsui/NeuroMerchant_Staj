using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

// ==============================================================
// COMPETITION MANAGER - MODÜLER TURNUVA MOTORU
// ==============================================================
public class CompetitionManager : MonoBehaviour
{
    public static CompetitionManager Instance;

    [Header("Oyun Sonu UI")]
    public GameObject gameOverPanel;
    public TMPro.TextMeshProUGUI winnerText;
    public TMPro.TextMeshProUGUI statsText;

    [Header("Turnuva Ayarları")]
    public int maxDays = 1800; // SessionData'dan üzerine yazılacak
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
            Destroy(gameObject);
        }
    }

    public void InitTournament()
    {
        // 1. Sahnedeki tüm agentları bul
        allAgents = FindObjectsOfType<MerchantAgent>().ToList();

        if (allAgents.Count == 0)
        {
            Debug.LogError("<color=red>[Competition] HATA: Sahnede hiç MerchantAgent bulunamadı!</color>");
            return;
        }

        // Agentlara standart isim ver
        for (int i = 0; i < allAgents.Count; i++)
        {
            allAgents[i].gameObject.name = $"Kervan_{i + 1}";
        }

        // 2. TimeManager kontrolü ve abonelik
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleNewDay;
        }
        else
        {
            Debug.LogError("<color=red>[Competition] FATAL: TimeManager bulunamadı!</color>");
            return;
        }

        // 3. Menüden gelen ayarları kontrol et (SessionData)
        // (Eğer oyunu direkt bu sahneden başlatırsan null yememek için varsayılanlar)
        if (SessionData.MaxDays > 0)
        {
            maxDays = SessionData.MaxDays;
        }

        // 4. Turnuvayı başlat
        isMatchActive = true;
        currentDay = 0;

        Debug.Log($"<color=cyan>╔══════════════════════════════════════════════════╗</color>");
        Debug.Log($"<color=cyan>║ MOD: {SessionData.CurrentMode} | TİP: {SessionData.CurrentType} </color>");
        Debug.Log($"<color=cyan>║ Toplam Kervan: {allAgents.Count} | Hedef Süre: {maxDays} Gün </color>");
        Debug.Log($"<color=cyan>╚══════════════════════════════════════════════════╝</color>");
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    // =========================================================
    // HER YENİ GÜN TETİKLENEN ANA DÖNGÜ
    // =========================================================
    private void HandleNewDay()
    {
        if (!isMatchActive) return;

        currentDay++;

        // --- YENİ EKLENEN İFLAS SİSTEMİ (BATTLE ROYALE) ---
        foreach (var agent in allAgents)
        {
            // Eğer ajan hala sahnede aktifse ve parası SIFIRA veya EKSİYE düştüyse

            if (agent.gameObject.activeSelf && agent.currentMoney <= 0)
            {
                agent.gameObject.SetActive(false);

                // Hangi modda olduğumuza göre ölüm sebebini belirle
                string sebep = (SessionData.CurrentMode == SessionData.GameMode.AcimasizKis) ? "soğuktan dondu" : "iflas etti";

                Debug.Log($"<color=red>💀 İFLAS: {agent.gameObject.name} {sebep} ve elendi! (Gün: {currentDay})</color>");
            }
        }
        // ----------------------------------------------------

        // Canlı kervanları güncelle (Artık sadece activeSelf bakmamız yeterli çünkü ölenleri kapattık)
        aliveAgentsCount = allAgents.Count(a => a != null && a.gameObject.activeSelf);

        if (aliveAgentsCount > 0)
        {
            topAgentMoney = allAgents.Where(a => a.gameObject.activeSelf).Max(a => a.currentMoney);
        }

        // Modlara Göre Kazanma/Bitme Kontrolü
        CheckWinConditions();
    }

    // =========================================================
    // MODLARA GÖRE KAZANMA KONTROLÜ
    // =========================================================
    public void CheckWinConditions()
    {
        if (!isMatchActive) return;

        switch (SessionData.CurrentMode)
        {
            case SessionData.GameMode.AltinYolu:
            case SessionData.GameMode.SarayinElcisi:
            case SessionData.GameMode.Tekel:
            case SessionData.GameMode.Loncalar:
                // Süreli Modlar
                if (currentDay >= maxDays)
                {
                    Debug.Log($"<color=yellow>[Competition] Süre doldu! ({maxDays} gün)</color>");
                    EndTournament();
                }
                break;

            case SessionData.GameMode.AcimasizKis:
                // Süresiz Mod: Hayatta kalan son kişiyi bul
                if (aliveAgentsCount <= 1 && currentDay > 5) // 30 yerine 5 yaptık
                {
                    // Artık currentMoney sormamıza gerek yok, sahnede açıksa yaşıyordur
                    MerchantAgent lastAlive = allAgents.FirstOrDefault(a => a.gameObject.activeSelf);
                    EndTournament(lastAlive);
                }
                break;
        }
    }

    // =========================================================
    // OYUNU BİTİR VE KAZANANI EKRANA YANSIT
    // =========================================================
    public void EndTournament(MerchantAgent forcedWinner = null)
    {
        isMatchActive = false;
        Time.timeScale = 0f; // Zamanı durdur

        string kazananIsmi = "KİMSE KAZANAMADI";
        string kazananDetayi = "Herkes iflas etti...";

        // A. ZATEN BELLİ BİR KAZANAN VARSA (Örn: Acımasız Kış)
        if (forcedWinner != null)
        {
            kazananIsmi = forcedWinner.gameObject.name;
            kazananDetayi = $"Hayatta Kalan Son Lord!\nKasa: {forcedWinner.currentMoney:F0} G";
        }
        // B. SÜRE BİTTİYSE MODA GÖRE KAZANAN HESAPLA
        else
        {
            switch (SessionData.CurrentMode)
            {
                case SessionData.GameMode.AltinYolu:
                    CalculateAltinYoluWinner(out kazananIsmi, out kazananDetayi);
                    break;

                case SessionData.GameMode.Loncalar:
                    CalculateGuildWinner(out kazananIsmi, out kazananDetayi);
                    break;

                case SessionData.GameMode.SarayinElcisi:
                    kazananIsmi = "SARAYIN ELÇİSİ (WIP)";
                    kazananDetayi = "Krallık Puanı Sistemi bekleniyor...";
                    // İleride: CalculateDiplomatWinner(out kazananIsmi, out kazananDetayi);
                    break;

                case SessionData.GameMode.Tekel:
                    kazananIsmi = "TEKEL MODU (WIP)";
                    kazananDetayi = "Pazar Payı Sistemi bekleniyor...";
                    // İleride: CalculateMonopolyWinner(out kazananIsmi, out kazananDetayi);
                    break;
            }
        }

        // C. UI GÜNCELLEMESİ
        if (winnerText != null) winnerText.text = $"KAZANAN:\n{kazananIsmi}";
        if (statsText != null) statsText.text = kazananDetayi;

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Debug.Log($"<color=yellow>═════════ TURNUVA SONA ERDİ ═════════</color>\nKazanan: {kazananIsmi}\nDurum: {kazananDetayi}");
    }

    // =========================================================
    // ÖZEL HESAPLAMA FONKSİYONLARI (MOD ALTYAPILARI)
    // =========================================================
    private void CalculateAltinYoluWinner(out string winnerName, out string winnerStats)
    {
        var zenginAjan = allAgents
            .Where(a => a.gameObject.activeSelf && a.currentMoney > 0)
            .OrderByDescending(a => a.currentMoney)
            .FirstOrDefault();

        if (zenginAjan != null)
        {
            winnerName = zenginAjan.gameObject.name;
            winnerStats = $"Nakit Kralı\nToplam Para: {zenginAjan.currentMoney:F0} G";
        }
        else
        {
            winnerName = "KİMSE KAZANAMADI";
            winnerStats = "Tüm Kervanlar Battı...";
        }
    }

    private void CalculateGuildWinner(out string guildName, out string guildStats)
    {
        guildName = "LONCA BULUNAMADI";
        guildStats = "Herkes battı...";

        if (allAgents.Count == 0) return;

        // 5 Loncaya bölme (Index / (toplam/5))
        float[] loncaKasalari = new float[5];
        int agentsPerGuild = Mathf.Max(1, Mathf.CeilToInt((float)allAgents.Count / 5f));

        for (int i = 0; i < allAgents.Count; i++)
        {
            if (allAgents[i].gameObject.activeSelf && allAgents[i].currentMoney > 0)
            {
                int guildIndex = i / agentsPerGuild;
                if (guildIndex < 5) loncaKasalari[guildIndex] += allAgents[i].currentMoney;
            }
        }

        float maxLoncaKasa = 0f;
        int bestGuildIndex = -1;

        for (int i = 0; i < 5; i++)
        {
            if (loncaKasalari[i] > maxLoncaKasa)
            {
                maxLoncaKasa = loncaKasalari[i];
                bestGuildIndex = i;
            }
        }

        if (bestGuildIndex != -1)
        {
            guildName = $"LONCA {bestGuildIndex + 1}";
            guildStats = $"Lonca Toplam Kasası:\n{maxLoncaKasa:F0} G";
        }
    }

    // =========================================================
    // UI BUTON TETİKLEYİCİLERİ
    // =========================================================
    public void Btn_ReturnToMainMenu()
    {
        // Zamanı akıtmaya devam et ki menüdeki animasyonlar donmasın
        Time.timeScale = 1f;
        SceneManager.LoadScene(0); // Menü sahnesine dön
    }

    [ContextMenu("Test: Hemen Bitir")]
    public void ForceEndMatch()
    {
        EndTournament();
    }
}