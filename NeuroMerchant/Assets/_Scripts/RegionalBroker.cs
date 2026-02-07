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

    public List<CityController> GetLocalMarketInfo()
    {
        return servicedSettlements;
    }
}