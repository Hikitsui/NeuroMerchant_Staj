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
// Gozlem Mimarisi - 281 GIRIS (SABIT):
//   [22]  Ajan Oz Verisi
//   [225] Yerleske Hafizasi (45 x 5)
//   [9]   Dis Sinyaller (Ders 7'den aktif)
//   [25]  Broker Gozlemleri (5 x 5)
//   TOPLAM: 281
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
    [Header("Debug")]
    public bool enableDebugLogs = false; // YENİ!

    [Header("Ekonomi")]
    public float currentMoney = 1000f;
    public float startingMoney = 2000f;

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

        // CurriculumManager'dan kaydedilmis dersi al
        if (curriculumManager != null)
            currentLesson = curriculumManager.currentLesson;

        // Multi-env: Inspector'dan atanmadiysa kendi TrainingArea'sindan bul
        if (localBrokerManager == null)
            localBrokerManager = GetComponentInParent<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = transform.root.GetComponentInChildren<BrokerManager>();

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

    // ==========================================================
    // EPISODE BASLANGICI + STEP BAZLI RAPORLAMA
    // ==========================================================
    public override void OnEpisodeBegin()
    {
        if (allSettlements == null || allSettlements.Count == 0) Initialize();
        // WorldGenerator Awake'den sonra gelmis olabilir, her episode'da guncelle
        LoadSettlements();

        // Episode bitti - odulu adim penceresine ekle
        if (CompletedEpisodes > 0)
        {
            stepWindowReward += episodeCumulativeReward;
            stepWindowEpisodes += 1;
        }

        // Episode degiskenlerini sifirla
        currentMoney = startingMoney;
        carriedAmount = 0;
        carriedItemData = null;
        lastCargoCost = 0f;
        lastBuyCity = null;
        lastSellCity = null;
        isMoving = false;
        currentDestination = null;
        episodeCumulativeReward = 0f;
        boughtLocalInfo = false;
        boughtGlobalInfo = false;
        currentTier = 0;
        maxCapacity = TierCapacities[0];
        pendingBrokerAction = 0;
        pendingBuyAmountIndex = 4;
        pendingSellAmountIndex = 4;
        warmupStepCount = 0;

        if (navAgent != null && navAgent.isOnNavMesh) navAgent.ResetPath();

        // Baslangita aktif sehirlerden birine yerles (ky degil)
        int active = ActiveSettlementCount();
        var startCities = allSettlements.Take(active).Where(s => !s.isProducer).ToList();
        if (startCities.Count > 0)
            transform.position = startCities[Random.Range(0, startCities.Count)].transform.position;
        else if (allSettlements.Count > 0)
            transform.position = allSettlements[0].transform.position;

        bool fullReset = (CompletedEpisodes % 10 == 0);
        foreach (var s in allSettlements) s.ResetCity(fullReset);

        brokerActionTakenThisVisit = false;

        // Doygunluk her zaman aktif (Ders 1 itibariyle)
        ApplySaturationSetting();
    }

    // ==========================================================
    // GOZLEMLER - 281 GIRIS
    // ==========================================================
    public override void CollectObservations(VectorSensor sensor)
    {
        // ---- BLOK A: AJAN OZ VERISI (22) ----
        sensor.AddObservation(currentMoney / 10000f);
        sensor.AddObservation(carriedAmount / (float)maxCapacity);
        sensor.AddObservation(transform.position.x / 500f);
        sensor.AddObservation(transform.position.z / 500f);
        sensor.AddObservation(currentTier / 2f);
        sensor.AddObservation(boughtLocalInfo ? 1f : 0f);
        sensor.AddObservation(boughtGlobalInfo ? 1f : 0f);

        // 12 urun slotu (aktif olmayanlar 0 olarak gonderilir)
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

        // ---- BLOK B: YERLESKE HAFIZASI (45 x 5 = 225) ----
        for (int i = 0; i < MAX_SETTLEMENTS; i++)
        {
            if (IsSettlementActive(i) && i < allSettlements.Count)
            {
                CityController s = allSettlements[i];
                var mem = memoryMap[i];
                float dist = Vector3.Distance(transform.position, s.transform.position);

                sensor.AddObservation(dist / 500f);
                sensor.AddObservation(mem.lastKnownPrice / 500f);
                sensor.AddObservation(mem.lastKnownStockRatio);

                // Ders 1-3: bilgi taze, age=0 (omniscient)
                // Ders 4+: gercek hafiza yasi gonderilir
                float age = (currentLesson >= LESSON_FOG_OF_WAR)
                    ? mem.GetInformationAge() / 300f
                    : 0f;
                sensor.AddObservation(age);

                sensor.AddObservation(s.isProducer ? 1f : 0f);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
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
                sensor.AddObservation(Mathf.Clamp01(
                    evt.consumptMod > 1f ? evt.consumptMod / 3f : evt.productMod / 2f));
                sensor.AddObservation(Mathf.Clamp01(
                    1f - (float)evt.daysElapsed / Mathf.Max(evt.durationDays, 1)));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            // Kontrat (3)
            if (ContractManager.Instance != null && ContractManager.Instance.activeContracts.Count > 0)
            {
                var c = ContractManager.Instance.activeContracts[0];
                sensor.AddObservation(1f);
                sensor.AddObservation(allSettlements.IndexOf(c.targetCity) / (float)MAX_SETTLEMENTS);
                sensor.AddObservation(c.daysLeft / 30f);
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

        // Komsu ajan (3) - ileride cok-ajanli sistem icin rezerve
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);

        // ---- BLOK D: BROKER GOZLEMLERI (5 x 5 = 25) ----
        // RegionalBroker kaldirildi, 25 slot korunuyor
        if (localBrokerManager != null)
        {
            sensor.AddObservation(1f);                                          // broker mevcut
            sensor.AddObservation(localBrokerManager.tier1Cost / 50000f);       // tier1 maliyet
            sensor.AddObservation(localBrokerManager.tier2Cost / 50000f);       // tier2 maliyet
            sensor.AddObservation(!boughtLocalInfo ? 1f : 0f);                 // local info alinabilir
            sensor.AddObservation(!boughtGlobalInfo ? 1f : 0f);                 // global info alinabilir
        }
        else
        {
            for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
        }
        // Kalan 20 slot: padding (eski 4 broker icin rezerve)
        for (int i = 0; i < 20; i++) sensor.AddObservation(0f);

        // TOPLAM: 22 + 225 + 9 + 25 = 281
    }

    // ==========================================================
    // AKSIYONLAR - 4 BRANCH
    // ==========================================================
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount >= maxStepsPerEpisode)
        {
            // Episode bitti, elindeki mal iin kk ceza
            if (carriedAmount > 0) AddReward(-0.01f);
            if (enableDebugLogs) Debug.Log($"<color=orange>[MAXSTEP]</color> Episode bitti | Para:{currentMoney:F0}G | Ders:{currentLesson}");
            EndEpisode();
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
            for (int i = 0; i < active && i < allSettlements.Count; i++)
            {
                var s = allSettlements[i];
                var item = s.marketItems.Find(x => x.itemData == (carriedItemData ?? itemWheat));
                float price = (item != null) ? s.GetPrice(item.itemData) : 0f;
                float stock = (item != null && item.maxStock > 0)
                    ? (float)item.currentStock / item.maxStock : 0f;
                memoryMap[i].UpdateAlwaysFresh(price, stock);
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
                AddReward(-0.00005f);
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

    // ==========================================================
    // VARIS - TICARET
    // ==========================================================
    void HandleArrivalInteraction()
    {
        if (currentDestination == null) return;

        brokerActionTakenThisVisit = false;

        int idx = allSettlements.IndexOf(currentDestination);
        UpdateMemory(idx, currentDestination);
        HandleBrokerAction();

        if (currentLesson >= LESSON_EXT_SIGNALS)
            CheckContractCompletion();

        // Derse gore en karli urunu sec
        ItemData activeItem = GetBestAvailableItem(currentDestination);

        // --- ALIM: kargo bos, uygun urun varsa al ---
        if (carriedAmount == 0)
        {
            if (activeItem == null) { AddReward(-0.001f); return; }

            var marketItem = currentDestination.marketItems.Find(x => x.itemData == activeItem);
            if (marketItem != null && marketItem.currentStock > 0)
            {
                int price = currentDestination.GetPrice(activeItem);
                float ratio = AmountRatios[pendingBuyAmountIndex];
                int wantToBuy = Mathf.Max(1, Mathf.RoundToInt(maxCapacity * ratio));

                // Köyde vergi rezervi bırak — satılabilir stok = currentStock - dailyTax
                int availableStock = currentDestination.isProducer
                    ? Mathf.Max(0, marketItem.currentStock - marketItem.dailyTax)
                    : marketItem.currentStock;

                int amount = Mathf.Min((int)(currentMoney / price), wantToBuy, availableStock);

                if (amount > 0)
                {
                    currentMoney -= amount * price;
                    marketItem.currentStock -= amount;
                    carriedAmount = amount;
                    carriedItemData = activeItem;
                    lastCargoCost = amount * price;
                    lastBuyCity = currentDestination;
                    AddReward(0.05f);
                    if (enableDebugLogs) Debug.Log($"<color=cyan>[BUY]</color> {amount}x {activeItem.itemName} " +
                              $"({ratio * 100:F0}%) @ {currentDestination.cityName}");
                }
                else AddReward(-0.001f);
            }
            else AddReward(-0.001f);
        }
        // --- SATIS: kargo dolu, ayni sehirde satma ---
        else
        {
            if (currentDestination == lastBuyCity) { AddReward(-0.05f); return; }

            var targetItem = currentDestination.marketItems.Find(x => x.itemData == carriedItemData);
            if (targetItem != null)
            {
                float ratio = AmountRatios[pendingSellAmountIndex];
                int amountSell = Mathf.Max(1, Mathf.RoundToInt(carriedAmount * ratio));
                float costPortion = lastCargoCost * ((float)amountSell / Mathf.Max(carriedAmount, 1));

                int sellValue = currentDestination.GetBulkSellValue(carriedItemData, amountSell);
                float profit = sellValue - costPortion;

                currentMoney += sellValue;
                targetItem.currentStock += amountSell;
                carriedAmount -= amountSell;
                lastCargoCost -= costPortion;

                if (profit > 0)
                {
                    // Kar: +40G=0.4, +120G=1.2, +130G=1.3 (max 1.3 clamp)
                    float reward = Mathf.Clamp(profit * REWARD_FACTOR, 0f, 1.3f);
                    AddReward(reward);
                    if (enableDebugLogs) Debug.Log($"<color=green>[SELL]</color> {amountSell}x " +
                              $"({ratio * 100:F0}%) Kar:+{profit:F0}G Odul:{reward:F3}");
                }
                else
                {
                    // Zarar: profit bazli ceza, min -0.5 (50G zarar)
                    float penalty = Mathf.Clamp(profit * REWARD_FACTOR, -0.5f, 0f);
                    AddReward(penalty);
                    if (enableDebugLogs) Debug.Log($"<color=orange>[SELL-ZARAR]</color> {amountSell}x " +
                              $"({ratio * 100:F0}%) Zarar:{profit:F0}G Ceza:{penalty:F3}");
                }

                if (carriedAmount <= 0)
                {
                    carriedAmount = 0;
                    carriedItemData = null;
                    lastCargoCost = 0f;
                    lastSellCity = currentDestination;
                    lastBuyCity = null;
                }
                return; // Satis yapildi, ayni varista alim yapma
            }
            else AddReward(-0.001f);
        }

        if (currentMoney <= 0)
        {
            // Iflas: buyuk ceza verilir, para sifirlanip episode devam eder
            if (enableDebugLogs) Debug.LogWarning($"<color=red>[IFLAS]</color> Para bitti! Son alim: {lastBuyCity?.cityName} | Mal: {carriedAmount}x {carriedItemData?.itemName}");
            AddReward(-1f);
            currentMoney = startingMoney;
            carriedAmount = 0;
            carriedItemData = null;
            lastBuyCity = null;
            lastSellCity = null;
        }
        // Hedef para: her ders 1000G artar (Ders 1: 4000G, Ders 7: 10000G)
        float moneyGoal = 3000f + currentLesson * 1000f;
        if (currentMoney >= moneyGoal)
        {
            if (enableDebugLogs) Debug.Log($"<color=yellow>[HEDEF]</color> {currentMoney:F0}G = {moneyGoal:F0}G | Ders:{currentLesson}");
            AddReward(2f);
            EndEpisode();
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
    // KONTRAT TAMAMLAMA (Ders 7)
    // ==========================================================
    void CheckContractCompletion()
    {
        if (ContractManager.Instance == null || carriedItemData == null) return;
        int reward;
        if (ContractManager.Instance.TryCompleteContract(
            currentDestination, carriedItemData, carriedAmount, out reward))
        {
            currentMoney += reward;
            AddReward(Mathf.Clamp(reward * 0.001f, 0f, 3f));
            carriedAmount = 0;
            carriedItemData = null;
            lastCargoCost = 0f;
            lastBuyCity = null;
            if (enableDebugLogs) Debug.Log($"<color=orange>[CONTRACT]</color> +{reward}G");
        }
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

    // Varis yerleskesinde alinabilecek en ucuz rn dner
    // Koy mu sehir mi fark etmez  stok varsa ve ucuzsa al
    ItemData GetBestAvailableItem(CityController city)
    {
        // Son satis yaptigin sehirden hemen alim yapma
        if (city == lastSellCity) return null;

        var items = GetActiveItems();
        ItemData best = null;
        float bestProfit = float.MinValue;
        int active = ActiveSettlementCount();

        foreach (var item in items)
        {
            var mi = city.marketItems.Find(x => x.itemData == item);
            if (mi == null || mi.currentStock <= 0) continue;

            int buyPrice = city.GetPrice(item);
            if (buyPrice <= 0) continue;

            // Köyde vergi rezervi bırak
            int availableStock = city.isProducer
                ? Mathf.Max(0, mi.currentStock - mi.dailyTax)
                : mi.currentStock;

            int affordableAmount = Mathf.Min((int)(currentMoney / buyPrice), availableStock, maxCapacity);
            if (affordableAmount <= 0) continue;

            float totalCost = affordableAmount * buyPrice;

            // Bu miktar icin en yuksek satis gelirini veren sehri bul
            float bestSellValue = 0f;
            for (int i = 0; i < active && i < allSettlements.Count; i++)
            {
                var sellCity = allSettlements[i];
                if (sellCity == city) continue; // Aldığın yerden satma
                if (sellCity == lastBuyCity) continue;

                var si = sellCity.marketItems.Find(x => x.itemData == item);
                if (si == null) continue;

                float sellVal = sellCity.GetBulkSellValue(item, affordableAmount);
                if (sellVal > bestSellValue) bestSellValue = sellVal;
            }

            float netProfit = bestSellValue - totalCost;

            // Net kar yoksa bu urunu / kaynagi atla
            if (netProfit <= 0) continue;

            if (netProfit > bestProfit)
            {
                bestProfit = netProfit;
                best = item;
            }
        }
        return best;
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
        // Ders 1-3: hafiza anliktir (omniscient)
        // Ders 4+: sadece ziyarette guncellenir (fog of war)
        var item = settle.marketItems.Find(x => x.itemData == (carriedItemData ?? itemWheat));
        float price = (item != null) ? settle.GetPrice(item.itemData) : 0f;
        float stock = (item != null && item.maxStock > 0)
            ? (float)item.currentStock / item.maxStock : 0f;

        if (currentLesson < LESSON_FOG_OF_WAR)
            memoryMap[idx].UpdateAlwaysFresh(price, stock); // Omniscient: yas=0
        else
            memoryMap[idx].Update(price, stock);            // Gerek zaman
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

    /*
    RegionalBroker GetNearestBroker(float maxDist)
    {
        if (localBrokerManager == null) return null;
        RegionalBroker nearest = null;
        float minDist = maxDist;
        foreach (var b in localBrokerManager.brokers)
        {
            float d = Vector3.Distance(transform.position, b.position);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }
    */

    // ==========================================================
    // YERLESKE YUKLEME (Her episode basinda cagrilir)
    // ==========================================================
    void LoadSettlements()
    {
        if (localBrokerManager == null)
            localBrokerManager = GetComponentInParent<BrokerManager>();
        if (localBrokerManager == null)
            localBrokerManager = transform.root.GetComponentInChildren<BrokerManager>();

        if (localBrokerManager != null && localBrokerManager.allSettlements.Count > 0)
        {
            // Sehirler once, koyler sonra (agent ilk hedeflerini sehirlerden secsin)
            allSettlements = localBrokerManager.allSettlements
                .Where(s => s != null)
                .OrderBy(s => s.isProducer ? 1 : 0)
                .ToList();
            if (allSettlements.Count > 0)
                if (enableDebugLogs) Debug.Log($"[{transform.root.name}] allSettlements yuklendi: {allSettlements.Count} yerleske");
        }
        else
        {
            var fallback = transform.root.GetComponentsInChildren<CityController>()
                .Where(c => !c.name.Contains("Guild") && !c.name.Contains("Broker"))
                .OrderBy(c => c.isProducer).ThenBy(c => c.name).ToList();
            if (fallback.Count > 0)
                allSettlements = fallback;
            if (enableDebugLogs) Debug.LogWarning($"[{transform.root.name}] BrokerManager bos, fallback: {allSettlements?.Count ?? 0} yerleske");
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

    void OnNewDay() { } // Zaman cezasi yok

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
}

// ==========================================================
// HAFIZA SINIFI
// ==========================================================
[System.Serializable]
public class SettlementMemory
{
    public float lastKnownPrice = 0f;
    public float lastKnownStockRatio = 0f;
    public float lastVisitTime = -999f;

    // Ders 1-3 (omniscient): zaman damgasi olmadan anlik guncelleme
    public void UpdateAlwaysFresh(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = 0f; // Omniscient - yas her zaman 0
    }

    // Ders 4+ (fog of war): gercek zaman damgasiyla guncelleme
    public void Update(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = Time.time;
    }

    public float GetInformationAge() => Mathf.Max(0f, Time.time - lastVisitTime);
}