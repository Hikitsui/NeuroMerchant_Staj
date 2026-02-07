using UnityEngine;
using System.Collections.Generic;

public class CityController : MonoBehaviour
{
    [Header("Identity & Population")]
    public string cityName;
    public bool isProducer; // True = Fabrika/Köy (Uretir), False = Şehir (Tuketir)
    public RegionalBroker assignedBroker;

    [Header("Living City Settings")]
    public bool enablePopulationGrowth = false; // <--- VARSAYILAN KAPALI (Kıtlıkta nüfus ölmesin diye)

    [Header("Event Modifiers (Read Only)")]
    public float consumptionMultiplier = 1.0f; // Normal: 1.0, Festival: 1.5
    public float productionMultiplier = 1.0f;  // Normal: 1.0, Kıtlık: 0.5

    public string activeEventName = ""; // Debug icin: "FESTIVAL", "WAR" vs.

    [Range(100, 5000)]
    public int population = 100; // Varsayilan Nufus
    public int minPopulation = 50;
    public int maxPopulation = 5000;

    // Nufus degisim hizlari (%5)
    private float growthFactor = 1.05f;
    private float decayFactor = 0.95f;

    [Header("Feudal System")]
    public CityController sovereignCity;

    [System.Serializable]
    public class MarketItem
    {
        public ItemData itemData;

        [Header("Stock Settings")]
        public int currentStock;
        public int maxStock = 200;

        [Header("DEBUG INFO (Read Only)")]
        public int currentDynamicPrice;
        public int lastDailyConsumption; // Debug: Dün kaç tane yendi?

        [Header("Production Settings")]
        public int dailyProduction = 15;
        public int dailyTax = 10;
    }

    [Header("Market")]
    public List<MarketItem> marketItems;

    void Start()
    {
        // Zamanlayıcıya abone ol
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleDailyEconomy;
        }
    }

    void OnDestroy()
    {
        // Abonelikten çık (Hata olmasın)
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyEconomy;
        }
    }

    void Update()
    {
        // Fiyatları her karede güncelle (Inspector'da görmek için)
        UpdateDebugPrices();
    }

    void UpdateDebugPrices()
    {
        foreach (var item in marketItems)
        {
            item.currentDynamicPrice = CalculatePriceLogic(item);
        }
    }

    // --- ALTYAPI: RANDOM HARITA ICIN KURULUM (Gelecek Özellik) ---
    public void InitializeCity(string name, bool producer, int startPop, List<ItemData> productsToSell)
    {
        cityName = name;
        isProducer = producer;
        population = startPop;

        marketItems = new List<MarketItem>();

        foreach (var data in productsToSell)
        {
            MarketItem newItem = new MarketItem();
            newItem.itemData = data;
            // Üreticiyse biraz stokla başla, değilse boş başla
            newItem.currentStock = producer ? 50 : 0;
            // Depo kapasitesi nüfusa göre artsın
            newItem.maxStock = 200 + (startPop / 10);

            newItem.dailyProduction = producer ? Random.Range(10, 20) : 0;

            marketItems.Add(newItem);
        }
    }

    // --- FIYAT HESAPLAMA MANTIGI (Doygunluk Eğrisi) ---
    int CalculatePriceLogic(MarketItem marketItem)
    {
        float currentAmount = Mathf.Max(marketItem.currentStock, 1);
        float fillRatio = currentAmount / marketItem.maxStock;

        float priceMultiplier = 1.0f;

        if (fillRatio < 1.0f)
        {
            // Kıtlık: Fiyat 3 katına kadar çıkabilir
            priceMultiplier = Mathf.Lerp(3.0f, 1.0f, fillRatio);
        }
        else
        {
            // Bolluk: Fiyat %40'a kadar düşebilir
            priceMultiplier = Mathf.Lerp(1.0f, 0.4f, Mathf.Min(fillRatio - 1.0f, 1.0f));
        }

        // Base Price'ı ScriptableObject'ten (ItemData) çekiyoruz
        float finalPrice = marketItem.itemData.basePrice * priceMultiplier;

        // Üretici İndirimi (Fabrika Satış)
        if (isProducer) finalPrice *= 0.8f;

        return Mathf.Clamp(Mathf.RoundToInt(finalPrice), 1, 1000);
    }

    // Ajanlar tekil fiyat sormak için bunu kullanır
    public int GetPrice(ItemData item)
    {
        foreach (var marketItem in marketItems)
        {
            if (marketItem.itemData == item) return CalculatePriceLogic(marketItem);
        }
        return 0;
    }

    // --- TOPLU SATIŞ SİMÜLASYONU (Marjinal Fayda) ---
    // Ajan: "Sana 20 tane satarsam elime toplam kaç geçer?"
    public int GetBulkSellValue(ItemData item, int amountToSell)
    {
        var marketItem = marketItems.Find(x => x.itemData == item);
        if (marketItem == null) return 0;

        int expectedTotalIncome = 0;
        int tempStock = marketItem.currentStock; // Sanal stok

        for (int i = 0; i < amountToSell; i++)
        {
            float amount = Mathf.Max(tempStock, 1);
            float ratio = amount / marketItem.maxStock;

            // Aynı mantığı tekrar uygula
            float mult = (ratio < 1.0f) ? Mathf.Lerp(3.0f, 1.0f, ratio) : Mathf.Lerp(1.0f, 0.4f, Mathf.Min(ratio - 1.0f, 1.0f));

            float price = marketItem.itemData.basePrice * mult;
            if (isProducer) price *= 0.8f;

            expectedTotalIncome += Mathf.Clamp(Mathf.RoundToInt(price), 1, 1000);

            // Sanal stoğu artır (Bir sonraki ürün daha ucuza satılacak)
            tempStock++;
        }
        return expectedTotalIncome;
    }

    // --- GÜNLÜK EKONOMİ VE NÜFUS DÖNGÜSÜ ---
    void HandleDailyEconomy()
    {
        bool allNeedsMet = true;

        foreach (var item in marketItems)
        {
            // 1. EVENT ETKILI TUKETIM
            float populationMultiplier = (float)population / 100.0f;

            // --- BURAYI DEGISTIRDIK: Event Carpanini ekledik ---
            int baseCons = Mathf.RoundToInt(item.itemData.dailyBaseConsumption * populationMultiplier);
            int totalConsumption = Mathf.RoundToInt(baseCons * consumptionMultiplier);

            if (totalConsumption < 1) totalConsumption = 1;
            item.lastDailyConsumption = totalConsumption; // Debug ekraninda artisi gorelim

            if (item.currentStock >= totalConsumption)
            {
                item.currentStock -= totalConsumption;
            }
            else
            {
                item.currentStock = 0;
                allNeedsMet = false;
            }

            // 2. EVENT ETKILI URETIM
            if (isProducer)
            {
                if (item.currentStock < item.maxStock * 5)
                {
                    // --- BURAYI DEGISTIRDIK: Event Carpanini ekledik ---
                    int productionAmount = Mathf.RoundToInt(item.dailyProduction * productionMultiplier);
                    item.currentStock += productionAmount;
                }
            }

            // ... Vergi ve Nufus kismi ayni ...
        }
        UpdatePopulation(allNeedsMet);
    }

    void UpdatePopulation(bool isHappy)
    {
        // Şalter kapalıysa nüfusu elleme (Sabit kalsın)
        if (!enablePopulationGrowth) return;

        if (isHappy)
        {
            // Her şey yolunda, nüfus artıyor (%5)
            population = Mathf.RoundToInt(population * growthFactor);
        }
        else
        {
            // Kıtlık var, insanlar terk ediyor (%5)
            population = Mathf.RoundToInt(population * decayFactor);
        }

        // Sınırlar
        population = Mathf.Clamp(population, minPopulation, maxPopulation);
    }

    public void ApplyEvent(string eventName, float consMult, float prodMult)
    {
        activeEventName = eventName;
        consumptionMultiplier = consMult;
        productionMultiplier = prodMult;
    }

    public void ClearEvent()
    {
        activeEventName = "";
        consumptionMultiplier = 1.0f;
        productionMultiplier = 1.0f;
    }
}