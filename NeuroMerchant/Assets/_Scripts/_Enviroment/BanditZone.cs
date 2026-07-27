using UnityEngine;
using UnityEngine.AI;

public class BanditZone : MonoBehaviour
{
    [Header("Bölge Ayarları")]
    [Tooltip("Açık yeşil alanın yarıçapı (Haydutların doğabileceği maksimum uzaklık)")]
    public float zoneRadius = 50f;

    [Tooltip("Bu bölgede doğacak haydut sayısı (Zorluğa göre GameManager'dan değiştirilebilir)")]
    public int banditsToSpawn = 2;

    [Header("Referanslar")]
    public GameObject banditPrefab;

    // Oyunu başlatan ana script (örneğin GameManager) bu fonksiyonu çağıracak
    public void SpawnBanditsInZone()
    {
        if (banditPrefab == null)
        {
            Debug.LogWarning($"[BanditZone] {gameObject.name} için Bandit Prefab atanmamış!");
            return;
        }

        for (int i = 0; i < banditsToSpawn; i++)
        {
            // Açık yeşil çember içinde rastgele bir XY noktası seç
            Vector2 randomDir = Random.insideUnitCircle * zoneRadius;
            Vector3 randomPos = transform.position + new Vector3(randomDir.x, 0, randomDir.y);

            NavMeshHit hit;
            // Bu noktanın haritada (NavMesh üzerinde) geçerli bir yer olup olmadığını kontrol et
            if (NavMesh.SamplePosition(randomPos, out hit, zoneRadius, NavMesh.AllAreas))
            {
                // Haydudu geçerli noktada yarat
                GameObject newBandit = Instantiate(banditPrefab, hit.position, Quaternion.identity);
                newBandit.name = $"Bandit_{gameObject.name}_{i + 1}";

                // Haydudun beynine devriye merkezini (kendi doğduğu yer) söyle
                BanditController controller = newBandit.GetComponent<BanditController>();
                if (controller != null)
                {
                    controller.InitializeBandit(hit.position);
                }
            }
        }
    }

    // Unity Editöründe açık yeşil alanları rahatça görebilmen için
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f); // Yarı saydam açık yeşil
        Gizmos.DrawSphere(transform.position, zoneRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, zoneRadius);
    }
}