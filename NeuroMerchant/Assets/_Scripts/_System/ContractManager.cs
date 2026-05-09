using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ContractManager : MonoBehaviour
{
    public static ContractManager Instance;

    [Header("DEVELOPER MODE")]
    public bool trainingMode = true; 

    [Header("Debug Settings")]
    public bool enableDebugLogs = false; 

    // PLANLANMIŞ SÖZLEŞME
    [System.Serializable]
    public class PendingContract
    {
        public int startDayOfMonth;
        public CityController targetCity;
        public ItemData requiredItem;
        public int requiredAmount;
        public int durationDays;
        public int rewardGold;
        public int contractPoints;
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
        public int contractPoints;
    }

    [Header("Status")]
    public List<PendingContract> scheduledContracts = new List<PendingContract>();
    public List<ActiveContract> activeContracts = new List<ActiveContract>();

    [Header("Settings")]
    public int trainingContractCount = 2; 
    public int productionContractCount = 6; 

    private CityController[] allCities;
    private ItemData[] allItems;

    void Awake() { Instance = this; }

    // Eski void Start() yerine bu gelecek:
    public void InitManager(bool isTraining)
    {
        this.trainingMode = isTraining;

        // 1. ZIRH: Sadece marketi olan (içi dolu) GERÇEK şehirleri listeye al!
        allCities = FindObjectsOfType<CityController>()
            .Where(c => c != null && c.marketItems != null && c.marketItems.Count > 0)
            .ToArray();

        if (allCities.Length == 0)
        {
            Debug.LogError("<color=red>[ContractManager] Marketi olan gerçek şehir bulunamadı!</color>");
            return;
        }

        // 2. ZIRH: Eğer marketItems içinde boş (null) bir şey varsa onu da atla.
        var marketItems = allCities
            .SelectMany(c => c.marketItems)
            .Where(m => m != null && m.itemData != null)
            .Select(m => m.itemData)
            .Distinct()
            .ToArray();

        allItems = marketItems;

        // ... Altındaki TimeManager abonelikleri ve Schedule kısmı aynen kalacak ...
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth -= ScheduleNextMonthContracts;

            TimeManager.Instance.OnNewDay += HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth += ScheduleNextMonthContracts;
        }

        ScheduleNextMonthContracts();
        Debug.Log($"<color=yellow>[ContractManager] Başlatıldı. Mod: {(trainingMode ? "Eğitim" : "Turnuva")}</color>");
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth -= ScheduleNextMonthContracts;
        }
    }

    void ScheduleNextMonthContracts()
    {
        scheduledContracts.Clear();
        if (allCities.Length == 0 || allItems.Length == 0) return;

        int contractCount = productionContractCount;
        bool isDiplomatMode = (!trainingMode && SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi);

        // 1. İHALE FREKANSI: Elçi modunda zorluk fark etmeksizin her ay 10 ile 20 arası ihale çıkar
        if (isDiplomatMode)
        {
            contractCount = Random.Range(10, 21); // 10 ile 20 arası (21 dahil değil)
        }

        if (enableDebugLogs) Debug.Log($"<color=magenta>CONTRACT MANAGER:</color> Drafting {contractCount} contracts...");

        var consumers = allCities.Where(c => !c.isProducer).ToList();
        if (consumers.Count == 0) consumers = allCities.ToList();

        for (int i = 0; i < contractCount; i++)
        {
            PendingContract newPlan = new PendingContract();
            newPlan.targetCity = consumers[Random.Range(0, consumers.Count)];
            newPlan.startDayOfMonth = Random.Range(1, 29);

            // 2. YENİ ALGORİTMA: ZORLUĞA GÖRE OLASILIK (YÜZDE), TIER VE MİKTAR BELİRLEME
            ItemTier selectedTier = ItemTier.Tier1;
            int minAmount = 20, maxAmount = 50;
            int minDuration = 7, maxDuration = 15;

            if (isDiplomatMode)
            {
                int zar = Random.Range(0, 100); // %0 ile %99 arası zar atıyoruz

                if (SessionData.DifficultyLevel == 0) // --- KOLAY ---
                {
                    // %100 Tier 1 | Miktar: 10-40 | Süre: Uzun
                    selectedTier = ItemTier.Tier1;
                    minAmount = 10; maxAmount = 25;
                    minDuration = 15; maxDuration = 25;
                }
                else if (SessionData.DifficultyLevel == 1) // --- ORTA ---
                {
                    if (zar < 50)
                    {
                        // %50 İhtimal: Tier 1 | Miktar: 30-50
                        selectedTier = ItemTier.Tier1;
                        minAmount = 30; maxAmount = 50;
                    }
                    else
                    {
                        // %50 İhtimal: Tier 2 | Miktar: 10-40
                        selectedTier = ItemTier.Tier2;
                        minAmount = 10; maxAmount = 40;
                    }
                    minDuration = 10; maxDuration = 20;
                }
                else // --- ZOR ---
                {
                    if (zar < 30)
                    {
                        // %30 İhtimal: Tier 1 | Miktar: 50-100 (Kargoyu fulletir!)
                        selectedTier = ItemTier.Tier1;
                        minAmount = 50; maxAmount = 100;
                    }
                    else if (zar < 60)
                    {
                        // %30 İhtimal (30-59 arası): Tier 2 | Miktar: 30-60
                        selectedTier = ItemTier.Tier2;
                        minAmount = 30; maxAmount = 50;
                    }
                    else
                    {
                        // %40 İhtimal (60-99 arası): Tier 3 | Miktar: 15-40
                        selectedTier = ItemTier.Tier3;
                        minAmount = 15; maxAmount = 20;
                    }
                    minDuration = 7; maxDuration = 15; // Süre çok kısıtlı!
                }
            }
            else
            {
                // Altın Yolu vb. diğer modlar için standart ayar
                selectedTier = (ItemTier)Random.Range(0, 3); // Rastgele Tier
                minAmount = 20; maxAmount = 100;
            }

            // Seçilen Tier'a uygun eşyaları filtrele ve birini seç
            List<ItemData> validItems = allItems.Where(x => x.tier == selectedTier).ToList();
            if (validItems.Count == 0) validItems = allItems.ToList(); // Güvenlik ağı

            newPlan.requiredItem = validItems[Random.Range(0, validItems.Count)];
            newPlan.requiredAmount = Random.Range(minAmount, maxAmount + 1); // +1 çünkü int'te son rakam dahil edilmez
            newPlan.durationDays = Random.Range(minDuration, maxDuration + 1);

            // 3. ÖDÜL VE PUAN HESAPLAMA
            float baseValue = newPlan.requiredItem.basePrice * newPlan.requiredAmount;
            float bonusMultiplier = Random.Range(1.3f, 1.5f);
            newPlan.rewardGold = Mathf.RoundToInt(baseValue * bonusMultiplier);

            // Puanı Tier seviyesine göre belirle
            if (newPlan.requiredItem.tier == ItemTier.Tier1) newPlan.contractPoints = 1;
            else if (newPlan.requiredItem.tier == ItemTier.Tier2) newPlan.contractPoints = 2;
            else newPlan.contractPoints = 3;

            scheduledContracts.Add(newPlan);
            if (enableDebugLogs) Debug.Log($"<color=grey>CONTRACT SCHEDULED:</color> {newPlan.requiredAmount}x {newPlan.requiredItem.itemName}...");
        }
    }

    void HandleDailyRoutine()
    {
        int today = TimeManager.Instance.currentDay;

        // Baslama zamani gelen ihaleler
        for (int i = scheduledContracts.Count - 1; i >= 0; i--)
        {
            var plan = scheduledContracts[i];
            if (plan.startDayOfMonth == today)
            {
                StartContract(plan);
                scheduledContracts.RemoveAt(i);
            }
        }

        // Aktif ihalelerin suresini dusur
        for (int i = activeContracts.Count - 1; i >= 0; i--)
        {
            activeContracts[i].daysLeft--;
            if (activeContracts[i].daysLeft <= 0)
            {
                if (enableDebugLogs) Debug.Log($"<color=red>CONTRACT FAILED/EXPIRED:</color> {activeContracts[i].requiredItem.itemName} to {activeContracts[i].targetCity.cityName}");
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
        newContract.contractPoints = plan.contractPoints;

        if (SessionData.CurrentMode == SessionData.GameMode.SarayinElcisi)
        {
            var tumAjanlar = FindObjectsOfType<MerchantAgent>();
            foreach (var ajan in tumAjanlar)
            {
                // Ajanın kulağına fısılda (Fog of War'u deliyoruz)
                ajan.ReceiveContractBroadcast(newContract.targetCity, newContract.requiredItem);
            }
        }

        activeContracts.Add(newContract);
        if (enableDebugLogs) Debug.Log($"<color=orange>NEW CONTRACT ACTIVE:</color> {newContract.targetCity.cityName} needs {newContract.requiredAmount}x...");
    }


    public bool TryCompleteContract(CityController city, ItemData item, int amount, out int rewardGold, out int rewardPoints)
    {
        rewardGold = 0;
        rewardPoints = 0;
        var contract = activeContracts.Find(c => c.targetCity == city && c.requiredItem == item);

        if (contract != null)
        {
            // KURAL: AJANIN GETİRDİĞİ MAL İSTENENDEN BÜYÜK VEYA EŞİT OLMALI!
            if (amount >= contract.requiredAmount)
            {
                // ========================================================
                // TEK ATIŞTA İHALEYİ KAPATTI!
                // ========================================================
                rewardGold = contract.rewardGold;       // Kralın devasa altın ödülü
                rewardPoints = contract.contractPoints; // Krallık puanı

                activeContracts.Remove(contract);       // İhale başarıyla bitti, haritadan sil!
                return true;
            }
            else
            {
                // ========================================================
                // EKSİK MAL GETİRDİ (TAKSİT YASAK!)
                // ========================================================
                // İhale iptal edilmez, ajan o malı normal fiyattan satar, devasa ödülü ve puanı ALAMAZ.
                return false;
            }
        }
        return false;
    }

    // Ajan yola cikmadan once cazip mi diye bakar
    public int GetPotentialContractReward(CityController city, ItemData item)
    {
        var contract = activeContracts.Find(c => c.targetCity == city && c.requiredItem == item);
        if (contract != null)
        {
            return contract.rewardGold;
        }
        return -1;
    }
}