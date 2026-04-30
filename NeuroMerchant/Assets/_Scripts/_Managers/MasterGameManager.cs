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

    [Header("Ajan (Kervan) Üretimi")]
    public GameObject merchantAgentPrefab; // Kervanının Prefab'ını buraya sürükleyeceksin
    public Transform agentsContainer;      // Sahnede ajanların duracağı boş obje (Düzen için)

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

    [Header("Lonca Ayarları (Guild Mode)")]
    public Material[] guildMaterials; // Unity Inspector'dan 5 renk materyalini buraya sürükleyeceksin
    public string[] guildNames = { "Yakut Loncası", "Safir Loncası", "Zümrüt Loncası", "Kehribar Loncası", "Obsidyen Loncası" };

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

    // MasterGameManager.cs içinde
    // MasterGameManager.cs içindeki ilgili kısımlar:

    void Start()
    {
        // Yüklenmeler için ufak bir bekleme ve ZİNCİRİ BAŞLAT
        Invoke("StartGameSequence", 0.5f);
    }

    private void StartGameSequence()
    {
        // ==========================================
        // 1. ZİNCİR: DÜNYAYI YARAT
        // ==========================================
        if (worldGenerator != null)
        {
            // Şef kendi güncel modunu (isTrainingMode) WorldGenerator'a yolluyor!
            worldGenerator.GenerateWorld(this.isTrainingMode);
        }
        else
        {
            Debug.LogError("<color=red>[MasterGM] HATA: WorldGenerator bulunamadı! Harita üretilemiyor.</color>");
            return;
        }

        // Şehirlerin Unity'de yerleşmesi için 1 kare bekle ve kalanını çalıştır
        StartCoroutine(SpawnAndStartRoutine());
    }

    private System.Collections.IEnumerator SpawnAndStartRoutine()
    {
        // ESKİ KOD: yield return null; // Sadece 1 kare bekliyordu, yetmedi!
        // YENİ KOD: Şehirlerin "InitializeCity" işlemlerini bitirmesi, 
        // pazar tezgahlarını kurması ve ürünlerini kaydetmesi için yarım saniye bekle.
        yield return new WaitForSeconds(0.5f);

        // ==========================================
        // 1.5 ZİNCİR: YARDIMCI SİSTEMLERİ UYANDIR
        // ==========================================
        if (eventManager != null) eventManager.InitManager(this.isTrainingMode);
        if (contractManager != null) contractManager.InitManager(this.isTrainingMode);

        // ==========================================
        // 2. ZİNCİR: AJANLARI (KERVANLARI) DOĞUR
        // ==========================================
        SpawnAgents(SessionData.AgentCount);

        // ==========================================
        // 3. ZİNCİR: TURNUVAYI BAŞLAT
        // ==========================================
        if (competitionManager != null)
        {
            competitionManager.InitTournament();
            competitionManager.maxDays = SessionData.MaxDays;
        }
    }

    private void SpawnAgents(int count)
    {
        CityController[] allCities = FindObjectsOfType<CityController>();
        if (allCities.Length == 0)
        {
            Debug.LogError("<color=red>[MasterGM] Şehirler bulunamadı! Ajanlar doğamıyor.</color>");
            return;
        }

        // Şehirlerin bağlı olduğu "Ana Odayı" (Root) bul (WorldGenerator objesinin ta kendisi)
        Transform mapRoot = worldGenerator.transform;

        // ========================================================
        // --- LONCA (GUILD) MODU HAZIRLIKLARI ---
        // ========================================================
        bool isGuildMode = (SessionData.CurrentMode == SessionData.GameMode.LoncalarIttifaki);

        // Dinamik Lonca Sayısı (Menüden gelen SessionData.GuildCount)
        int numGuilds = SessionData.GuildCount > 0 ? SessionData.GuildCount : 5;

        // Sana bahsettiğim yuvarlama sistemi: (Örn: 7 Kervan / 5 Lonca -> Her loncaya 2 kişi)
        int agentsPerGuild = Mathf.Max(1, Mathf.CeilToInt((float)count / numGuilds));

        Transform[] guildParents = new Transform[numGuilds];

        // Eğer Lonca modundaysak, önce boş Lonca klasörlerini (Parent) oluştur
        if (isGuildMode)
        {
            for (int i = 0; i < numGuilds; i++)
            {
                string gName = (guildNames != null && guildNames.Length > i) ? guildNames[i] : $"Lonca_{i + 1}";
                GameObject guildObj = new GameObject(gName);
                guildObj.transform.SetParent(mapRoot); // Ana haritanın altına koy
                guildParents[i] = guildObj.transform;
            }
        }
        // ========================================================

        for (int i = 0; i < count; i++)
        {
            CityController randomCity = allCities[Random.Range(0, allCities.Length)];
            Vector3 spawnPos = randomCity.transform.position + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));

            // HANGİ KLASÖRÜN İÇİNE DOĞACAK?
            Transform targetParent = mapRoot; // Standart modda direkt mapRoot içine
            int myGuildIndex = 0;

            if (isGuildMode)
            {
                myGuildIndex = Mathf.Clamp(i / agentsPerGuild, 0, numGuilds - 1);
                targetParent = guildParents[myGuildIndex]; // Lonca modunda kendi lonca klasörünün içine!
            }

            // SİHİR BURADA: Ajanı hedef klasörün (targetParent) içine doğuruyoruz!
            GameObject newAgent = Instantiate(merchantAgentPrefab, spawnPos, Quaternion.identity, targetParent);
            newAgent.name = $"Kervan_{i + 1}";

            // --- YENİ: KERVANI BOYA (MATERYAL DEĞİŞTİR) ---
            if (isGuildMode && guildMaterials != null && guildMaterials.Length > myGuildIndex)
            {
                MeshRenderer[] renderers = newAgent.GetComponentsInChildren<MeshRenderer>();
                foreach (var renderer in renderers)
                {
                    renderer.material = guildMaterials[myGuildIndex];
                }
            }
        }

        Debug.Log($"<color=cyan>[MasterGM] Toplam {count} kervan haritaya yerleştirildi. (Lonca Modu: {isGuildMode})</color>");
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

        // 1. ÖNCE AJANLARI ÜRET (SPAWN)
        SpawnAgents(SessionData.AgentCount);

        // 2. SONRA TURNUVA KURALLARINI UYGULA
        if (competitionManager != null)
        {
            competitionManager.maxDays = SessionData.MaxDays;
        }

        // 3. OYUNCU VS AI DURUMU
        if (SessionData.CurrentType == SessionData.GameType.PlayerVsAI)
        {
            Debug.Log("<color=green>PVE Modu Aktif! Oyuncu kontrolleri açılıyor...</color>");
            // İleride oyuncu scriptini burada aktif edeceksin
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

            case SessionData.GameMode.LoncalarIttifaki:
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
