// MainMenuController.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro; // TextMeshPro kullanıyorsan

public class MainMenuController : MonoBehaviour
{
    [Header("Paneller (Ekranlar)")]
    public GameObject typeSelectPanel;  // Resim 1 (PvE / AI Sim)
    public GameObject modeSelectPanel;  // Resim 2 (Mod Seçimi)
    public GameObject detailsPanel;     // Resim 3 (Açıklama ve Başla)

    [Header("Açıklama UI (Resim 3 için)")]
    public TextMeshProUGUI modeTitleText;
    public TextMeshProUGUI modeDescriptionText;

    private void Start()
    {
        // Başlangıçta sadece ilk ekran açık olsun
        ShowPanel(typeSelectPanel);
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
    // Butonların OnClick eventlerinden bu fonksiyona int değer yollayacağız (0,1,2,3,4)
    public void Btn_SelectMode(int modeIndex)
    {
        SessionData.CurrentMode = (SessionData.GameMode)modeIndex;
        UpdateDetailsScreen();
        ShowPanel(detailsPanel);
    }

    // ==========================================
    // 3. EKRAN DETAYLARI VE BAŞLATMA (Resim 3)
    // ==========================================
    private void UpdateDetailsScreen()
    {
        switch (SessionData.CurrentMode)
        {
            case SessionData.GameMode.AltinYolu:
                modeTitleText.text = "ALTIN YOLU";
                modeDescriptionText.text = "Süre: 5 Yıl.\nHedef: En yüksek servete ulaşan tüccar kazanır.";
                SessionData.MaxDays = 1800;
                break;
            case SessionData.GameMode.AcimasizKis:
                modeTitleText.text = "ACIMASIZ KIŞ";
                modeDescriptionText.text = "Süre: Sınırsız.\nHedef: Piyasa krizlerine dayanıp ayakta kalan son Lord ol.";
                SessionData.MaxDays = 0; // Sınırsız
                break;
                // Diğer modların açıklamalarını buraya eklersin...
        }

        // Seçilen Type'ı da başlığa ekleyelim (Örn: ALTIN YOLU (AI SIMULATION))
        string typeStr = SessionData.CurrentType == SessionData.GameType.PlayerVsAI ? "PVE" : "AI SIM";
        modeTitleText.text += $" [{typeStr}]";
    }

    public void Btn_StartGame()
    {
        // 1. İndeksli sahneyi (Simülasyon sahnesi) yükle
        SceneManager.LoadScene(1);
    }

    public void Btn_BackToMenu()
    {
        ShowPanel(typeSelectPanel);
    }
}