using UnityEngine;
using System.Collections.Generic;

public class CityController : MonoBehaviour
{
    [Header("Identity & Population")]
    public string cityName;
    public bool isProducer; // True = Fabrika/Köy (Uretir), False = Şehir (Tuketir)
    public string assignedBrokerName; // Hangi broker cluster'ina ait
    [HideInInspector] public RegionalBroker assignedBroker; // Runtime'da WorldGenerator tarafindan atanir

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
    public int minPopulation = 100;
    public int maxPopulation = 600;

    // Nufus degisim hizlari (%5)
    private float growthFactor = 1.05f;
    private float decayFactor = 0.95f;

    [Header("Feudal System")]
    public CityController sovereignCity;
    public List<CityController> satelliteVillages = new List<CityController>();

    // --- ML-AGENTS ICIN EKLEME: RESETLEME ---
    public bool freezeReset = false; // Eger true ise, Episode Reset'te bu sehrin stogu/nufusu sifirlanmaz.
    private int startStockBuffer; // Baslangic stogunu hafizada tutmak icin
    private int startPopBuffer;

    [Header("Dinamik Vergi Ayarları (Köy için)")]
    [Tooltip("Günlük verginin baseConsumption'a oranı (min-max)")]
    public Vector2 taxRateRange = new Vector2(0.75f, 1.25f);
    [Tooltip("Üretimin vergiden fazlası — köyde stok biriksin")]
    public float productionSurplus = 5f;

    [Header("Haftalık Toplu Sevkiyat (Bulk Tax)")]
    public bool enableWeeklyBulkTax = true;
    public int bulkTaxDayInterval = 7;
    public int bulkTaxAmount = 200;
    private int lastBulkTaxDay = -1;

    // Tier'a göre şehir maxStock
    public static int GetMaxStockForTier(ItemTier tier)
    {
        switch (tier)
        {
            case ItemTier.Tier1: return 600; // Temel: Wheat, Wood, Coal, Fish
            case ItemTier.Tier2: return 400; // Orta: Iron, Leather, Meat, Clothes
            case ItemTier.Tier3: return 200; // Lüks: Tools, Spices, Jewelry
            default: return 400;
        }
    }

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

            // --- STOK LİMİTLERİ: Tier bazlı ---
            if (enableDynamicStorage)
            {
                newItem.maxStock = 200 + (startPop / 10);
            }
            else if (producer)
            {
                // Köy stok limiti tier'dan bağımsız sabit
                newItem.maxStock = 400;
            }
            else
            {
                // Şehir: tier'a göre maxStock
                newItem.maxStock = GetMaxStockForTier(data.tier);
            }

            float randomRatio = producer
                ? Random.Range(0.30f, 0.50f)
                : Random.Range(0.15f, 0.25f);
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
        // KÖY (isProducer): Fiyat her zaman sabit = basePrice
        if (isProducer)
            return marketItem.itemData.basePrice;

        // ŞEHİR (tüketici): Stok bazlı dinamik fiyat
        // Stok 0          → 2x basePrice (kıtlık)
        // Stok maxStock/2 → 1x basePrice (denge)
        // Stok maxStock   → 0.5x basePrice (bolluk)
        float currentAmount = Mathf.Max(marketItem.currentStock, 1);
        float halfStock = marketItem.maxStock * 0.5f;
        float fillRatio = currentAmount / halfStock;

        float priceMultiplier;
        if (fillRatio <= 1.0f)
            priceMultiplier = Mathf.Lerp(2.0f, 1.0f, fillRatio);
        else
            priceMultiplier = Mathf.Lerp(1.0f, 0.5f, Mathf.Min(fillRatio - 1.0f, 1.0f));

        return Mathf.Clamp(Mathf.RoundToInt(marketItem.itemData.basePrice * priceMultiplier), 1, 9999);
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
            float price;
            if (isProducer)
            {
                // Köy: sabit fiyat
                price = marketItem.itemData.basePrice;
            }
            else
            {
                // Şehir: stok bazlı dinamik fiyat
                float halfMax = marketItem.maxStock * 0.5f;
                float fillR = Mathf.Max(tempStock, 1) / halfMax;
                float mult = (fillR <= 1.0f)
                    ? Mathf.Lerp(2.0f, 1.0f, fillR)
                    : Mathf.Lerp(1.0f, 0.5f, Mathf.Min(fillR - 1.0f, 1.0f));
                price = marketItem.itemData.basePrice * mult;
            }

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
            // TÜKETİM: x = dailyBaseConsumption, ratio = pop/300
            // pop=150→x/2 | pop=300→x | pop=600→3x/2  (clamp: 0.5–1.5)
            float popRatio = Mathf.Clamp(population / 300f, 0.5f, 1.5f);
            int totalConsumption = Mathf.Max(1, Mathf.RoundToInt(item.itemData.dailyBaseConsumption * popRatio * consumptionMultiplier));
            item.lastDailyConsumption = totalConsumption;

            if (item.currentStock >= totalConsumption)
                item.currentStock -= totalConsumption;
            else
            {
                item.currentStock = 0;
                allNeedsMet = false;
            }

            // KERVAN TAKVİYESİ — sadece şehirlerde (tüketici), stok kritik altındaysa
            // basePrice'a göre tier belirlenir: ucuz=temel, pahalı=lüks
            if (!isProducer)
            {
                int caravanThreshold, caravanMin, caravanMax;
                switch (item.itemData.tier)
                {
                    case ItemTier.Tier1: // Temel (Wheat, Wood, Coal, Fish)
                        caravanThreshold = 20; caravanMin = 50; caravanMax = 150; break;
                    case ItemTier.Tier2: // Orta (Iron, Leather, Meat, Clothes)
                        caravanThreshold = 20; caravanMin = 50; caravanMax = 150; break;
                    case ItemTier.Tier3: // Lüks (Tools, Spices, Jewelry)
                        caravanThreshold = 20; caravanMin = 50; caravanMax = 150; break;
                    default:
                        caravanThreshold = 20; caravanMin = 50; caravanMax = 150; break;
                }

                if (item.currentStock < caravanThreshold)
                {
                    int supply = Random.Range(caravanMin, caravanMax);
                    item.currentStock = Mathf.Min(item.currentStock + supply, item.maxStock);
                    // Debug.Log($"[KERVAN] {cityName} | {item.itemData.itemName} +{supply}");
                }
            }
            if (isProducer)
            {
                // 1. DİNAMİK VERGİ — baseConsumption'a endeksli, rastgele çarpanla
                if (sovereignCity != null)
                {
                    float randomMult = Random.Range(taxRateRange.x, taxRateRange.y);
                    int dailyTax = Mathf.RoundToInt(item.itemData.dailyBaseConsumption * randomMult);
                    int taxAmount = Mathf.Min(dailyTax, item.currentStock);
                    if (taxAmount > 0)
                    {
                        item.currentStock -= taxAmount;
                        sovereignCity.ReceiveTax(item.itemData, taxAmount);
                    }
                }

                // 2. ÜRETİM — vergi + surplus kadar üret, maxStock'u geçme
                if (item.currentStock < item.maxStock)
                {
                    float baseTax = item.itemData.dailyBaseConsumption * ((taxRateRange.x + taxRateRange.y) / 2f);
                    int productionAmount = Mathf.RoundToInt((baseTax + productionSurplus) * productionMultiplier);
                    item.currentStock = Mathf.Min(item.currentStock + productionAmount, item.maxStock);
                }

                // 3. HAFTALIK TOPLU SEVKİYAT
                if (enableWeeklyBulkTax && sovereignCity != null && TimeManager.Instance != null)
                {
                    int currentDay = TimeManager.Instance.TotalDays;
                    if (currentDay > 0 && currentDay % bulkTaxDayInterval == 0 && currentDay != lastBulkTaxDay)
                    {
                        lastBulkTaxDay = currentDay;
                        int bulk = Mathf.Min(bulkTaxAmount, item.currentStock);
                        if (bulk > 0)
                        {
                            item.currentStock -= bulk;
                            sovereignCity.ReceiveTax(item.itemData, bulk);
                            Debug.Log($"[BULK TAX] {cityName} → {sovereignCity.cityName} | {item.itemData.itemName} +{bulk}");
                        }
                    }
                }
            }
        }
        UpdatePopulation(allNeedsMet);
        UpdateDebugPrices();
    }

    public void ReceiveTax(ItemData item, int amount)
    {
        var marketItem = marketItems.Find(x => x.itemData == item);
        if (marketItem != null)
        {
            marketItem.currentStock = Mathf.Min(marketItem.currentStock + amount, marketItem.maxStock);
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
    public void ResetCity(bool forceFullReset = false)
    {
        if (freezeReset) return;

        ClearEvent();

        // Zorunlu reset veya stok negatifse tam sıfırla
        if (forceFullReset)
        {
            population = 100;
            PerformFullReset();
            return;
        }

        // Soft reset: sadece kritik uç durumlara müdahale et
        foreach (var item in marketItems)
        {
            // Kıtlık engeli: %5'in altındaysa %15'e çek
            if (item.currentStock < item.maxStock * 0.05f)
                item.currentStock = Mathf.RoundToInt(item.maxStock * 0.15f);

            // Enflasyon engeli: %95'in üstündeyse %75'e indir
            else if (item.currentStock > item.maxStock * 0.95f)
                item.currentStock = Mathf.RoundToInt(item.maxStock * 0.75f);

            // Aksi halde stok olduğu gibi kalır
        }
    }

    private void PerformFullReset()
    {
        foreach (var item in marketItems)
        {
            if (isProducer)
                item.currentStock = Mathf.RoundToInt(item.maxStock * Random.Range(0.30f, 0.50f));
            else
                item.currentStock = Mathf.RoundToInt(item.maxStock * Random.Range(0.15f, 0.25f));
        }
    }
}