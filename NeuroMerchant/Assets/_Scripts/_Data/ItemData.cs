using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Trade/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("Global Economy")]
    public int basePrice = 10; // Fiyat artik burada da durabilir referans icin

    [Header("Consumption Settings")]
    // Iste senin istedigin ayar: Bu urunden gunde kac tane yenir?
    public int dailyBaseConsumption = 5;
}