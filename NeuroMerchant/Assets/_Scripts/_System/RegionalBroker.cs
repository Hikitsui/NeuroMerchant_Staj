using UnityEngine;
using System.Collections.Generic;

// ==============================================================
// REGIONAL BROKER
// Sadece kimlik ve hizmet verdiği yerleşke listesini tutar.
// Tüm bilgi satın alma ve para işlemleri BrokerManager üzerinden.
// ==============================================================
public class RegionalBroker : MonoBehaviour
{
    [Header("Kimlik")]
    public string brokerName = "Trader Guild";

    [Header("Hizmet Bölgesi")]
    public List<CityController> servicedSettlements = new List<CityController>();

    // WorldGenerator tarafından çağrılır
    public void InitBroker(List<CityController> assignedCluster)
    {
        servicedSettlements = new List<CityController>();

        foreach (var city in assignedCluster)
        {
            servicedSettlements.Add(city);
            if (city.satelliteVillages != null)
                servicedSettlements.AddRange(city.satelliteVillages);
        }
    }
}