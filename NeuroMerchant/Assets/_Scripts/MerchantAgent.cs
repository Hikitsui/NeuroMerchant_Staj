using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public enum AgentState { Idle, Thinking, MovingToBuy, MovingToSell }

public class MerchantAgent : MonoBehaviour
{
    [Header("State Info")]
    public AgentState currentState = AgentState.Idle;
    public string targetCityName;

    [Header("Movement")]
    public NavMeshAgent navAgent;

    [Header("Economy & Inventory")]
    public float currentMoney = 1000f;
    public int maxCapacity = 20;

    [Header("Current Cargo")]
    public ItemData carriedItemData; // Ne tasiyor?
    public int carriedAmount;        // Kac tane tasiyor?
    public int lastCargoCost;

    [Header("Brain")]
    public CityController[] knownSettlements;
    private CityController targetBuyCity;
    private CityController targetSellCity;

    void Start()
    {
        navAgent = GetComponent<NavMeshAgent>();
        knownSettlements = FindObjectsOfType<CityController>();
        StartCoroutine(ThinkingProcess());
    }

    void Update()
    {
        if (!navAgent.pathPending && navAgent.remainingDistance < 1.0f)
        {
            if (currentState == AgentState.MovingToBuy) ExecuteBuy();
            else if (currentState == AgentState.MovingToSell) ExecuteSell();
        }
    }

    IEnumerator ThinkingProcess()
    {
        currentState = AgentState.Thinking;
        targetCityName = "Analyzing Market...";

        float waitTime = Random.Range(1.0f, 2.0f);
        yield return new WaitForSeconds(waitTime);

        FindBestTradeRoute();
    }

    void FindBestTradeRoute()
    {
        float maxTotalProfit = -9999;
        CityController bestBuySpot = null;
        CityController bestSellSpot = null;

        foreach (var seller in knownSettlements)
        {
            foreach (var buyer in knownSettlements)
            {
                if (seller == buyer) continue;
                if (seller.marketItems.Count == 0 || buyer.marketItems.Count == 0) continue;
                if (seller.marketItems[0].currentStock <= 0) continue;

                // KURAL: Sadece SEHIRLERE sat (Koyler alim yapmaz)
                if (buyer.cityName.Contains("Village")) continue;

                ItemData item = seller.marketItems[0].itemData;

                // --- TOPTANCI MATEMATIGI ---

                // 1. Fiyatlari Al
                int buyPrice = seller.GetPrice(item);
                int sellPrice = buyer.GetPrice(item);

                // 2. Kac tane alabilirim? (Inventory ve Para limiti)
                int canAfford = (int)(currentMoney / buyPrice); // Param kaca yetiyor?
                int stockAvailable = seller.marketItems[0].currentStock; // Dukkanda kac tane var?
                int mySpace = maxCapacity; // Cantamda ne kadar yer var?

                // En kucuk olani sec (Limiti belirle)
                int potentialAmount = Mathf.Min(canAfford, stockAvailable, mySpace);

                // Eger hic alamazsam bu rotayi gec
                if (potentialAmount <= 0) continue;

                // 3. Toplam Kar Hesabi (Adet * Kar)
                float grossProfit = (sellPrice - buyPrice) * potentialAmount;

                // 4. Yol Masrafi
                float distToSeller = Vector3.Distance(transform.position, seller.transform.position);
                float distToBuyer = Vector3.Distance(seller.transform.position, buyer.transform.position);
                float travelCost = (distToSeller + distToBuyer) * 0.05f; // Biraz maliyetli olsun

                float finalScore = grossProfit - travelCost;

                // EN IYI ROTA MI?
                if (finalScore > maxTotalProfit && finalScore > 10.0f) // En az 10 altin kar biraksin
                {
                    maxTotalProfit = finalScore;
                    bestBuySpot = seller;
                    bestSellSpot = buyer;
                }
            }
        }

        if (bestBuySpot != null && bestSellSpot != null)
        {
            Debug.Log($"<color=green>PLAN:</color> Buy {bestBuySpot.cityName} -> Sell {bestSellSpot.cityName} | Exp. Profit: {maxTotalProfit:F0}");

            targetBuyCity = bestBuySpot;
            targetSellCity = bestSellSpot;

            currentState = AgentState.MovingToBuy;
            targetCityName = targetBuyCity.cityName;
            navAgent.SetDestination(targetBuyCity.transform.position);
            navAgent.isStopped = false;
        }
        else
        {
            Debug.Log("No profitable trades. Waiting for Day Cycle...");
            StartCoroutine(ThinkingProcess());
        }
    }

    void ExecuteBuy()
    {
        if (targetBuyCity == null || currentState != AgentState.MovingToBuy) return;

        int price = targetBuyCity.GetPrice(targetBuyCity.marketItems[0].itemData);
        int stock = targetBuyCity.marketItems[0].currentStock;

        int canAfford = (int)(currentMoney / price);
        int amountToBuy = Mathf.Min(canAfford, stock, maxCapacity);

        if (amountToBuy > 0)
        {
            // Islemi Yap
            int totalCost = amountToBuy * price;

            currentMoney -= totalCost;
            targetBuyCity.marketItems[0].currentStock -= amountToBuy;

            // Cantaya Koy ve Fisi Kaydet
            carriedItemData = targetBuyCity.marketItems[0].itemData;
            carriedAmount = amountToBuy;
            lastCargoCost = totalCost; // <--- MALIYETI KAYDETTIK

            // DETAYLI LOG (Alis Fisi)
            Debug.Log($"<color=cyan>BUYING:</color> Bought {carriedAmount}x {carriedItemData.itemName} from {targetBuyCity.cityName}.\n" +
                      $"Unit Price: {price} G | Total Cost: <color=red>-{totalCost} G</color>");

            currentState = AgentState.MovingToSell;
            targetCityName = targetSellCity.cityName;
            navAgent.SetDestination(targetSellCity.transform.position);
        }
        else
        {
            StartCoroutine(ThinkingProcess());
        }
    }

    void ExecuteSell()
    {
        if (carriedItemData != null && targetSellCity != null && currentState == AgentState.MovingToSell)
        {
            // Hedef sehrin market listesinde elimdeki urunu bul
            var targetMarketItem = targetSellCity.marketItems.Find(x => x.itemData == carriedItemData);

            if (targetMarketItem != null)
            {
                // Satis İslemi
                int price = targetSellCity.GetPrice(carriedItemData);
                int totalIncome = price * carriedAmount;

                currentMoney += totalIncome;
                targetMarketItem.currentStock += carriedAmount;

                // KAR HESABI (Satis - Alis Maliyeti)
                int netProfit = totalIncome - lastCargoCost;

                // DETAYLI LOG (Satis Fisi)
                Debug.Log($"<color=yellow>SELLING:</color> Sold {carriedAmount}x {carriedItemData.itemName} to {targetSellCity.cityName}.\n" +
                          $"Unit Price: {price} G | Income: <color=green>+{totalIncome} G</color> | NET PROFIT: <color=green>+{netProfit} G</color> 🤑");
            }
            else
            {
                Debug.LogError($"ERROR: {targetSellCity.cityName} does not accept {carriedItemData.itemName}!");
            }

            // Cantayi ve Fisi Sifirla
            carriedItemData = null;
            carriedAmount = 0;
            lastCargoCost = 0;

            StartCoroutine(ThinkingProcess());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CityController city = other.GetComponent<CityController>();
        if (city != null && TradingUI.Instance != null) TradingUI.Instance.ShowTrade(city, this);
    }
}