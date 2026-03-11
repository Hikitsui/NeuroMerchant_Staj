using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// NEUROMERCHANT � ANA AJAN
// ==============================================================
// G�zlem Mimarisi � 281 GIRIS (SABIT):
//   [22]  Ajan �z Verisi
//   [225] Yerleske Hafizasi (45 � 5)
//   [9]   Dis Sinyaller (Ders 7'den aktif)
//   [25]  Broker G�zlemleri (5 � 5)
//   TOPLAM: 281
//
// Aksiyon: 4 Branch (50 / 5 / 5 / 5)
//
// Ders Teknik A�ilim Tablosu:
// +---------------------------------------------------------------------------+
// � Ders � Isim            � Teknik A�ilim                                    �
// +------+-----------------+--------------------------------------------------�
// �  0   � Temel Ticaret   � 4 sehir, 1 �r�n, anlik fiyat g�r�n�r            �
// �  1   � �retim Zinciri  � +5 k�y (9 yerleske), alim miktari branch aktif  �
// �  2   � Pazar Dinamigi  � Doygunluk aktif, satis miktari branch aktif      �
// �  3   � Envanter Y�n.   � 4 �r�n, 14 yerleske                             �
// �  4   � Hafiza ve Sis   � Fog of War: hafiza yasi g�zleme ger�ekten yansir�
// �  5   � Bilgi Yatirimi  � Broker branch aktif, 27 yerleske                �
// �  6   � Imparatorluk    � 45 yerleske, 12 �r�n                            �
// �  7   � Kriz Y�netimi   � Event + Kontrat sinyalleri aktif                �
// +---------------------------------------------------------------------------+
// ==============================================================

public class MerchantAgent : Agent
{
    // ----------------------------------------------------------
    // INSPECTOR
    // ----------------------------------------------------------
    [Header("Ekonomi")]
    public float currentMoney = 1000f;
    public float startingMoney = 2000f;

    [Header("Dynamic Training Controls")]
    public float movementPenalty = -0.00005f;
    public float invalidActionPenalty = -0.001f;
    public float invalidTargetPenalty = -0.05f;
    public float profitRewardMultiplier = 0.01f;

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

    [Header("Training � �r�n Listesi (Ders sirasina g�re)")]
    public ItemData itemWheat;    // Ders 0+
    public ItemData itemIron;     // Ders 3+
    public ItemData itemCoal;     // Ders 3+
    public ItemData itemCotton;   // Ders 3+
    // Ders 6'da t�m �r�nler a�ilir � WorldGenerator'daki liste kullanilir

    [Header("Episode")]
    public int maxStepsPerEpisode = 15000;

    [Header("Baglantilar")]
    public CurriculumManager curriculumManager;

    // ----------------------------------------------------------
    // MIMARI SABITLER
    // ----------------------------------------------------------
    private const int MAX_SETTLEMENTS = 45;
    private const int MAX_BROKERS = 5;
    private const float REWARD_FACTOR = 0.01f;

    private static readonly float[] AmountRatios = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

    // Ders bazli �zellik a�ilim esikleri (Yol Haritasi tablosundan)
    private const int LESSON_VILLAGES = 1; // K�yler a�ilir
    private const int LESSON_SATURATION = 2; // Doygunluk aktif
    private const int LESSON_SELL_BRANCH = 3; // Satis miktari branch
    private const int LESSON_BUY_BRANCH = 3; // Alim miktari branch
    private const int LESSON_MULTI_PRODUCT = 3; // 4 �r�n
    private const int LESSON_FOG_OF_WAR = 4; // Hafiza yasi ger�ek
    private const int LESSON_BROKER_BRANCH = 5; // Broker branch aktif
    private const int LESSON_FULL_MAP = 6; // 45 yerleske, 12 �r�n
    private const int LESSON_EXT_SIGNALS = 7; // Event + Kontrat

    // Ders basina aktif �r�n sayisi
    private static readonly int[] LessonProductCount = { 1, 1, 1, 4, 4, 4, 12, 12 };

    // ----------------------------------------------------------
    // BROKER
    // ----------------------------------------------------------
    private bool boughtLocalInfo = false;
    private bool boughtGlobalInfo = false;
    // allBrokers artik BrokerManager.Instance.brokers �zerinden erisiliyor

    // ----------------------------------------------------------
    // CURRICULUM
    // ----------------------------------------------------------
    [HideInInspector] public int currentLesson = 0;
    // Ders basina aktif yerleske sayisi
    // Ders 0: 4  (Broker_1 sehirleri)
    // Ders 1: 9  (Broker_1 tam cluster)
    // Ders 2: 9  (ayni, doygunluk aktif)
    // Ders 3: 14 (+ Broker_2 sehirleri 4 + 1 k�y)
    // Ders 4: 18 (Broker_2 tam cluster)
    // Ders 5: 27 (+ Broker_3 tam cluster, broker branch aktif)
    // Ders 6: 45 (t�m harita)
    // Ders 7: 45 (ayni, event+kontrat aktif)
    // Ders 0: 9  (Broker_1 tam cluster - sehir+koy)
    // Ders 1: 18 (Broker_2 tam cluster - yeni bolge)
    // Ders 2: 18 (ayni, doygunluk aktif)
    // Ders 3: 27 (Broker_3, 4 urun aktif)
    // Ders 4: 36 (Broker_4, fog of war)
    // Ders 5: 45 (Broker_5, broker branch)
    // Ders 6: 45 (tum 12 urun)
    // Ders 7: 45 (event+kontrat)
    private static readonly int[] LessonSettlementCount = { 9, 18, 18, 27, 36, 45, 45, 45 };

    // Step bazli raporlama
    private float episodeCumulativeReward = 0f;
    private int globalStepCount = 0;
    private int lastReportedStep = 0;
    private float stepWindowReward = 0f;
    private int stepWindowEpisodes = 0;
    private const int STEP_WINDOW = 50000;
    private const int WARMUP_STEPS = 10;   // Episode basinda bekleme adimi
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
    // BASLANGI�
    // ==========================================================
    public override void Initialize()
    {
        navAgent = GetComponent<NavMeshAgent>();

        // CurriculumManager'dan kaydedilmis dersi al
        if (curriculumManager != null)
            currentLesson = curriculumManager.currentLesson;

        // allSettlements'i BrokerManager cluster sirasina g�re doldur
        // Her broker: �nce sehirler (isProducer=false), sonra k�yler (isProducer=true)
        // Ders 0: index 0-3 = Broker_1 sehirleri (birbirine yakin)
        // Ders 1: index 4-8 = Broker_1 k�yleri de aktif
        // Ders 2+: sonraki broker cluster'lari eklenir
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
            Debug.Log($"[Agent] allSettlements BrokerManager'dan y�klendi: {allSettlements.Count} yerleske");
        }
        else
        {
            // Fallback
            allSettlements = FindObjectsOfType<CityController>()
                .Where(c => !c.name.Contains("Guild") && !c.name.Contains("Broker"))
                .OrderBy(c => c.isProducer).ThenBy(c => c.name).ToList();
            Debug.LogWarning("[Agent] BrokerManager bulunamadi, fallback kullaniliyor!");
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
    // EPISODE BASLANGICI + STEP BAZLI RAPORLAMA
    // ==========================================================
    public override void OnEpisodeBegin()
    {
        if (allSettlements == null || allSettlements.Count == 0) Initialize();

        // Episode bitti � �d�l� pencereye ekle
        if (CompletedEpisodes > 0)
        {
            stepWindowReward += episodeCumulativeReward;
            stepWindowEpisodes += 1;
        }

        // Sifirla
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

        // Baslangi�ta aktif sehirlerden birine yerles (k�y degil)
        int active = ActiveSettlementCount();
        var startCities = allSettlements.Take(active).Where(s => !s.isProducer).ToList();
        if (startCities.Count > 0)
            transform.position = startCities[Random.Range(0, startCities.Count)].transform.position;
        else if (allSettlements.Count > 0)
            transform.position = allSettlements[0].transform.position;

        bool fullReset = (CompletedEpisodes % 10 == 0);
        foreach (var s in allSettlements) s.ResetCity(fullReset);

        // Ders 2'den itibaren doygunluk aktif
        ApplySaturationSetting();
    }

    // ==========================================================
    // G�ZLEMLER � 281 GIRIS
    // ==========================================================
    public override void CollectObservations(VectorSensor sensor)
    {
        // ---- BLOK A: AJAN �Z VERISI (22) ----
        sensor.AddObservation(currentMoney / 10000f);
        sensor.AddObservation(carriedAmount / (float)maxCapacity);
        sensor.AddObservation(transform.position.x / 500f);
        sensor.AddObservation(transform.position.z / 500f);
        sensor.AddObservation(currentTier / 2f);
        sensor.AddObservation(boughtLocalInfo ? 1f : 0f);
        sensor.AddObservation(boughtGlobalInfo ? 1f : 0f);

        // 12 �r�n envanter slotu
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

        // ---- BLOK B: YERLESKE HAFIZASI (45 � 5 = 225) ----
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

                // Ders 4'ten �nce: bilgi her zaman taze (Fog of War kapali)
                // Ders 4'ten itibaren: ger�ek yas g�nderilir
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
            // Ders 7'den �nce � padding (6 slot)
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
        }

        // Komsu ajan (3) � ileride agent-to-agent
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);
        sensor.AddObservation(0f);

        // ---- BLOK D: BROKER G�ZLEMLERI (5 � 5 = 25) ----
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

        // TOPLAM: 22 + 225 + 9 + 25 = 281 ?
    }

    // ==========================================================
    // AKSIYONLAR � 4 BRANCH
    // ==========================================================
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount >= maxStepsPerEpisode)
        {
            // Episode bitti, elindeki mal i�in k���k ceza
            if (carriedAmount > 0) AddReward(-0.01f);
            Debug.Log($"<color=orange>[MAXSTEP]</color> Episode bitti | Para:{currentMoney:F0}G | Ders:{currentLesson}");
            EndEpisode();
            return;
        }

        // Warmup: episode basinda N adim bekle (reset yerlesmesi i�in)
        warmupStepCount++;
        if (warmupStepCount <= WARMUP_STEPS) return;

        // Her adimda global sayaci artir ve curriculum penceresi dolu mu kontrol et
        globalStepCount++;
        if (globalStepCount - lastReportedStep >= STEP_WINDOW &&
            stepWindowEpisodes > 0 && curriculumManager != null)
        {
            float avg = stepWindowReward / stepWindowEpisodes;
            curriculumManager.ReportStepWindow(avg, currentLesson);
            lastReportedStep = globalStepCount;
            stepWindowReward = 0f;
            stepWindowEpisodes = 0;
            Debug.Log($"[Curriculum] Pencere raporlandi | Step:{globalStepCount} | Ort:{avg:F3} | Ders:{currentLesson}");
        }

        // Ders 0-3: T�m aktif yerleskelerin bilgisi her adimda g�ncellenir (omniscient)
        // Ders 4+: Sadece ziyarette g�ncellenir (fog of war)
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
                // Yolda ge�en her adim i�in �ok k���k zaman cezasi
                AddReward(-0.00005f);
            }
            return;
        }

        int targetIndex = actions.DiscreteActions[0];

        // Branch 1: Broker (Ders 5'ten aktif)
        pendingBrokerAction = (currentLesson >= LESSON_BROKER_BRANCH)
            ? actions.DiscreteActions[1] : 0;

        // Branch 2: Alim miktari (Ders 1'den aktif)
        pendingBuyAmountIndex = (currentLesson >= LESSON_BUY_BRANCH)
            ? Mathf.Clamp(actions.DiscreteActions[2], 0, 4) : 4;

        // Branch 3: Satis miktari (Ders 2'den aktif)
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
    // VARIS � TICARET
    // ==========================================================
    void HandleArrivalInteraction()
    {
        if (currentDestination == null) return;

        int idx = allSettlements.IndexOf(currentDestination);
        UpdateMemory(idx, currentDestination);
        HandleBrokerAction();

        if (currentLesson >= LESSON_EXT_SIGNALS)
            CheckContractCompletion();

        // Aktif �r�n se�
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
                    Debug.Log($"<color=cyan>[BUY]</color> {amount}x {activeItem.itemName} " +
                              $"({ratio * 100:F0}%) @ {currentDestination.cityName}");
                }
                else AddReward(-0.001f);
            }
            else AddReward(-0.001f);
        }
        // ===== SATIS =====
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
                              $"({ratio * 100:F0}%) K�r:+{profit:F0}G �d�l:{reward:F3}");
                }
                else
                {
                    AddReward(-0.01f);
                    Debug.Log($"<color=orange>[SELL-ZARAR]</color> {amountSell}x " +
                              $"({ratio * 100:F0}%) Zarar:{profit:F0}G");
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
            // Iflas � b�y�k ceza, para sifirla ve devam et
            Debug.LogWarning($"<color=red>[IFLAS]</color> Para bitti! Son alim: {lastBuyCity?.cityName} | Mal: {carriedAmount}x {carriedItemData?.itemName}");
            AddReward(-1f);
            currentMoney = startingMoney;
            carriedAmount = 0;
            carriedItemData = null;
            lastBuyCity = null;
            lastSellCity = null;
        }
        // Hedef para: ders ilerledik�e artar
        float moneyGoal = 3000f + currentLesson * 1000f;
        if (currentMoney >= moneyGoal)
        {
            Debug.Log($"<color=yellow>[HEDEF]</color> {currentMoney:F0}G = {moneyGoal:F0}G | Ders:{currentLesson}");
            AddReward(2f);
            EndEpisode();
        }
    }

    // ==========================================================
    // BROKER AKSIYONU
    // ==========================================================
    void HandleBrokerAction()
    {
        if (BrokerManager.Instance == null) return;

        // Ders 5'ten �nce broker kullanilmaz
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

    // Derse g�re aktif �r�n listesi
    // Ders 0-2: sadece Wheat (tek �r�n, basit �grenme)
    // Ders 3-5: Wheat + Iron + Coal + Cotton (4 �r�n)
    // Ders 6-7: t�m 12 �r�n
    List<ItemData> GetActiveItems()
    {
        var items = BrokerManager.Instance?.activeItems;
        if (items == null || items.Count == 0) return new List<ItemData>();

        if (currentLesson >= LESSON_FULL_MAP)
            return items; // 12 �r�n

        if (currentLesson >= LESSON_MULTI_PRODUCT)
        {
            // 4 temel �r�n: Wheat, Iron, Coal, Cotton
            return items.Where(i => i == itemWheat || i == itemIron ||
                                    i == itemCoal || i == itemCotton).ToList();
        }

        // Ders 0-2: sadece Wheat
        return items.Where(i => i == itemWheat).ToList();
    }

    // Varis yerleskesinde alinabilecek en ucuz �r�n� d�ner
    // Koy mu sehir mi fark etmez � stok varsa ve ucuzsa al
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

            // Bu miktarı satabileceğimiz en iyi şehri bul
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

            // Kar beklentisi yoksa bu kaynağı atla
            if (netProfit <= 0) continue;

            if (netProfit > bestProfit)
            {
                bestProfit = netProfit;
                best = item;
            }
        }
        return best;
    }

    // Doygunluk (Ders 2'de a�ilir) � CityController �zerinde toggle
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
        // Ders 4'ten �nce hafiza her zaman g�ncellenir (Fog of War kapali)
        // Ders 4'ten itibaren sadece ziyarette g�ncellenir (mevcut davranis)
        var item = settle.marketItems.Find(x => x.itemData == (carriedItemData ?? itemWheat));
        float price = (item != null) ? settle.GetPrice(item.itemData) : 0f;
        float stock = (item != null && item.maxStock > 0)
            ? (float)item.currentStock / item.maxStock : 0f;

        if (currentLesson < LESSON_FOG_OF_WAR)
            memoryMap[idx].UpdateAlwaysFresh(price, stock); // Anlik, yas=0
        else
            memoryMap[idx].Update(price, stock);            // Ger�ek zaman
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

    // Ders 4'ten �nce kullanilir � zaman damgasi olmadan anlik g�ncelleme
    public void UpdateAlwaysFresh(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = 0f; // Yas her zaman 0
    }

    // Ders 4'ten itibaren kullanilir � ger�ek zaman damgasi
    public void Update(float price, float stockRatio)
    {
        lastKnownPrice = price;
        lastKnownStockRatio = stockRatio;
        lastVisitTime = Time.time;
    }

    public float GetInformationAge() => Mathf.Max(0f, Time.time - lastVisitTime);
}