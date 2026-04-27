using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro; // TextMeshPro ve Dropdown için
using UnityEngine.UI; // Slider için GEREKLİ

public class MainMenuController : MonoBehaviour
{
    [Header("Ana Paneller (Ekranlar)")]
    public GameObject typeSelectPanel;  // Resim 1 (PvE / AI Sim)
    public GameObject modeSelectPanel;  // Resim 2 (Mod Seçimi)
    public GameObject detailsPanel;     // Resim 3 (Açıklama ve Başla)

    [Header("Açıklama UI (Resim 3 için)")]
    public TextMeshProUGUI modeTitleText;
    public TextMeshProUGUI modeDescriptionText;

    [Header("Dinamik Ayar Panelleri")]
    public GameObject agentSettingGroup;       
    public GameObject daysSettingGroup;  
    public GameObject difficultySettingGroup;
    public GameObject guildSettingGroup;

    [Header("Ayar Kontrolleri")]
    public Slider agentSlider;
    public TextMeshProUGUI agentText;

    public Slider daysSlider;
    public TextMeshProUGUI daysText;

    public TMP_Dropdown difficultyDropdown;
    public TextMeshProUGUI difficultyDescText;

    private void Start()
    {
        // Başlangıçta sadece ilk ekran açık olsun
        ShowPanel(typeSelectPanel);

        // SLIDER VE DROPDOWN'LARI KODA BAĞLAMA
        if (agentSlider != null)
        {
            agentSlider.onValueChanged.AddListener(delegate { OnAgentSliderChanged(); });
            agentSlider.value = SessionData.AgentCount; // Varsayılanı ata
        }

        if (daysSlider != null)
        {
            daysSlider.onValueChanged.AddListener(delegate { OnDaysSliderChanged(); });
            daysSlider.value = SessionData.MaxDays; // Varsayılanı ata
        }

        if (difficultyDropdown != null)
        {
            difficultyDropdown.onValueChanged.AddListener(delegate { OnDifficultyDropdownChanged(); });
            difficultyDropdown.value = SessionData.DifficultyLevel; // Varsayılanı ata
        }
    }

    // ARAYÜZ YARDIMCISI
    private void ShowPanel(GameObject panelToShow)
    {
        typeSelectPanel.SetActive(false);
        modeSelectPanel.SetActive(false);
        detailsPanel.SetActive(false);

        panelToShow.SetActive(true);
    }

    // ==========================================
    // 1. EKRAN BUTONLARI (Resim 1)
    // ==========================================
    public void Btn_SelectType_PlayerVsAI()
    {
        SessionData.CurrentType = SessionData.GameType.PlayerVsAI;
        ShowPanel(modeSelectPanel);
    }

    public void Btn_SelectType_AIVsAI()
    {
        SessionData.CurrentType = SessionData.GameType.AIVsAI;
        ShowPanel(modeSelectPanel);
    }

    // ==========================================
    // 2. EKRAN BUTONLARI (Resim 2 - Mod Seçimi)
    // ==========================================
    public void Btn_SelectMode(int modeIndex)
    {
        SessionData.CurrentMode = (SessionData.GameMode)modeIndex;
        UpdateDetailsScreen();
        ShowPanel(detailsPanel);
    }

    // ==========================================
    // AYAR DEĞİŞİM FONKSİYONLARI (Slider / Dropdown)
    // ==========================================
    public void OnAgentSliderChanged()
    {
        SessionData.AgentCount = (int)agentSlider.value;
        if (agentText != null) agentText.text = $"Kervan Sayısı: {SessionData.AgentCount}";
    }

    public void OnDaysSliderChanged()
    {
        SessionData.MaxDays = (int)daysSlider.value;
        if (daysText != null) daysText.text = $"Süre: {SessionData.MaxDays} Gün";
    }

    public void OnDifficultyDropdownChanged()
    {
        int level = difficultyDropdown.value;
        SessionData.DifficultyLevel = level;

        if (difficultyDescText == null) return;

        // SEÇİLEN MODA GÖRE ZORLUK AÇIKLAMASINI DEĞİŞTİR
        if (SessionData.CurrentMode == SessionData.GameMode.AcimasizKis)
        {
            if (level == 0) difficultyDescText.text = "<color=green>Zorluk: KOLAY</color>\nMakas: %10";
            else if (level == 1) difficultyDescText.text = "<color=yellow>Zorluk: ORTA</color>\nMakas: %20";
            else if (level == 2) difficultyDescText.text = "<color=red>Zorluk: ZOR</color>\nMakas: %30 (Saf Kaos!)";
        }
        else if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi)
        {
            if (level == 0) difficultyDescText.text = "<color=green>İhale: KOLAY</color>\nÜrünler: Temel Gıda ve Odun\nSüre: Uzun";
            else if (level == 1) difficultyDescText.text = "<color=yellow>İhale: ORTA</color>\nÜrünler: İşlenmiş Eşyalar\nSüre: Normal";
            else if (level == 2) difficultyDescText.text = "<color=red>İhale: ZOR</color>\nÜrünler: Lüks ve Zor Bulunanlar\nSüre: Kısa";
        }
    }

    // ==========================================
    // 3. EKRAN DETAYLARI VE BAŞLATMA (Resim 3)
    // ==========================================
    private void UpdateDetailsScreen()
    {
        // 1. TÜM GRUPLARI KAPAT (Temiz sayfa)
        if (agentSettingGroup != null) agentSettingGroup.SetActive(false);
        if (daysSettingGroup != null) daysSettingGroup.SetActive(false);
        if (difficultySettingGroup != null) difficultySettingGroup.SetActive(false);
        if (guildSettingGroup != null) guildSettingGroup.SetActive(false);

        // Ajan sayısı her modda lazım, direkt açalım
        if (agentSettingGroup != null) agentSettingGroup.SetActive(true);

        switch (SessionData.CurrentMode)
        {
            case SessionData.GameMode.AltinYolu:
                modeTitleText.text = "ALTIN YOLU";
                modeDescriptionText.text = "Süre: Seçilen güne kadar.\nHedef: En yüksek servete ulaşan tüccar kazanır.";

                daysSettingGroup.SetActive(true); // Sadece Gün ve Ajan
                if (daysSlider != null) OnDaysSliderChanged();
                break;

            case SessionData.GameMode.AcimasizKis:
                modeTitleText.text = "ACIMASIZ KIŞ";
                modeDescriptionText.text = "Süre: Sınırsız.\nHedef: Piyasa krizlerine dayanıp hayatta kalan son kervan ol.";
                SessionData.MaxDays = 999999;

                difficultySettingGroup.SetActive(true); // Sadece Zorluk ve Ajan
                if (difficultyDropdown != null) OnDifficultyDropdownChanged();
                break;

            case SessionData.GameMode.SarayinElcisi:
                modeTitleText.text = "SARAYIN ELÇİSİ";
                modeDescriptionText.text = "Süre: Seçilen güne kadar.\nHedef: Krallık ihalelerini tamamla, en yüksek puanı topla.";

                daysSettingGroup.SetActive(true);       // Gün + 
                difficultySettingGroup.SetActive(true); // Zorluk + Ajan

                if (daysSlider != null) OnDaysSliderChanged();
                if (difficultyDropdown != null) OnDifficultyDropdownChanged();
                break;

            case SessionData.GameMode.TekelSavaslari:
                modeTitleText.text = "TEKEL SAVAŞLARI";
                modeDescriptionText.text = "Süre: Seçilen güne kadar.\nHedef: Şehirlerdeki ürün stoklarını tekeline alarak rakipleri saf dışı bırak.";

                daysSettingGroup.SetActive(true); // Sadece Gün ve Ajan (İsteğin üzerine)
                if (daysSlider != null) OnDaysSliderChanged();
                break;

            case SessionData.GameMode.LoncalarIttifaki:
                modeTitleText.text = "LONCALAR İTTİFAKI (WIP)";
                modeDescriptionText.text = "Süre: Sınırsız.\nHedef: Diğer loncaları iflas ettir, haritaya hükmet.";

                agentSettingGroup.SetActive(true);
                guildSettingGroup.SetActive(true); // Sadece bu moda özel!
                break;
        }

        // Seçilen Type'ı (PvE / AI Sim) başlığa ekle
        string typeStr = SessionData.CurrentType == SessionData.GameType.PlayerVsAI ? "PVE" : "AI SIM";
        modeTitleText.text += $" [{typeStr}]";
    }

    public void Btn_StartGame()
    {
        SceneManager.LoadScene(1);
    }

    public void Btn_BackToMenu()
    {
        ShowPanel(typeSelectPanel);
    }
}