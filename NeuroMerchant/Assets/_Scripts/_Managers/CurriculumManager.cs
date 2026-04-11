using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// CURRICULUM MANAGER — ZORLAŞTIRILMIŞ VE STABİLİZE EDİLMİŞ VERSİYON
// ==============================================================
public class CurriculumManager : MonoBehaviour
{
    [Header("Referans")]
    public MerchantAgent merchantAgent;

    [Header("Run Kimliği")]
    public string runId = "NeuroMerchant_V7";

    private string SavePath => System.IO.Path.Combine(Application.dataPath, "..", $"curriculum_{runId}.txt");

    [Header("Ders Geçiş Eşikleri (Zorlaştırılmış)")]
    private static readonly float[] LevelUpThresholds =
    {
        1.30f, // [0] Ders 1 → 2 (Sadece Wheat ile istikrarlı kar kanıtlanmalı)
        1.50f, // [1] Ders 2 → 3 (Çoklu ürünlerde hata payı az olmalı)
        1.80f, // [2] Ders 3 → 4 (Discrete branch kontrolünü tam çözmeli)
        2.00f, // [3] Ders 4 → 5 (Sis perdesi ve hafıza kullanımında stabilite)
        1.90f, // [4] Ders 5 → 6 (Broker kullanımı)
        1.80f, // [5] Ders 6 → 7 (Tam Ekonomi)
        999f,  // [6] Final
        999f   // [7] Yedek
    };

    [Header("Zorunlu Test Süresi")]
    [Tooltip("Ajanın seviye atlayabilmesi için en az bu kadar pencere (örn 5 x 50k = 250k adım) o derste kalması ve istikrarını koruması şarttır.")]
    public int minWindowsToLevelUp = 5;

    [Header("Düşüş Eşiği")]
    [Tooltip("Ajanın alt sınıfa düşmesi için gereken ciddi başarısızlık sınırı.")]
    public float levelDownThreshold = -1.5f;

    [Header("Durum (Read Only)")]
    public int currentLesson = 1;
    public float lastWindowAvg = 0f;
    public int windowCountInLesson = 0;
    public float currentUpThreshold = 0f;

    private List<float> lessonWindowAverages = new List<float>();

    private void Awake()
    {
        LoadLesson();
    }

    private void SaveLesson()
    {
        File.WriteAllText(SavePath, currentLesson.ToString());
        Debug.Log($"[Curriculum] Ders kaydedildi: {currentLesson} → {SavePath}");
    }

    private void LoadLesson()
    {
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
        if (reportedLesson != currentLesson)
        {
            Debug.Log($"[Curriculum] Gecikmeli rapor yoksayıldı (Rapor L{reportedLesson} → Mevcut L{currentLesson})");
            return;
        }

        lessonWindowAverages.Add(avg);

        // Kayan pencere limitini 20'ye sabitledik (Son 1 milyon adımın ortalaması)
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
        float upThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson - 1, 0, 6)];

        // --- SEVİYE ATLATMA (Minimum pencere şartı eklendi!) ---
        if (windowCountInLesson >= minWindowsToLevelUp && lessonAvg >= upThreshold && currentLesson < 7)
        {
            int old = currentLesson;
            currentLesson++;
            ResetLessonTracking();
            ApplyLessonToAgent();
            SaveLesson();
            Debug.Log($"<color=yellow>🏆 DERS ATLADI! {old} → {currentLesson}</color>");
        }
        // --- SEVİYE DÜŞÜRME ---
        else if (windowCountInLesson >= minWindowsToLevelUp && lessonAvg <= levelDownThreshold && currentLesson > 1)
        {
            // YENİ: Sadece Ders 4'ten Ders 3'e düşüşü engelle!
            if (currentLesson == 4)
            {
                Debug.Log($"[Curriculum] Ders 4 (Sis Perdesi) eğitimi olduğu için Ders 3'e otomatik düşüş engellendi.");
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
        else
        {
            string status = (windowCountInLesson < minWindowsToLevelUp) ? "(Test Süresi Bekleniyor)" : "(Puan Bekleniyor)";
            Debug.Log($"[Curriculum] Ders {currentLesson} devam. Ort: {lessonAvg:F3} {status}");
        }
    }

    private void ResetLessonTracking()
    {
        lessonWindowAverages.Clear();
        windowCountInLesson = 0;
        lastWindowAvg = 0f;
    }

    private void ApplyLessonToAgent()
    {
        // Sahnedeki TÜM ajanları bul ve hepsine aynı dersi uygula
        MerchantAgent[] allAgentsInScene = FindObjectsOfType<MerchantAgent>();

        foreach (var agent in allAgentsInScene)
        {
            agent.currentLesson = currentLesson;
            agent.EndEpisode(); // Yeni derse temiz bir başlangıç yapmaları için bölümü bitir
        }

        Debug.Log($"[Curriculum] Sahnedeki toplam {allAgentsInScene.Length} ajana Ders {currentLesson} uygulandı.");
    }

    [ContextMenu("Manuel Ders Atla")]
    public void DebugLevelUp()
    {
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
        currentLesson = 1;
        ResetLessonTracking();
        SaveLesson();
        ApplyLessonToAgent();
        Debug.Log("[DEBUG] Ders sıfırlandı → 1");
    }

    [ContextMenu("Manuel Ders Düşür")]
    public void DebugLevelDown()
    {
        if (currentLesson <= 1) return;
        currentLesson--;
        ResetLessonTracking();
        ApplyLessonToAgent();
        SaveLesson();
        Debug.Log($"[DEBUG] Manuel ders düşürüldü → {currentLesson}");
    }
}