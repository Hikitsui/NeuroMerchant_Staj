using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class EventManager : MonoBehaviour
{
    public static EventManager Instance;

    [Header("DEVELOPER MODE")]
    public bool trainingMode = true; 

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

    void Start()
    {
        allCities = FindObjectsOfType<CityController>();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += HandleDailyRoutine;
            TimeManager.Instance.OnNewMonth += ScheduleNextMonthEvents;
        }

        ScheduleNextMonthEvents();
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

        // MODA GORE SAYIYI BELIRLE
        int eventCount = trainingMode ? trainingEventsCount : productionEventsCount;
        string modeLog = trainingMode ? "TRAINING (Low Chaos)" : "FULL (High Chaos)";

        Debug.Log($"<color=magenta>EVENT MANAGER:</color> Drafting schedule ({modeLog}). Target Events: {eventCount}");

        // Sehir sayisi event sayisindan azsa hata vermesin diye kontrol
        int safeCount = Mathf.Min(eventCount, allCities.Length);

        List<CityController> potentialTargets = allCities.OrderBy(x => Random.value).Take(safeCount).ToList();

        foreach (var city in potentialTargets)
        {
            PendingEvent newPlan = new PendingEvent();
            newPlan.targetCity = city;
            newPlan.startDayOfMonth = Random.Range(1, 29);
            newPlan.duration = Random.Range(5, 15);

            // SEHIR TIPINE GORE OLAY SEC
            newPlan.type = GetValidEventForCity(city);

            scheduledEvents.Add(newPlan);

            string cityType = city.isProducer ? "Producer" : "Consumer";
            Debug.Log($"<color=grey>SCHEDULED:</color> {newPlan.type} in {city.cityName} ({cityType}) on Day {newPlan.startDayOfMonth}.");
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
                Debug.Log($"<color=green>EVENT ENDED:</color> {evt.name} is over in {evt.targetCity.cityName}.");
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
        Debug.Log($"<color=red>EVENT STARTED:</color> {newEvent.name} ({plan.type}) in {plan.targetCity.cityName}!");
    }
}