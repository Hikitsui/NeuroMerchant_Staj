using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class WorldGenerator : MonoBehaviour
{
    [Header("DEVELOPER MODE")]
    public bool trainingMode = true; // Harita boyutunu ve görev sayısını kontrol eder
    public bool enablePopulationDynamics = false; // <-- YENİ: Nüfus artışını/azalışını kontrol eder

    [Header("Prefabs")]
    public GameObject cityPrefab;
    public GameObject villagePrefab;
    public GameObject brokerPrefab;

    [Header("Map Settings")]
    public float fullMapSize = 180f;
    public float trainingMapSize = 18f; // 21x21 harita için (şehirler ~9 birim aralıklı)

    // Grid Ayarlari
    private int gridCols;
    private int gridRows;

    [Header("Village Settings")]
    public float minVillageDist = 10f;
    public float maxVillageDist = 25f;

    [Header("Economy Items")]
    public ItemData itemWheat;
    public ItemData itemWood;
    public ItemData itemFish;
    public ItemData itemCotton;
    public ItemData itemMeat;
    public ItemData itemCoal;
    public ItemData itemLeather;
    public ItemData itemIron;
    public ItemData itemClothes;
    public ItemData itemTools;
    public ItemData itemSpices;
    public ItemData itemJewelry;

    private List<GameObject> spawnedCities = new List<GameObject>();
    private List<GameObject> spawnedVillages = new List<GameObject>();
    private List<ItemData> allItemsForCities = new List<ItemData>();
    private List<Vector3> allSettlementPositions = new List<Vector3>();

    private float currentMapSize;

    void Awake()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        string modeLog = trainingMode ? "TRAINING (Tiny World)" : "FULL PRODUCTION (Massive World)";
        string popLog = enablePopulationDynamics ? "DYNAMIC POPULATION (Growth ON)" : "STATIC POPULATION (Growth OFF)";

        Debug.Log($"<color=cyan>WORLD GENERATOR:</color> Initializing {modeLog} with {popLog}...");

        // --- ADIM 0: MOD AYARLARI ---
        if (trainingMode)
        {
            currentMapSize = trainingMapSize;
            gridCols = 2; // 2x2 = 4 Sehir
            gridRows = 2;
        }
        else
        {
            currentMapSize = fullMapSize;
            gridCols = 5; // 5x4 = 20 Sehir
            gridRows = 4;
        }

        // --- ADIM 1: URETIM LISTESINI HAZIRLA ---
        Queue<ItemData> villageProductionQueue = new Queue<ItemData>();
        allItemsForCities = new List<ItemData>();

        if (trainingMode)
        {
            villageProductionQueue.Enqueue(itemWheat);
            villageProductionQueue.Enqueue(itemWood);
            villageProductionQueue.Enqueue(itemIron);
            villageProductionQueue.Enqueue(itemCotton);
            villageProductionQueue.Enqueue(itemCoal);

            allItemsForCities = new List<ItemData> { itemWheat, itemWood, itemIron, itemCotton, itemCoal };
        }
        else
        {
            // ============================================================
            // 25 KÖY ÜRETIM PLANI
            // Grand_City_1-5: her biri 2 köy (low+mid veya low+high)
            // City_6-20:      her biri 1 köy (low tekrar + mid/high kalan)
            // ============================================================

            // Grand_City_1 → Wheat + Iron       (low + mid)
            villageProductionQueue.Enqueue(itemWheat);
            villageProductionQueue.Enqueue(itemIron);

            // Grand_City_2 → Wood + Leather      (low + mid)
            villageProductionQueue.Enqueue(itemWood);
            villageProductionQueue.Enqueue(itemLeather);

            // Grand_City_3 → Fish + Meat         (low + mid)
            villageProductionQueue.Enqueue(itemFish);
            villageProductionQueue.Enqueue(itemMeat);

            // Grand_City_4 → Cotton + Clothes    (low + mid)
            villageProductionQueue.Enqueue(itemCotton);
            villageProductionQueue.Enqueue(itemClothes);

            // Grand_City_5 → Coal + Tools        (low + high)
            villageProductionQueue.Enqueue(itemCoal);
            villageProductionQueue.Enqueue(itemTools);

            // City_6-20: 15 köy
            // LOW tekrar ×2 (her low ürün 2 kez daha) = 10 köy
            villageProductionQueue.Enqueue(itemWheat);
            villageProductionQueue.Enqueue(itemWheat);
            villageProductionQueue.Enqueue(itemWood);
            villageProductionQueue.Enqueue(itemWood);
            villageProductionQueue.Enqueue(itemFish);
            villageProductionQueue.Enqueue(itemFish);
            villageProductionQueue.Enqueue(itemCotton);
            villageProductionQueue.Enqueue(itemCotton);
            villageProductionQueue.Enqueue(itemCoal);
            villageProductionQueue.Enqueue(itemCoal);

            // HIGH + MID kalan = 5 köy
            villageProductionQueue.Enqueue(itemSpices);
            villageProductionQueue.Enqueue(itemJewelry);
            villageProductionQueue.Enqueue(itemIron);
            villageProductionQueue.Enqueue(itemLeather);
            villageProductionQueue.Enqueue(itemMeat);

            allItemsForCities = new List<ItemData>
            {
                itemWheat, itemWood, itemFish, itemCotton, itemCoal,  // LOW
                itemIron, itemLeather, itemMeat, itemClothes,          // MID
                itemTools, itemSpices, itemJewelry                     // HIGH
            };
        }

        // --- ADIM 2: GRID SISTEMI ILE SEHIRLERI OLUSTUR ---

        float cellWidth = currentMapSize / gridCols;
        float cellHeight = currentMapSize / gridRows;

        float startOffsetX = -currentMapSize / 2f + cellWidth / 2f;
        float startOffsetZ = -currentMapSize / 2f + cellHeight / 2f;

        int cityCount = 0;

        for (int x = 0; x < gridCols; x++)
        {
            for (int z = 0; z < gridRows; z++)
            {
                float cellCenterX = startOffsetX + (x * cellWidth);
                float cellCenterZ = startOffsetZ + (z * cellHeight);

                float randomOffsetX = Random.Range(-cellWidth * 0.25f, cellWidth * 0.25f);
                float randomOffsetZ = Random.Range(-cellHeight * 0.25f, cellHeight * 0.25f);

                Vector3 cityPos = new Vector3(cellCenterX + randomOffsetX, 0, cellCenterZ + randomOffsetZ);

                cityPos = GetNavMeshPos(cityPos);

                if (cityPos != Vector3.zero)
                {
                    GameObject cityObj = Instantiate(cityPrefab, cityPos, Quaternion.identity, this.transform);

                    string name = $"City_{cityCount + 1}";
                    if (!trainingMode && cityCount < 5) name = $"Grand_City_{cityCount + 1}";

                    cityObj.name = name;

                    CityController cc = cityObj.GetComponent<CityController>();
                    int startPop = (!trainingMode && cityCount < 5) ? Random.Range(300, 800) : Random.Range(100, 300);

                    cc.InitializeCity(name, false, startPop, allItemsForCities);

                    // --- YENİ EKLENEN KISIM: NÜFUS DİNAMİĞİNİ AYARLA ---
                    cc.enablePopulationGrowth = enablePopulationDynamics;
                    // ---------------------------------------------------

                    spawnedCities.Add(cityObj);
                    allSettlementPositions.Add(cityPos);
                    cityCount++;
                }
            }
        }

        // --- ADIM 3: KOYLERI DAGIT ---
        for (int i = 0; i < spawnedCities.Count; i++)
        {
            if (spawnedCities[i] == null) continue;

            int villagesToSpawn = 1;

            if (trainingMode)
            {
                if (i == 0) villagesToSpawn = 2;
            }
            else
            {
                if (spawnedCities[i].name.Contains("Grand")) villagesToSpawn = 2;
            }

            for (int k = 0; k < villagesToSpawn; k++)
            {
                if (villageProductionQueue.Count == 0) break;

                ItemData product = villageProductionQueue.Dequeue();

                Vector3 villagePos = GetValidVillagePosition(spawnedCities[i].transform.position);

                if (villagePos != Vector3.zero)
                {
                    GameObject villageObj = Instantiate(villagePrefab, villagePos, Quaternion.identity, this.transform);
                    villageObj.name = $"Village_{spawnedCities[i].name}_{product.itemName}";

                    CityController cc = villageObj.GetComponent<CityController>();
                    cc.InitializeCity(villageObj.name, true, Random.Range(50, 150), new List<ItemData> { product });

                    // --- YENİ EKLENEN KISIM: KÖYLER DE ETKİLENSİN ---
                    cc.enablePopulationGrowth = enablePopulationDynamics;
                    // ------------------------------------------------

                    CityController parentCityCheck = spawnedCities[i].GetComponent<CityController>();
                    if (parentCityCheck != null)
                    {
                        cc.sovereignCity = parentCityCheck;
                    }

                    spawnedVillages.Add(villageObj);
                    allSettlementPositions.Add(villagePos);
                }
            }
        }

        // --- ADIM 4: BROKER KURULUMU ---
        InitializeBrokersAndClusters();

        Debug.Log($"<color=green>WORLD GEN COMPLETE:</color> Cities: {spawnedCities.Count}. Pop Dynamics: {enablePopulationDynamics}");
    }

    void InitializeBrokersAndClusters()
    {
        if (BrokerManager.Instance == null) return;

        if (trainingMode)
        {
            // Training: 1 broker, tum sehirler + koyleri
            List<CityController> allCities = spawnedCities.Select(o => o.GetComponent<CityController>()).ToList();
            List<CityController> allVillages = spawnedVillages.Select(o => o.GetComponent<CityController>()).ToList();

            Vector3 centroid = Vector3.zero;
            foreach (var c in allCities) centroid += c.transform.position;
            if (allCities.Count > 0) centroid /= allCities.Count;

            Vector3 brokerPos = GetNavMeshPos(centroid);
            if (brokerPos == Vector3.zero) brokerPos = allCities[0].transform.position;

            // Fiziksel broker objesi spawn et
            if (brokerPrefab != null)
            {
                GameObject brokerObj = Instantiate(brokerPrefab, brokerPos, Quaternion.identity, this.transform);
                brokerObj.name = "Training_Guild";
            }

            BrokerManager.Instance.RegisterBroker("Training_Guild", brokerPos, allCities, allVillages);
            BrokerManager.Instance.SetActiveItems(new List<ItemData> { itemWheat });
            return;
        }

        // Full mod: Grand_City basli sehirler hub, etrafindakiler cluster
        List<CityController> hubs = new List<CityController>();
        List<CityController> others = new List<CityController>();

        foreach (GameObject cityObj in spawnedCities)
        {
            CityController cc = cityObj.GetComponent<CityController>();
            if (cityObj.name.Contains("Grand")) hubs.Add(cc);
            else others.Add(cc);
        }

        int brokerIndex = 0;
        foreach (CityController hub in hubs)
        {
            List<CityController> clusterCities = new List<CityController> { hub };

            others.Sort((a, b) =>
                Vector3.Distance(hub.transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(hub.transform.position, b.transform.position)));

            for (int i = 0; i < 3 && others.Count > 0; i++)
            {
                clusterCities.Add(others[0]);
                others.RemoveAt(0);
            }

            List<CityController> clusterVillages = spawnedVillages
                .Select(o => o.GetComponent<CityController>())
                .Where(v => v != null && clusterCities.Contains(v.sovereignCity))
                .ToList();

            Vector3 centroid = Vector3.zero;
            foreach (var c in clusterCities) centroid += c.transform.position;
            centroid /= clusterCities.Count;

            Vector3 brokerPos = GetNavMeshPos(centroid);
            if (brokerPos == Vector3.zero) brokerPos = clusterCities[0].transform.position;

            // Fiziksel broker objesi spawn et
            if (brokerPrefab != null)
            {
                string bName = $"Broker_{hub.cityName}";
                GameObject brokerObj = Instantiate(brokerPrefab, brokerPos, Quaternion.identity, this.transform);
                brokerObj.name = bName;
            }

            BrokerManager.Instance.RegisterBroker(
                $"Broker_{hub.cityName}", brokerPos, clusterCities, clusterVillages);

            brokerIndex++;
        }

        BrokerManager.Instance.SetActiveItems(allItemsForCities);
    }

    Vector3 GetNavMeshPos(Vector3 pos)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(pos, out hit, 15f, NavMesh.AllAreas)) return hit.position;
        return Vector3.zero;
    }

    Vector3 GetValidVillagePosition(Vector3 centerCity)
    {
        for (int i = 0; i < 50; i++)
        {
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            float distance = Random.Range(minVillageDist, maxVillageDist);
            Vector3 candidate = centerCity + new Vector3(randomDir.x, 0, randomDir.y) * distance;

            if (IsPositionValid(candidate, 8f)) return GetNavMeshPos(candidate);
        }
        return Vector3.zero;
    }

    bool IsPositionValid(Vector3 pos, float minSpacing)
    {
        foreach (var existing in allSettlementPositions)
        {
            if (Vector3.Distance(pos, existing) < minSpacing) return false;
        }
        return true;
    }
}