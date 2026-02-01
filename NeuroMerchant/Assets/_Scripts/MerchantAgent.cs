using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq; // List filtering (LINQ)

public class MerchantAgent : MonoBehaviour
{
    [Header("Movement Settings")]
    public NavMeshAgent navAgent;

    [Header("Economy & Inventory")]
    public float currentMoney = 1000f;
    public int carryCapacity = 10;

    [Header("AI Brain (Knowledge)")]
    public CityController[] knownSettlements; // List of all cities in the world

    void Start()
    {
        navAgent = GetComponent<NavMeshAgent>();

        // AUTO-DISCOVERY: Find all cities in the map automatically
        knownSettlements = FindObjectsOfType<CityController>();

        Debug.Log($"AI initialized. Found {knownSettlements.Length} settlements.");
    }

    void Update()
    {
        // MANUAL OVERRIDE (Right Click)
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                MoveToLocation(hit.point);
            }
        }

        // AI TEST: Press SPACE to find the cheapest city and go there
        if (Input.GetKeyDown(KeyCode.Space))
        {
            FindBestDealAndGo();
        }
    }

    public void MoveToLocation(Vector3 targetPoint)
    {
        navAgent.SetDestination(targetPoint);
        navAgent.isStopped = false;
    }

    // --- AI LOGIC: GREEDY ALGORITHM ---
    void FindBestDealAndGo()
    {
        CityController bestCity = null;
        int lowestPrice = int.MaxValue;

        // Loop through all known cities to find the cheapest item
        // (Currently assuming everyone trades the first item in their list)
        foreach (var city in knownSettlements)
        {
            // Skip if city has no items
            if (city.marketItems.Count == 0) continue;

            ItemData item = city.marketItems[0].itemData;
            int price = city.GetPrice(item);

            Debug.Log($"Analysis: {city.cityName} sells {item.itemName} for {price} Gold.");

            // Logic: Is this cheaper than what I found so far?
            if (price < lowestPrice && city.marketItems[0].currentStock > 0)
            {
                lowestPrice = price;
                bestCity = city;
            }
        }

        if (bestCity != null)
        {
            Debug.Log($"<color=green>DECISION:</color> Going to {bestCity.cityName} to buy for {lowestPrice} Gold!");
            MoveToLocation(bestCity.transform.position);
        }
    }

    // --- TRIGGER LOGIC ---
    private void OnTriggerEnter(Collider other)
    {
        CityController city = other.GetComponent<CityController>();

        if (city != null)
        {
            navAgent.ResetPath();
            navAgent.velocity = Vector3.zero; // Full stop

            if (TradingUI.Instance != null)
            {
                TradingUI.Instance.ShowTrade(city, this);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<CityController>())
        {
            TradingUI.Instance.Hide();
        }
    }
}