using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// NEUROMERCHANT - ANA AJAN
// ==============================================================
// Gozlem Mimarisi - 1271 GIRIS (GUNCEL):
//   [22]   Ajan Oz Verisi (Para, Kargo, Konum, Tier, 12 Urun, 3 Padding)
//   [1215] Yerleske Hafizasi (45 Sehir x 27 Veri) 
//          -> (1 Mesafe + 12 Fiyat + 12 Stok + 1 Bilgi Yasi + 1 Uretici Mi)
//   [9]    Dis Sinyaller (Event [3] + Kontrat [3] + Komsu Ajan Padding [3])
//   [25]   Broker Gozlemleri (1 Aktif Broker [5] + 4 Eski Broker Padding [20])
//   TOPLAM: 1271
//
// Aksiyon: 4 Branch (50 / 5 / 5 / 5)
//
// ==============================================================
// Curriculum - 7 Ders (1'den baslar):
// +----+-------------------+----------+---------------------------------------+
// | D. | Isim              | Yerleske | Teknik Acilim                         |
// +----+-------------------+----------+---------------------------------------+
// |  1 | Temel Ticaret     |    9     | 1 urun (Wheat), omniscient            |
// |  2 | Coklu Urun        |   18     | 4 urun aktif, omniscient              |
// |  3 | Branch Kontrolu   |   18     | sell/buy branch aktif                 |
// |  4 | Fog of War        |   27     | hafiza yasi gercek zamana baglar      |
// |  5 | Broker Erisimi    |   36     | broker branch aktif                   |
// |  6 | Tam Ekonomi       |   45     | 12 urun aktif                         |
// |  7 | Kriz Yonetimi     |   45     | Event + Kontrat sinyalleri aktif      |
// +----+-------------------+----------+---------------------------------------+
// ==============================================================

public class MerchantAgent : Agent
{
    // ----------------------------------------------------------
    // INSPECTOR
    // ----------------------------------------------------------
    [Header("🎮 Oyun Modu")]
    public bool isTournamentMode = false;

    [Header("Debug")]
    public bool enableDebugLogs = false; // YENİ!

    [Header("Ekonomi")]
    public float currentMoney = 1000f;
    public float startingMoney = 2000f;
    public int contractPoints = 0;
    public int completedContractsCount = 0;

    [Header("Haydut ve Koruma")]
    public float robberyImmunityDuration = 10f; // Soyulduktan sonra 10 saniye kalkan
    private float robberyImmunityTimer = 0f;
    public bool IsImmuneToRobbery() => robberyImmunityTimer > 0f;

    [Header("Dynamic Training Controls")]
    public float movementPenalty = -0.00005f;
    public float invalidActionPenalty = -0.001f;
    public float invalidTargetPenalty = -0.05f;
    public float profitRewardMultiplier = 0.01f;

    [Header("Cache")]
    private List<ItemData> cachedActiveItems = new List<ItemData>();
    private int cachedLessonForItems = -1;

    [Header("Kapasite Sistemi")]
    public int currentTier = 0;
    public int maxCapacity = 20;
    private static readonly int[] TierCapacities = { 20, 50, 100 };

    [Header("Kargo")]
    public ItemData carriedItemData;
    public int carriedAmount;
    private float lastCargoCost;
    private CityController lastBuyCity;
    private CityController lastSellCity;

    [Header("Trade Planning")]
    private float lastExpectedProfit;
    private float lastExpectedSellPrice;
    private int lastTravelDays;
    private ItemData lastPlannedItem;
    private CityController lastPlannedBuyCity;
    private CityController lastPlannedSellCity;
    private float lastBuyPrice;
    private float averageBuyPrice;
    private CityController lastVisitedCity;

    [Header("Training  rn Listesi (Ders sirasina gre)")]
    public ItemData itemWheat;    // Ders 1+ (baslangic urunu)
    public ItemData itemIron;     // Ders 2+ (coklu urun)
    public ItemData itemCoal;     // Ders 2+ (coklu urun)
    public ItemData itemCotton;   // Ders 2+ (coklu urun)
    // Ders 6'da tm rnler ailir  WorldGenerator'daki liste kullanilir

    [Header("Episode")]
    public int maxStepsPerEpisode = 15000;
    private bool brokerActionTakenThisVisit = false;

    [Header("Baglantilar")]
    public CurriculumManager curriculumManager;
    // Multi-env: Inspector'dan atanmazsa parent Transform'dan otomatik bulunur
    public BrokerManager localBrokerManager;
    private float deneme;

    // ----------------------------------------------------------
    // MIMARI SABITLER
    // ----------------------------------------------------------
    private const int MAX_SETTLEMENTS = 45;
    private const int MAX_BROKERS = 5;
    private const float REWARD_FACTOR = 0.01f;

    private static readonly float[] AmountRatios = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

    // Ders bazli ozellik acilim esikleri (1'den baslar)
    // Ders 1: 18 yerleske, 1 urun, omniscient
    // Ders 2: 27 yerleske, 4 urun, omniscient
    // Ders 3: 36 yerleske, 4 urun, sell/buy branch
    // Ders 4: 45 yerleske, 4 urun, FOG OF WAR
    // Ders 5: 45 yerleske, 4 urun, broker branch
    // Ders 6: 45 yerleske, 12 urun
    // Ders 7: 45 yerleske, 12 urun, event+kontrat
    private const int LESSON_SELL_BRANCH = 3;
    private const int LESSON_BUY_BRANCH = 3;
    private const int LESSON_MULTI_PRODUCT = 2; // 4 urun Ders 2'de
    private const int LESSON_FOG_OF_WAR = 4;
    private const int LESSON_BROKER_BRANCH = 5;
    private const int LESSON_FULL_PRODUCTS = 6; // 12 urun
    private const int LESSON_EXT_SIGNALS = 7;

    // Ders basina aktif urun sayisi [index=ders], ders 1'den baslar
    private static readonly int[] LessonProductCount = { 1, 1, 4, 4, 4, 4, 12, 12 };

    // ----------------------------------------------------------
    // BROKER
    // ----------------------------------------------------------
    private bool boughtLocalInfo = false;
    private bool boughtGlobalInfo = false;
    // Broker listesine localBrokerManager.brokers uzerinden erisiliyor

    // ----------------------------------------------------------
    // CURRICULUM
    // ----------------------------------------------------------
    [HideInInspector] public int currentLesson = 1; // 1'den baslar
    // Ders 1:  9 yerleske | 1 urun  | omniscient
    // Ders 2: 18 yerleske | 4 urun  | omniscient
    // Ders 3: 18 yerleske | 4 urun  | sell/buy branch aktif
    // Ders 4: 27 yerleske | 4 urun  | fog of war
    // Ders 5: 36 yerleske | 4 urun  | broker branch aktif
    // Ders 6: 45 yerleske | 12 urun | tam ekonomi
    // Ders 7: 45 yerleske | 12 urun | event + kontrat
    // [index=ders], ders 1'den baslar (index 0 kullanilmaz)
    // Ders 1:9 | Ders 2:18 | Ders 3:18 | Ders 4:27 | Ders 5:36 | Ders 6:45 | Ders 7:45
    private static readonly int[] LessonSettlementCount = { 9, 9, 18, 18, 27, 36, 45, 45 };

    // Step bazli raporlama
    private float episodeCumulativeReward = 0f;
    private int globalStepCount = 0;
    private int lastReportedStep = 0;
    private float stepWindowReward = 0f;
    private int stepWindowEpisodes = 0;
    private const int STEP_WINDOW = 50000;
    private const int WARMUP_STEPS = 0;   // Episode basinda bekleme adimi
    private int warmupStepCount = 0;

    // ----------------------------------------------------------
    // HAFIZA
    // ----------------------------------------------------------
    private Dictionary<int, SettlementMemory> memoryMap = new Dictionary<int, SettlementMemory>();
    private MerchantAgent[] cachedAllies;

    // TEKEL SAVAŞLARI: Ajanın sattığı malların çetelesi
    public Dictionary<string, int> soldItemsTracker = new Dictionary<string, int>();

    // ----------------------------------------------------------
    // HAREKET
    // ----------------------------------------------------------
    private NavMeshAgent navAgent;
    private bool isMoving = false;
    private CityController currentDestination;
    private List<CityController> allSettlements;

    // Bekleyen aksiyon kararlari
    private int pendingBrokerAction = 0;
    private int pendingBuyAmountIndex = 4;
    private int pendingSellAmountIndex = 4;

    // ==========================================================
    // BASLANGIC
    // ==========================================================
    public override void Initialize()
    {
        navAgent = GetComponent<NavMeshAgent>();

        cachedAllies = FindObjectsOfType<MerchantAgent>(true);

        if (curriculumManager == null)
        {
            curriculumManager = FindObjectOfType<CurriculumManager>();
        }

        // Eğitimdeysek hocayı (Curriculum) dinle
        if (curriculumManager != null && curriculumManager.isTrainingMode)
        {
            currentLesson = curriculumManager.currentLesson;
        }
        else
        {
            // Turnuva Modundaysak veya Hoca yoksa, Ajanlar otomatik "Master" (Ders 7) sayılır!
            currentLesson = 7; // Tüm ürünler (Ders 6) ve Event Sinyalleri (Ders 7) açık.
            if (enableDebugLogs) Debug.Log($"<color=green>[MASTER AGENT]</color> {gameObject.name} Turnuva modunda Master (Ders 7) olarak uyandı!");
        }

        // Multi-env: Inspector'dan atanmadiysa kendi TrainingArea'sindan bul
        if (localBrokerManager == null)
            localBrokerManager = GetComponentInParent<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = transform.root.GetComponentInChildren<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = FindObjectOfType<BrokerManager>();

        // allSettlements yuklemesi: WorldGenerator Start()'ta bitmis olsun diye
        // OnEpisodeBegin'de de tekrar cagrilir (LoadSettlements)
        LoadSettlements();

        for (int i = 0; i < MAX_SETTLEMENTS; i++)
            memoryMap[i] = new SettlementMemory();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= OnNewDay;
            TimeManager.Instance.OnNewDay += OnNewDay;
        }
    }

    void Update()
    {
        // Dokunulmazlık sayacını zamanla azalt
        if (robberyImmunityTimer > 0f)
        {
            robberyImmunityTimer -= Time.deltaTime;
        }
    }

    // ==========================================================
    // EPISODE BASLANGICI + STEP BAZLI RAPORLAMA
    // ==========================================================
    public override void OnEpisodeBegin()
    {
        // ==========================================
        // 1. GÜVENLİK ZIRHI (NULL CHECK)
        // ==========================================
        // Eğer WorldManager (veya şehirleri tutan ana script) henüz yüklenmediyse işlemi iptal et.
        // Senin projende şehirleri ne tutuyorsa (Örn: WorldGenerator, CityManager) onu kontrol et:
        if (FindObjectOfType<CityController>() == null)
        {
            Debug.LogWarning($"<color=yellow>[{gameObject.name}] OnEpisodeBegin iptal edildi: Şehirler henüz yüklenmedi!</color>");
            return;
        }

        if (allSettlements == null || allSettlements.Count == 0) Initialize();
        LoadSettlements();

        // 1. ZAMAN VE ADIM SINIRLARI
        bool inTournament = CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive;
        if (inTournament)
        {
            this.MaxStep = 0;
            this.maxStepsPerEpisode = 0;
        }
        else
        {
            maxStepsPerEpisode = 25000;
            /*
            switch (currentLesson)
            {
                case 1: maxStepsPerEpisode = 12000; break;
                case 2: maxStepsPerEpisode = 12500; break;
                case 3: maxStepsPerEpisode = 13000; break;
                case 4: maxStepsPerEpisode = 135000; break;
                case 5: maxStepsPerEpisode = 14000; break;
                case 6: maxStepsPerEpisode = 14500; break;
                case 7: maxStepsPerEpisode = 180000; break;
                default: maxStepsPerEpisode = 14000; break;
            }
            */
        }

        // 2. SIFIRLAMALAR
        currentMoney = startingMoney;
        carriedAmount = 0;
        carriedItemData = null;
        lastCargoCost = 0f;
        lastBuyCity = null;
        lastSellCity = null;
        isMoving = false; // DONMAYI ÖNLER
        currentDestination = null;
        episodeCumulativeReward = 0f;
        boughtLocalInfo = false;
        boughtGlobalInfo = false;
        currentTier = 0;
        maxCapacity = TierCapacities[0];
        pendingBrokerAction = 0;
        warmupStepCount = 0;

        if (navAgent != null && navAgent.isOnNavMesh) navAgent.ResetPath();

        // 3. IŞINLANMA (Haritaya dağınık ve rastgele yerleştirme)
        int active = ActiveSettlementCount();

        // ESKİ KOD: Sadece şehirlere doğuruyordu (.Where(s => !s.isProducer) kısmı silindi)
        var startCities = allSettlements.Take(active).ToList();

        if (startCities.Count > 0)
        {
            // Ajanların tam üst üste binmemesi için X ve Z ekseninde -10 ile +10 arası rastgele dağıtıyoruz
            Vector3 randomOffset = new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
            transform.position = startCities[Random.Range(0, startCities.Count)].transform.position + randomOffset;
        }
        else if (allSettlements.Count > 0)
        {
            transform.position = allSettlements[0].transform.position;
        }
    }

    // ==========================================================
    // GOZLEMLER - 281 GIRIS
    // ==========================================================
    // ==========================================================
    // GOZLEMLER - 1271 GIRIS (AKILLI RADAR UYUMLU)
    // ==========================================================
    public override void CollectObservations(VectorSensor sensor)
    {
        // ========================================================
        // 1. ORTAK AKILLI RADAR: Hedef kontratı EN BAŞTA 1 KERE belirliyoruz!
        // ========================================================
        ContractManager.ActiveContract targetContract = null;

        if (currentLesson >= LESSON_EXT_SIGNALS && ContractManager.Instance != null && ContractManager.Instance.activeContracts.Count > 0)
        {
            // Önce kervandaki mala uygun kontratı ara
            if (carriedAmount > 0 && carriedItemData != null)
            {
                targetContract = ContractManager.Instance.activeContracts.Find(c => c.requiredItem == carriedItemData);
            }

            // Bulamazsa (veya kervan boşsa) listedeki ilk göreve odaklan
            if (targetContract == null)
            {
                targetContract = ContractManager.Instance.activeContracts[0];
            }
        }

        // ---- BLOK A: AJAN OZ VERISI (22) ----
        sensor.AddObservation(currentMoney / 10000f);
        sensor.AddObservation(carriedAmount / (float)maxCapacity);
        sensor.AddObservation(transform.position.x / 500f);
        sensor.AddObservation(transform.position.z / 500f);
        sensor.AddObservation(currentTier / 2f);
        sensor.AddObservation(boughtLocalInfo ? 1f : 0f);
        sensor.AddObservation(boughtGlobalInfo ? 1f : 0f);

        int activeProducts = LessonProductCount[Mathf.Clamp(currentLesson, 0, 7)];
        var activeItems = GetActiveItems();
        for (int i = 0; i < 12; i++)
        {
            if (i < activeItems.Count && carriedItemData == activeItems[i] && carriedAmount > 0)
                sensor.AddObservation(carriedAmount / (float)maxCapacity);
            else
                sensor.AddObservation(0f);
        }

        sensor.AddObservation(0f); // padding 20
        sensor.AddObservation(0f); // padding 21
        sensor.AddObservation(0f); // padding 22

        // ---- BLOK B: YERLESKE HAFIZASI (45 x 27 = 1215) ----
        for (int i = 0; i < MAX_SETTLEMENTS; i++)
        {
            if (IsSettlementActive(i) && i < allSettlements.Count)
            {
                CityController s = allSettlements[i];
                var mem = memoryMap[i];
                float dist = Vector3.Distance(transform.position, s.transform.position);

                sensor.AddObservation(dist / 500f);

                for (int p = 0; p < 12; p++) sensor.AddObservation(mem.knownPrices[p] / 500f);
                for (int k = 0; k < 12; k++) sensor.AddObservation(mem.knownStocks[k]);

                float normalizedAge = 0f;
                if (currentLesson >= 4)
                {
                    float rawAge = mem.GetInformationAge();
                    normalizedAge = Mathf.Clamp01(rawAge / 30f);
                }
                sensor.AddObservation(normalizedAge);
                sensor.AddObservation(s.isProducer ? 1f : 0f);
            }
            else
            {
                sensor.AddObservation(0f); // dist
                for (int p = 0; p < 12; p++) sensor.AddObservation(0f); // prices
                for (int k = 0; k < 12; k++) sensor.AddObservation(0f); // stocks
                sensor.AddObservation(0f); // age
                sensor.AddObservation(0f); // isProducer
            }
        }

        // ---- BLOK C: DIS SINYALLER (9) ----
        if (currentLesson >= LESSON_EXT_SIGNALS && EventManager.Instance != null)
        {
            // Event (3)
            var events = EventManager.Instance.activeEvents;
            if (events.Count > 0)
            {
                var evt = events[0];
                sensor.AddObservation(GetEventTypeNormalized(evt.name));
                sensor.AddObservation(Mathf.Clamp01(evt.consumptMod > 1f ? evt.consumptMod / 3f : evt.productMod / 2f));
                sensor.AddObservation(Mathf.Clamp01(1f - (float)evt.daysElapsed / Mathf.Max(evt.durationDays, 1)));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            // Kontrat (3) -> YUKARIDAKI ORTAK targetContract KULLANILIYOR
            if (targetContract != null)
            {
                sensor.AddObservation(1f);
                sensor.AddObservation(allSettlements.IndexOf(targetContract.targetCity) / (float)MAX_SETTLEMENTS);
                sensor.AddObservation(targetContract.daysLeft / 150f); // 150 GÜNLÜK YENİ SINIR NORMALIZE EDİLDİ
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
        else
        {
            // Ders 7'den once - padding (6 slot)
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
        }

        // Komsu ajan (3)
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);

        // ---- BLOK D: BROKER GOZLEMLERI (25) ----
        if (localBrokerManager != null)
        {
            sensor.AddObservation(1f);                                          // broker mevcut
            sensor.AddObservation(localBrokerManager.tier1Cost / 50000f);       // tier1 maliyet
            sensor.AddObservation(localBrokerManager.tier2Cost / 50000f);       // tier2 maliyet
            sensor.AddObservation(!boughtLocalInfo ? 1f : 0f);                  // local info alinabilir
            sensor.AddObservation(!boughtGlobalInfo ? 1f : 0f);                 // global info alinabilir
        }
        else
        {
            for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
        }

        // Oyun Modu (5)
        for (int i = 0; i < 5; i++)
        {
            sensor.AddObservation((int)SessionData.CurrentMode == i ? 1.0f : 0.0f);
        }

        // ========================================================
        // KALAN 15 SLOT (YENİ KONTRAT DETAYLARI)
        // ========================================================
        if (targetContract != null)
        {
            // 12 Slot -> Ürün ID (Binary)
            for (int i = 0; i < 12; i++)
            {
                if (i < activeItems.Count && activeItems[i] == targetContract.requiredItem)
                    sensor.AddObservation(1.0f);
                else
                    sensor.AddObservation(0.0f);
            }

            // 1 Slot -> Miktar
            float normalizedAmount = Mathf.Clamp01((float)targetContract.requiredAmount / 100f);
            sensor.AddObservation(normalizedAmount);

            // 2 Slot -> Boşluk
            sensor.AddObservation(0.0f);
            sensor.AddObservation(0.0f);
        }
        else
        {
            for (int i = 0; i < 15; i++) sensor.AddObservation(0f);
        }

        // TOPLAM: 22 + 1215 + 9 + 25 = 1271
    }

    // ==========================================================
    // AKSIYONLAR - 4 BRANCH
    // ==========================================================
    public override void OnActionReceived(ActionBuffers actions)
    {

        // 1. TURNUVA DURUMUNU GÜVENLİ KONTROL ET
        bool inTournament = CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive;

        // 2. ADIM SINIRI KONTROLÜ (Sıfır Tuzağı Düzeltildi)
        if (!inTournament && maxStepsPerEpisode > 0 && StepCount >= maxStepsPerEpisode)
        {
            // Episode bitti, elindeki mal için küçük ceza
            if (carriedAmount > 0) AddReward(-0.01f);
            if (enableDebugLogs) Debug.Log($"<color=orange>[MAXSTEP]</color> Episode bitti | Para:{currentMoney:F0}G | Ders:{currentLesson}");
            SafeEndEpisode("Maksimum Adıma (Süreye) Ulaşıldı");
            return;
        }

        // Warmup: episode basinda N adim bekle (sehir resetinin tamamlanmasi icin)
        warmupStepCount++;
        if (warmupStepCount <= WARMUP_STEPS) return;

        // Her adimda global sayaci artir, curriculum penceresi doldu mu kontrol et
        globalStepCount++;
        if (globalStepCount - lastReportedStep >= STEP_WINDOW &&
            stepWindowEpisodes > 0 && curriculumManager != null)
        {
            float avg = stepWindowReward / stepWindowEpisodes;
            curriculumManager.ReportStepWindow(avg, currentLesson);
            lastReportedStep = globalStepCount;
            stepWindowReward = 0f;
            stepWindowEpisodes = 0;
            if (enableDebugLogs) Debug.Log($"[Curriculum] Pencere raporlandi | Step:{globalStepCount} | Ort:{avg:F3} | Ders:{currentLesson}");
        }

        // Ders 1-3 (omniscient): her adimda tum aktif yerleskelerin hafizasi guncellenir
        // Ders 4+ (fog of war): sadece bizzat ziyaret edilen yerleske guncellenir
        if (currentLesson < LESSON_FOG_OF_WAR)
        {
            int active = ActiveSettlementCount();
            var activeItems = localBrokerManager?.activeItems;

            for (int i = 0; i < active && i < allSettlements.Count; i++)
            {
                var s = allSettlements[i];
                float[] prices = new float[12];
                float[] stocks = new float[12];

                for (int p = 0; p < 12; p++)
                {
                    if (activeItems != null && p < activeItems.Count)
                    {
                        var itemData = activeItems[p];
                        var mi = s.marketItems.Find(x => x.itemData == itemData);
                        prices[p] = (mi != null) ? s.GetPrice(itemData) : 0f;
                        stocks[p] = (mi != null && mi.maxStock > 0) ? (float)mi.currentStock / mi.maxStock : 0f;
                    }
                    else
                    {
                        prices[p] = 0f;
                        stocks[p] = 0f;
                    }
                }
                memoryMap[i].UpdateAlwaysFresh(prices, stocks);
            }
        }

        if (isMoving)
        {
            if (!navAgent.pathPending && navAgent.hasPath && navAgent.remainingDistance <= 2.0f)
            {
                isMoving = false;
                HandleArrivalInteraction();
            }
            else
            {
                // Yolda geen her adim iin ok kk zaman cezasi
                AddReward(-0.00001f);
            }
            return;
        }

        int targetIndex = actions.DiscreteActions[0];

        // Branch 1: Broker aksiyonu (Ders 5'ten aktif)
        pendingBrokerAction = (currentLesson >= LESSON_BROKER_BRANCH)
            ? actions.DiscreteActions[1] : 0;

        // Branch 2: Alim miktari (Ders 3'ten aktif)
        pendingBuyAmountIndex = (currentLesson >= LESSON_BUY_BRANCH)
            ? Mathf.Clamp(actions.DiscreteActions[2], 0, 4) : 4;

        // Branch 3: Satis miktari (Ders 3'ten aktif)
        pendingSellAmountIndex = (currentLesson >= LESSON_SELL_BRANCH)
            ? Mathf.Clamp(actions.DiscreteActions[3], 0, 4) : 4;

        if (targetIndex < 0 || targetIndex >= allSettlements.Count || !IsSettlementActive(targetIndex))
        {
            AddReward(-0.001f);
            return;
        }

        CityController target = allSettlements[targetIndex];

        if (carriedAmount > 0 && target == lastBuyCity)
        {
            AddReward(-0.05f);
            CityController best = GetBestSellCity();
            if (best != null)
            {
                currentDestination = best;
                navAgent.SetDestination(best.transform.position);
                isMoving = true;
            }
            return;
        }

        if (Vector3.Distance(transform.position, target.transform.position) > 3.0f)
        {
            currentDestination = target;
            navAgent.SetDestination(target.transform.position);
            isMoving = true;
        }
        else
        {
            currentDestination = target;
            HandleArrivalInteraction();
        }
    }

    int GetLastKnownPrice(CityController city, ItemData item)
    {
        int idx = allSettlements.IndexOf(city);
        if (idx >= 0 && memoryMap.ContainsKey(idx))
        {
            var mem = memoryMap[idx];
            int itemIndex = GetActiveItems().IndexOf(item);
            if (itemIndex >= 0 && itemIndex < 12)
                return Mathf.RoundToInt(mem.knownPrices[itemIndex]);
        }
        return 0;
    }

    // ==========================================================
    // VARIS - TICARET (RL DOSTU + DETAYLI LOG VERSİYONU)
    // ==========================================================
    void HandleArrivalInteraction()
    {
        if (currentDestination == null) return;

        brokerActionTakenThisVisit = false;

        int idx = allSettlements.IndexOf(currentDestination);
        UpdateMemory(idx, currentDestination);

        if (currentMoney > 300)
        {
            HandleBrokerAction();
        }
        else if (enableDebugLogs)
        {
            Debug.Log($"<color=grey>[{gameObject.name}] Para az ({currentMoney}G), Broker pas geçildi.</color>");
        }

        // Derse gore o an bulunulan sehirdeki en karli urunu sec
        ItemData activeItem = GetBestAvailableItem(currentDestination);

        // Şehir hafızasını güncelle
        lastVisitedCity = currentDestination;

        // --- ALIM: kargo bos, uygun urun varsa al ---
        if (carriedAmount == 0)
        {
            if (activeItem == null) { AddReward(-0.001f); return; }

            var marketItem = currentDestination.marketItems.Find(x => x.itemData == activeItem);
            if (marketItem != null && marketItem.currentStock > 0)
            {
                int alisFiyati = currentDestination.GetPrice(activeItem);
                float ratio = AmountRatios[pendingBuyAmountIndex];
                int wantToBuy = Mathf.Max(1, Mathf.RoundToInt(maxCapacity * ratio));

                int availableStock = currentDestination.isProducer
                    ? Mathf.Max(0, marketItem.currentStock - marketItem.dailyTax)
                    : marketItem.currentStock;

                int amount = Mathf.Min((int)(currentMoney / alisFiyati), wantToBuy, availableStock);

                if (amount > 0)
                {
                    currentMoney -= amount * alisFiyati;
                    marketItem.currentStock -= amount;
                    carriedAmount = amount;
                    carriedItemData = activeItem;
                    lastCargoCost = amount * alisFiyati;
                    lastBuyCity = currentDestination;
                    averageBuyPrice = (float)lastCargoCost / carriedAmount;

                    AddReward(0.05f); // Başarılı alım ödülü

                    // --- DETAYLI GÜVENLİ LOG (AJANA MÜDAHALE ETMEZ) ---
                    if (enableDebugLogs)
                    {
                        // Ajanın hafızasındaki en iyi satış yerini sadece ekrana yazdırmak için buluyoruz
                        CityController bestExpectedCity = null;
                        int bestExpectedPrice = 0;
                        for (int i = 0; i < allSettlements.Count; i++)
                        {
                            if (!IsSettlementActive(i) || allSettlements[i] == currentDestination) continue;
                            int kPrice = GetLastKnownPrice(allSettlements[i], activeItem);
                            if (kPrice > bestExpectedPrice)
                            {
                                bestExpectedPrice = kPrice;
                                bestExpectedCity = allSettlements[i];
                            }
                        }

                        string hedefStr = bestExpectedCity != null
                            ? $"{bestExpectedCity.cityName} (Beklenen: {bestExpectedPrice}G)"
                            : "Bilinmiyor/Hafıza Boş";

                        Debug.Log($"<color=cyan>========== [ALIM YAPILDI & PLAN] ==========</color>\n" +
                                  $"<color=cyan>📍 Nereden Alındı:</color> {currentDestination.cityName}\n" +
                                  $"<color=cyan>📦 Ne Alındı:</color> {amount}x {activeItem.itemName}\n" +
                                  $"<color=cyan>💰 Alış Fiyatı:</color> {alisFiyati} G/birim\n" +
                                  $"<color=cyan>💸 Toplam Maliyet:</color> {amount * alisFiyati} G\n" +
                                  $"<color=cyan>🎯 Planlanan Satış Yeri:</color> {hedefStr}\n" +
                                  $"<color=cyan>===========================================</color>");
                    }
                }
                else AddReward(-0.001f);
            }
            else AddReward(-0.001f);
        }
        // --- SATIS: kargo dolu ---
        else
        {


            if (currentDestination == lastBuyCity) { AddReward(-0.05f); return; }

            var targetItem = currentDestination.marketItems.Find(x => x.itemData == carriedItemData);
            if (targetItem != null)
            {
                float ratio = AmountRatios[pendingSellAmountIndex];
                int amountSell = Mathf.Max(1, Mathf.RoundToInt(carriedAmount * ratio));

                if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi && ContractManager.Instance != null)
                {
                    var ihale = ContractManager.Instance.activeContracts.Find(c => c.targetCity == currentDestination && c.requiredItem == carriedItemData);

                    if (ihale != null)
                    {
                        // Ajan ihaleyi tamamlayacak kadar malı getirdiyse ama beyni "az satayım" diyorsa, onu eziyoruz!
                        if (carriedAmount >= ihale.requiredAmount && amountSell < ihale.requiredAmount)
                        {
                            amountSell = carriedAmount; // Tüm kargoyu ihaleye bas!
                            if (enableDebugLogs) Debug.Log($"<color=magenta>[SİSTEM MÜDAHALESİ]</color> {gameObject.name} kısmî satış yapacaktı, ihaleyi alması için miktar {amountSell} olarak zorlandı!");
                        }
                    }
                }

                if (!soldItemsTracker.ContainsKey(carriedItemData.itemName))
                    soldItemsTracker[carriedItemData.itemName] = 0;

                soldItemsTracker[carriedItemData.itemName] += amountSell;

                float costPortion = lastCargoCost * ((float)amountSell / Mathf.Max(carriedAmount, 1));
                int satisFiyatiBirim = currentDestination.GetPrice(carriedItemData);
                int satisFiyatiToplam = currentDestination.GetBulkSellValue(carriedItemData, amountSell);

                // ========================================================
                // --- YENİ: İHALE TAMAMLAMA KONTROLÜ (SARAYIN ELÇİSİ) ---
                // ========================================================
                int ihaleOdulu = 0;
                int kazanilanPuan = 0;
                bool ihaleTamamlandiMi = false;

                if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi && ContractManager.Instance != null)
                {
                    // Ajan elindeki malı şehre basar. Eğer ihale şartlarını sağlıyorsa ihaleyi kapatır!
                    ihaleTamamlandiMi = ContractManager.Instance.TryCompleteContract(currentDestination, carriedItemData, amountSell, out ihaleOdulu, out kazanilanPuan);
                }

                if (ihaleTamamlandiMi)
                {
                    satisFiyatiToplam = ihaleOdulu;
                    this.contractPoints += kazanilanPuan;
                    this.completedContractsCount++;

                    // --- ŞALTERE BAĞLANDI ---
                    if (enableDebugLogs)
                        Debug.Log($"<color=yellow>🏆 İHALE TAMAMLANDI: {gameObject.name} krallığa malı teslim etti! (+{kazanilanPuan} Puan / {ihaleOdulu} G)</color>");
                }
                // ========================================================

                float profit = satisFiyatiToplam - costPortion;
                float alisFiyatiOrtalama = lastCargoCost / carriedAmount;

                currentMoney += satisFiyatiToplam;
                targetItem.currentStock += amountSell;
                carriedAmount -= amountSell;
                lastCargoCost -= costPortion;

                // --- DETAYLI GÜVENLİ LOG (SONUÇLAR) ---
                if (enableDebugLogs)
                {
                    string karZararRengi = profit > 0 ? "green" : "red";
                    string logMessage = $"<color={karZararRengi}>========== [GERÇEKLEŞEN SATIŞ] ==========</color>\n" +
                                        $"<color={karZararRengi}>📍 Nerede Satıldı:</color> {currentDestination.cityName}\n" +
                                        $"<color={karZararRengi}>📦 Satılan Miktar:</color> {amountSell}x {carriedItemData?.itemName}\n" +
                                        $"<color={karZararRengi}>📉 Ortalama Maliyet:</color> {alisFiyatiOrtalama:F1} G/birim\n" +
                                        $"<color={karZararRengi}>📈 Gerçek Satış Fiyatı:</color> {satisFiyatiBirim} G/birim\n" +
                                        $"<color={karZararRengi}>💵 Satış Geliri (Bu parti):</color> {satisFiyatiToplam} G\n" +
                                        $"<color={karZararRengi}>📊 GERÇEK KAR/ZARAR:</color> {profit:F0} G\n";
                    if (carriedAmount > 0)
                    {
                        float kalanAlisFiyatiOrt = lastCargoCost / carriedAmount;
                        logMessage += $"<color={karZararRengi}>📦 Kalan Mal:</color> {carriedAmount} birim (Ort. alış: {kalanAlisFiyatiOrt:F1}G)\n";
                    }
                    logMessage += $"<color={karZararRengi}>=========================================</color>";
                    Debug.Log(logMessage);
                }

                // Kar/Zarara göre ödül/ceza
                if (profit < 0)
                {
                    float basePenalty = Mathf.Clamp(profit * REWARD_FACTOR * 2.0f, -3.0f, 0f);
                    AddReward(basePenalty);
                }

                switch (SessionData.CurrentMode)
                {
                    case SessionData.GameMode.AltinYolu:
                        if (profit > 0)
                        {
                            float reward = Mathf.Clamp(profit * REWARD_FACTOR, 0f, 1.5f);
                            AddReward(reward);
                        }
                        else
                        {
                            float penalty = Mathf.Clamp(profit * REWARD_FACTOR * 1.5f, -2.0f, 0f);
                            AddReward(penalty);
                        }
                        break;

                    case SessionData.GameMode.SarayinElcisi:
                        if (profit > 0 && !ihaleTamamlandiMi)
                        {
                            // Sadece al-sat yaparak kâr ederse alacağı ödülü artırdık (0.5'e kadar)
                            AddReward(Mathf.Clamp(profit * REWARD_FACTOR * 0.2f, 0f, 0.5f));
                        }
                        else if (profit < 0)
                        {
                            AddReward(Mathf.Clamp(profit * REWARD_FACTOR * 0.5f, -0.5f, 0f));
                        }

                        if (ihaleTamamlandiMi)
                        {
                            // ESKİ: 2.0f ile 8.0f arasıydı.
                            // YENİ: İhale ödülü artık çok daha çekici (5.0f ile 20.0f arası!)
                            float contractReward = Mathf.Clamp(ihaleOdulu * REWARD_FACTOR * 1.5f, 5.0f, 20.0f);
                            AddReward(contractReward);
                            if (enableDebugLogs) Debug.Log($"<color=yellow>[ÖDÜL] İhale tamamlandı! +{contractReward:F1} Puan</color>");
                        }
                        break;

                    case SessionData.GameMode.TekelSavaslari:
                        if (profit > 0)
                        {
                            float baseReward = Mathf.Clamp(profit * REWARD_FACTOR, 0f, 1.5f);
                            int totalSoldOfThisItem = soldItemsTracker.ContainsKey(carriedItemData.itemName) ? soldItemsTracker[carriedItemData.itemName] : 1;

                            // Her 10 üründe çarpan 0.1 artar. Örn: 30 ürün sattıysa multiplier = 1.3
                            float tekelMultiplier = 1.0f + (totalSoldOfThisItem / 10) * 0.1f;
                            tekelMultiplier = Mathf.Clamp(tekelMultiplier, 1.0f, 3.0f);

                            AddReward(baseReward * tekelMultiplier);
                        }
                        else
                        {
                            float penalty = Mathf.Clamp(profit * REWARD_FACTOR * 2.0f, -3.0f, 0f);
                            AddReward(penalty);
                        }
                        break;

                    case SessionData.GameMode.LoncalarIttifaki:
                        // TAKIM MODU: Kârı (veya Zararı) tüm sahaya (loncaya) dağıt!
                        if (profit > 0)
                        {
                            float groupReward = Mathf.Clamp(profit * REWARD_FACTOR, 0f, 1.5f);
                            if (cachedAllies != null)
                            {
                                foreach (var ally in cachedAllies) { ally.AddReward(groupReward); }
                            }
                        }
                        else
                        {
                            float groupPenalty = Mathf.Clamp(profit * REWARD_FACTOR, -1.5f, 0f);
                            if (cachedAllies != null)
                            {
                                foreach (var ally in cachedAllies) { ally.AddReward(groupPenalty); }
                            }
                        }
                        break;

                    case SessionData.GameMode.AcimasizKis:
                        if (profit > 0) AddReward(0.1f);
                        else AddReward(-0.2f);
                        break;
                }

                if (carriedAmount <= 0)
                {
                    carriedAmount = 0;
                    carriedItemData = null;
                    lastCargoCost = 0f;
                    lastSellCity = currentDestination;
                    lastBuyCity = null;
                    averageBuyPrice = 0f;
                }
                return;
            }
            else AddReward(-0.001f);
        }

        // ===============================================
        // İFLAS KONTROLÜ
        // ===============================================
        if (currentMoney <= 0 && carriedAmount == 0)
        {
            // Turnuvada mıyız kontrol et
            bool inTournament = CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive;

            if (inTournament)
            {
                // TURNUVADAYSAK SADECE OYUNDAN AT (RESETLEME)
                Debug.Log($"<color=red>💀 [{gameObject.name}] İFLAS ETTİ! Tüm parasını harcadı ve malı yok. Elendi.</color>");
                gameObject.SetActive(false);
            }
            else
            {
                // EĞİTİMDEYSEK RESETLE
                if (enableDebugLogs) Debug.LogWarning($"<color=red>[IFLAS]</color> Para bitti ve kargo boş!");
                AddReward(-2f);
                SafeEndEpisode("İflas - Para Sıfırlandı");
            }
            return; // İşlemi anında kes
        }

        bool isTurnuva = CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive;

        // EĞER TURNUVADA DEĞİLSEK (Sadece eğitimdeysek) hedefi kontrol et!
        if (!isTurnuva)
        {
            float moneyGoal = 2000f + currentLesson * 1000f; // Normalde 9000G

            // ---> İZOLE EĞİTİM: ELÇİ MODUNDA HEDEF PARAYI UÇUR! <---
            if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi)
            {
                moneyGoal = 999999f; // Ajanın tek seferde resetlenmemesi için hedefi ulaşılmaz yapıyoruz.
            }

            if (currentMoney >= moneyGoal)
            {
                if (enableDebugLogs) Debug.Log($"<color=yellow>[HEDEF]</color> {currentMoney:F0}G = {moneyGoal:F0}G | Ders:{currentLesson}");
                AddReward(2f);
                SafeEndEpisode("Hedef Paraya Ulaşıldı (Dersi Geçti)");
            }
        }
    }

    // --- YENİ: KRALİYET HABERCİSİNİ DİNLE ---
    public void ReceiveContractBroadcast(CityController targetCity, ItemData requiredItem)
    {
        int cityIdx = allSettlements.IndexOf(targetCity);
        int itemIdx = GetActiveItems().IndexOf(requiredItem);

        if (cityIdx >= 0 && itemIdx >= 0)
        {
            // Ajan oraya gitmemiş olsa bile hafızasını "Tellal" sayesinde güncelliyor
            if (!memoryMap.ContainsKey(cityIdx))
                memoryMap[cityIdx] = new SettlementMemory();

            // İhale fiyatını (illüzyonu) hafızaya zorla işliyoruz
            float ihaleFiyati = targetCity.GetPrice(requiredItem);
            memoryMap[cityIdx].knownPrices[itemIdx] = ihaleFiyati;

            // Haberi aldığı anı kaydet (opsiyonel)
            memoryMap[cityIdx].lastVisitTime = Time.time;

            if (enableDebugLogs)
                Debug.Log($"<color=cyan>[TELLAL]</color> {gameObject.name}: Kralın fermanını duydum! {targetCity.cityName} şehrinde {requiredItem.itemName} çok değerli!");
        }
    }

    // ==========================================================
    // BROKER AKSIYONU
    // ==========================================================
    void HandleBrokerAction()
    {
        if (localBrokerManager == null) return;

        // Ders 5'ten once broker aktif degil
        if (currentLesson < LESSON_BROKER_BRANCH) return;

        if (brokerActionTakenThisVisit) return;

        switch (pendingBrokerAction)
        {
            case 0: break;

            case 1:
                if (!boughtLocalInfo && currentMoney >= localBrokerManager.GetLocalInfoCost())
                    ExecuteBuyLocalInfo();
                break;

            case 2:
                int gCost = localBrokerManager.GetGlobalInfoCost();
                if (!boughtGlobalInfo && currentMoney >= gCost)
                {
                    currentMoney -= gCost;
                    boughtGlobalInfo = true;

                    // HATA BURADAYDI! Eski karmaşık LINQ sorgusu yerine direkt yeni fonksiyonu çağırıyoruz:
                    var gList = localBrokerManager.GetGlobalInfo();

                    foreach (var s in gList)
                    {
                        int i = allSettlements.IndexOf(s);
                        if (i >= 0) UpdateMemory(i, s);
                    }
                    AddReward(0.03f);
                    if (enableDebugLogs) Debug.Log($"<color=magenta>[BROKER GLOBAL]</color> -{gCost}G");
                }
                break;

            case 3:
                int t1 = localBrokerManager.GetTierCost(1);
                if (currentTier < 1 && currentMoney >= t1)
                {
                    currentMoney -= t1; currentTier = 1; maxCapacity = TierCapacities[1];
                    AddReward(0.5f);
                    if (enableDebugLogs) Debug.Log($"<color=yellow>[TIER 1]</color> Kapasite:{maxCapacity}");
                }
                break;

            case 4:
                int t2 = localBrokerManager.GetTierCost(2);
                if (currentTier < 2 && currentMoney >= t2)
                {
                    currentMoney -= t2; currentTier = 2; maxCapacity = TierCapacities[2];
                    AddReward(1.0f);
                    if (enableDebugLogs) Debug.Log($"<color=yellow>[TIER 2]</color> Kapasite:{maxCapacity}");
                }
                break;
        }
        if (pendingBrokerAction != 0)
            brokerActionTakenThisVisit = true;
    }

    void ExecuteBuyLocalInfo()
    {
        currentMoney -= localBrokerManager.GetLocalInfoCost();
        boughtLocalInfo = true;
        var list = localBrokerManager.GetLocalInfo(transform.position);
        foreach (var s in list)
        {
            int i = allSettlements.IndexOf(s);
            if (i >= 0) UpdateMemory(i, s);
        }
        AddReward(0.02f);
        if (enableDebugLogs) Debug.Log($"<color=magenta>[BROKER LOCAL]</color> {list.Count} lokasyon");
    }

    // ==========================================================
    // DERS BAZLI YARDIMCILAR
    // ==========================================================

    // Derse gre aktif rn listesi
    // Ders 1: sadece Wheat (tek rn, basit grenme)
    // Ders 3-5: Wheat + Iron + Coal + Cotton (4 rn)
    // Ders 6-7: tm 12 rn
    List<ItemData> GetActiveItems()
    {
        if (cachedLessonForItems == currentLesson && cachedActiveItems.Count > 0)
            return cachedActiveItems;

        if (itemWheat == null)
        {
            Debug.LogError($"<color=red>[{name}] KRİTİK HATA: itemWheat atanmamış!</color>");
            return new List<ItemData>();
        }

        var items = localBrokerManager?.activeItems;
        if (items == null || items.Count == 0) return new List<ItemData>();

        List<ItemData> result = new List<ItemData>();

        if (currentLesson >= LESSON_FULL_PRODUCTS)
            result = items;
        else if (currentLesson >= LESSON_MULTI_PRODUCT)
            result = items.Where(i => i == itemWheat || i == itemIron ||
                                      i == itemCoal || i == itemCotton).ToList();
        else
            result = items.Where(i => i == itemWheat).ToList();

        // Cache'e kaydet
        cachedActiveItems = result;
        cachedLessonForItems = currentLesson;

        return result;
    }

    ItemData GetBestAvailableItem(CityController city)
    {
        if (city == lastSellCity) return null;

        var items = GetActiveItems();
        ItemData bestItem = null;
        float maxExpectedProfit = float.MinValue;

        foreach (var item in items)
        {
            var mi = city.marketItems.Find(x => x.itemData == item);
            if (mi == null || mi.currentStock <= 0) continue;

            // 1. Alış Fiyatı
            int buyPrice = city.GetPrice(item);
            if (buyPrice <= 0) continue;

            int availableStock = city.isProducer
                ? Mathf.Max(0, mi.currentStock - mi.dailyTax)
                : mi.currentStock;

            int affordableAmount = Mathf.Min((int)(currentMoney / buyPrice), availableStock, maxCapacity);
            if (affordableAmount <= 0) continue;

            // ==========================================================
            // 2. YENİ: GELECEĞİ ÖNGÖR (Predict Future Price & Evaluate)
            // ==========================================================
            int itemIndex = items.IndexOf(item);
            float bestFutureSellPrice = 0f;

            // Ajan beynindeki (memoryMap) tüm şehirlere bakar. 
            // "Eğer ben bunu alırsam, en pahalı nereye satabilirim?"
            for (int i = 0; i < ActiveSettlementCount() && i < allSettlements.Count; i++)
            {
                var targetCity = allSettlements[i];
                if (targetCity == city) continue; // Aldığı yere geri satmayacak

                if (memoryMap.ContainsKey(i))
                {
                    float knownSellPrice = memoryMap[i].knownPrices[itemIndex];
                    if (knownSellPrice > bestFutureSellPrice)
                    {
                        bestFutureSellPrice = knownSellPrice;
                    }
                }
            }

            // 3. KAR HESABI: "En iyi satış yerindeki fiyat - Buradaki alış fiyatım"
            // Beklenen Net Kar (Miktar ile çarpılarak toplam fırsat bulunur)
            float expectedProfit = (bestFutureSellPrice - buyPrice) * affordableAmount;

            // 4. EĞER BİR İHALE VARSA:
            // İllüzyon sayesinde bestFutureSellPrice devasa (Örn: 150 G) olacağı için, 
            // expectedProfit tavan yapacak ve ajan gözü kapalı bu malı seçecektir!

            // Eğitimdeki köyler için ekstra bir çekicilik kat sayısı (Opsiyonel güvenlik ağı)
            if (city.isProducer && expectedProfit < 500)
            {
                expectedProfit += (mi.currentStock * 0.5f);
            }

            if (expectedProfit > maxExpectedProfit)
            {
                maxExpectedProfit = expectedProfit;
                bestItem = item;
            }
        }

        return bestItem;
    }

    // Doygunluk her zaman aktif (Ders 1'den itibaren)
    void ApplySaturationSetting()
    {
        bool satActive = true;
        foreach (var s in allSettlements)
            s.enableSaturation = satActive;
    }

    // ==========================================================
    // GENEL YARDIMCILAR
    // ==========================================================
    public new void AddReward(float reward)
    {
        episodeCumulativeReward += reward;
        base.AddReward(reward);
    }

    bool IsSettlementActive(int index)
    {
        if (index >= allSettlements.Count) return false;
        return index < LessonSettlementCount[Mathf.Clamp(currentLesson, 0, 7)];
    }

    int ActiveSettlementCount() =>
        Mathf.Min(LessonSettlementCount[Mathf.Clamp(currentLesson, 0, 7)], allSettlements.Count);

    void UpdateMemory(int idx, CityController settle)
    {
        if (!memoryMap.ContainsKey(idx)) return;

        float[] prices = new float[12];
        float[] stocks = new float[12];

        var activeItems = localBrokerManager?.activeItems;

        for (int i = 0; i < 12; i++)
        {
            // Eğer ürün aktifse ve şehirde varsa kaydet, yoksa 0 yaz
            if (activeItems != null && i < activeItems.Count)
            {
                var itemData = activeItems[i];
                var mi = settle.marketItems.Find(x => x.itemData == itemData);
                prices[i] = (mi != null) ? settle.GetPrice(itemData) : 0f;
                stocks[i] = (mi != null && mi.maxStock > 0) ? (float)mi.currentStock / mi.maxStock : 0f;
            }
            else
            {
                prices[i] = 0f;
                stocks[i] = 0f;
            }
        }

        if (currentLesson < LESSON_FOG_OF_WAR)
            memoryMap[idx].UpdateAlwaysFresh(prices, stocks);
        else
            memoryMap[idx].Update(prices, stocks);
    }

    CityController GetBestSellCity()
    {
        CityController best = null;
        int bestVal = int.MinValue;
        int active = ActiveSettlementCount();
        for (int i = 0; i < active && i < allSettlements.Count; i++)
        {
            var city = allSettlements[i];
            if (city == lastBuyCity) continue; // Aldigin yerden satma
            var item = city.marketItems.Find(x => x.itemData == carriedItemData);
            if (item == null) continue;
            int val = city.GetBulkSellValue(carriedItemData, carriedAmount);
            if (val > bestVal) { bestVal = val; best = city; }
        }
        return best;
    }

    // ==========================================================
    // YERLESKE YUKLEME (GÜNCELLENMİŞ FERMUAR MODELİ)
    // ==========================================================
    void LoadSettlements()
    {
        if (localBrokerManager == null)
            localBrokerManager = GetComponentInParent<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = transform.root.GetComponentInChildren<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = FindObjectOfType<BrokerManager>();

        List<CityController> sourceList = new List<CityController>();

        if (localBrokerManager != null && localBrokerManager.allSettlements.Count > 0)
        {
            sourceList = localBrokerManager.allSettlements.Where(s => s != null).ToList();
        }
        else
        {
            sourceList = transform.root.GetComponentsInChildren<CityController>()
                .Where(c => !c.name.Contains("Guild") && !c.name.Contains("Broker"))
                .ToList();
        }

        if (sourceList.Count > 0)
        {
            // Şehirleri (Tüketici) ve Köyleri (Üretici) ayır
            var cities = sourceList.Where(s => !s.isProducer).ToList();
            var villages = sourceList.Where(s => s.isProducer).ToList();

            allSettlements = new List<CityController>();
            int maxCount = Mathf.Max(cities.Count, villages.Count);

            // Fermuar Yöntemi: 1 Şehir, 1 Köy, 1 Şehir, 1 Köy...
            for (int i = 0; i < maxCount; i++)
            {
                if (i < cities.Count) allSettlements.Add(cities[i]);
                if (i < villages.Count) allSettlements.Add(villages[i]);
            }

            if (enableDebugLogs)
                Debug.Log($"[{transform.root.name}] allSettlements yuklendi: {allSettlements.Count} yerleske (Fermuar Modeli)");
        }
    }

    float GetEventTypeNormalized(string name)
    {
        if (name.Contains("Festival")) return 0.25f;
        if (name.Contains("War")) return 0.5f;
        if (name.Contains("Drought")) return 0.75f;
        if (name.Contains("Harvest")) return 1.0f;
        return 0f;
    }

    void OnNewDay()
    {
        if (SessionData.CurrentMode == SessionData.GameMode.AcimasizKis)
        {
            AddReward(-0.01f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        for (int i = 0; i < 9; i++)
            if (Input.GetKey(KeyCode.Alpha1 + i)) d[0] = i;
        if (Input.GetKey(KeyCode.Q)) d[2] = Mathf.Max(0, d[2] - 1);
        if (Input.GetKey(KeyCode.E)) d[2] = Mathf.Min(4, d[2] + 1);
        if (Input.GetKey(KeyCode.Z)) d[3] = Mathf.Max(0, d[3] - 1);
        if (Input.GetKey(KeyCode.X)) d[3] = Mathf.Min(4, d[3] + 1);
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null) TimeManager.Instance.OnNewDay -= OnNewDay;
    }

    public void HandleAgentDeath()
    {
        if (CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive)
        {
            Debug.Log($"<color=red>☠️ {gameObject.name} İFLAS ETTİ VE PİYASADAN SİLİNDİ!</color>");
            this.gameObject.SetActive(false);
            return;
        }
        base.EndEpisode();
    }

    private void CheckBankruptcy()
    {
        if (currentMoney <= 0 && carriedAmount == 0)
        {
            bool inTournament = CompetitionManager.Instance != null && CompetitionManager.Instance.isMatchActive;

            if (inTournament)
            {
                Debug.Log($"<color=red>💀 [{gameObject.name}] İFLAS ETTİ! (Parasız ve malsız kaldı)</color>");
                gameObject.SetActive(false);
            }
            else
            {
                if (enableDebugLogs) Debug.LogWarning($"<color=red>[IFLAS]</color> Para bitti ve kargo boş!");
                AddReward(-2f);
                base.EndEpisode();
            }
        }
    }

    // ===============================================
    // 🚨 DEBUG RADARI: Resetleri Yakalayan Fonksiyon
    // ===============================================
    public void SafeEndEpisode(string sebep)
    {
        // Konsola bas bas bağıracak kırmızı/pembe bir hata mesajı atıyoruz
        Debug.LogError($"<color=magenta>🚨 RESET TETİKLENDİ! SEBEP: {sebep} | Ajan: {gameObject.name} | Para: {currentMoney}G</color>");

        // Asıl resetleme komutunu çalıştır
        base.EndEpisode();
    }

    // =========================================================
    // HAYDUT SOYGUNU (ROBBERY) SİSTEMİ
    // =========================================================
    public void HandleRobbery()
    {
        if (IsImmuneToRobbery()) return;

        robberyImmunityTimer = robberyImmunityDuration;
        // 1. YAPAY ZEKAYA AĞIR CEZA
        AddReward(-5.0f);

        // 2. MALLARI SIFIRLA (Kervan yağmalandı)
        carriedAmount = 0;
        carriedItemData = null;
        lastCargoCost = 0f;
        averageBuyPrice = 0f;

        // 3. PARANIN BÜYÜK KISMINI KAYBET (%10 ile %15 arası kalsın)
        float kalanYuzde = UnityEngine.Random.Range(0.10f, 0.15f);
        currentMoney = Mathf.FloorToInt(currentMoney * kalanYuzde);

        // 4. EN YAKIN ŞEHRİ / KÖYÜ BUL
        if (allSettlements != null && allSettlements.Count > 0)
        {
            CityController nearestCity = null;
            float minDistance = float.MaxValue;

            foreach (var city in allSettlements)
            {
                float dist = Vector3.Distance(transform.position, city.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearestCity = city;
                }
            }

            // 5. AJANI EN YAKIN ŞEHRE IŞINLA
            if (nearestCity != null)
            {
                UnityEngine.AI.NavMeshAgent nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nav != null)
                {
                    nav.Warp(nearestCity.transform.position); // Güvenli ışınlanma
                    nav.ResetPath(); // Eski hedefini unut
                }

                currentDestination = null; // Beynindeki hedefi sıfırla

                if (enableDebugLogs)
                {
                    Debug.Log($"<color=red>☠️ HAYDUT SOYGUNU!</color> {gameObject.name} tüm mallarını kaybetti! Parası {currentMoney}G'ye düştü ve {nearestCity.cityName} şehrine sığındı!");
                }
            }
        }
    }
}


// ==========================================================
// HAFIZA SINIFI
// ==========================================================
[System.Serializable]
public class SettlementMemory
{
    public float[] knownPrices = new float[12];
    public float[] knownStocks = new float[12];
    public float lastVisitTime = -999f;

    // Ders 1-3 (omniscient)
    public void UpdateAlwaysFresh(float[] prices, float[] stocks)
    {
        for (int i = 0; i < 12; i++)
        {
            knownPrices[i] = prices[i];
            knownStocks[i] = stocks[i];
        }
        lastVisitTime = 0f;
    }

    // Ders 4+ (fog of war)
    public void Update(float[] prices, float[] stocks)
    {
        for (int i = 0; i < 12; i++)
        {
            knownPrices[i] = prices[i];
            knownStocks[i] = stocks[i];
        }
        lastVisitTime = Time.time;
    }

    public float GetInformationAge() => Mathf.Max(0f, Time.time - lastVisitTime);
}

