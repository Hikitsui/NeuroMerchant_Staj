using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ============================================================
// RegionalBroker: 4 şehir + bağlı köyleri tutar
// Düzenli olarak en karlı al-sat rotasını hesaplar
// ============================================================
[System.Serializable]
public class RegionalBroker
{
    public string brokerName;
    public Vector3 position;
    public List<CityController> cities = new List<CityController>();
    public List<CityController> villages = new List<CityController>();

    // En karlı rota
    public CityController bestBuyCity;
    public CityController bestSellCity;
    public ItemData bestItem;
    public int bestProfit;
    public float lastUpdateTime;

    public List<CityController> AllSettlements =>
        cities.Concat(villages).ToList();

    public bool IsNearby(Vector3 agentPos, float radius)
        => Vector3.Distance(agentPos, position) <= radius;

    public void RecalculateBestRoute(List<ItemData> activeItems)
    {
        bestProfit = 0;
        bestBuyCity = null;
        bestSellCity = null;
        bestItem = null;

        var producers = villages.Where(v => v != null && v.isProducer).ToList();
        var consumers = cities.Where(c => c != null && !c.isProducer).ToList();

        // Ders 0: producer yoksa sehirler arasi hesapla
        if (producers.Count == 0) producers = cities.Where(c => c != null).ToList();
        if (consumers.Count == 0) consumers = cities.Where(c => c != null).ToList();

        foreach (var item in activeItems)
        {
            foreach (var buyCity in producers)
            {
                var buyItem = buyCity.marketItems?.Find(x => x.itemData == item);
                if (buyItem == null || buyItem.currentStock <= 0) continue;
                int buyPrice = buyCity.GetPrice(item);

                foreach (var sellCity in consumers)
                {
                    if (sellCity == buyCity) continue;
                    var sellItem = sellCity.marketItems?.Find(x => x.itemData == item);
                    if (sellItem == null) continue;
                    int sellPrice = sellCity.GetPrice(item);

                    int profit = sellPrice - buyPrice;
                    if (profit > bestProfit)
                    {
                        bestProfit = profit;
                        bestBuyCity = buyCity;
                        bestSellCity = sellCity;
                        bestItem = item;
                    }
                }
            }
        }

        lastUpdateTime = Time.time;
        Debug.Log($"<color=magenta>[BROKER {brokerName}]</color> En karli rota: " +
                  $"{bestBuyCity?.cityName ?? "?"} -> {bestSellCity?.cityName ?? "?"} " +
                  $"| {bestItem?.itemName ?? "?"} | Kar: {bestProfit}G");
    }
}

// ============================================================
// BrokerManager: 5 regional broker yonetir
// ============================================================
public class BrokerManager : MonoBehaviour
{
    public static BrokerManager Instance;

    [Header("Maliyet Ayarlari")]
    public int localInfoCost = 10;
    public int globalInfoCost = 200;
    public int tier1Cost = 300;
    public int tier2Cost = 600;

    [Header("Rota Guncelleme")]
    public float routeUpdateInterval = 30f;

    [Header("Broker Erisim Yaricapi")]
    public float brokerRadius = 15f;

    public List<RegionalBroker> brokers = new List<RegionalBroker>();

    public List<ItemData> activeItems = new List<ItemData>();

    void Awake() => Instance = this;

    void Start()
    {
        InvokeRepeating(nameof(UpdateAllRoutes), 5f, routeUpdateInterval);
    }

    // WorldGenerator tarafindan cagirilir
    public void RegisterBroker(string name, Vector3 pos,
                               List<CityController> cities,
                               List<CityController> villages)
    {
        var broker = new RegionalBroker
        {
            brokerName = name,
            position = pos,
            cities = cities ?? new List<CityController>(),
            villages = villages ?? new List<CityController>()
        };
        brokers.Add(broker);

        // Her yerleşkeye hangi broker'a bağlı olduğunu kaydet
        foreach (var c in broker.cities)
            if (c != null) c.assignedBrokerName = name;
        foreach (var v in broker.villages)
            if (v != null) v.assignedBrokerName = name;

        Debug.Log($"<color=magenta>[BROKER]</color> {name} kuruldu | " +
                  $"{broker.cities.Count} sehir + {broker.villages.Count} koy");
    }

    public void SetActiveItems(List<ItemData> items)
    {
        activeItems = items ?? new List<ItemData>();
    }

    void UpdateAllRoutes()
    {
        if (activeItems.Count == 0) return;
        foreach (var b in brokers)
            b.RecalculateBestRoute(activeItems);
    }

    public void ForceUpdateRoutes(List<ItemData> items)
    {
        activeItems = items ?? activeItems;
        UpdateAllRoutes();
    }

    // En yakin broker
    public RegionalBroker GetNearestBroker(Vector3 agentPos)
    {
        RegionalBroker nearest = null;
        float minDist = float.MaxValue;
        foreach (var b in brokers)
        {
            float d = Vector3.Distance(agentPos, b.position);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    // Local bilgi: en yakin broker'in yerleskeleri
    public List<CityController> GetLocalInfo(Vector3 agentPos, float radius = -1f)
    {
        float r = radius > 0 ? radius : brokerRadius;
        var broker = brokers.FirstOrDefault(b => b.IsNearby(agentPos, r))
                     ?? GetNearestBroker(agentPos);

        if (broker == null) return new List<CityController>();

        Debug.Log($"<color=magenta>[BROKER]</color> Local bilgi hazir: " +
                  $"{broker.AllSettlements.Count} lokasyon");
        return broker.AllSettlements;
    }

    // En karli rota
    public (CityController buy, CityController sell, ItemData item, int profit)
        GetBestRoute(Vector3 agentPos)
    {
        var broker = brokers.FirstOrDefault(b => b.IsNearby(agentPos, brokerRadius))
                     ?? GetNearestBroker(agentPos);

        if (broker == null || broker.bestBuyCity == null)
            return (null, null, null, 0);

        return (broker.bestBuyCity, broker.bestSellCity, broker.bestItem, broker.bestProfit);
    }

    // Eski API uyumu
    public int GetLocalInfoCost() => localInfoCost;
    public int GetGlobalInfoCost() => globalInfoCost;
    public int GetTierCost(int tier) => tier == 1 ? tier1Cost : tier2Cost;

    public List<CityController> BuyLocalInfo(MerchantAgent agent)
    {
        if (agent.currentMoney < localInfoCost) return null;
        agent.currentMoney -= localInfoCost;
        return GetLocalInfo(agent.transform.position);
    }

    public List<CityController> BuyGlobalInfo(MerchantAgent agent)
    {
        if (agent.currentMoney < globalInfoCost) return null;
        agent.currentMoney -= globalInfoCost;
        return brokers.SelectMany(b => b.AllSettlements).Distinct().ToList();
    }
}