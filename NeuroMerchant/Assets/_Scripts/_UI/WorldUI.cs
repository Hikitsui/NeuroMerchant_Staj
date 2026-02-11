using UnityEngine;
using TMPro;
using System.Text;
using System.Collections.Generic;

public class WorldUI : MonoBehaviour
{
    public static WorldUI Instance;

    [Header("UI Elements")]
    public GameObject mainPanel;
    public TMP_Text eventInfoText;
    public TMP_Text agentLogText;

    [Header("Settings")]
    public int maxLogLines = 8;

    private List<string> tradeLogs = new List<string>();

    void Awake()
    {
        Instance = this;
        if (mainPanel != null) mainPanel.SetActive(false);
    }

    void Update()
    {
        // U Tusuna basinca ac/kapa
        if (Input.GetKeyDown(KeyCode.U))
        {
            if (mainPanel != null)
            {
                mainPanel.SetActive(!mainPanel.activeSelf);
                if (mainPanel.activeSelf) UpdateEventDisplay();
            }
        }

        if (mainPanel != null && mainPanel.activeSelf)
        {
            UpdateEventDisplay();
        }
    }

    void UpdateEventDisplay()
    {
        if (EventManager.Instance == null) return;

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("<color=red><b>--- ACTIVE CHAOS ---</b></color>");
        if (EventManager.Instance.activeEvents.Count > 0)
        {
            foreach (var evt in EventManager.Instance.activeEvents)
            {
                sb.AppendLine($"> {evt.name} @ {evt.targetCity.cityName} ({evt.daysElapsed}/{evt.durationDays} Days)");
            }
        }
        else
        {
            sb.AppendLine("<i>World is peaceful... for now.</i>");
        }

        sb.AppendLine("");

        sb.AppendLine("<color=yellow><b>--- UPCOMING DOOM ---</b></color>");
        if (EventManager.Instance.scheduledEvents.Count > 0)
        {
            foreach (var plan in EventManager.Instance.scheduledEvents)
            {
                if (plan.startDayOfMonth > TimeManager.Instance.currentDay)
                {
                    string typeName = plan.type.ToString();
                    sb.AppendLine($"> Day {plan.startDayOfMonth}: {typeName} @ {plan.targetCity.cityName}");
                }
            }
        }
        // --- DÜZELTİLEN KISIM BURASI (Ait olduğu yere geldi) ---
        else
        {
            sb.AppendLine("<i>No more events this month.</i>");
        }
        // ---------------------------------------------------------

        sb.AppendLine("");

        // YENI EKLENEN KISIM: AKTIF IHALELER
        sb.AppendLine("<color=orange><b>--- ROYAL CONTRACTS ---</b></color>");
        if (ContractManager.Instance != null && ContractManager.Instance.activeContracts.Count > 0)
        {
            foreach (var contract in ContractManager.Instance.activeContracts)
            {
                sb.AppendLine($"> Deliver {contract.requiredAmount}x {contract.requiredItem.itemName}");
                sb.AppendLine($"  To: {contract.targetCity.cityName} ({contract.daysLeft} Days) | Reward: <color=green>{contract.rewardGold} G</color>");
            }
        }
        else
        {
            sb.AppendLine("<i>No active contracts.</i>");
        }

        if (eventInfoText != null) eventInfoText.text = sb.ToString();
    }

    public void AddLog(string message)
    {
        // YENI: Mesajin basina gercek saati ekle [14:30:05]
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
        string finalMessage = $"<color=grey>[{timestamp}]</color> {message}";

        tradeLogs.Add(finalMessage);

        if (tradeLogs.Count > maxLogLines)
        {
            tradeLogs.RemoveAt(0);
        }

        StringBuilder sb = new StringBuilder();
        // Tersten dongu kuralim ki EN YENI mesaj EN USTTE olsun (Console gibi)
        for (int i = tradeLogs.Count - 1; i >= 0; i--)
        {
            sb.AppendLine(tradeLogs[i]);
        }

        if (agentLogText != null) agentLogText.text = sb.ToString();
    }
}