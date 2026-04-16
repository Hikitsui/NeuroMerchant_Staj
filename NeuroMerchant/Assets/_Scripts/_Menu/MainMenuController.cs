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
    public GameObject commonSettingsPanel;     // Ajan Slider'ı burada olacak
    public GameObject altinYoluSettingsPanel;  // Gün Slider'ı burada olacak
    public GameObject acimasizKisSettingsPanel;// Zorluk Dropdown'ı burada olacak

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

        if (difficultyDescText != null)
        {
            if (level == 0) difficultyDescText.text = "<color=green>Zorluk: KOLAY</color>\nMakas: %10";
            else if (level == 1) difficultyDescText.text = "<color=yellow>Zorluk: ORTA</color>\nMakas: %20";
            else if (level == 2) difficultyDescText.text = "<color=red>Zorluk: ZOR</color>\nMakas: %30";
        }
    }

    // ==========================================
    // 3. EKRAN DETAYLARI VE BAŞLATMA (Resim 3)
    // ==========================================
    private void UpdateDetailsScreen()
    {
        // Önce tüm özel ayar panellerini gizle
        if (altinYoluSettingsPanel != null) altinYoluSettingsPanel.SetActive(false);
        if (acimasizKisSettingsPanel != null) acimasizKisSettingsPanel.SetActive(false);

        // Ortak panel (Ajan Sayısı) hep açık kalsın
        if (commonSettingsPanel != null) commonSettingsPanel.SetActive(true);

        switch (SessionData.CurrentMode)
        {
            case SessionData.GameMode.AltinYolu:
                modeTitleText.text = "ALTIN YOLU";
                modeDescriptionText.text = "Süre: Seçilen güne kadar.\nHedef: En yüksek servete ulaşan tüccar kazanır.";

                if (altinYoluSettingsPanel != null) altinYoluSettingsPanel.SetActive(true);
                // Gün sayısını güncelle
                if (daysSlider != null) OnDaysSliderChanged();
                break;

            case SessionData.GameMode.AcimasizKis:
                modeTitleText.text = "ACIMASIZ KIŞ";
                modeDescriptionText.text = "Süre: Sınırsız.\nHedef: Piyasa krizlerine dayanıp ayakta kalan son Lord ol.";
                SessionData.MaxDays = 999999; // Sınırsız

                if (acimasizKisSettingsPanel != null) acimasizKisSettingsPanel.SetActive(true);
                // Dropdown yazısını güncelle
                if (difficultyDropdown != null) OnDifficultyDropdownChanged();
                break;

                // Diğer modların açıklamalarını ileride buraya eklersin...
        }

        // Seçilen Type'ı da başlığa ekleyelim
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