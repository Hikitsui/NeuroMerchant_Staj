using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RegionalBroker : MonoBehaviour
{
    [Header("Broker Identity")]
    public string brokerName = "Trader Guild";

    [Header("Information Service")]
    public List<CityController> servicedSettlements;

    public int tier1Cost = 2500;  // Merchant Wagon (50 Cap)
    public int tier2Cost = 10000; // Trade Caravan (100 Cap)


    // Broker'in dunya pozisyonu (MerchantAgent mesafe hesabi icin)
    [HideInInspector] public Vector3 position;

    // cities ve villages ayri listeler (BrokerManager.brokers iterasyonu icin)
    [HideInInspector] public List<CityController> cities = new List<CityController>();
    [HideInInspector] public List<CityController> villages = new List<CityController>();

    // Tum yerleskelerin birlesmis listesi
    public List<CityController> AllSettlements => servicedSettlements ?? new List<CityController>();

    private List<CityController> clusterCities;

    public void InitBroker(List<CityController> assignedCluster)
    {
        clusterCities = assignedCluster;
        servicedSettlements = new List<CityController>();

        // Tum sehirleri ve uydularini toplu listeye ekle
        foreach (var city in assignedCluster)
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


// ==============================================================
// BROKER MANAGER — RegionalBroker kaldirildi
// Her TrainingArea'nin kendi instance'i var (singleton yok).
// WorldGenerator tum yerleskeleri ve aktif urunleri buraya kaydeder.
// MerchantAgent bu scripte localBrokerManager referansiyla erisir.
// ==============================================================
public class BrokerManager : MonoBehaviour
{
    [Header("Aktif Urun Listesi (WorldGenerator doldurur)")]
    public List<ItemData> activeItems = new List<ItemData>();

    [Header("Tum Yerleskeler (WorldGenerator doldurur)")]
    public List<CityController> allSettlements = new List<CityController>();

    [Header("Bilgi Maliyetleri")]
    public int localInfoCost = 50;
    public int globalInfoCost = 200;

    [Header("Kapasite Yukseltme Maliyetleri")]
    public int tier1Cost = 2500;
    public int tier2Cost = 10000;

    // ----------------------------------------------------------
    // MALIYET SORGULARI
    // ----------------------------------------------------------
    public int GetLocalInfoCost() => localInfoCost;
    public int GetGlobalInfoCost() => globalInfoCost;
    public int GetTierCost(int tier)
    {
        if (tier == 1) return tier1Cost;
        if (tier == 2) return tier2Cost;
        return int.MaxValue;
    }

    // ----------------------------------------------------------
    // YEREL BILGI: Ajana yakin yerleskeleri dondurur
    // ----------------------------------------------------------
    public List<CityController> GetLocalInfo(Vector3 agentPos)
    {
        float radius = 120f;
        return allSettlements
            .Where(s => s != null && Vector3.Distance(agentPos, s.transform.position) <= radius)
            .ToList();
    }

    // ----------------------------------------------------------
    // GLOBAL BILGI: Tum yerleskeleri dondurur
    // ----------------------------------------------------------
    public List<CityController> GetGlobalInfo()
    {
        return allSettlements.Where(s => s != null).ToList();
    }

    // ----------------------------------------------------------
    // YERLESKE KAYIT (WorldGenerator cagirır)
    // ----------------------------------------------------------
    public void RegisterSettlement(CityController city)
    {
        if (city != null && !allSettlements.Contains(city))
            allSettlements.Add(city);
    }
}