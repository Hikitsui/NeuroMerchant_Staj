using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
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
            case SessionData.GameMode.TekelSavaslari:
                // SADECE SÜRELİ MODLAR
                if (currentDay >= maxDays)
                {
                    Debug.Log($"<color=yellow>[Competition] Süre doldu! ({maxDays} gün)</color>");
                    EndTournament();
                }
                break;

            case SessionData.GameMode.LoncalarIttifaki:
                // HEM SÜRELİ HEM DE "SON LONCA AYAKTA KALSIN" MODU

                // 1. KOŞUL: Süre bitti mi?
                if (currentDay >= maxDays)
                {
                    Debug.Log($"<color=yellow>[Competition] Süre doldu! ({maxDays} gün)</color>");
                    EndTournament();
                    return; // Alt koda geçip iki kez bitirmemesi için dönüyoruz
                }

                // 2. KOŞUL: Kaç farklı lonca hayatta kaldı?
                int numGuilds = SessionData.GuildCount > 0 ? SessionData.GuildCount : 5;
                int agentsPerGuild = Mathf.Max(1, Mathf.CeilToInt((float)allAgents.Count / (float)numGuilds));

                // HashSet aynı sayıyı sadece bir kere tutar (Örn: 2 kervan da 1. Loncadan ise sadece '1' sayısını tutar)
                HashSet<int> aliveGuilds = new HashSet<int>();

                for (int i = 0; i < allAgents.Count; i++)
                {
                    // Kervan yaşıyorsa, onun lonca indeksini 'Yaşayan Loncalar' listesine ekle
                    if (allAgents[i].gameObject.activeSelf && allAgents[i].currentMoney > 0)
                    {
                        int gIndex = i / agentsPerGuild;
                        aliveGuilds.Add(gIndex);
                    }
                }

                // Eğer sadece 1 (veya hiç) lonca ayakta kaldıysa süreyi beklemeden bitir!
                if (aliveGuilds.Count <= 1 && currentDay > 1)
                {
                    Debug.Log($"<color=yellow>[Competition] Savaş bitti! Diğer tüm loncalar çöktü.</color>");
                    EndTournament();
                }
                break;

            case SessionData.GameMode.AcimasizKis:
                // Süresiz Mod: Hayatta kalan son kervanı bul
                if (aliveAgentsCount <= 1 && currentDay > 5)
                {
                    MerchantAgent lastAlive = allAgents.FirstOrDefault(a => a.gameObject.activeSelf);
                    EndTournament(lastAlive);
                }
                break;

            case SessionData.GameMode.SarayinElcisi:
                // Hem Süreli Hem Hedefli
                if (currentDay >= maxDays)
                {
                    Debug.Log($"<color=yellow>[Competition] Süre doldu! ({maxDays} gün)</color>");
                    EndTournament();
                }
                else
                {
                    // 25 Puana ilk ulaşan kazanır!
                    MerchantAgent elci = allAgents.FirstOrDefault(a => a.gameObject.activeSelf && a.contractPoints >= 25);
                    if (elci != null)
                    {
                        Debug.Log($"<color=yellow>[Competition] {elci.gameObject.name} 25 Krallık Puanına ulaştı ve oyunu bitirdi!</color>");
                        EndTournament(elci);
                    }
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

            if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi)
                kazananDetayi = $"Sarayın Baş Elçisi!\nKrallık Puanı: {forcedWinner.contractPoints} (Hedefe Ulaştı!)\nKasa: {forcedWinner.currentMoney:F0} G";
            else
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

                case SessionData.GameMode.LoncalarIttifaki:
                    CalculateGuildWinner(out kazananIsmi, out kazananDetayi);
                    break;

                case SessionData.GameMode.SarayinElcisi:
                    CalculateDiplomatWinner(out kazananIsmi, out kazananDetayi);
                    break;

                case SessionData.GameMode.TekelSavaslari:
                    CalculateMonopolyWinner(out kazananIsmi, out kazananDetayi);
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
        // 1. DİNAMİK LONCA SAYISI (Sabit 5 yerine menüden gelen sayıyı alıyoruz)
        int numGuilds = SessionData.GuildCount > 0 ? SessionData.GuildCount : 5;

        string[] guildNames = { "Yakut Loncası", "Safir Loncası", "Zümrüt Loncası", "Kehribar Loncası", "Obsidyen Loncası" };

        int agentsPerGuild = Mathf.Max(1, Mathf.CeilToInt((float)allAgents.Count / (float)numGuilds));

        float[] loncaKasalari = new float[numGuilds];
        List<string>[] loncaUyeleri = new List<string>[numGuilds];

        for (int i = 0; i < numGuilds; i++)
        {
            loncaUyeleri[i] = new List<string>();
        }

        // 2. Üyelerin paralarını lonca kasasında birleştir
        for (int i = 0; i < allAgents.Count; i++)
        {
            if (allAgents[i].gameObject.activeSelf && allAgents[i].currentMoney > 0)
            {
                int guildIndex = i / agentsPerGuild;
                if (guildIndex < numGuilds)
                {
                    loncaKasalari[guildIndex] += allAgents[i].currentMoney;
                    loncaUyeleri[guildIndex].Add(allAgents[i].gameObject.name); // Hangi üyelerin yaşadığını kaydediyoruz
                }
            }
        }

        float maxLoncaKasa = 0f;
        int bestGuildIndex = -1;

        // 3. En zengin loncayı bul
        for (int i = 0; i < numGuilds; i++)
        {
            if (loncaKasalari[i] > maxLoncaKasa)
            {
                maxLoncaKasa = loncaKasalari[i];
                bestGuildIndex = i;
            }
        }

        // 4. SONUÇ EKRANI
        if (bestGuildIndex != -1)
        {
            // İsim listesi sınırını aşarsa otomatik "Lonca X" yazar (Güvenlik önlemi)
            string finalGuildName = bestGuildIndex < guildNames.Length ? guildNames[bestGuildIndex] : $"Lonca {bestGuildIndex + 1}";

            guildName = $"🏆 KAZANAN:\n{finalGuildName}";

            // Kazanılan para ve o parayı toplayan kahraman üyelerin listesi
            guildStats = $"<color=yellow>Lonca Kasası: {maxLoncaKasa:F0} G</color>\n\n<color=white>Hayatta Kalan Üyeler:</color>\n<color=cyan>" + string.Join("\n", loncaUyeleri[bestGuildIndex]) + "</color>";
        }
        else
        {
            guildName = "LONCA BULUNAMADI";
            guildStats = "Tüm loncalar iflas etti...";
        }
    }

    private void CalculateDiplomatWinner(out string winnerName, out string winnerStats)
    {
        winnerName = "KİMSE KAZANAMADI";
        winnerStats = "Herkes elendi.";

        if (aliveAgentsCount > 0)
        {
            // En çok puanı olanı bul. Puanlar eşitse parası çok olanı seç!
            var diplomat = allAgents
                .Where(a => a.gameObject.activeSelf)
                .OrderByDescending(a => a.contractPoints)
                .ThenByDescending(a => a.currentMoney)
                .FirstOrDefault();

            if (diplomat != null)
            {
                winnerName = diplomat.gameObject.name;
                winnerStats = $"Sarayın Baş Elçisi!\nKrallık Puanı: {diplomat.contractPoints}\nKasa: {diplomat.currentMoney:F0} G";
            }
        }
    }

    private void CalculateMonopolyWinner(out string winnerName, out string winnerStats)
    {
        winnerName = "PAZARIN EFENDİLERİ";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // 12 Temel Ürünün Listesi ve Türkçe Çevirileri
        string[] allItems = { "Wheat", "Wood", "Fish", "Cotton", "Meat", "Coal", "Leather", "Iron", "Clothes", "Tools", "Spices", "Jewelry" };
        string[] trItems = { "Buğday", "Odun", "Balık", "Pamuk", "Et", "Kömür", "Deri", "Demir", "Kıyafet", "Alet", "Baharat", "Mücevher" };

        sb.AppendLine("<size=120%><color=yellow>--- ÜRÜN KRALLARI ---</color></size>");
        sb.AppendLine("");

        Dictionary<MerchantAgent, int> kingCount = new Dictionary<MerchantAgent, int>();
        foreach (var a in allAgents) kingCount[a] = 0;

        for (int i = 0; i < allItems.Length; i++)
        {
            string item = allItems[i];
            string trName = trItems[i];
            MerchantAgent bestAgent = null;
            int maxSold = 0;

            foreach (var agent in allAgents)
            {
                if (agent.soldItemsTracker != null && agent.soldItemsTracker.TryGetValue(item, out int sold) && sold > maxSold)
                {
                    maxSold = sold;
                    bestAgent = agent;
                }
            }

            if (bestAgent != null)
            {
                // Örn: 1-) Buğday Kralı - Kervan_5 - 86 tane sattı
                sb.AppendLine($"{i + 1}-) <color=orange>{trName} Kralı</color> - <color=yellow>{bestAgent.gameObject.name}</color> - <color=green>{maxSold} tane sattı</color>");
                kingCount[bestAgent]++;
            }
            else
            {
                sb.AppendLine($"{i + 1}-) <color=orange>{trName} Kralı</color> - <color=grey>Kimse Satmadı</color> - <color=green>0 tane sattı</color>");
            }
        }

        // Genel Kazanan: En çok dalda "Kral" olan ajan
        var ultimateWinner = kingCount.OrderByDescending(x => x.Value).ThenByDescending(x => x.Key.currentMoney).FirstOrDefault();
        if (ultimateWinner.Key != null && ultimateWinner.Value > 0)
        {
            winnerName = $"👑 BÜYÜK TEKEL: {ultimateWinner.Key.gameObject.name}";
            sb.AppendLine("");
            sb.AppendLine($"<color=cyan>Toplam {ultimateWinner.Value} farklı üründe pazar lideri!</color>");
        }

        winnerStats = sb.ToString();
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