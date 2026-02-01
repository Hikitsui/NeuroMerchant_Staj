using UnityEngine;
using TMPro; // TextMeshPro namespace

public class TradingUI : MonoBehaviour
{
    public static TradingUI Instance;

    [Header("UI Elements")]
    public GameObject panelObj;
    public TMP_Text infoText;

    private CityController currentCity;
    private MerchantAgent currentAgent;
    private ItemData currentItem;

    void Awake()
    {
        Instance = this;
        Hide();
    }

    // Called when Agent enters a city
    public void ShowTrade(CityController city, MerchantAgent agent)
    {
        currentCity = city;
        currentAgent = agent;

        // Check if city has items
        if (city.marketItems.Count > 0)
        {
            currentItem = city.marketItems[0].itemData;
            panelObj.SetActive(true);
            RefreshUI();
        }
    }

    public void Hide()
    {
        panelObj.SetActive(false);
        currentCity = null;
    }

    void RefreshUI()
    {
        if (currentCity == null || currentItem == null) return;

        int price = currentCity.GetPrice(currentItem);
        int cityStock = currentCity.marketItems[0].currentStock;

        infoText.text = $"CITY: {currentCity.cityName}\n" +
                        $"ITEM: {currentItem.itemName}\n" +
                        $"PRICE: {price} Gold\n" +
                        $"STOCK: {cityStock}\n" +
                        $"MY MONEY: {currentAgent.currentMoney}";
    }

    // --- BUY LOGIC ---
    public void OnBuyButton()
    {
        if (currentCity == null) return;

        int price = currentCity.GetPrice(currentItem);
        int stock = currentCity.marketItems[0].currentStock;

        // Check conditions: Have Money? City has Stock?
        if (currentAgent.currentMoney >= price && stock > 0)
        {
            // Transaction
            currentAgent.currentMoney -= price;
            currentCity.marketItems[0].currentStock--; // Decrease city stock

            // TODO: Add item to Agent's inventory list (Next Step)

            Debug.Log("BOUGHT Item!");
            RefreshUI();
        }
    }

    // --- SELL LOGIC (Eklenen Kısım) ---
    public void OnSellButton()
    {
        if (currentCity == null) return;

        int price = currentCity.GetPrice(currentItem);

        // Transaction (Simplified for testing)
        // Logic: I give item -> I get money -> City gets stock

        currentAgent.currentMoney += price;
        currentCity.marketItems[0].currentStock++; // Increase city stock

        // TODO: Remove item from Agent's inventory list (Next Step)

        Debug.Log("SOLD Item!");
        RefreshUI();
    }
}