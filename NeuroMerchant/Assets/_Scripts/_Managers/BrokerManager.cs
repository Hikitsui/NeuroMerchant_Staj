using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// BROKER MANAGER — Tek Yetkili Bilgi Sistemi
// Para kesme işlemi BURADA DEĞİL, MerchantAgent'ta yapılır.
// BrokerManager sadece hangi bilginin kaç para ettiğini söyler
// ve listeyi döner. Çift kesme riski yoktur.
// ==============================================================
public class BrokerManager : MonoBehaviour
{
    public static BrokerManager Instance;

    [Header("Paket Fiyatları")]
    public int localInfoCost = 50;
    public float globalInfoCostRatio = 0.05f; // Paranın %5'i
    public int globalInfoMinCost = 200;

    [Header("Tier Maliyetleri")]
    public int tier1Cost = 2500;
    public int tier2Cost = 10000;

    private CityController[] allCities;

    void Awake() { Instance = this; }

    void Start()
    {
        allCities = FindObjectsOfType<CityController>();
    }

    // ----------------------------------------------------------
    // FİYAT SORGULARI (Para kesmez, sadece bilgi verir)
    // ----------------------------------------------------------
    public int GetLocalInfoCost() => localInfoCost;

    public int GetGlobalInfoCost(float agentMoney) =>
        Mathf.Max(globalInfoMinCost, Mathf.RoundToInt(agentMoney * globalInfoCostRatio));

    public int GetTierCost(int tier) => tier == 1 ? tier1Cost : tier2Cost;

    // ----------------------------------------------------------
    // PAKET 1: LOCAL BİLGİ
    // Agent'ın konumuna göre yakın yerleşkeleri döner.
    // Para kesmeyi MerchantAgent yapar.
    // ----------------------------------------------------------
    public List<CityController> GetLocalInfo(Vector3 agentPosition, float radius = 60f)
    {
        List<CityController> result = new List<CityController>();

        foreach (var city in allCities)
        {
            if (city == null) continue;
            float dist = Vector3.Distance(agentPosition, city.transform.position);
            if (dist <= radius && dist > 1.0f)
                result.Add(city);
        }

        Debug.Log($"<color=magenta>[BROKER]</color> Local bilgi hazır: {result.Count} lokasyon");
        return result;
    }

    // ----------------------------------------------------------
    // PAKET 2: GLOBAL BİLGİ
    // Tüm haritayı döner.
    // Para kesmeyi MerchantAgent yapar.
    // ----------------------------------------------------------
    public List<CityController> GetGlobalInfo()
    {
        Debug.Log($"<color=magenta>[BROKER]</color> Global bilgi hazır: {allCities.Length} lokasyon");
        return allCities.ToList();
    }

    // ----------------------------------------------------------
    // EN KARLİ ROTA (Global bilgi alındıktan sonra kullanılır)
    // ----------------------------------------------------------
    public (CityController buyCity, CityController sellCity, ItemData item, int estimatedProfit)
        GetBestTradeRoute(ItemData targetItem)
    {
        CityController bestBuy = null;
        CityController bestSell = null;
        int bestProfit = int.MinValue;

        foreach (var buyCity in allCities)
        {
            if (!buyCity.isProducer) continue;
            var buyItem = buyCity.marketItems.Find(x => x.itemData == targetItem);
            if (buyItem == null || buyItem.currentStock <= 0) continue;

            int buyPrice = buyCity.GetPrice(targetItem);

            foreach (var sellCity in allCities)
            {
                if (sellCity == buyCity || sellCity.isProducer) continue;
                int sellPrice = sellCity.GetPrice(targetItem);
                int profit = sellPrice - buyPrice;
                if (profit > bestProfit)
                {
                    bestProfit = profit;
                    bestBuy = buyCity;
                    bestSell = sellCity;
                }
            }
        }

        return (bestBuy, bestSell, targetItem, bestProfit);
    }
}