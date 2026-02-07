using UnityEngine;
using System;

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    [Header("Time Settings")]
    public float realSecondsPerGameDay = 2.0f; // Test icin hizlandirdim (2 sn = 1 Gun)

    [Header("Calendar")]
    public int currentDay = 1;
    public int currentMonth = 1;
    public int currentYear = 1;

    private float timer;
    private int daysPerMonth = 30;

    // Olaylar
    public event Action OnNewDay;
    public event Action OnNewMonth; // Aylik maas/kira/event tetikleyicisi

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= realSecondsPerGameDay)
        {
            AdvanceDay();
        }
    }

    void AdvanceDay()
    {
        timer = 0;
        currentDay++;

        // AY DONGUSU
        if (currentDay > daysPerMonth)
        {
            currentDay = 1;
            currentMonth++;
            if (currentMonth > 12)
            {
                currentMonth = 1;
                currentYear++;
            }

            Debug.Log($"<color=magenta>--- NEW MONTH! (Month {currentMonth}, Year {currentYear}) ---</color>");
            OnNewMonth?.Invoke(); // Event Manager bunu dinleyecek
        }

        Debug.Log($"Day {currentDay} / Month {currentMonth}");
        OnNewDay?.Invoke();
    }
}