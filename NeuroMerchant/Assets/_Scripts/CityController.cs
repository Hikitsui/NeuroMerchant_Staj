using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MarketItem
{
    public ItemData itemData;
    public int currentStock;
    public int maxStock = 100;
    public int consumptionRate; // Amount added or removed per tick
}

public class CityController : MonoBehaviour
{
    public string cityName;
    public bool isProducer; // TRUE = Village (Produces), FALSE = City (Consumes)

    [Header("Market Inventory")]
    public List<MarketItem> marketItems;

    private float timer;

    void Start()
    {
        cityName = gameObject.name;
    }

    void Update()
    {
        // Economic simulation tick (Runs every 1 second)
        timer += Time.deltaTime;
        if (timer >= 1.0f)
        {
            SimulateEconomy();
            timer = 0;
        }
    }

    void SimulateEconomy()
    {
        foreach (var item in marketItems)
        {
            if (isProducer)
            {
                // Producer logic: Increase stock until max
                if (item.currentStock < item.maxStock)
                    item.currentStock += item.consumptionRate;
            }
            else
            {
                // Consumer logic: Decrease stock until zero
                if (item.currentStock > 0)
                    item.currentStock -= item.consumptionRate;
            }
        }
    }

    public int GetPrice(ItemData targetItem)
    {
        MarketItem foundItem = marketItems.Find(x => x.itemData == targetItem);
        if (foundItem == null) return 0;

        // Calculate Supply vs Demand
        float fullness = (float)foundItem.currentStock / foundItem.maxStock;

        // Scarcity (Empty) -> 3x Price | Abundance (Full) -> 0.5x Price
        float priceMultiplier = Mathf.Lerp(3.0f, 0.5f, fullness);

        return Mathf.RoundToInt(foundItem.itemData.basePrice * priceMultiplier);
    }
}