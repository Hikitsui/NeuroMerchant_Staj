using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

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
    public float trainingMapSize = 60f;

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
        List<ItemData> allItemsForCities = new List<ItemData>();

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
            // Tier 1
            for (int i = 0; i < 4; i++) villageProductionQueue.Enqueue(itemWheat);
            for (int i = 0; i < 3; i++) villageProductionQueue.Enqueue(itemWood);
            for (int i = 0; i < 3; i++) villageProductionQueue.Enqueue(itemFish);
            for (int i = 0; i < 3; i++) villageProductionQueue.Enqueue(itemCotton);
            // Tier 2
            for (int i = 0; i < 2; i++) villageProductionQueue.Enqueue(itemMeat);
            for (int i = 0; i < 2; i++) villageProductionQueue.Enqueue(itemCoal);
            for (int i = 0; i < 2; i++) villageProductionQueue.Enqueue(itemLeather);
            for (int i = 0; i < 2; i++) villageProductionQueue.Enqueue(itemIron);
            // Tier 3
            villageProductionQueue.Enqueue(itemClothes);
            villageProductionQueue.Enqueue(itemTools);
            villageProductionQueue.Enqueue(itemSpices);
            villageProductionQueue.Enqueue(itemJewelry);

            allItemsForCities = new List<ItemData> { itemWheat, itemWood, itemFish, itemCotton, itemMeat, itemCoal, itemLeather, itemIron, itemClothes, itemTools, itemSpices, itemJewelry };
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
        if (trainingMode && spawnedCities.Count > 0)
        {
            CreateBrokerForCluster(spawnedCities, "Training_Guild");
            return;
        }

        List<CityController> hubs = new List<CityController>();
        List<CityController> others = new List<CityController>();

        foreach (GameObject cityObj in spawnedCities)
        {
            CityController cc = cityObj.GetComponent<CityController>();
            if (cityObj.name.Contains("Grand")) hubs.Add(cc);
            else others.Add(cc);
        }

        foreach (CityController hub in hubs)
        {
            List<CityController> cluster = new List<CityController>();
            cluster.Add(hub);

            others.Sort((a, b) => Vector3.Distance(hub.transform.position, a.transform.position)
                                    .CompareTo(Vector3.Distance(hub.transform.position, b.transform.position)));

            int countToAdd = 3;
            for (int i = 0; i < countToAdd; i++)
            {
                if (others.Count > 0)
                {
                    cluster.Add(others[0]);
                    others.RemoveAt(0);
                }
            }

            List<GameObject> clusterObjs = new List<GameObject>();
            foreach (var c in cluster) clusterObjs.Add(c.gameObject);

            CreateBrokerForCluster(clusterObjs, $"Broker_{hub.cityName}");
        }
    }

    void CreateBrokerForCluster(List<GameObject> cluster, string brokerName)
    {
        if (brokerPrefab == null) return;

        Vector3 centroid = Vector3.zero;
        foreach (var obj in cluster) centroid += obj.transform.position;
        centroid /= cluster.Count;

        Vector3 brokerPos = GetNavMeshPos(centroid);
        if (brokerPos == Vector3.zero) brokerPos = cluster[0].transform.position;

        GameObject brokerObj = Instantiate(brokerPrefab, brokerPos, Quaternion.identity, this.transform);
        brokerObj.name = brokerName;

        RegionalBroker rb = brokerObj.GetComponent<RegionalBroker>();
        if (rb != null)
        {
            List<CityController> ccList = new List<CityController>();
            foreach (var obj in cluster) ccList.Add(obj.GetComponent<CityController>());

            rb.servicedSettlements = ccList;

            foreach (var c in ccList) c.assignedBroker = rb;
        }
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