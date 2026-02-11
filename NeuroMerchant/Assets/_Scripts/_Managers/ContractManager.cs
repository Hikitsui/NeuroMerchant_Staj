using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ContractManager : MonoBehaviour
{
    public static ContractManager Instance;

    // PLANLANMIŞ SÖZLEŞME (Ayın ilerleyen günlerinde çıkacak)
    [System.Serializable]
    public class PendingContract
    {
        public int startDayOfMonth;
        public CityController targetCity;
        public ItemData requiredItem;
        public int requiredAmount;
        public int durationDays;
        public int rewardGold;
    }

    // AKTİF SÖZLEŞME
    [System.Serializable]
    public class ActiveContract
    {
        public CityController targetCity;
        public ItemData requiredItem;
        public int requiredAmount;
        public int rewardGold;
        public int daysLeft;
    }

    [Header("Status")]
    public List<PendingContract> scheduledContracts = new List<PendingContract>();
    public List<ActiveContract> activeContracts = new List<ActiveContract>();

    [Header("Settings")]
    public int contractsPerMonth = 5; // Ayda 5-6 gorev

    private CityController[] allCities;
    private ItemData[] allItems;

    void Awake() { Instance = this; }

    void Start()
    {
        allCities = FindObjectsOfType<CityController>();
        // Piyasada dönen tüm ürün çeşitlerini bul
        var marketItems = FindObjectsOfType<CityController>().SelectMany(c => c.marketItems).Select(m => m.itemData).Distinct().ToArray();
        allItems = marketItems;

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth += ScheduleNextMonthContracts;
        }

        ScheduleNextMonthContracts();
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth -= ScheduleNextMonthContracts;
        }
    }

    // --- 1. AYLIK İHALE PLANLAMASI ---
    void ScheduleNextMonthContracts()
    {
        scheduledContracts.Clear();
        if (allCities.Length == 0 || allItems.Length == 0) return;

        Debug.Log($"<color=magenta>CONTRACT MANAGER:</color> Drafting {contractsPerMonth} contracts for the new month...");

        var consumers = allCities.Where(c => !c.isProducer).ToList();
        if (consumers.Count == 0) consumers = allCities.ToList();

        for (int i = 0; i < contractsPerMonth; i++)
        {
            PendingContract newPlan = new PendingContract();
            newPlan.targetCity = consumers[Random.Range(0, consumers.Count)];
            newPlan.requiredItem = allItems[Random.Range(0, allItems.Length)];

            newPlan.startDayOfMonth = Random.Range(1, 29); // Ayin rastgele gunu
            newPlan.durationDays = Random.Range(7, 15);    // 7-14 gun sure
            newPlan.requiredAmount = Random.Range(20, 101); // 20-100 arasi urun

            // ODUL HESABI: (BasePrice * Miktar) + %30-%50 Bonus
            float baseValue = newPlan.requiredItem.basePrice * newPlan.requiredAmount;
            float bonusMultiplier = Random.Range(1.3f, 1.5f);
            newPlan.rewardGold = Mathf.RoundToInt(baseValue * bonusMultiplier);

            scheduledContracts.Add(newPlan);
            Debug.Log($"<color=grey>CONTRACT SCHEDULED:</color> {newPlan.requiredAmount}x {newPlan.requiredItem.itemName} to {newPlan.targetCity.cityName} on Day {newPlan.startDayOfMonth}");
        }
    }

    // --- 2. GÜNLÜK KONTROLLER ---
    void HandleDailyRoutine()
    {
        int today = TimeManager.Instance.currentDay;

        // A. Baslama zamani gelen ihaleleri atesle
        for (int i = scheduledContracts.Count - 1; i >= 0; i--)
        {
            var plan = scheduledContracts[i];
            if (plan.startDayOfMonth == today)
            {
                StartContract(plan);
                scheduledContracts.RemoveAt(i);
            }
        }

        // B. Aktif ihalelerin suresini dusur
        for (int i = activeContracts.Count - 1; i >= 0; i--)
        {
            activeContracts[i].daysLeft--;
            if (activeContracts[i].daysLeft <= 0)
            {
                Debug.Log($"<color=red>CONTRACT FAILED/EXPIRED:</color> {activeContracts[i].requiredItem.itemName} to {activeContracts[i].targetCity.cityName}");
                activeContracts.RemoveAt(i);
            }
        }
    }

    void StartContract(PendingContract plan)
    {
        ActiveContract newContract = new ActiveContract();
        newContract.targetCity = plan.targetCity;
        newContract.requiredItem = plan.requiredItem;
        newContract.requiredAmount = plan.requiredAmount;
        newContract.rewardGold = plan.rewardGold;
        newContract.daysLeft = plan.durationDays;

        activeContracts.Add(newContract);
        Debug.Log($"<color=orange>NEW CONTRACT ACTIVE:</color> {newContract.targetCity.cityName} needs {newContract.requiredAmount}x {newContract.requiredItem.itemName} in {newContract.daysLeft} days! Reward: {newContract.rewardGold} G");
    }

    // --- 3. AJAN İÇİN İHALE KONTROLLERİ ---

    // Ajan satici oldugunda cagirilir: Tek seferde yeterli mal getirdi mi?
    public bool TryCompleteContract(CityController city, ItemData item, int amount, out int reward)
    {
        reward = 0;
        var contract = activeContracts.Find(c => c.targetCity == city && c.requiredItem == item);

        // Eger ajan TEK SEFERDE istenileni getirdiyse gorev basarili!
        if (contract != null && amount >= contract.requiredAmount)
        {
            reward = contract.rewardGold;
            activeContracts.Remove(contract); // İhaleyi kapat
            return true;
        }
        return false;
    }

    // Ajan yola cikmadan once cazip mi diye bakar (Tuzakli RL kontrolu)
    public int GetPotentialContractReward(CityController city, ItemData item)
    {
        var contract = activeContracts.Find(c => c.targetCity == city && c.requiredItem == item);
        if (contract != null)
        {
            return contract.rewardGold; // Kapasitesi yetmese bile odulu gorsun, giderse husrana ugrasin!
        }
        return -1;
    }
}