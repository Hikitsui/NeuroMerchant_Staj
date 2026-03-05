using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// NEUROMERCHANT — ANA AJAN
// ==============================================================
// Gözlem Mimarisi — 281 GİRİŞ (SABİT):
//   [22]  Ajan Öz Verisi
//   [225] Yerleşke Hafızası (45 × 5)
//   [9]   Dış Sinyaller (Ders 7'den aktif)
//   [25]  Broker Gözlemleri (5 × 5)
//   TOPLAM: 281
//
// Aksiyon: 4 Branch (50 / 5 / 5 / 5)
//
// Ders Teknik Açılım Tablosu:
// ┌──────┬─────────────────┬──────────────────────────────────────────────────┐
// │ Ders │ İsim            │ Teknik Açılım                                    │
// ├──────┼─────────────────┼──────────────────────────────────────────────────┤
// │  0   │ Temel Ticaret   │ 4 şehir, 1 ürün, anlık fiyat görünür            │
// │  1   │ Üretim Zinciri  │ +5 köy (9 yerleşke), alım miktarı branch aktif  │
// │  2   │ Pazar Dinamiği  │ Doygunluk aktif, satış miktarı branch aktif      │
// │  3   │ Envanter Yön.   │ 4 ürün, 14 yerleşke                             │
// │  4   │ Hafıza ve Sis   │ Fog of War: hafıza yaşı gözleme gerçekten yansır│
// │  5   │ Bilgi Yatırımı  │ Broker branch aktif, 27 yerleşke                │
// │  6   │ İmparatorluk    │ 45 yerleşke, 12 ürün                            │
// │  7   │ Kriz Yönetimi   │ Event + Kontrat sinyalleri aktif                │
// └──────┴─────────────────┴──────────────────────────────────────────────────┘
// ==============================================================

public class MerchantAgent : Agent
{
    // ----------------------------------------------------------
    // INSPECTOR
    // ----------------------------------------------------------
    [Header("Ekonomi")]
    public float currentMoney = 1000f;
    public float startingMoney = 2000f;

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

    [Header("Training — Ürün Listesi (Ders sırasına göre)")]
    public ItemData itemWheat;    // Ders 0+
    public ItemData itemIron;     // Ders 3+
    public ItemData itemCoal;     // Ders 3+
    public ItemData itemCotton;   // Ders 3+
    // Ders 6'da tüm ürünler açılır — WorldGenerator'daki liste kullanılır

    [Header("Episode")]
    public int maxStepsPerEpisode = 3000;

    [Header("Bağlantılar")]
    public CurriculumManager curriculumManager;

    // ----------------------------------------------------------
    // MİMARİ SABİTLER
    // ----------------------------------------------------------
    private const int MAX_SETTLEMENTS = 45;
    private const int MAX_BROKERS = 5;
    private const float REWARD_FACTOR = 0.01f;

    private static readonly float[] AmountRatios = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

    // Ders bazlı özellik açılım eşikleri (Yol Haritası tablosundan)
    private const int LESSON_VILLAGES = 1; // Köyler açılır
    private const int LESSON_SATURATION = 2; // Doygunluk aktif
    private const int LESSON_SELL_BRANCH = 3; // Satış miktarı branch
    private const int LESSON_BUY_BRANCH = 3; // Alım miktarı branch
    private const int LESSON_MULTI_PRODUCT = 3; // 4 ürün
    private const int LESSON_FOG_OF_WAR = 4; // Hafıza yaşı gerçek
    private const int LESSON_BROKER_BRANCH = 5; // Broker branch aktif
    private const int LESSON_FULL_MAP = 6; // 45 yerleşke, 12 ürün
    private const int LESSON_EXT_SIGNALS = 7; // Event + Kontrat

    // Ders başına aktif ürün sayısı
    private static readonly int[] LessonProductCount = { 1, 1, 1, 4, 4, 4, 12, 12 };

    // ----------------------------------------------------------
    // BROKER
    // ----------------------------------------------------------
    private bool boughtLocalInfo = false;
    private bool boughtGlobalInfo = false;
    // allBrokers artık BrokerManager.Instance.brokers üzerinden erişiliyor

    // ----------------------------------------------------------
    // CURRICULUM
    // ----------------------------------------------------------
    [HideInInspector] public int currentLesson = 0;
    // Ders başına aktif yerleşke sayısı
    // Ders 0: 4  (Broker_1 şehirleri)
    // Ders 1: 9  (Broker_1 tam cluster)
    // Ders 2: 9  (aynı, doygunluk aktif)
    // Ders 3: 14 (+ Broker_2 şehirleri 4 + 1 köy)
    // Ders 4: 18 (Broker_2 tam cluster)
    // Ders 5: 27 (+ Broker_3 tam cluster, broker branch aktif)
    // Ders 6: 45 (tüm harita)
    // Ders 7: 45 (aynı, event+kontrat aktif)
    private static readonly int[] LessonSettlementCount = { 4, 9, 9, 14, 18, 27, 45, 45 };

    // Step bazlı raporlama
    private float episodeCumulativeReward = 0f;
    private int globalStepCount = 0;
    private int lastReportedStep = 0;
    private float stepWindowReward = 0f;
    private int stepWindowEpisodes = 0;
    private const int STEP_WINDOW = 50000;
    private const int WARMUP_STEPS = 10;   // Episode başında bekleme adımı
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

    // Bekleyen aksiyon kararları
    private int pendingBrokerAction = 0;
    private int pendingBuyAmountIndex = 4;
    private int pendingSellAmountIndex = 4;

    // ==========================================================
    // BAŞLANGIÇ
    // ==========================================================
    public override void Initialize()
    {
        navAgent = GetComponent<NavMeshAgent>();

        // CurriculumManager'dan kaydedilmiş dersi al
        if (curriculumManager != null)
            currentLesson = curriculumManager.currentLesson;

        // allSettlements'i BrokerManager cluster sırasına göre doldur
        // Her broker: önce şehirler (isProducer=false), sonra köyler (isProducer=true)
        // Ders 0: index 0-3 = Broker_1 şehirleri (birbirine yakın)
        // Ders 1: index 4-8 = Broker_1 köyleri de aktif
        // Ders 2+: sonraki broker cluster'ları eklenir
        allSettlements = new List<CityController>();
        if (BrokerManager.Instance != null && BrokerManager.Instance.brokers.Count > 0)
        {
            foreach (var broker in BrokerManager.Instance.brokers)
            {
                foreach (var city in broker.cities)
                    if (city != null && !allSettlements.Contains(city))
                        allSettlements.Add(city);
                foreach (var village in broker.villages)
                    if (village != null && !allSettlements.Contains(village))
                        allSettlements.Add(village);
            }
            Debug.Log($"[Agent] allSettlements BrokerManager'dan yüklendi: {allSettlements.Count} yerleşke");
        }
        else
        {
            // Fallback
            allSettlements = FindObjectsOfType<CityController>()
                .Where(c => !c.name.Contains("Guild") && !c.name.Contains("Broker"))
                .OrderBy(c => c.isProducer).ThenBy(c => c.name).ToList();
            Debug.LogWarning("[Agent] BrokerManager bulunamadı, fallback kullanılıyor!");
        }

        for (int i = 0; i < MAX_SETTLEMENTS; i++)
            memoryMap[i] = new SettlementMemory();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= OnNewDay;
            TimeManager.Instance.OnNewDay += OnNewDay;
        }
    }

    // ==========================================================
    // EPISODE BAŞLANGICI + STEP BAZLI RAPORLAMA
    // ==========================================================
    public override void OnEpisodeBegin()
    {
        if (allSettlements == null || allSettlements.Count == 0) Initialize();

        // Episode bitti — ödülü pencereye ekle
        if (CompletedEpisodes > 0)
        {
            stepWindowReward += episodeCumulativeReward;
            stepWindowEpisodes += 1;
        }

        // Sıfırla
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

        // Başlangıçta aktif şehirlerden birine yerleş (köy değil)
        int active = ActiveSettlementCount();
        var startCities = allSettlements.Take(active).Where(s => !s.isProducer).ToList();
        if (startCities.Count > 0)
            transform.position = startCities[Random.Range(0, startCities.Count)].transform.position;
        else if (allSettlements.Count > 0)
            transform.position = allSettlements[0].transform.position;

        foreach (var s in allSettlements) s.ResetCity();

        // Ders 2'den itibaren doygunluk aktif
        ApplySaturationSetting();
    }

    // ==========================================================
    // GÖZLEMLER — 281 GİRİŞ
    // ==========================================================
    public override void CollectObservations(VectorSensor sensor)
    {
        // ---- BLOK A: AJAN ÖZ VERİSİ (22) ----
        sensor.AddObservation(currentMoney / 10000f);
        sensor.AddObservation(carriedAmount / (float)maxCapacity);
        sensor.AddObservation(transform.position.x / 500f);
        sensor.AddObservation(transform.position.z / 500f);
        sensor.AddObservation(currentTier / 2f);
        sensor.AddObservation(boughtLocalInfo ? 1f : 0f);
        sensor.AddObservation(boughtGlobalInfo ? 1f : 0f);

        // 12 ürün envanter slotu
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

        // ---- BLOK B: YERLEŞKE HAFIZASI (45 × 5 = 225) ----
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

                // Ders 4'ten önce: bilgi her zaman taze (Fog of War kapalı)
                // Ders 4'ten itibaren: gerçek yaş gönderilir
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

        // ---- BLOK C: DIŞ SİNYALLER (9) ----
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
            // Ders 7'den önce — padding (6 slot)
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
        }

        // Komşu ajan (3) — ileride agent-to-agent
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);

        // ---- BLOK D: BROKER GÖZLEMLERİ (5 × 5 = 25) ----
        for (int i = 0; i < MAX_BROKERS; i++)
        {
            if (BrokerManager.Instance != null && i < BrokerManager.Instance.brokers.Count)
            {
                RegionalBroker b = BrokerManager.Instance.brokers[i];
                float dist = Vector3.Distance(transform.position, b.position);
                sensor.AddObservation(dist / 500f);
                sensor.AddObservation(!boughtLocalInfo && currentMoney >= BrokerManager.Instance.GetLocalInfoCost() ? 1f : 0f);
                sensor.AddObservation(!boughtGlobalInfo && currentMoney >= BrokerManager.Instance.GetGlobalInfoCost() ? 1f : 0f);
                sensor.AddObservation(currentTier < 1 && currentMoney >= BrokerManager.Instance.GetTierCost(1) ? 1f : 0f);
                sensor.AddObservation(currentTier < 2 && currentMoney >= BrokerManager.Instance.GetTierCost(2) ? 1f : 0f);
            }
            else
            {
                for (int j = 0; j < 5; j++) sensor.AddObservation(0f);
            }
        }

        // TOPLAM: 22 + 225 + 9 + 25 = 281 ✓
    }

    // ==========================================================
    // AKSİYONLAR — 4 BRANCH
    // ==========================================================
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount >= maxStepsPerEpisode)
        {
            // Episode bitti, elindeki mal için küçük ceza
            if (carriedAmount > 0) AddReward(-0.01f);
            Debug.Log($"<color=orange>[MAXSTEP]</color> Episode bitti | Para:{currentMoney:F0}G | Ders:{currentLesson}");
            EndEpisode();
            return;
        }

        // Warmup: episode başında N adım bekle (reset yerleşmesi için)
        warmupStepCount++;
        if (warmupStepCount <= WARMUP_STEPS) return;

        // Her adımda global sayacı artır ve curriculum penceresi dolu mu kontrol et
        globalStepCount++;
        if (globalStepCount - lastReportedStep >= STEP_WINDOW &&
            stepWindowEpisodes > 0 && curriculumManager != null)
        {
            float avg = stepWindowReward / stepWindowEpisodes;
            curriculumManager.ReportStepWindow(avg, currentLesson);
            lastReportedStep = globalStepCount;
            stepWindowReward = 0f;
            stepWindowEpisodes = 0;
            Debug.Log($"[Curriculum] Pencere raporlandı | Step:{globalStepCount} | Ort:{avg:F3} | Ders:{currentLesson}");
        }

        // Ders 0-3: Tüm aktif yerleşkelerin bilgisi her adımda güncellenir (omniscient)
        // Ders 4+: Sadece ziyarette güncellenir (fog of war)
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
                // Yolda geçen her adım için çok küçük zaman cezası
                AddReward(-0.00005f);
            }
            return;
        }

        int targetIndex = actions.DiscreteActions[0];

        // Branch 1: Broker (Ders 5'ten aktif)
        pendingBrokerAction = (currentLesson >= LESSON_BROKER_BRANCH)
            ? actions.DiscreteActions[1] : 0;

        // Branch 2: Alım miktarı (Ders 1'den aktif)
        pendingBuyAmountIndex = (currentLesson >= LESSON_BUY_BRANCH)
            ? Mathf.Clamp(actions.DiscreteActions[2], 0, 4) : 4;

        // Branch 3: Satış miktarı (Ders 2'den aktif)
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
    // VARIŞ — TİCARET
    // ==========================================================
    void HandleArrivalInteraction()
    {
        if (currentDestination == null) return;

        int idx = allSettlements.IndexOf(currentDestination);
        UpdateMemory(idx, currentDestination);
        HandleBrokerAction();

        if (currentLesson >= LESSON_EXT_SIGNALS)
            CheckContractCompletion();

        // Aktif ürün seç
        ItemData activeItem = GetBestAvailableItem(currentDestination);

        // ===== ALIM =====
        if (carriedAmount == 0)
        {
            if (activeItem == null) { AddReward(-0.001f); return; }

            var marketItem = currentDestination.marketItems.Find(x => x.itemData == activeItem);
            if (marketItem != null && marketItem.currentStock > 0)
            {
                int price = currentDestination.GetPrice(activeItem);
                float ratio = AmountRatios[pendingBuyAmountIndex];
                int wantToBuy = Mathf.Max(1, Mathf.RoundToInt(maxCapacity * ratio));
                int amount = Mathf.Min((int)(currentMoney / price), wantToBuy, marketItem.currentStock);

                if (amount > 0)
                {
                    currentMoney -= amount * price;
                    marketItem.currentStock -= amount;
                    carriedAmount = amount;
                    carriedItemData = activeItem;
                    lastCargoCost = amount * price;
                    lastBuyCity = currentDestination;
                    AddReward(0.05f);
                    Debug.Log($"<color=cyan>[BUY]</color> {amount}x {activeItem.itemName} " +
                              $"({ratio * 100:F0}%) @ {currentDestination.cityName}");
                }
                else AddReward(-0.001f);
            }
            else AddReward(-0.001f);
        }
        // ===== SATIŞ =====
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
                    float reward = Mathf.Clamp(profit * REWARD_FACTOR, 0f, 2f);
                    AddReward(reward);
                    Debug.Log($"<color=green>[SELL]</color> {amountSell}x " +
                              $"({ratio * 100:F0}%) Kâr:+{profit:F0}G Ödül:{reward:F3}");
                }
                else AddReward(-0.01f);

                if (carriedAmount <= 0)
                {
                    carriedAmount = 0;
                    carriedItemData = null;
                    lastCargoCost = 0f;
                    lastSellCity = currentDestination;
                    lastBuyCity = null;
                }
                return; // Satış yapıldı, aynı varışta alım yapma
            }
            else AddReward(-0.001f);
        }

        if (currentMoney <= 0)
        {
            // İflas — büyük ceza, para sıfırla ve devam et
            Debug.LogWarning($"<color=red>[IFLAS]</color> Para bitti! Son alım: {lastBuyCity?.cityName} | Mal: {carriedAmount}x {carriedItemData?.itemName}");
            AddReward(-1f);
            currentMoney = startingMoney;
            carriedAmount = 0;
            carriedItemData = null;
            lastBuyCity = null;
            lastSellCity = null;
        }
        // Hedef para: ders ilerledikçe artar
        float moneyGoal = 3000f + currentLesson * 1000f;
        if (currentMoney >= moneyGoal)
        {
            Debug.Log($"<color=yellow>[HEDEF]</color> {currentMoney:F0}G ≥ {moneyGoal:F0}G | Ders:{currentLesson}");
            AddReward(2f);
            EndEpisode();
        }
    }

    // ==========================================================
    // BROKER AKSİYONU
    // ==========================================================
    void HandleBrokerAction()
    {
        if (BrokerManager.Instance == null) return;

        // Ders 5'ten önce broker kullanılmaz
        if (currentLesson < LESSON_BROKER_BRANCH) return;

        switch (pendingBrokerAction)
        {
            case 0: break;

            case 1:
                if (!boughtLocalInfo && currentMoney >= BrokerManager.Instance.GetLocalInfoCost())
                    ExecuteBuyLocalInfo();
                break;

            case 2:
                int gCost = BrokerManager.Instance.GetGlobalInfoCost();
                if (!boughtGlobalInfo && currentMoney >= gCost)
                {
                    currentMoney -= gCost;
                    boughtGlobalInfo = true;
                    var gList = BrokerManager.Instance.brokers
                        .SelectMany(b => b.AllSettlements).Distinct().ToList();
                    foreach (var s in gList)
                    {
                        int i = allSettlements.IndexOf(s);
                        if (i >= 0) UpdateMemory(i, s);
                    }
                    AddReward(0.03f);
                    Debug.Log($"<color=magenta>[BROKER GLOBAL]</color> -{gCost}G");
                }
                break;

            case 3:
                int t1 = BrokerManager.Instance.GetTierCost(1);
                if (currentTier < 1 && currentMoney >= t1)
                {
                    currentMoney -= t1; currentTier = 1; maxCapacity = TierCapacities[1];
                    AddReward(0.5f);
                    Debug.Log($"<color=yellow>[TIER 1]</color> Kapasite:{maxCapacity}");
                }
                break;

            case 4:
                int t2 = BrokerManager.Instance.GetTierCost(2);
                if (currentTier < 2 && currentMoney >= t2)
                {
                    currentMoney -= t2; currentTier = 2; maxCapacity = TierCapacities[2];
                    AddReward(1.0f);
                    Debug.Log($"<color=yellow>[TIER 2]</color> Kapasite:{maxCapacity}");
                }
                break;
        }
    }

    void ExecuteBuyLocalInfo()
    {
        currentMoney -= BrokerManager.Instance.GetLocalInfoCost();
        boughtLocalInfo = true;
        var list = BrokerManager.Instance.GetLocalInfo(transform.position);
        foreach (var s in list)
        {
            int i = allSettlements.IndexOf(s);
            if (i >= 0) UpdateMemory(i, s);
        }
        AddReward(0.02f);
        Debug.Log($"<color=magenta>[BROKER LOCAL]</color> {list.Count} lokasyon");
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
            Debug.Log($"<color=orange>[CONTRACT]</color> +{reward}G");
        }
    }

    // ==========================================================
    // DERS BAZLI YARDIMCILAR
    // ==========================================================

    // Derse göre aktif ürün listesi
    // Ders 0-2: sadece Wheat (tek ürün, basit öğrenme)
    // Ders 3-5: Wheat + Iron + Coal + Cotton (4 ürün)
    // Ders 6-7: tüm 12 ürün
    List<ItemData> GetActiveItems()
    {
        var items = BrokerManager.Instance?.activeItems;
        if (items == null || items.Count == 0) return new List<ItemData>();

        if (currentLesson >= LESSON_FULL_MAP)
            return items; // 12 ürün

        if (currentLesson >= LESSON_MULTI_PRODUCT)
        {
            // 4 temel ürün: Wheat, Iron, Coal, Cotton
            return items.Where(i => i == itemWheat || i == itemIron ||
                                    i == itemCoal || i == itemCotton).ToList();
        }

        // Ders 0-2: sadece Wheat
        return items.Where(i => i == itemWheat).ToList();
    }

    // Varış yerleşkesinde alınabilecek en ucuz ürünü döner
    // Koy mu şehir mi fark etmez — stok varsa ve ucuzsa al
    ItemData GetBestAvailableItem(CityController city)
    {
        // Son satış yaptığın şehirden hemen alım yapma
        if (city == lastSellCity) return null;

        var items = GetActiveItems();
        ItemData best = null;
        int bestStock = 0;
        foreach (var item in items)
        {
            var mi = city.marketItems.Find(x => x.itemData == item);
            if (mi != null && mi.currentStock > bestStock)
            {
                bestStock = mi.currentStock;
                best = item;
            }
        }
        return best;
    }

    // Doygunluk (Ders 2'de açılır) — CityController üzerinde toggle
    void ApplySaturationSetting()
    {
        bool satActive = currentLesson >= LESSON_SATURATION;
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
        // Ders 4'ten önce hafıza her zaman güncellenir (Fog of War kapalı)
        // Ders 4'ten itibaren sadece ziyarette güncellenir (mevcut davranış)
        var item = settle.marketItems.Find(x => x.itemData == (carriedItemData ?? itemWheat));
        float price = (item != null) ? settle.GetPrice(item.itemData) : 0f;
        float stock = (item != null && item.maxStock > 0)
            ? (float)item.currentStock / item.maxStock : 0f;

        if (currentLesson < LESSON_FOG_OF_WAR)
            memoryMap[idx].UpdateAlwaysFresh(price, stock); // Anlık, yaş=0
        else
            memoryMap[idx].Update(price, stock);            // Gerçek zaman
    }

    CityController GetBestSellCity()
    {
        CityController best = null;
        int bestVal = int.MinValue;
        int active = ActiveSettlementCount();
        for (int i = 0; i < active && i < allSettlements.Count; i++)
        {
            var city = allSettlements[i];
            if (city == lastBuyCity) continue; // Aldığın yerden satma
            var item = city.marketItems.Find(x => x.itemData == carriedItemData);
            if (item == null) continue;
            int val = city.GetBulkSellValue(carriedItemData, carriedAmount);
            if (val > bestVal) { bestVal = val; best = city; }
        }
        return best;
    }

    RegionalBroker GetNearestBroker(float maxDist)
    {
        if (BrokerManager.Instance == null) return null;
        RegionalBroker nearest = null;
        float minDist = maxDist;
        foreach (var b in BrokerManager.Instance.brokers)
        {
            float d = Vector3.Distance(transform.position, b.position);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    float GetEventTypeNormalized(string name)
    {
        if (name.Contains("Festival")) return 0.25f;
        if (name.Contains("War")) return 0.5f;
        if (name.Contains("Drought")) return 0.75f;
        if (name.Contains("Harvest")) return 1.0f;
        return 0f;
    }

    void OnNewDay() { } // Zaman cezası yok

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

    // Ders 4'ten önce kullanılır — zaman damgası olmadan anlık güncelleme
    public void UpdateAlwaysFresh(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = 0f; // Yaş her zaman 0
    }

    // Ders 4'ten itibaren kullanılır — gerçek zaman damgası
    public void Update(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = Time.time;
    }

    public float GetInformationAge() => Mathf.Max(0f, Time.time - lastVisitTime);
}