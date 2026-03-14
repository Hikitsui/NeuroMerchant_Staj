using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

// ==============================================================
// CURRICULUM MANAGER — Step Bazlı Otomatik Ders Yönetimi
//
// Her 5000 adımda bir MerchantAgent bu scripte raporlama yapar.
// Raporlanan değer: o penceredeki episode'ların ortalama kümülatif ödülü.
//
// Ders geçiş eşikleri (Yol Haritasına göre):
//   Ders 0 → 1  : >= 0.8   (Temel Ticaret)
//   Ders 1 → 2  : >= 0.8   (Üretim Zinciri)
//   Ders 2 → 3  : >= 0.7   (Pazar Dinamiği)
//   Ders 3 → 4  : >= 0.7   (Envanter Yönetimi)
//   Ders 4 → 5  : >= 0.6   (Hafıza ve Sis)
//   Ders 5 → 6  : >= 0.6   (Bilgi Yatırımı)
//   Ders 6 → 7  : >= 0.5   (İmparatorluk)
//   Ders 7       : Final    (Kriz Yönetimi)
//
// Düşüş eşiği her ders için -0.2'dir.
// Lesson değişince sayaç sıfırlanır.
// ==============================================================
public class CurriculumManager : MonoBehaviour
{
    [Header("Referans")]
    public MerchantAgent merchantAgent;

    [Header("Run Kimliği")]
    [Tooltip("config.yaml --run-id ile aynı olmalı (örn: NeuroMerchant_V4)")]
    public string runId = "NeuroMerchant_V7";

    // Ders kaydı run_id'ye göre ayrı dosyaya
    private string SavePath => System.IO.Path.Combine(Application.dataPath, "..", $"curriculum_{runId}.txt");

    [Header("Ders Geçiş Eşikleri")]
    private static readonly float[] LevelUpThresholds =
    {
        0.85f, // [0] Ders 1 → 2
        0.82f, // [1] Ders 2 → 3
        0.80f, // [2] Ders 3 → 4
        0.78f, // [3] Ders 4 → 5
        0.75f, // [4] Ders 5 → 6
        0.72f, // [5] Ders 6 → 7
        999f,  // [6] Ders 7 final (geçiş yok)
        999f   // [7] Yedek
    };

    [Header("Düşüş Eşiği (Tüm Dersler)")]
    public float levelDownThreshold = -0.2f;

    [Header("Durum (Read Only)")]
    public int currentLesson = 1; // 1'den başlar
    public float lastWindowAvg = 0f;
    public int windowCountInLesson = 0;
    public float currentUpThreshold = 0.8f; // Inspector'da görünsün

    // Bu lesson'da toplanan pencere ortalamalarının listesi
    private List<float> lessonWindowAverages = new List<float>();

    // ==========================================================
    // ANA FONKSİYON — MerchantAgent her 5000 adımda çağırır
    // avg          : O penceredeki episode'ların ort. kümülatif ödülü
    // reportedLesson: Raporlama anındaki ders
    // ==========================================================
    private void Awake()
    {
        LoadLesson();
    }

    // ==========================================================
    // KAYDET / YÜKLE
    // ==========================================================
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
                // saved 1-7 arasında olmalı
                currentLesson = Mathf.Clamp(saved, 1, 7);
                // Dikkat: currentLesson 1 ise index 0'ı kullan
                currentUpThreshold = LevelUpThresholds[currentLesson - 1];
                Debug.Log($"<color=cyan>[Curriculum] Ders yüklendi: {currentLesson}</color>");
            }
        }
        else
        {
            // Kayıt yoksa Ders 1'den başla
            currentLesson = 1;
            currentUpThreshold = LevelUpThresholds[0]; // Ders 1'in eşiği
            SaveLesson();
            Debug.Log("[Curriculum] Kayıt bulunamadı, Ders 1'den başlanıyor.");
        }
    }

    public void ReportStepWindow(float avg, int reportedLesson)
    {
        // Farklı dersten gelen gecikmeli raporu yoksay
        if (reportedLesson != currentLesson)
        {
            Debug.Log($"[Curriculum] Gecikmeli rapor yoksayıldı " +
                      $"(Rapor L{reportedLesson} → Mevcut L{currentLesson})");
            return;
        }

        lessonWindowAverages.Add(avg);
        // Son 10 pencereyi tut, eskisini at (kayan pencere)
        if (lessonWindowAverages.Count > 40)
            lessonWindowAverages.RemoveAt(0);

        windowCountInLesson = lessonWindowAverages.Count;
        lastWindowAvg = avg;
        currentUpThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson - 1, 0, 6)];

        // Kayan pencere ortalaması (son 10)
        float lessonAvg = lessonWindowAverages.Average();

        Debug.Log($"[Curriculum] Ders {currentLesson} | " +
                  $"Pencere #{windowCountInLesson} | " +
                  $"Bu Pencere: {avg:F3} | " +
                  $"Ders Ort: {lessonAvg:F3} | " +
                  $"Geçiş Eşiği ≥{currentUpThreshold}");

        EvaluateAndDecide(lessonAvg);
    }

    // ==========================================================
    // DEĞERLENDİRME
    // ==========================================================
    private void EvaluateAndDecide(float lessonAvg)
    {
        float upThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson - 1, 0, 6)];

        // --- SEVİYE ATLATMA ---
        if (lessonAvg >= upThreshold && currentLesson < 7)
        {
            int old = currentLesson;
            currentLesson++;
            ResetLessonTracking();
            ApplyLessonToAgent();
            SaveLesson();
            Debug.Log($"<color=yellow>🏆 DERS ATLADI! {old} → {currentLesson}</color>");
        }
        // --- SEVİYE DÜŞÜRME ---
        else if (lessonAvg <= levelDownThreshold && currentLesson > 1) // Ders 1'den aşağı düşme
        {
            int old = currentLesson;
            currentLesson--;
            ResetLessonTracking();
            ApplyLessonToAgent();
            SaveLesson();
            Debug.LogWarning($"⚠️ DERS DÜŞTÜ! {old} → {currentLesson}");
        }
        else
        {
            Debug.Log($"[Curriculum] Ders {currentLesson} devam. " +
                      $"Ort: {lessonAvg:F3} " +
                      $"(Hedef ≥{upThreshold} | Düşüş ≤{levelDownThreshold})");
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
        if (merchantAgent == null) return;
        merchantAgent.currentLesson = currentLesson;
        merchantAgent.EndEpisode();
    }

    // ==========================================================
    // DEBUG ARAÇLARI (Inspector sağ tık menüsü)
    // ==========================================================
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

    [ContextMenu("Dersi Sıfırla (Ders 0)")]
    public void DebugReset()
    {
        currentLesson = 0;
        ResetLessonTracking();
        SaveLesson();
        ApplyLessonToAgent();
        Debug.Log("[DEBUG] Ders sıfırlandı → 0");
    }

    [ContextMenu("Manuel Ders Düşür")]
    public void DebugLevelDown()
    {
        if (currentLesson <= 0) return;
        currentLesson--;
        ResetLessonTracking();
        ApplyLessonToAgent();
        SaveLesson();
        Debug.Log($"[DEBUG] Manuel ders düşürüldü → {currentLesson}");
    }
}