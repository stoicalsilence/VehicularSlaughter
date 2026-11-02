
using UnityEngine;

public class HordeSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;

    [Header("Spawn Settings")]
    [SerializeField] private int enemiesPerWave = 30;
    [SerializeField] private float spawnInterval = 15f;
    [SerializeField] private float spawnRadius = 30f;
    [SerializeField] private float minSpawnRadius = 15f;

    [Header("Terrain Detection")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastHeight = 100f;
    [SerializeField] private float enemySpawnHeight = 1f;

    private void Start()
    {
        InvokeRepeating(
            nameof(SpawnWave),
            0f,
            spawnInterval
        );
    }

    private void SpawnWave()
    {
        if (HordeManager.Instance == null || player == null)
            return;

        for (int i = 0; i < enemiesPerWave; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle.normalized
                * Random.Range(minSpawnRadius, spawnRadius);

            Vector3 rayOrigin = player.position + new Vector3(
                randomCircle.x,
                raycastHeight,
                randomCircle.y
            );

            if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                raycastHeight * 2f,
                groundLayer,
                QueryTriggerInteraction.Ignore))
            {
                Vector3 spawnPosition =
                    hit.point + Vector3.up * enemySpawnHeight;

                HordeManager.Instance.SpawnEnemy(
                    spawnPosition,
                    Quaternion.identity
                );
            }
        }
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(SpawnWave));
    }
}
