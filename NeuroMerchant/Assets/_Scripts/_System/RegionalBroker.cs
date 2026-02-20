using UnityEngine;
using System.Collections.Generic;

public class RegionalBroker : MonoBehaviour
{
    [Header("Broker Identity")]
    public string brokerName = "Trader Guild";

    [Header("Information Service")]
    public List<CityController> servicedSettlements;

    public int tier1Cost = 2500;  // Merchant Wagon (50 Cap)
    public int tier2Cost = 10000; // Trade Caravan (100 Cap)


    private List<CityController> clusterCities;

    public void InitBroker(List<CityController> assignedCluster)
    {
        clusterCities = assignedCluster;
        servicedSettlements = new List<CityController>();

        // Tum sehirleri ve uydularini toplu listeye ekle
        foreach(var city in assignedCluster)
        {
            servicedSettlements.Add(city);
            if (city.satelliteVillages != null)
            {
                servicedSettlements.AddRange(city.satelliteVillages);
            }
        }
    }

    // --- PAKET 1: YEREL PAZAR BILGISI (Cluster Info) ---
    public List<CityController> BuyLocalInfo(MerchantAgent agent)
    {
        //TODO Sistem manager uzerinde isliyor hangisi daha verimli olacaksa ondan devam edecek suan bos
        Debug.Log($"<color=magenta>BROKER ({brokerName}):</color> Sold LOCAL info ({servicedSettlements.Count} locations) to Agent.");
        return servicedSettlements;
    }

    // --- PAKET 2: GLOBAL TICARET IPUCU (En Karli Rota) ---
    public string BuyGlobalTradeRoute(MerchantAgent agent)
    {
        // ustteki ile ayni
        Debug.Log($"<color=magenta>BROKER ({brokerName}):</color> Sold GLOBAL trade route to Agent.");
        return "Global Market Analysis: Buy Iron in City_3, Sell in Grand_City_1";
    }
}