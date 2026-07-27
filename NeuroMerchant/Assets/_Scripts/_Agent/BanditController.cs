using UnityEngine;
using UnityEngine.AI;

public class BanditController : MonoBehaviour
{
    public enum BanditState { Patrol, Chase, Returning }

    [Header("Durum")]
    public BanditState currentState = BanditState.Patrol;

    [Header("Alan Ayarları (Koyu Yeşil)")]
    public float patrolRadius = 15f; // Devriye atacağı dar alan
    public float chaseDetectRadius = 35f; // Kervanı görüp kovalayacağı geniş alan
    public float catchDistance = 3f; // Yakalama/Soyma mesafesi

    [Header("Hareket Hızları")]
    public float patrolSpeed = 3.5f;
    public float chaseSpeed = 7f;

    [Header("Güvenlik Ayarları")]
    public float citySafeZoneRadius = 5f; // Kervan şehre bundan yakınsa dokunulmazdır
    private CityController[] allCities;

    [HideInInspector]
    public Vector3 patrolCenter; // BanditZone tarafından atanacak

    private NavMeshAgent navAgent;
    private MerchantAgent targetAgent;

    void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        allCities = FindObjectsOfType<CityController>();
    }

    // BanditZone (Spawner) haydudu yarattığında bu fonksiyonu tetikler
    public void InitializeBandit(Vector3 centerPosition)
    {
        patrolCenter = centerPosition;
        SetState(BanditState.Patrol);
    }

    void Update()
    {
        switch (currentState)
        {
            case BanditState.Patrol:
                HandlePatrol();
                CheckForTargets();
                break;

            case BanditState.Chase:
                HandleChase();
                break;

            case BanditState.Returning:
                HandleReturn();
                CheckForTargets();
                break;
        }
    }

    private void SetState(BanditState newState)
    {
        currentState = newState;

        if (currentState == BanditState.Patrol)
        {
            navAgent.speed = patrolSpeed;
            SetRandomPatrolDestination();
        }
        else if (currentState == BanditState.Chase)
        {
            navAgent.speed = chaseSpeed;
        }
        else if (currentState == BanditState.Returning)
        {
            navAgent.speed = patrolSpeed;
            navAgent.SetDestination(patrolCenter);
        }
    }

    private void HandlePatrol()
    {
        if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance)
        {
            SetRandomPatrolDestination();
        }
    }

    private void HandleChase()
    {
        if (targetAgent == null || !targetAgent.gameObject.activeInHierarchy)
        {
            SetState(BanditState.Returning);
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, targetAgent.transform.position);

        // Ajan kovalama çemberinden çıktıysa peşini bırak
        if (distanceToTarget > chaseDetectRadius)
        {
            targetAgent = null;
            SetState(BanditState.Returning);
            return;
        }

        navAgent.SetDestination(targetAgent.transform.position);

        // Yakalama Kontrolü
        if (distanceToTarget <= catchDistance)
        {
            Debug.Log($"<color=red>HAYDUT SALDIRISI!</color> {targetAgent.name} soyuldu!");

            targetAgent.HandleRobbery();

            targetAgent = null;
            SetState(BanditState.Returning);
        }
    }

    private void HandleReturn()
    {
        if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance)
        {
            SetState(BanditState.Patrol);
        }
    }

    private void CheckForTargets()
    {
        MerchantAgent[] allAgents = FindObjectsOfType<MerchantAgent>();
        MerchantAgent closestAgent = null;
        float closestDist = float.MaxValue;

        foreach (var agent in allAgents)
        {
            // 1. KONTROL: Ajan kapalıysa veya 10 saniyelik soygun kalkanı varsa pas geç
            if (!agent.gameObject.activeInHierarchy || agent.IsImmuneToRobbery()) continue;

            // 2. KONTROL: Şehir Kalkanı (Safe Zone)
            bool isInsideCitySafeZone = false;
            if (allCities != null)
            {
                foreach (var city in allCities)
                {
                    if (Vector3.Distance(agent.transform.position, city.transform.position) <= citySafeZoneRadius)
                    {
                        isInsideCitySafeZone = true;
                        break; // Şehre yakın olduğunu bulduk, diğer şehirlere bakmaya gerek yok
                    }
                }
            }

            // Eğer kervan şehre sığınmışsa onu da pas geç
            if (isInsideCitySafeZone) continue;

            // 3. KONTROL: Haydudun kendi görüş menzili
            float dist = Vector3.Distance(patrolCenter, agent.transform.position);

            if (dist <= chaseDetectRadius && dist < closestDist)
            {
                closestDist = dist;
                closestAgent = agent;
            }
        }

        if (closestAgent != null)
        {
            targetAgent = closestAgent;
            SetState(BanditState.Chase);
        }
    }

    private void SetRandomPatrolDestination()
    {
        Vector2 randomDir = Random.insideUnitCircle * patrolRadius;
        Vector3 randomPos = patrolCenter + new Vector3(randomDir.x, 0, randomDir.y);

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPos, out hit, patrolRadius, NavMesh.AllAreas))
        {
            navAgent.SetDestination(hit.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? patrolCenter : transform.position;

        Gizmos.color = new Color(0f, 0.5f, 0f, 0.5f); // Koyu yeşil
        Gizmos.DrawSphere(center, patrolRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, chaseDetectRadius);
    }
}