using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BrokerManager : MonoBehaviour
{
    public static BrokerManager Instance;

    [Header("Settings")]
    public int localInfoCost = 50;
    public int globalInfoCost = 200;

    private CityController[] allCities;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Broker, oyun basinda tum dunyayi tanir (Tanri Modu onda)
        allCities = FindObjectsOfType<CityController>();
    }

    // --- PAKET 1: YEREL DEDIKODU (Local Gossip) ---
    // Ajanin bulundugu yere yakin (Orn: 30 birim) sehirleri dondurur.
    public List<CityController> BuyLocalInfo(MerchantAgent agent)
    {
        if (agent.currentMoney < localInfoCost) return null;

        agent.currentMoney -= localInfoCost;

        List<CityController> localCities = new List<CityController>();
        float radius = 40f; // Yakindaki sehirleri gorme mesafesi

        foreach (var city in allCities)
        {
            if (city == null) continue;
            float dist = Vector3.Distance(agent.transform.position, city.transform.position);

            // Kendisi haric, menzildeki sehirleri ver
            if (dist <= radius && dist > 1.0f)
            {
                localCities.Add(city);
            }
        }

        Debug.Log($"<color=magenta>BROKER:</color> Sold LOCAL info to Agent. (-{localInfoCost} G)");
        return localCities;
    }

    // --- PAKET 2: PIYASA RAPORU (Global Insider) ---
    // Haritadaki TUM sehirleri dondurur.
    public List<CityController> BuyGlobalInfo(MerchantAgent agent)
    {
        if (agent.currentMoney < globalInfoCost) return null;

        agent.currentMoney -= globalInfoCost;

        Debug.Log($"<color=magenta>BROKER:</color> Sold GLOBAL info to Agent. (-{globalInfoCost} G)");

        // Listenin kopyasini dondur (Orijinali bozulmasin)
        return allCities.ToList();
    }
}