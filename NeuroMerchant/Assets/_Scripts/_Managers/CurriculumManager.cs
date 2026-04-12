using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

// ==============================================================
// CURRICULUM MANAGER — TURNUVA MODU İÇİN DÜZELTİLMİŞ
// ==============================================================
public class CurriculumManager : MonoBehaviour
{
    public static CurriculumManager Instance;

    [Header("Mod Ayarı")]
    public bool isTrainingMode = true; // ❗ Turnuva sahnesinde FALSE olmalı

    [Header("Referans")]
    public MerchantAgent merchantAgent;

    [Header("Run Kimliği")]
    public string runId = "NeuroMerchant_V7";

    private string SavePath => System.IO.Path.Combine(Application.dataPath, "..", $"curriculum_{runId}.txt");

    [Header("Ders Geçiş Eşikleri")]
    private static readonly float[] LevelUpThresholds =
    {
        1.30f, 1.50f, 1.80f, 2.00f, 1.90f, 1.80f, 999f, 999f
    };

    [Header("Zorunlu Test Süresi")]
    public int minWindowsToLevelUp = 5;

    [Header("Düşüş Eşiği")]
    public float levelDownThreshold = -1.5f;

    [Header("Durum (Read Only)")]
    public int currentLesson = 1;
    public float lastWindowAvg = 0f;
    public int windowCountInLesson = 0;
    public float currentUpThreshold = 0f;

    private List<float> lessonWindowAverages = new List<float>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // TURNUVA MODUNDA: Curriculum sistemini tamamen devre dışı bırak
        if (!isTrainingMode)
        {
            currentLesson = 6; // En zor ders (sadece bilgi amaçlı)
            Debug.Log("<color=magenta>[Curriculum] TURNUVA MODU: Curriculum sistemi devre dışı. Agentlar resetlenmeyecek.</color>");
            return; // ❗ Burada durur, agent'lara müdahale etmez
        }

        // Eğitim modundaysa normal çalış
        LoadLesson();
        ApplyLessonToAgent();
    }

    private void SaveLesson()
    {
        if (!isTrainingMode) return; // Turnuvada kayıt yapma

        File.WriteAllText(SavePath, currentLesson.ToString());
        Debug.Log($"[Curriculum] Ders kaydedildi: {currentLesson} → {SavePath}");
    }

    private void LoadLesson()
    {
        if (!isTrainingMode) return;

        if (File.Exists(SavePath))
        {
            string txt = File.ReadAllText(SavePath).Trim();
            if (int.TryParse(txt, out int saved))
            {
                currentLesson = Mathf.Clamp(saved, 1, 7);
                currentUpThreshold = LevelUpThresholds[currentLesson - 1];
                Debug.Log($"<color=cyan>[Curriculum] Ders yüklendi: {currentLesson}</color>");
            }
        }
        else
        {
            currentLesson = 1;
            currentUpThreshold = LevelUpThresholds[0];
            SaveLesson();
            Debug.Log("[Curriculum] Kayıt bulunamadı, Ders 1'den başlanıyor.");
        }
    }

    public void ReportStepWindow(float avg, int reportedLesson)
    {
        if (!isTrainingMode) return; // ❗ Turnuvada rapor alma

        if (reportedLesson != currentLesson)
        {
            Debug.Log($"[Curriculum] Gecikmeli rapor yoksayıldı (Rapor L{reportedLesson} → Mevcut L{currentLesson})");
            return;
        }

        lessonWindowAverages.Add(avg);

        if (lessonWindowAverages.Count > 20)
            lessonWindowAverages.RemoveAt(0);

        windowCountInLesson = lessonWindowAverages.Count;
        lastWindowAvg = avg;
        currentUpThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson - 1, 0, 6)];

        float lessonAvg = lessonWindowAverages.Average();

        Debug.Log($"[Curriculum] Ders {currentLesson} | " +
                  $"Pencere #{windowCountInLesson}/{minWindowsToLevelUp} | " +
                  $"Bu Pencere: {avg:F3} | " +
                  $"Ders Ort: {lessonAvg:F3} | " +
                  $"Geçiş Eşiği ≥{currentUpThreshold}");

        EvaluateAndDecide(lessonAvg);
    }

    private void EvaluateAndDecide(float lessonAvg)
    {
        if (!isTrainingMode) return; // ❗ Turnuvada karar verme

        float upThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson - 1, 0, 6)];

        // Seviye atlama
        if (windowCountInLesson >= minWindowsToLevelUp && lessonAvg >= upThreshold && currentLesson < 7)
        {
            int old = currentLesson;
            currentLesson++;
            ResetLessonTracking();
            ApplyLessonToAgent();
            SaveLesson();
            Debug.Log($"<color=yellow>🏆 DERS ATLADI! {old} → {currentLesson}</color>");
        }
        // Seviye düşürme
        else if (windowCountInLesson >= minWindowsToLevelUp && lessonAvg <= levelDownThreshold && currentLesson > 1)
        {
            if (currentLesson == 4)
            {
                Debug.Log($"[Curriculum] Ders 4 düşüşü engellendi.");
            }
            else
            {
                int old = currentLesson;
                currentLesson--;
                ResetLessonTracking();
                ApplyLessonToAgent();
                SaveLesson();
                Debug.LogWarning($"⚠️ DERS DÜŞTÜ! {old} → {currentLesson}");
            }
        }
    }

    private void ResetLessonTracking()
    {
        if (!isTrainingMode) return;

        lessonWindowAverages.Clear();
        windowCountInLesson = 0;
        lastWindowAvg = 0f;
    }

    private void ApplyLessonToAgent()
    {
        if (!isTrainingMode) return; // ❗ TURNUVADA AGENT'LARA DOKUNMA!

        // Eğitim modunda agentlara ders ata
        MerchantAgent[] allAgentsInScene = FindObjectsOfType<MerchantAgent>();

        foreach (var agent in allAgentsInScene)
        {
            agent.currentLesson = currentLesson;
            agent.EndEpisode(); // Sadece eğitimde reset
        }

        Debug.Log($"[Curriculum] {allAgentsInScene.Length} ajana Ders {currentLesson} uygulandı ve resetlendi.");
    }

    // --- DEBUG KOMUTLARI (Sadece eğitimde çalışır) ---
    [ContextMenu("Manuel Ders Atla")]
    public void DebugLevelUp()
    {
        if (!isTrainingMode)
        {
            Debug.LogWarning("[Curriculum] Turnuva modunda manuel ders değiştirme kapalı.");
            return;
        }

        if (currentLesson >= 7) return;
        currentLesson++;
        ResetLessonTracking();
        ApplyLessonToAgent();
        SaveLesson();
        Debug.Log($"[DEBUG] Manuel ders atlandı → {currentLesson}");
    }

    [ContextMenu("Dersi Sıfırla (Ders 1)")]
    public void DebugReset()
    {
        if (!isTrainingMode) return;

        currentLesson = 1;
        ResetLessonTracking();
        SaveLesson();
        ApplyLessonToAgent();
        Debug.Log("[DEBUG] Ders sıfırlandı → 1");
    }

    [ContextMenu("Manuel Ders Düşür")]
    public void DebugLevelDown()
    {
        if (!isTrainingMode) return;

        if (currentLesson <= 1) return;
        currentLesson--;
        ResetLessonTracking();
        ApplyLessonToAgent();
        SaveLesson();
        Debug.Log($"[DEBUG] Manuel ders düşürüldü → {currentLesson}");
    }
}