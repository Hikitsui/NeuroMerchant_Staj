using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class WorldGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject cityPrefab;
    public GameObject villagePrefab;
    public GameObject brokerPrefab; // <--- NEW: Broker Prefab


    [Header("Map Settings")]
    [Header("Map Settings")]
    public float mapSize = 85f;       // 20x20 Plane (200 birim) icin 85f (-85, +85) tum haritayi doldurur.
    public float cityMinDistance = 8f; // Harita buyudugu icin sehirleri biraz acalim (Rahat yerlesim)
    
    [Header("Village Settings")]
    public float minVillageDist = 1.5f; // KOYLER IP GIBI YAKIN (Degismedi)
    public float maxVillageDist = 3.5f;

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

    // Olusan sehirleri ve tum yerlesimleri tutan listeler
    private List<GameObject> spawnedCities = new List<GameObject>();
    private List<Vector3> allSettlementPositions = new List<Vector3>();

    void Awake()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        Debug.Log("<color=cyan>WORLD GENERATOR:</color> Establishing Hub & Spoke Network...");

        // --- ADIM 1: KÖY ÜRETİM LİSTESİNİ HAZIRLA (25 Adet) ---
        // Bu liste sırasıyla şehirlere dağıtılacak
        Queue<ItemData> villageProductionQueue = new Queue<ItemData>();
        
        // Tier 1 (Bol)
        for(int i=0; i<4; i++) villageProductionQueue.Enqueue(itemWheat);
        for(int i=0; i<3; i++) villageProductionQueue.Enqueue(itemWood);
        for(int i=0; i<3; i++) villageProductionQueue.Enqueue(itemFish);
        for(int i=0; i<3; i++) villageProductionQueue.Enqueue(itemCotton);
        // Tier 2 (Orta)
        for(int i=0; i<2; i++) villageProductionQueue.Enqueue(itemMeat);
        for(int i=0; i<2; i++) villageProductionQueue.Enqueue(itemCoal);
        for(int i=0; i<2; i++) villageProductionQueue.Enqueue(itemLeather);
        for(int i=0; i<2; i++) villageProductionQueue.Enqueue(itemIron);
        // Tier 3 (Nadir)
        villageProductionQueue.Enqueue(itemClothes);
        villageProductionQueue.Enqueue(itemTools);
        villageProductionQueue.Enqueue(itemSpices);
        villageProductionQueue.Enqueue(itemJewelry);


        // --- ADIM 2: GRID SISTEMI ILE SEHIRLERI OLUSTUR (4x5 = 20 Sehir) ---
        List<ItemData> allItems = new List<ItemData> { itemWheat, itemWood, itemFish, itemCotton, itemMeat, itemCoal, itemLeather, itemIron, itemClothes, itemTools, itemSpices, itemJewelry };
        
        int gridCols = 5; // Yatayda 5
        int gridRows = 4; // Dikeyde 4
        
        float totalMapSize = 180f; // 200 birim haritanin guvenli alani (-90, +90)
        float cellWidth = totalMapSize / gridCols; // ~36 birim genislik
        float cellHeight = totalMapSize / gridRows; // ~45 birim yukseklik
        
        float startOffsetX = -totalMapSize / 2f + cellWidth / 2f; 
        float startOffsetZ = -totalMapSize / 2f + cellHeight / 2f;

        int cityCount = 0;

        for (int x = 0; x < gridCols; x++)
        {
            for (int z = 0; z < gridRows; z++)
            {
                // Hucrenin merkezini bul (Dikdortgen olabilir)
                float cellCenterX = startOffsetX + (x * cellWidth);
                float cellCenterZ = startOffsetZ + (z * cellHeight);
                
                // Merkezden hafif sapma yap (Dogallik icin)
                float randomOffsetX = Random.Range(-cellWidth * 0.3f, cellWidth * 0.3f);
                float randomOffsetZ = Random.Range(-cellHeight * 0.3f, cellHeight * 0.3f);
                
                Vector3 cityPos = new Vector3(cellCenterX + randomOffsetX, 0, cellCenterZ + randomOffsetZ);
                
                // NavMesh uzerinde gecerli mi?
                cityPos = GetNavMeshPos(cityPos);
                
                if (cityPos != Vector3.zero)
                {
                    GameObject cityObj = Instantiate(cityPrefab, cityPos, Quaternion.identity, this.transform);
                    
                    // İlk 5 Şehir "Büyük Merkez" olsun
                    bool isGrand = (cityCount < 5);
                    string name = isGrand ? $"Grand_City_{cityCount+1}" : $"City_{cityCount+1}";
                    cityObj.name = name;
                    
                    CityController cc = cityObj.GetComponent<CityController>();
                    int startPop = isGrand ? Random.Range(300, 800) : Random.Range(200, 500);
                    cc.InitializeCity(name, false, startPop, allItems);
                    
                    spawnedCities.Add(cityObj);
                    allSettlementPositions.Add(cityPos);
                    cityCount++;
                }
            }
        }

        // --- ADIM 3: KÖYLERİ ŞEHİRLERİN ETRAFINA DİZ (UYDULAR) ---
        // İlk 5 şehre 2'şer köy, geri kalan 15 şehre 1'er köy (Toplam 25)
        
        for (int i = 0; i < spawnedCities.Count; i++)
        {
            if (spawnedCities[i] == null) continue;

            // BUG FIX: Index yerine isme bak. (Cunku bazi sehirler spawn olamayabilir)
            bool isGrandCity = spawnedCities[i].name.Contains("Grand");
            // Ilk 5 sehir grand oldugu icin koyleri oncelikli alirlar. (Toplam 25 Koy: 5x2 + 15x1)
            int villagesToSpawn = isGrandCity ? 2 : 1; 

            for (int k = 0; k < villagesToSpawn; k++)
            {
                if (villageProductionQueue.Count == 0) break;

                ItemData product = villageProductionQueue.Dequeue();
                
                // Şehrin etrafında (5-10 birim) uygun yer bul
                Vector3 villagePos = GetValidVillagePosition(spawnedCities[i].transform.position);
                
                if (villagePos != Vector3.zero)
                {
                    GameObject villageObj = Instantiate(villagePrefab, villagePos, Quaternion.identity, this.transform);
                    villageObj.name = $"Village_{spawnedCities[i].name}_{product.itemName}"; // Örn: Village_City_1_Iron
                    
                    Debug.Log($"SPAWNED: {villageObj.name} (Parent: {spawnedCities[i].name})");

                    CityController cc = villageObj.GetComponent<CityController>();
                    cc.InitializeCity(villageObj.name, true, Random.Range(50, 150), new List<ItemData>{ product });
                    
                    allSettlementPositions.Add(villagePos);
                }
            }
        }

        // --- ADIM 4: BROKER SISTEMI VE CLUSTERING ---
        InitializeBrokersAndClusters();

        if (spawnedCities.Count == 0)
        {
            Debug.LogError("WORLD GENERATOR: 0 Cities spawned! Did you bake the NavMesh?");
        }
        else
        {
            Debug.Log($"<color=green>WORLD GEN COMPLETE:</color> {spawnedCities.Count} Cities, Villages and Brokers placed.");
        }
    }

    void InitializeBrokersAndClusters()
    {
        // 5 Tane Grand City var, bunlari HUB yapiyoruz.
        // Her Hub'a yakinindaki diger sehirleri baglayacagiz.
        
        List<CityController> hubs = new List<CityController>();
        List<CityController> others = new List<CityController>();

        foreach(GameObject cityObj in spawnedCities)
        {
            CityController cc = cityObj.GetComponent<CityController>();
            if (cityObj.name.Contains("Grand")) 
                hubs.Add(cc);
            else 
                others.Add(cc);
        }

        // Her HUB icin islem yap
        foreach (CityController hub in hubs)
        {
            List<CityController> cluster = new List<CityController>();
            cluster.Add(hub); // Kendisi de dahil

            // 1. HUB'a bir Broker ata (Prefab Instantiate)
            if (brokerPrefab != null)
            {
                // Grand City'nin tam ortasina degil, YOL USTUNE (Orta noktaya) koyuyoruz.
                // Henuz uydu sehirleri bilmiyoruz, once onlari bulalim.
                
                // 2. Cluster olustur (En yakin 3 normal sehri bul ve bagla)
                others.Sort((a, b) => Vector3.Distance(hub.transform.position, a.transform.position)
                                        .CompareTo(Vector3.Distance(hub.transform.position, b.transform.position)));

                int countToAdd = 3; // Her hub'a 3 uydu sehir (Toplam 5x4 = 20 sehir eder - 5 Grand + 15 Normal / 5 = 3)
                
                CityController firstSatellite = null;

                for (int i = 0; i < countToAdd; i++)
                {
                    if (others.Count > 0)
                    {
                        CityController satellite = others[0];
                        if (firstSatellite == null) firstSatellite = satellite; // Ilk uyduyu sakla, yol icin lazim
                        cluster.Add(satellite);
                        others.RemoveAt(0); // Artik bu sehir sahipli
                    }
                }

                // 3. BROKER YERLESIMI (CENTROID - Agirlik Merkezi)
                Vector3 centroid = Vector3.zero;
                foreach(var c in cluster)
                {
                    centroid += c.transform.position;
                }
                centroid /= cluster.Count; // Tum sehirlerin ortalamasi
                
                Vector3 brokerPos = centroid; 

                // Hafif bir offset ekleyelim ki tam ust uste binmesin
                brokerPos += new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
                
                // NavMesh uzerinde gecerli bir nokta bulalim
                brokerPos = GetNavMeshPos(brokerPos);
                if (brokerPos == Vector3.zero) brokerPos = hub.transform.position; // Guvenli liman

                GameObject brokerObj = Instantiate(brokerPrefab, brokerPos, Quaternion.identity, this.transform);
                brokerObj.name = $"Broker_Cluster_{hub.cityName}";
                
                RegionalBroker rb = brokerObj.GetComponent<RegionalBroker>();
                if(rb != null)
                {
                    rb.InitBroker(cluster);
                    
                    // Sehirlere de brokerlarini ata
                    foreach(var c in cluster)
                    {
                        c.assignedBroker = rb;
                    }
                }
            }
        }
    }

    // Şehirler için Global Rastgele Konum
    Vector3 GetValidCityPosition()
    {
        for (int i = 0; i < 100; i++)
        {
            float x = Random.Range(-mapSize, mapSize);
            float z = Random.Range(-mapSize, mapSize);
            Vector3 candidate = new Vector3(x, 0, z);

            if (IsPositionValid(candidate, cityMinDistance))
            {
                return GetNavMeshPos(candidate);
            }
        }
        return Vector3.zero;
    }

    // Köyler için "Şehir Merkezli" Halka Konum (10-30 birim)
    Vector3 GetValidVillagePosition(Vector3 centerCity)
    {
        for (int i = 0; i < 50; i++)
        {
            // Rastgele yön ve mesafe seç
            Vector2 randomDir = Random.insideUnitCircle.normalized; 
            float distance = Random.Range(minVillageDist, maxVillageDist);
            
            Vector3 offset = new Vector3(randomDir.x, 0, randomDir.y) * distance;
            Vector3 candidate = centerCity + offset;

            // Diğer köylerle/şehirlerle çakışmasın (5 birim pay bırak)
            if (IsPositionValid(candidate, 8f)) 
            {
                return GetNavMeshPos(candidate);
            }
        }
        return Vector3.zero;
    }

    // Çakışma Kontrolü
    bool IsPositionValid(Vector3 pos, float minSpacing)
    {
        foreach (var existing in allSettlementPositions)
        {
            if (Vector3.Distance(pos, existing) < minSpacing) return false;
        }
        return true;
    }

    // NavMesh Kontrolü (Denize/Dağa denk gelmesin)
    Vector3 GetNavMeshPos(Vector3 pos)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(pos, out hit, 10f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return Vector3.zero;
    }
}