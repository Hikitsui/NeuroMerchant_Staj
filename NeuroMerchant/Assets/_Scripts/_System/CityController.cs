using UnityEngine;
using System.Collections.Generic;

public class CityController : MonoBehaviour
{
    [Header("Identity & Population")]
    public string cityName;
    public bool isProducer; // True = Fabrika/Köy (Uretir), False = Şehir (Tuketir)
    public string assignedBrokerName; // Hangi broker cluster'ına ait

    [Header("Living City Settings")]
    public bool enablePopulationGrowth = false; // <--- Kıtlıkta nüfuslar taban yapmamasi icin (Varsayilan KAPALI)
    public bool enableDynamicStorage = false;   // <--- Nufus arttikca depo kapasitesi artsin mi? (Varsayilan KAPALI)
    public bool enableSaturation = false; // <--- Bu satırı ekle

    [Header("Event Modifiers (Read Only)")]
    public float consumptionMultiplier = 1.0f; // Normal: 1.0, Festival: 1.5
    public float productionMultiplier = 1.0f;  // Normal: 1.0, Kıtlık: 0.5

    public string activeEventName = ""; // Debug icin: "FESTIVAL", "WAR" vs.

    [Range(100, 5000)]
    public int population = 100;
    public int minPopulation = 50;
    public int maxPopulation = 5000;

    // Nufus degisim hizlari (%5)
    private float growthFactor = 1.05f;
    private float decayFactor = 0.95f;

    [Header("Feudal System")]
    public CityController sovereignCity;
    public List<CityController> satelliteVillages = new List<CityController>();

    // --- ML-AGENTS ICIN EKLEME: RESETLEME ---
    private int startStockBuffer; // Baslangic stogunu hafizada tutmak icin
    private int startPopBuffer;

    public void RegisterSatellite(CityController village)
    {
        if (!satelliteVillages.Contains(village))
        {
            satelliteVillages.Add(village);
        }
    }

    [System.Serializable]
    public class MarketItem
    {
        public ItemData itemData;

        [Header("Stock Settings")]
        public int currentStock;
        public int maxStock = 200;

        [Header("DEBUG INFO (Read Only)")]
        public int currentDynamicPrice;
        public int lastDailyConsumption;

        [Header("Production Settings")]
        public int dailyProduction = 15;
        public int dailyTax = 10;
    }

    [Header("Market")]
    public List<MarketItem> marketItems;

    void Start()
    {
        // timer bagla
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleDailyEconomy;
        }
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyEconomy;
        }
    }

    void Update()
    {
        // her frame fiyat guncellemesi (Debug)
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
            // --- STOK LIMITLERI (Once limiti belirle) ---
            if (enableDynamicStorage)
            {
                // Dinamik: Nufusa gore (200 + pop/10)
                newItem.maxStock = 200 + (startPop / 10);
            }
            else
            {
                // Sabit: Sehir=500, Koy=400 (Uretici=Koy)
                newItem.maxStock = producer ? 400 : 500;
            }

            // Başlangıç stoğu: köy dolu, şehir boş — fiyat asimetrisi garantili
            float randomRatio = producer
                ? Random.Range(0.75f, 0.90f)   // Köy: %75-90 dolu → ucuz
                : Random.Range(0.15f, 0.35f);  // Şehir: %15-35 dolu → pahalı
            newItem.currentStock = Mathf.RoundToInt(newItem.maxStock * randomRatio);

            newItem.dailyProduction = producer ? Random.Range(10, 20) : 0;

            marketItems.Add(newItem);
        }
    }

    // --- FIYAT HESAPLAMA MANTIGI ---
    // Denge noktası: maxStock/2 stokta = basePrice
    // Stok 0'a yakın  → 2x basePrice (kıtlık)
    // Stok maxStock/2 → 1x basePrice (denge)
    // Stok maxStock'a → 0.5x basePrice (bolluk)
    int CalculatePriceLogic(MarketItem marketItem)
    {
        float currentAmount = Mathf.Max(marketItem.currentStock, 1);
        float halfStock = marketItem.maxStock * 0.5f;

        // fillRatio: 0 = boş, 1 = yarı dolu (denge), 2 = tam dolu
        float fillRatio = currentAmount / halfStock;

        float priceMultiplier;

        if (fillRatio <= 1.0f)
        {
            // Kıtlık bölgesi: stok 0→yarı, fiyat 2x→1x
            priceMultiplier = Mathf.Lerp(2.0f, 1.0f, fillRatio);
        }
        else
        {
            // Bolluk bölgesi: stok yarı→dolu, fiyat 1x→0.5x
            priceMultiplier = Mathf.Lerp(1.0f, 0.5f, Mathf.Min(fillRatio - 1.0f, 1.0f));
        }

        float finalPrice = marketItem.itemData.basePrice * priceMultiplier;

        // Üretici indirimi: köy her zaman %20 daha ucuz satar
        if (isProducer) finalPrice *= 0.8f;

        return Mathf.Clamp(Mathf.RoundToInt(finalPrice), 1, 9999);
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
            float halfMax = marketItem.maxStock * 0.5f;
            float fillR = amount / halfMax;
            float mult = (fillR <= 1.0f)
                ? Mathf.Lerp(2.0f, 1.0f, fillR)
                : Mathf.Lerp(1.0f, 0.5f, Mathf.Min(fillR - 1.0f, 1.0f));

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
            // FORMUL: MaxPop/2 nufusta Base consumption kadar tuketsin. MaxPop'ta 2 kati.
            // Ornek: Pop=2500 (Yarim), Max=5000 -> Ratio=1. Base=25 -> Cons=25. (500 stok 20 gun yeter) = bugday temel alinarak planlandi egitim surecine gore degisiklik yapilabilir
            float consumptionRatio = (float)population / (maxPopulation / 2.0f);

            // --- Event Carpani eklendi ---
            int baseCons = Mathf.RoundToInt(item.itemData.dailyBaseConsumption * consumptionRatio);
            int totalConsumption = Mathf.RoundToInt(baseCons * consumptionMultiplier);

            if (totalConsumption < 1) totalConsumption = 1;
            item.lastDailyConsumption = totalConsumption;

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
                if (item.currentStock < item.maxStock)
                {
                    // --- Event Carpani eklendi ---
                    int productionAmount = Mathf.RoundToInt(item.dailyProduction * productionMultiplier);
                    item.currentStock += productionAmount;

                    // --- TAX SYSTEM (GOODS BASED) ---
                    // Uretilen malin %20'si (veya sabit miktar) vergi olarak gider
                    int taxAmount = Mathf.CeilToInt(productionAmount * 0.40f);

                    if (sovereignCity != null)
                    {
                        // Bagli oldugum sehire URUN olarak vergi ode
                        sovereignCity.ReceiveTax(item.itemData, taxAmount);

                        // Vergiyi stoktan dus (Cunku gonderdildi)
                        item.currentStock -= taxAmount;
                    }
                }
            }
        }
        UpdatePopulation(allNeedsMet);
    }

    public void ReceiveTax(ItemData item, int amount)
    {
        var marketItem = marketItems.Find(x => x.itemData == item);
        if (marketItem != null)
        {
            marketItem.currentStock += amount;
        }
    }

    void UpdatePopulation(bool isHappy)
    {
        // Şalter kapalıysa nüfusu elleme (Sabit kalsın)
        if (!enablePopulationGrowth) return;

        if (isHappy)
        {
            // nüfus artıs (%5)
            population = Mathf.RoundToInt(population * growthFactor);
        }
        else
        {
            // Kıtlık 
            population = Mathf.RoundToInt(population * decayFactor);
        }

        // Sınırlar
        population = Mathf.Clamp(population, minPopulation, maxPopulation);

        //TODO: dynamic storage degisecek populasyon sistemiyle birlikte sonsuz nufus ve sonsuz kapasiteye gidebilir. Aciliyet dusuk baslangicta bu fonksiyon kapali

        // --- YENI: DYNAMIC STORAGE --- 
        if (enableDynamicStorage)
        {
            foreach (var item in marketItems)
            {
                // Depo kapasitesi: Temel 200 + (Nüfus / 10)
                item.maxStock = 200 + (population / 10);
            }
        }
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

    // startStockBuffer = marketItems[0].currentStock; (Bunu tum itemler icin yapacak basit bir yapi lazim)
    // startPopBuffer = population;
    public void ResetCity()
    {
        population = 100;
        ClearEvent();

        foreach (var item in marketItems)
        {
            if (isProducer)
            {
                // Köy/üretici: dolu başlar (%75-90) — ucuz fiyat, bol stok
                float ratio = Random.Range(0.75f, 0.90f);
                item.currentStock = Mathf.RoundToInt(item.maxStock * ratio);
            }
            else
            {
                // Şehir/tüketici: boş başlar (%15-35) — pahalı fiyat, kıtlık
                float ratio = Random.Range(0.15f, 0.35f);
                item.currentStock = Mathf.RoundToInt(item.maxStock * ratio);
            }
        }
    }
}