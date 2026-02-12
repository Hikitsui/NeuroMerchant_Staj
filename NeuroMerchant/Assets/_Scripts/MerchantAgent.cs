using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public enum AgentState { Idle, Thinking, MovingToBuy, MovingToSell, Wandering }

public class MerchantAgent : MonoBehaviour
{
    [Header("State Info")]
    public AgentState currentState = AgentState.Idle;
    public string targetCityName;

    [Header("Movement")]
    public NavMeshAgent navAgent;

    [Header("Economy & Progression")]
    public float currentMoney = 1000f;
    public int currentTier = 0;
    public int maxCapacity = 20;

    [Header("Maintenance (Daily Costs)")]
    public int maintenanceCostPerUnit = 2;
    public int baseDailyCost = 10;

    [Header("Current Cargo")]
    public ItemData carriedItemData;
    public int carriedAmount;
    public int lastCargoCost;

    [Header("Brain & Memory")]
    public List<CityController> knownSettlements = new List<CityController>();

    private CityController targetBuyCity;
    private CityController targetSellCity;
    private ItemData targetItemToBuy;

    void Start()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null) gameObject.AddComponent<NavMeshAgent>();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += PayMaintenanceCost;
        }

        var allCities = FindObjectsOfType<CityController>();
        if (allCities.Length == 0)
        {
            Debug.LogWarning("MerchantAgent: No cities found! Retrying in 1s...");
            StartCoroutine(WaitForCities());
            return;
        }

        ScanSurroundings();
        StartCoroutine(ThinkingProcess());
    }

    IEnumerator WaitForCities()
    {
        while (true)
        {
            yield return new WaitForSeconds(1.0f);
            var allCities = FindObjectsOfType<CityController>();
            if (allCities.Length > 0)
            {
                Debug.Log("MerchantAgent: Cities found! Starting...");
                ScanSurroundings();
                StartCoroutine(ThinkingProcess());
                yield break;
            }
        }
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= PayMaintenanceCost;
        }
    }

    void Update()
    {
        if (navAgent != null && !navAgent.pathPending && navAgent.remainingDistance < 2.0f)
        {
            if (currentState == AgentState.MovingToBuy)
            {
                ExecuteBuy();
            }
            else if (currentState == AgentState.MovingToSell)
            {
                ExecuteSell();
            }
            // Geziniyorsa ve vardiysa, tekrar dusun
            else if (currentState == AgentState.Wandering)
            {
                currentState = AgentState.Idle;
                StartCoroutine(ThinkingProcess());
            }
        }
    }

    // --- BU KISIM ONEMLI: EGITIMDE CEZA PUANI OLACAK ---
    void PayMaintenanceCost()
    {
        int cargoCost = carriedAmount * maintenanceCostPerUnit;
        int totalDailyCost = baseDailyCost + cargoCost;

        currentMoney -= totalDailyCost;

        if (currentMoney < 0)
        {
            // İleride buraya: AddReward(-100f); EndEpisode(); yazacağız.
            Debug.LogError("🚨 AGENT BANKRUPT! Money is negative!");
        }
    }

    void ScanSurroundings()
    {
        var allCities = FindObjectsOfType<CityController>();
        float visionRange = 50f;

        foreach (var city in allCities)
        {
            float dist = Vector3.Distance(transform.position, city.transform.position);
            if (dist < visionRange)
            {
                if (!knownSettlements.Contains(city))
                {
                    knownSettlements.Add(city);
                }
            }
        }
    }

    IEnumerator ThinkingProcess()
    {
        currentState = AgentState.Thinking;
        targetCityName = "Planning...";

        ScanSurroundings();

        yield return new WaitForSeconds(1.0f);

        if (CheckForUpgrade())
        {
            yield break;
        }

        FindBestTradeRoute();
    }

    bool CheckForUpgrade()
    {
        CityController nearestCity = FindNearestCity();
        if (nearestCity == null || nearestCity.assignedBroker == null) return false;

        RegionalBroker broker = nearestCity.assignedBroker;
        bool wantUpgrade = false;

        if (currentTier == 0 && currentMoney >= broker.tier1Cost) wantUpgrade = true;
        else if (currentTier == 1 && currentMoney >= broker.tier2Cost) wantUpgrade = true;

        if (wantUpgrade)
        {
            targetCityName = "UPGRADING @ " + broker.brokerName;
            navAgent.SetDestination(broker.transform.position);
            currentState = AgentState.MovingToBuy;
            StartCoroutine(WaitForArrivalAndUpgrade(broker));
            return true;
        }
        return false;
    }

    IEnumerator WaitForArrivalAndUpgrade(RegionalBroker broker)
    {
        while (navAgent.pathPending || navAgent.remainingDistance > 2.0f)
        {
            yield return null;
        }
        PerformUpgrade(broker);
    }

    void PerformUpgrade(RegionalBroker broker)
    {
        if (currentTier == 0 && currentMoney >= broker.tier1Cost)
        {
            currentMoney -= broker.tier1Cost;
            currentTier = 1;
            maxCapacity = 50;
            Debug.Log($"<color=gold>UPGRADE COMPLETE!</color> Wagon Acquired!");
        }
        else if (currentTier == 1 && currentMoney >= broker.tier2Cost)
        {
            currentMoney -= broker.tier2Cost;
            currentTier = 2;
            maxCapacity = 100;
            Debug.Log($"<color=gold>UPGRADE COMPLETE!</color> Caravan Acquired!");
        }

        currentState = AgentState.Idle;
        StartCoroutine(ThinkingProcess());
    }

    void FindBestTradeRoute()
    {
        // Hafiza bos ise once Broker'a git
        if (knownSettlements.Count < 2)
        {
            ConsultBroker();
            return;
        }

        float maxTotalProfit = -9999;
        CityController bestBuySpot = null;
        CityController bestSellSpot = null;
        ItemData bestItem = null;

        foreach (var seller in knownSettlements)
        {
            if (seller == null) continue;

            foreach (var sellerItem in seller.marketItems)
            {
                if (sellerItem == null || sellerItem.itemData == null) continue;
                if (sellerItem.currentStock <= 0) continue;

                ItemData item = sellerItem.itemData;

                foreach (var buyer in knownSettlements)
                {
                    if (buyer == null || seller == buyer) continue;

                    var buyerItem = buyer.marketItems.Find(x => x.itemData == item);
                    if (buyerItem == null) continue;

                    if (buyer.cityName.Contains("Village")) continue;

                    int unitBuyPrice = seller.GetPrice(item);
                    if (unitBuyPrice <= 0) continue;

                    // --- DEGISTI: GUVENLI LIMAN YOK! AGRESIF TICARET ---
                    // Cebindeki tum parayla alabilecegin kadar al.
                    // Eger batarsan batarsin (Training'de ogreneceksin).
                    int canAfford = (int)(currentMoney / unitBuyPrice);
                    // ----------------------------------------------------

                    int stockAvailable = sellerItem.currentStock;
                    int potentialAmount = Mathf.Min(canAfford, stockAvailable, maxCapacity);

                    if (potentialAmount <= 0) continue;

                    int totalCost = unitBuyPrice * potentialAmount;
                    // 2. Gelir Hesabı
                    int expectedRevenue = buyer.GetBulkSellValue(item, potentialAmount);

                    // --- SÖZLEŞME / İHALE ALGISI (RL TUZAĞI) ---
                    // Eger sehirde bu mal icin ihale varsa, ajan "Ben bu ihaleyi alirim!" diyecek.
                    // (Kapasitesi yetmese bile bunu zannedecek. Oraya gidince patlayacak).
                    if (ContractManager.Instance != null)
                    {
                        int contractBounty = ContractManager.Instance.GetPotentialContractReward(buyer, item);
                        if (contractBounty > -1)
                        {
                            expectedRevenue = contractBounty; // Gozu parayla dondu!
                        }
                    }
                    // ------------------------------------------

                    float grossProfit = expectedRevenue - totalCost;

                    float distToSeller = Vector3.Distance(transform.position, seller.transform.position);
                    float distToBuyer = Vector3.Distance(seller.transform.position, buyer.transform.position);
                    float travelCost = (distToSeller + distToBuyer) * 0.1f;

                    float finalScore = grossProfit - travelCost;

                    if (finalScore > maxTotalProfit && finalScore > 5.0f)
                    {
                        maxTotalProfit = finalScore;
                        bestBuySpot = seller;
                        bestSellSpot = buyer;
                        bestItem = item;
                    }
                }
            }
        }

        if (bestBuySpot != null && bestSellSpot != null && bestItem != null)
        {
            targetBuyCity = bestBuySpot;
            targetSellCity = bestSellSpot;
            targetItemToBuy = bestItem;

            // --- DEBUG LOG (Console) ---
            Debug.Log($"<color=green>PLAN:</color> Buy {targetItemToBuy.itemName} ({targetBuyCity.cityName} -> {targetSellCity.cityName})");

            // --- UI LOG (YENI: Plan'i ekrana yazdir) ---
            if (WorldUI.Instance != null)
            {
                // Debug formatinin aynisi
                string uiLog = $"<color=green>PLAN:</color> Buy {targetItemToBuy.itemName} ({targetBuyCity.cityName} -> {targetSellCity.cityName})";
                WorldUI.Instance.AddLog(uiLog);
            }
            // ------------------------------------------

            currentState = AgentState.MovingToBuy;
            targetCityName = targetBuyCity.cityName;
            navAgent.SetDestination(targetBuyCity.transform.position);
            navAgent.isStopped = false;
        }
    }

    void ConsultBroker()
    {
        if (BrokerManager.Instance == null)
        {
            GoToNearestCity();
            return;
        }

        // Global Bilgi
        if (currentMoney >= BrokerManager.Instance.globalInfoCost)
        {
            var newInfo = BrokerManager.Instance.BuyGlobalInfo(this);
            if (newInfo != null)
            {
                knownSettlements = newInfo;
                StartCoroutine(ThinkingProcess());
                return;
            }
        }

        // Yerel Bilgi
        CityController nearestCity = FindNearestCity();
        if (nearestCity != null && nearestCity.assignedBroker != null)
        {
            int localCost = BrokerManager.Instance.localInfoCost;
            if (currentMoney >= localCost)
            {
                currentMoney -= localCost;
                List<CityController> localInfo = nearestCity.assignedBroker.BuyLocalInfo(this);
                foreach (var city in localInfo)
                {
                    if (!knownSettlements.Contains(city)) knownSettlements.Add(city);
                }
                StartCoroutine(ThinkingProcess());
                return;
            }
        }

        // Para yoksa en yakin sehre git (Rastgele gezme yok)
        GoToNearestCity();
    }

    // --- RASTGELE GEZME YERINE -> SEHIR HOPPLAMA ---
    void GoToNearestCity()
    {
        var allCities = FindObjectsOfType<CityController>();
        CityController bestTarget = null;
        float minDst = Mathf.Infinity;

        if (allCities.Length == 0)
        {
            Debug.LogWarning("MerchantAgent: No cities found! Waiting for world gen...");
            return;
        }

        foreach (var city in allCities)
        {
            // Su an oldugum yer haric
            if (Vector3.Distance(transform.position, city.transform.position) < 5.0f) continue;

            float dst = Vector3.Distance(transform.position, city.transform.position);
            if (dst < minDst)
            {
                minDst = dst;
                bestTarget = city;
            }
        }

        if (bestTarget != null)
        {
            Debug.Log($"<color=orange>EXPLORING:</color> No trade found. Moving to {bestTarget.cityName} to check prices.");
            targetCityName = bestTarget.cityName;
            navAgent.SetDestination(bestTarget.transform.position);
            currentState = AgentState.Wandering; // YENI STATE
        }
        else
        {
            Debug.LogWarning("Nowhere to go! Waiting...");
            StartCoroutine(ThinkingProcess());
        }
    }

    void ExecuteBuy()
    {
        // Hedef yoksa veya durum yanlışa işlem yapma
        if (targetBuyCity == null || currentState != AgentState.MovingToBuy) return;

        // Marketteki ürünü bul
        var marketItem = targetBuyCity.marketItems.Find(x => x.itemData == targetItemToBuy);

        // Ürün varsa ve stoğu bitmemişse
        if (marketItem != null && marketItem.currentStock > 0)
        {
            int price = targetBuyCity.GetPrice(targetItemToBuy);

            // Paramızla kaç tane alabiliriz?
            int canAfford = (int)(currentMoney / price);

            // Alabileceğimiz miktar (Para limiti, Stok limiti, Taşıma kapasitesi limiti)
            int amountToBuy = Mathf.Min(canAfford, marketItem.currentStock, maxCapacity);

            if (amountToBuy > 0)
            {
                // -- İŞLEM BAŞLIYOR --
                int totalCost = amountToBuy * price;
                currentMoney -= totalCost;              // Parayı düş
                marketItem.currentStock -= amountToBuy; // Stoğu düş

                // Kargo bilgilerini güncelle
                carriedItemData = targetItemToBuy;
                carriedAmount = amountToBuy;
                lastCargoCost = totalCost;

                // --- UI LOG GÖNDERİMİ ---
                if (WorldUI.Instance != null)
                {
                    // Cyan rengini kelime bitiminde kapattik (Hata olmasin diye)
                    string logMessage = $"BUYING:</color> {carriedAmount}x {carriedItemData.itemName} from {targetBuyCity.cityName}";
                    WorldUI.Instance.AddLog(logMessage);
                }

                // Bir sonraki aşamaya geç: Satmaya git
                currentState = AgentState.MovingToSell;
                targetCityName = targetSellCity.cityName;
                navAgent.SetDestination(targetSellCity.transform.position);
            }
            else
            {
                // Paramız yetmediyse veya stok 0 ise tekrar düşün
                StartCoroutine(ThinkingProcess());
            }
        }
        else
        {
            // Ürün bulunamadıysa tekrar düşün
            StartCoroutine(ThinkingProcess());
        }
    }

    void ExecuteSell()
    {
        if (carriedItemData != null && targetSellCity != null && currentState == AgentState.MovingToSell)
        {
            var targetMarketItem = targetSellCity.marketItems.Find(x => x.itemData == carriedItemData);

            if (targetMarketItem != null)
            {
                int realTotalIncome = 0;
                int contractReward = 0;

                // --- SÖZLEŞME TESLİMAT KONTROLÜ ---
                bool contractCompleted = false;
                if (ContractManager.Instance != null)
                {
                    // Ajan elindeki mali sehre verir. Miktar sozlesmeye yetiyorsa odulu alir.
                    contractCompleted = ContractManager.Instance.TryCompleteContract(targetSellCity, carriedItemData, carriedAmount, out contractReward);
                }

                if (contractCompleted)
                {
                    realTotalIncome = contractReward; // DEVASA ODUL!
                    if (WorldUI.Instance != null) WorldUI.Instance.AddLog($"<color=orange>[CONTRACT COMPLETED]</color> {targetSellCity.cityName} (+{realTotalIncome} G)");
                }
                else
                {
                    // Eğer ajan ihale icin geldi ama 55 yerine 50 getirdiyse ihale TAMAMLANMAZ.
                    // Malı normal piyasa fiyatinan (ucuzdan) satmak zorunda kalir (Ceza niteliginde).
                    realTotalIncome = targetSellCity.GetBulkSellValue(carriedItemData, carriedAmount);
                    if (WorldUI.Instance != null) WorldUI.Instance.AddLog($"<color=green>[SELL]</color> {carriedItemData.itemName} to {targetSellCity.cityName} (+{realTotalIncome - lastCargoCost} G)");
                }

                currentMoney += realTotalIncome;
                targetMarketItem.currentStock += carriedAmount;
                int netProfit = realTotalIncome - lastCargoCost;

                // Envanteri temizle
                carriedItemData = null;
                carriedAmount = 0;
                lastCargoCost = 0;
            }

            // İş bitti, yeni plan yap
            StartCoroutine(ThinkingProcess());
        }
    }

    CityController FindNearestCity()
    {
        var all = FindObjectsOfType<CityController>();
        CityController nearest = null;
        float minDst = Mathf.Infinity;
        foreach (var city in all)
        {
            float dst = Vector3.Distance(transform.position, city.transform.position);
            if (dst < minDst) { minDst = dst; nearest = city; }
        }
        return nearest;
    }

    private void OnTriggerEnter(Collider other)
    {
        CityController city = other.GetComponent<CityController>();
        if (city != null && TradingUI.Instance != null) TradingUI.Instance.ShowTrade(city, this);
    }
}