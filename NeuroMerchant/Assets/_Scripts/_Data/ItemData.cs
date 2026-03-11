using UnityEngine;

public enum ItemTier
{
    Tier1 = 1, // Temel mallar (Wheat, Wood, Coal, Fish) — maxStock: 600
    Tier2 = 2, // Orta mallar (Iron, Leather, Meat, Clothes) — maxStock: 400
    Tier3 = 3  // Lüks mallar (Tools, Spices, Jewelry) — maxStock: 200
}

[CreateAssetMenu(fileName = "New Item", menuName = "Trade/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("Global Economy")]
    public int basePrice = 10;

    [Header("Item Tier")]
    public ItemTier tier = ItemTier.Tier1; // Inspector'dan ayarla

    [Header("Consumption Settings")]
    public int dailyBaseConsumption = 5;
}