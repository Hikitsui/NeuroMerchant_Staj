using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance;

    [Header("DEVELOPER MODE")]
    public bool trainingMode = true;

    [Header("Debug Settings")]
    public bool enableDebugLogs = false;

    public enum EventType { None, Festival, Famine, Boom, War }

    [System.Serializable]
    public class PendingEvent
    {
        public int startDayOfMonth;
        public CityController targetCity;
        public int duration;
        public EventType type;
    }

    [System.Serializable]
    public class ActiveEvent
    {
        public string name;
        public CityController targetCity;
        public int durationDays;
        public int daysElapsed;
        public float consumptMod;
        public float productMod;
    }

    [Header("Status")]
    public List<PendingEvent> scheduledEvents = new List<PendingEvent>();
    public List<ActiveEvent> activeEvents = new List<ActiveEvent>();

    [Header("Settings")]
    public int trainingEventsCount = 2; 
    public int productionEventsCount = 10; 

    private CityController[] allCities;

    void Awake() { Instance = this; }

    // Eski void Start() yerine bu gelecek:
    public void InitManager(bool isTraining)
    {
        this.trainingMode = isTraining;

        // ZIRH: Sahte şehirleri (Lonca vs.) etkinliklerden uzak tut!
        allCities = FindObjectsOfType<CityController>()
            .Where(c => c != null && c.marketItems != null && c.marketItems.Count > 0)
            .ToArray();

        if (allCities.Length == 0)
        {
            Debug.LogError("<color=red>[EventManager] Marketi olan gerçek şehir bulunamadı!</color>");
            return;
        }

        // ... Altındaki kodlar (TimeManager ve Event üretme) aynen kalıyor ...
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth -= ScheduleNextMonthEvents;

            TimeManager.Instance.OnNewDay += HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth += ScheduleNextMonthEvents;
        }

        ScheduleNextMonthEvents();
        Debug.Log($"<color=orange>[EventManager] Başlatıldı. Mod: {(trainingMode ? "Eğitim" : "Turnuva")}</color>");
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth -= ScheduleNextMonthEvents;
        }
    }

    void ScheduleNextMonthEvents()
    {
        scheduledEvents.Clear();

        // 1. MODA VE ZORLUĞA GÖRE EVENT SAYISINI BELİRLE
        int eventCount = productionEventsCount;
        bool isWinterMode = (SessionData.CurrentMode == SessionData.GameMode.AcimasizKis);

        if (!trainingMode)
        {
            if (isWinterMode)
            {
                // --- İŞTE BURASI: Zorluğa göre saf kaos miktarı! ---
                // 0 = Kolay (5 Olay), 1 = Orta (8 Olay), 2 = Zor (12 Olay)
                if (SessionData.DifficultyLevel == 0) eventCount = 5;
                else if (SessionData.DifficultyLevel == 1) eventCount = 8;
                else eventCount = 12;
            }
            else
            {
                eventCount = productionEventsCount;
            }
        }

        // Konsola yazarken hangi zorlukta kaç kriz çıktığını da görelim
        string modeLog = trainingMode ? "TRAINING" : (isWinterMode ? $"WINTER CHAOS (Zorluk: {SessionData.DifficultyLevel})" : "FULL PROD");
        if (enableDebugLogs) Debug.Log($"<color=magenta>EVENT MANAGER:</color> Drafting schedule ({modeLog}). Target Events: {eventCount}");

        int safeCount = Mathf.Min(eventCount, allCities.Length);
        List<CityController> potentialTargets = allCities.OrderBy(x => Random.value).Take(safeCount).ToList();

        foreach (var city in potentialTargets)
        {
            PendingEvent newPlan = new PendingEvent();
            newPlan.targetCity = city;
            newPlan.startDayOfMonth = Random.Range(1, 29);

            // Kış modunda krizler daha uzun sürer (10-20 gün), Normalde (5-15 gün)
            newPlan.duration = isWinterMode ? Random.Range(10, 20) : Random.Range(5, 15);

            // 2. MODA GÖRE OLAY SEÇ
            if (isWinterMode)
            {
                // KIŞ MODU: Sadece kötü olaylar (Kıtlık ve Savaş)
                newPlan.type = city.isProducer ? EventType.Famine : EventType.War;
            }
            else
            {
                // NORMAL MOD: İyi ve Kötü karışık
                newPlan.type = GetValidEventForCity(city);
            }

            scheduledEvents.Add(newPlan);
            if (enableDebugLogs) Debug.Log($"<color=grey>SCHEDULED:</color> {newPlan.type} in {city.cityName} on Day {newPlan.startDayOfMonth}.");
        }
    }

    EventType GetValidEventForCity(CityController city)
    {
        if (city.isProducer)
        {
            // ÜRETİCİLER (KÖYLER): Üretimi etkileyen olaylar
            return (Random.value > 0.5f) ? EventType.Famine : EventType.Boom;
        }
        else
        {
            // TÜKETİCİLER (ŞEHİRLER): Tüketimi etkileyen olaylar
            return (Random.value > 0.5f) ? EventType.Festival : EventType.War;
        }
    }

    void HandleDailyRoutine()
    {
        int today = TimeManager.Instance.currentDay;

        // Baslama zamani gelenleri baslat
        for (int i = scheduledEvents.Count - 1; i >= 0; i--)
        {
            var plan = scheduledEvents[i];
            if (plan.startDayOfMonth == today)
            {
                StartEvent(plan);
                scheduledEvents.RemoveAt(i);
            }
        }

        // Suresi dolanlari bitir
        for (int i = activeEvents.Count - 1; i >= 0; i--)
        {
            var evt = activeEvents[i];
            evt.daysElapsed++;

            if (evt.daysElapsed >= evt.durationDays)
            {
                if (enableDebugLogs) Debug.Log($"<color=green>EVENT ENDED:</color> {evt.name} is over in {evt.targetCity.cityName}.");
                evt.targetCity.ClearEvent();
                activeEvents.RemoveAt(i);
            }
        }
    }

    void StartEvent(PendingEvent plan)
    {
        if (activeEvents.Exists(x => x.targetCity == plan.targetCity)) return;

        ActiveEvent newEvent = new ActiveEvent();
        newEvent.targetCity = plan.targetCity;
        newEvent.durationDays = plan.duration;
        newEvent.daysElapsed = 0;

        switch (plan.type)
        {
            case EventType.Festival: // ŞEHİR
                newEvent.name = "Harvest Festival";
                newEvent.consumptMod = 2.0f; // Tüketim x2 (Çok yerler)
                break;

            case EventType.War: // ŞEHİR (Kriz)
                newEvent.name = "Civil War";
                newEvent.consumptMod = 3.0f; // Tüketim x3 (Ordu besleme - Stok eritir)
                break;

            case EventType.Famine: // KÖY (Kötü Hasat)
                newEvent.name = "Great Drought";
                newEvent.productMod = 0.2f;  // Üretim ÇÖKER (x0.2) -> Fiyatlar artar
                break;

            case EventType.Boom: // KÖY (İyi Hasat)
                newEvent.name = "Bountiful Harvest";
                newEvent.productMod = 2.0f;  // Üretim PATLAR (x2.0) -> Fiyatlar düşer
                break;
        }

        plan.targetCity.ApplyEvent(newEvent.name, newEvent.consumptMod, newEvent.productMod);

        activeEvents.Add(newEvent);
        if (enableDebugLogs) Debug.Log($"<color=red>EVENT STARTED:</color> {newEvent.name} ({plan.type}) in {plan.targetCity.cityName}!");
    }
}