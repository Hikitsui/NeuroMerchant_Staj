using UnityEngine;

// ==============================================================
// MASTER GAME MANAGER
// Tüm sistemleri merkezi olarak yöneten ve senkronize eden kontrol merkezi
// ==============================================================
public class MasterGameManager : MonoBehaviour
{
    public static MasterGameManager Instance;

    [Header("🎮 OYUN MODU")]
    [Tooltip("TRUE = Eğitim Modu (AI öğreniyor) | FALSE = Turnuva Modu (AI yarışıyor)")]
    public bool isTrainingMode = false; // ❗ Inspector'dan değiştir

    [Header("📊 Referanslar")]
    public WorldGenerator worldGenerator;
    public ContractManager contractManager;
    public EventManager eventManager;
    public BrokerManager brokerManager;
    public CurriculumManager curriculumManager;
    public CompetitionManager competitionManager;
    public TimeManager timeManager;

    [Header("⚙️ Otomatik Bulma")]
    public bool autoFindManagers = true;

    private void Awake()
    {
        // Singleton kontrolü
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Referansları otomatik bul
        if (autoFindManagers)
        {
            FindAllManagers();
        }

        // TÜM SİSTEMLERİ SENKRONIZE ET
        SynchronizeAllSystems();

        Debug.Log($"<color=cyan>╔════════════════════════════════════════╗</color>");
        Debug.Log($"<color=cyan>║  MASTER GAME MANAGER BAŞLATILDI       ║</color>");
        Debug.Log($"<color=cyan>║  Mod: {(isTrainingMode ? "EĞİTİM" : "TURNUVA"),-28} ║</color>");
        Debug.Log($"<color=cyan>╚════════════════════════════════════════╝</color>");
    }

    private void FindAllManagers()
    {
        if (worldGenerator == null) 
            worldGenerator = FindObjectOfType<WorldGenerator>();
        
        if (contractManager == null) 
            contractManager = FindObjectOfType<ContractManager>();
        
        if (eventManager == null) 
            eventManager = FindObjectOfType<EventManager>();
        
        if (brokerManager == null) 
            brokerManager = FindObjectOfType<BrokerManager>();
        
        if (curriculumManager == null) 
            curriculumManager = FindObjectOfType<CurriculumManager>();
        
        if (competitionManager == null) 
            competitionManager = FindObjectOfType<CompetitionManager>();
        
        if (timeManager == null) 
            timeManager = FindObjectOfType<TimeManager>();

        Debug.Log("[MasterGM] Tüm manager'lar tarandı.");
        SetupSelectedGameMode();
    }

    // MasterGameManager.cs içine Awake() fonksiyonunun sonuna ekle:

    private void SetupSelectedGameMode()
    {
        // Eğitimi kapat, turnuvayı aç
        isTrainingMode = false;

        // 1. OYUNCU VS AI DURUMU
        if (SessionData.CurrentType == SessionData.GameType.PlayerVsAI)
        {
            Debug.Log("<color=green>PVE Modu Aktif! Oyuncu kontrolleri açılıyor...</color>");
            // İleride oyuncu scriptini burada aktif edeceksin
        }

        // 2. MOD KURALLARINI UYGULA
        if (competitionManager != null)
        {
            competitionManager.maxDays = SessionData.MaxDays;
        }

        switch (SessionData.CurrentMode)
        {
            case SessionData.GameMode.AltinYolu:
                Debug.Log("Oyun Modu: Altın Yolu başlatıldı.");
                break;

            case SessionData.GameMode.AcimasizKis:
                Debug.Log("Oyun Modu: Acımasız Kış başlatıldı.");
                // EventManager'a Kriz modunu aç emri ver!
                if (eventManager != null) eventManager.productionEventsCount = 20; // Kaos!
                break;

            case SessionData.GameMode.Loncalar:
                Debug.Log("Oyun Modu: Loncalar Savaşı başlatıldı.");
                // Ajan sayısını 20 yap (Gelecekte)
                break;
        }
    }

    /// <summary>
    /// TÜM SİSTEMLERİN AYARLARINI MERKEZI OLARAK SENKRONIZE EDER
    /// </summary>
    private void SynchronizeAllSystems()
    {
        // 1. WORLD GENERATOR
        if (worldGenerator != null)
        {
            worldGenerator.trainingMode = isTrainingMode;
            worldGenerator.enablePopulationDynamics = !isTrainingMode; // Turnuvada dinamik nüfus
            Debug.Log($"[MasterGM] WorldGenerator → TrainingMode: {isTrainingMode}");
        }

        // 2. CONTRACT MANAGER
        if (contractManager != null)
        {
            contractManager.trainingMode = isTrainingMode;
            Debug.Log($"[MasterGM] ContractManager → TrainingMode: {isTrainingMode}");
        }

        // 3. EVENT MANAGER
        if (eventManager != null)
        {
            eventManager.trainingMode = isTrainingMode;
            Debug.Log($"[MasterGM] EventManager → TrainingMode: {isTrainingMode}");
        }

        // 4. CURRICULUM MANAGER (En kritik!)
        if (curriculumManager != null)
        {
            curriculumManager.isTrainingMode = isTrainingMode;
            Debug.Log($"[MasterGM] CurriculumManager → TrainingMode: {isTrainingMode}");
            
            if (!isTrainingMode)
            {
                Debug.Log("<color=yellow>[MasterGM] ⚠️ TURNUVA MODU: CurriculumManager agentlara müdahale etmeyecek!</color>");
            }
        }

        // 5. COMPETITION MANAGER
        if (competitionManager != null)
        {
            if (isTrainingMode)
            {
                competitionManager.enabled = false; // Eğitimde kapalı
                Debug.Log("[MasterGM] CompetitionManager → KAPALI (Eğitim Modu)");
            }
            else
            {
                competitionManager.enabled = true; // Turnuvada açık
                Debug.Log("[MasterGM] CompetitionManager → AKTİF (Turnuva Modu)");
            }
        }

        // 6. TIME MANAGER
        if (timeManager != null)
        {
            // Turnuvada hızlı zaman
            timeManager.realSecondsPerGameDay = isTrainingMode ? 2f : 1f;
            Debug.Log($"[MasterGM] TimeManager → Hız: {timeManager.realSecondsPerGameDay}s/gün");
        }

        Debug.Log("<color=green>[MasterGM] ✓ Tüm sistemler senkronize edildi!</color>");
    }

    /// <summary>
    /// Oyun modunu değiştir (Runtime'da)
    /// </summary>
    public void SwitchMode(bool newTrainingMode)
    {
        if (isTrainingMode == newTrainingMode)
        {
            Debug.LogWarning($"[MasterGM] Zaten {(newTrainingMode ? "Eğitim" : "Turnuva")} modundasınız.");
            return;
        }

        isTrainingMode = newTrainingMode;
        SynchronizeAllSystems();

        Debug.Log($"<color=magenta>[MasterGM] 🔄 Mod Değiştirildi: {(isTrainingMode ? "EĞİTİM" : "TURNUVA")}</color>");
    }

    // Inspector'dan çağırılabilir debug komutları
    [ContextMenu("🎓 Eğitim Moduna Geç")]
    public void SwitchToTraining()
    {
        SwitchMode(true);
    }

    [ContextMenu("🏆 Turnuva Moduna Geç")]
    public void SwitchToTournament()
    {
        SwitchMode(false);
    }

    [ContextMenu("🔍 Sistemleri Kontrol Et")]
    public void DebugCheckSystems()
    {
        Debug.Log("═════════ SİSTEM DURUMU ═════════");
        Debug.Log($"WorldGenerator: {(worldGenerator != null ? "✓" : "✗")} - TrainingMode: {worldGenerator?.trainingMode}");
        Debug.Log($"ContractManager: {(contractManager != null ? "✓" : "✗")} - TrainingMode: {contractManager?.trainingMode}");
        Debug.Log($"EventManager: {(eventManager != null ? "✓" : "✗")} - TrainingMode: {eventManager?.trainingMode}");
        Debug.Log($"CurriculumManager: {(curriculumManager != null ? "✓" : "✗")} - TrainingMode: {curriculumManager?.isTrainingMode}");
        Debug.Log($"CompetitionManager: {(competitionManager != null ? "✓" : "✗")} - Enabled: {competitionManager?.enabled}");
        Debug.Log($"TimeManager: {(timeManager != null ? "✓" : "✗")}");
        Debug.Log($"BrokerManager: {(brokerManager != null ? "✓" : "✗")}");
        Debug.Log("═════════════════════════════════");
    }
}
