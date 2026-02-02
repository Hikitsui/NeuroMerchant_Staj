using UnityEngine;
using System.Collections.Generic;

public class CityController : MonoBehaviour
{
    [Header("Identity")]
    public string cityName;
    public bool isProducer; // True = Village (Factory), False = City (Consumer)

    [Header("Feudal System")]
    // The city that owns this settlement.
    // IF this is a Village, drag its Master City here.
    // IF this is a City, leave this empty (None).
    public CityController sovereignCity;

    [System.Serializable]
    public class MarketItem
    {
        public ItemData itemData;

        [Header("Stock Settings")]
        public int currentStock;
        public int maxStock = 200; // Target stock level

        [Header("Economy")]
        public int basePrice = 10;

        [Header("DEBUG INFO (Read Only)")]
        public int currentDynamicPrice; // Watch this in Inspector to see real-time price!

        [Header("Production Settings")]
        public int dailyProduction = 15; // How much is produced daily?
        public int dailyTax = 10;        // How much is sent to the Lord daily?
    }

    [Header("Market")]
    public List<MarketItem> marketItems;

    void Start()
    {
        // Subscribe to the Day Cycle
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleDailyEconomy;
        }
    }

    void OnDestroy()
    {
        // Unsubscribe to prevent errors
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyEconomy;
        }
    }

    void Update()
    {
        // Calculate prices every frame so we can see them in Inspector
        UpdateDebugPrices();
    }

    // --- VISUAL DEBUGGING ---
    void UpdateDebugPrices()
    {
        foreach (var item in marketItems)
        {
            item.currentDynamicPrice = CalculatePriceLogic(item);
        }
    }

    // --- BALANCED AND REALISTIC PRICE FORMULA (v6.0) ---
    int CalculatePriceLogic(MarketItem marketItem)
    {
        // 1. FILL RATIO (a value between 0.0 and 3.0)
        float fillRatio = (float)marketItem.currentStock / marketItem.maxStock;

        float priceMultiplier = 1.0f;

        // 2. DETERMINE THE CURVE
        if (fillRatio < 1.0f)
        {
            // --- SCARCITY STATE (Stock < Max) ---
            // As stock decreases, the price increases but does NOT SKYROCKET.
            // Stock = 0   -> 3.0x (Maximum cap)
            // Stock = 100 -> 1.0x (Normal)
            priceMultiplier = Mathf.Lerp(3.0f, 1.0f, fillRatio);
        }
        else
        {
            // --- ABUNDANCE STATE (Stock > Max) ---
            // As stock overflows, the price decreases but NEVER BECOMES FREE.
            // Stock = 100     -> 1.0x (Normal)
            // Stock = 200+    -> 0.4x (Maximum 60% discount)
            // By using (fillRatio - 1.0f), we remap the 1–2 range to 0–1
            priceMultiplier = Mathf.Lerp(1.0f, 0.4f, Mathf.Min(fillRatio - 1.0f, 1.0f));
        }

        // 3. CALCULATE BASED ON BASE PRICE
        float finalPrice = marketItem.basePrice * priceMultiplier;

        // 4. PRODUCER DISCOUNT (Factory Sale)
        // Villages should always be 20% cheaper than cities to encourage trade.
        if (isProducer)
        {
            finalPrice *= 0.8f;
        }

        // 5. SAFETY (Min 1, Max 1000)
        return Mathf.Clamp(Mathf.RoundToInt(finalPrice), 1, 1000);
    }


    // --- PUBLIC API FOR AGENTS ---
    public int GetPrice(ItemData item)
    {
        foreach (var marketItem in marketItems)
        {
            if (marketItem.itemData == item)
            {
                return CalculatePriceLogic(marketItem);
            }
        }
        return 0; // Item not sold here
    }

    // --- DAILY ECONOMY SIMULATION ---
    void HandleDailyEconomy()
    {
        // Only Producers (Villages) generate resources
        if (!isProducer) return;

        foreach (var item in marketItems)
        {
            // 1. PRODUCTION
            // Allow stock to grow up to 5x MaxStock to allow extreme price crashes
            if (item.currentStock < item.maxStock * 5)
            {
                item.currentStock += item.dailyProduction;
            }

            // 2. TAX / LOGISTICS
            if (sovereignCity != null)
            {
                // Can we pay the tax?
                int taxToPay = Mathf.Min(item.currentStock, item.dailyTax);

                if (taxToPay > 0)
                {
                    // Find the same item in the Sovereign City
                    var lordItem = sovereignCity.marketItems.Find(x => x.itemData == item.itemData);

                    if (lordItem != null)
                    {
                        // Transfer goods
                        item.currentStock -= taxToPay;
                        lordItem.currentStock += taxToPay;

                        // Optional: Clamp Lord's stock so it doesn't overflow infinitely?
                        // For now, we let it grow so price drops there too.
                    }
                }
            }
        }
    }
}