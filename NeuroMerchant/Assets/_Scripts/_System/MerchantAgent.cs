using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class MerchantAgent : Agent
{
    [Header("Movement")]
    public NavMeshAgent navAgent;

    [Header("Economy")]
    public float currentMoney = 1000f;
    public float startingMoney = 1000f;
    public int maxCapacity = 20;

    [Header("Cargo")]
    public ItemData carriedItemData;
    public int carriedAmount;
    private float lastCargoCost;

    [Header("Training")]
    public ItemData targetTrainingItem;

    private const float REWARD_FACTOR = 0.01f;

    [Header("Episode Ayarları")]
    public int maxStepsPerEpisode = 3000;

    private List<CityController> allSettlements;
    private bool isMoving = false;
    private CityController currentDestination;
    private CityController lastBuyCity;

    public override void Initialize()
    {
        navAgent = GetComponent<NavMeshAgent>();

        // Sadece Şehir ve Köyleri al, Broker/Training_Guild yapılarını filtrele
        allSettlements = FindObjectsOfType<CityController>()
            .Where(c => (c.name.Contains("City") || c.name.Contains("Village")) && !c.name.Contains("Guild"))
            .OrderBy(c => c.name)
            .Take(9) // 4 Şehir + 5 Köy
            .ToList();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= OnNewDay;
            TimeManager.Instance.OnNewDay += OnNewDay;
        }
    }

    public override void OnEpisodeBegin()
    {
        if (allSettlements == null || allSettlements.Count == 0) Initialize();

        currentMoney = startingMoney;
        carriedAmount = 0;
        carriedItemData = null;
        lastCargoCost = 0;
        lastBuyCity = null;
        isMoving = false;
        currentDestination = null;

        if (navAgent.isOnNavMesh) navAgent.ResetPath();

        // Rastgele bir yerleşkede başlat
        int rnd = Random.Range(0, allSettlements.Count);
        transform.position = allSettlements[rnd].transform.position;

        foreach (var settle in allSettlements) settle.ResetCity();
    }

    // -------------------------------------------------------
    // GÖZLEM (GÖZLER) - 9 YERLEŞKE İÇİN (Space Size: 39)
    // -------------------------------------------------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Kendi Durumu (3 Veri)
        sensor.AddObservation(currentMoney / 5000f);
        sensor.AddObservation(carriedAmount / (float)maxCapacity);
        sensor.AddObservation(carriedItemData != null ? 1f : 0f);

        // 2. 9 Yerleşkenin Durumu (Her biri için 4 Veri -> Toplam 36 Veri)
        for (int i = 0; i < 9; i++)
        {
            if (allSettlements != null && i < allSettlements.Count)
            {
                CityController settle = allSettlements[i];

                // A. Mesafe
                float dist = Vector3.Distance(transform.position, settle.transform.position);
                sensor.AddObservation(dist / 300f);

                if (targetTrainingItem != null)
                {
                    var marketItem = settle.marketItems.Find(x => x.itemData == targetTrainingItem);

                    // B. Fiyat
                    float price = (marketItem != null) ? settle.GetPrice(targetTrainingItem) : 0;
                    sensor.AddObservation(price / 150f);

                    // C. Stok Durumu
                    float stockRatio = (marketItem != null && marketItem.maxStock > 0)
                        ? (float)marketItem.currentStock / marketItem.maxStock
                        : 0f;
                    sensor.AddObservation(stockRatio);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }

                // D. Üretici (Köy) mi?
                sensor.AddObservation(settle.isProducer ? 1f : 0f);
            }
            else
            {
                // Padding
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount >= maxStepsPerEpisode)
        {
            EndEpisode();
            return;
        }

        if (isMoving)
        {
            if (!navAgent.pathPending && navAgent.hasPath && navAgent.remainingDistance <= 2.0f)
            {
                isMoving = false;
                HandleArrivalInteraction();
            }
            return;
        }

        int targetIndex = actions.DiscreteActions[0];
        if (targetIndex < 0 || targetIndex >= allSettlements.Count) return;

        CityController targetSettle = allSettlements[targetIndex];

        if (Vector3.Distance(transform.position, targetSettle.transform.position) > 3.0f)
        {
            currentDestination = targetSettle;
            navAgent.SetDestination(targetSettle.transform.position);
            isMoving = true;
        }
        else
        {
            currentDestination = targetSettle;
            HandleArrivalInteraction();
        }
    }

    void HandleArrivalInteraction()
    {
        if (currentDestination == null) return;

        // ===== ALIM (BUY) =====
        if (carriedAmount == 0)
        {
            var marketItem = currentDestination.marketItems.Find(x => x.itemData == targetTrainingItem);
            if (marketItem != null && marketItem.currentStock > 0)
            {
                int price = currentDestination.GetPrice(targetTrainingItem);
                int amountToBuy = Mathf.Min((int)(currentMoney / price), maxCapacity, marketItem.currentStock);

                if (amountToBuy > 0)
                {
                    currentMoney -= amountToBuy * price;
                    marketItem.currentStock -= amountToBuy;
                    carriedAmount = amountToBuy;
                    carriedItemData = targetTrainingItem;
                    lastCargoCost = amountToBuy * price;
                    lastBuyCity = currentDestination;
                    AddReward(0.05f);
                }
            }
            else AddReward(-0.01f);
        }
        // ===== SATIŞ (SELL) =====
        else
        {
            if (currentDestination == lastBuyCity)
            {
                AddReward(-0.2f);
                return;
            }

            var targetItem = currentDestination.marketItems.Find(x => x.itemData == carriedItemData);
            if (targetItem != null)
            {
                int sellValue = currentDestination.GetBulkSellValue(carriedItemData, carriedAmount);
                float profit = sellValue - lastCargoCost;
                currentMoney += sellValue;
                targetItem.currentStock += carriedAmount;

                if (profit > 0)
                {
                    AddReward(Mathf.Clamp(profit * REWARD_FACTOR, 0f, 2f));
                    lastBuyCity = null;
                }
                else
                {
                    // Zarar cezası çok küçük tut
                    AddReward(-0.05f);
                }
                carriedAmount = 0;
                carriedItemData = null;
            }
        }

        if (currentMoney < 0) { AddReward(-1f); EndEpisode(); }
        if (currentMoney >= 5000) { AddReward(2f); EndEpisode(); }
    }

    void OnNewDay() { AddReward(-0.005f); }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKey(KeyCode.Alpha1 + i)) d[0] = i;
        }
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null) TimeManager.Instance.OnNewDay -= OnNewDay;
    }
}