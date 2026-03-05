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
    public string runId = "NeuroMerchant_V4";

    // Ders kaydı run_id'ye göre ayrı dosyaya
    private string SavePath => System.IO.Path.Combine(Application.dataPath, "..", $"curriculum_{runId}.txt");

    [Header("Ders Geçiş Eşikleri (Yol Haritası)")]
    // Index = mevcut ders, değer = o dersten bir üste geçiş eşiği
    private static readonly float[] LevelUpThresholds =
    {
        0.8f,  // Ders 0 → 1
        0.8f,  // Ders 1 → 2
        0.7f,  // Ders 2 → 3
        0.7f,  // Ders 3 → 4
        0.6f,  // Ders 4 → 5
        0.6f,  // Ders 5 → 6
        0.5f,  // Ders 6 → 7
        999f,  // Ders 7 = Final, geçiş yok
    };

    [Header("Düşüş Eşiği (Tüm Dersler)")]
    public float levelDownThreshold = -0.2f;

    [Header("Durum (Read Only)")]
    public int currentLesson = 0;
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
                currentLesson = saved;
                currentUpThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson, 0, 7)];
                Debug.Log($"<color=cyan>[Curriculum] Ders yüklendi: {currentLesson}</color>");
            }
        }
        else
        {
            Debug.Log("[Curriculum] Kayıt bulunamadı, Ders 0'dan başlanıyor.");
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
        if (lessonWindowAverages.Count > 10)
            lessonWindowAverages.RemoveAt(0);

        windowCountInLesson = lessonWindowAverages.Count;
        lastWindowAvg = avg;
        currentUpThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson, 0, 7)];

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
        float upThreshold = LevelUpThresholds[Mathf.Clamp(currentLesson, 0, 7)];

        // --- SEVİYE ATLATMA ---
        if (lessonAvg >= upThreshold && currentLesson < 7)
        {
            int old = currentLesson;
            currentLesson++;
            ResetLessonTracking();
            ApplyLessonToAgent();

            SaveLesson();
            Debug.Log($"<color=yellow>🏆 DERS ATLADI! {old} → {currentLesson} " +
                      $"(Ort: {lessonAvg:F3} ≥ {upThreshold})</color>");
        }
        // --- SEVİYE DÜŞÜRME ---
        else if (lessonAvg <= levelDownThreshold && currentLesson > 0)
        {
            int old = currentLesson;
            currentLesson--;
            ResetLessonTracking();
            ApplyLessonToAgent();

            SaveLesson();
            Debug.LogWarning($"⚠️ DERS DÜŞTÜ! {old} → {currentLesson} " +
                             $"(Ort: {lessonAvg:F3} ≤ {levelDownThreshold})");
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