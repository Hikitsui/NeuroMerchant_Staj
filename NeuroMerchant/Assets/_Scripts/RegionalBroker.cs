using UnityEngine;
using System.Collections.Generic;

public class RegionalBroker : MonoBehaviour
{
    [Header("Broker Identity")]
    public string brokerName = "Trader Guild";

    [Header("Information Service")]
    public List<CityController> servicedSettlements;

    // --- YENI: UPGRADE FIYATLARI (Design Doc'tan) ---
    public int tier1Cost = 2500;  // Merchant Wagon (50 Cap)
    public int tier2Cost = 10000; // Trade Caravan (100 Cap)

    // --- SERVICE DATA ---
    private List<CityController> clusterCities;

    public void InitBroker(List<CityController> assignedCluster)
    {
        clusterCities = assignedCluster;
        servicedSettlements = assignedCluster;
    }

    // --- PAKET 1: YEREL PAZAR BILGISI (Cluster Info) ---
    public List<CityController> BuyLocalInfo(MerchantAgent agent)
    {
        // Cluster icindeki koy ve sehirlerin market verisini dondur
        Debug.Log($"<color=magenta>BROKER ({brokerName}):</color> Sold LOCAL info to Agent.");
        return clusterCities;
    }

    // --- PAKET 2: GLOBAL TICARET IPUCU (En Karli Rota) ---
    public string BuyGlobalTradeRoute(MerchantAgent agent)
    {
        // Basit bir ornek: Rastgele bi tavsiye (Ileride gercek hesap yapilabilir)
        Debug.Log($"<color=magenta>BROKER ({brokerName}):</color> Sold GLOBAL trade route to Agent.");
        return "Global Market Analysis: Buy Iron in City_3, Sell in Grand_City_1";
    }
}