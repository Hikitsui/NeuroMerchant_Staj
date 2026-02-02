using UnityEngine;
using System; // Action eventleri icin

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    [Header("Time Settings")]
    public float realSecondsPerGameDay = 5.0f; // Gercek hayatta 5 saniye = Oyunda 1 Gün
    public int currentDay = 1;
    private float timer;

    // Tum sehirlerin dinleyecegi "Gun Bitti" zili
    public event Action OnNewDay;

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
        Debug.Log($"<color=cyan>--- DAY {currentDay} BEGAN ---</color>");

        // Zile bas! Tum sehirler duysun ve uretim yapsin.
        OnNewDay?.Invoke();
    }
}