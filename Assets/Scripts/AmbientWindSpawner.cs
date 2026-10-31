using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AmbientWindSpawner : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public ParticleSystem windVariantA;
    public ParticleSystem windVariantB;

    [Header("Spawn Settings")]
    public int windCount = 12;
    public float spawnRadius = 40f;
    public float repositionDistance = 25f;

    [Header("Timing")]
    public Vector2 spawnDelayRange = new Vector2(0f, 6f);
    public Vector2 repositionCooldownRange = new Vector2(3f, 8f);

    [Header("Height")]
    public float minHeight = 2f;
    public float maxHeight = 10f;

    private class WindInstance
    {
        public ParticleSystem system;
        public float nextRepositionTime;
    }

    private List<WindInstance> winds = new List<WindInstance>();

    void Start()
    {
        if (!player)
        {
            Debug.LogError("AmbientWindSpawner: Player reference missing.");
            enabled = false;
            return;
        }

        StartCoroutine(SpawnRoutine());
    }

    IEnumerator SpawnRoutine()
    {
        for (int i = 0; i < windCount; i++)
        {
            ParticleSystem prefab = Random.value > 0.5f ? windVariantA : windVariantB;

            ParticleSystem wind = Instantiate(prefab, transform);
            wind.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            wind.transform.position = GetRandomPositionAroundPlayer();

            float delay = Random.Range(spawnDelayRange.x, spawnDelayRange.y);
            yield return new WaitForSeconds(delay);

            wind.Play();

            winds.Add(new WindInstance
            {
                system = wind,
                nextRepositionTime = Time.time + Random.Range(
                    repositionCooldownRange.x,
                    repositionCooldownRange.y)
            });
        }
    }

    void Update()
    {
        float time = Time.time;

        foreach (WindInstance wind in winds)
        {
            if (time < wind.nextRepositionTime)
                continue;

            float distance = Vector3.Distance(player.position, wind.system.transform.position);

            if (distance > repositionDistance)
            {
                wind.system.transform.position = GetRandomPositionAroundPlayer();
            }

            wind.nextRepositionTime = time + Random.Range(
                repositionCooldownRange.x,
                repositionCooldownRange.y);
        }
    }

    Vector3 GetRandomPositionAroundPlayer()
    {
        Vector2 circle = Random.insideUnitCircle.normalized *
                         Random.Range(spawnRadius * 0.4f, spawnRadius);

        return new Vector3(
            player.position.x + circle.x,
            player.position.y + Random.Range(minHeight, maxHeight),
            player.position.z + circle.y
        );
    }
}
