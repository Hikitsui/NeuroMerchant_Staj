using UnityEngine;

// Bu satir, Unity'nin sag tik menusuze "Economy > Item" diye bir secenek ekler.
[CreateAssetMenu(fileName = "New Item", menuName = "Economy/Item")]
public class ItemData : ScriptableObject
{
    public string itemName; // Urunun adi (Orn: Bugday)
    public int basePrice;   // Taban fiyati (Orn: 10 Altin)
}