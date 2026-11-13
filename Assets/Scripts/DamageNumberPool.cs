using TMPro;
using UnityEngine;

/// <summary>
/// Shared world-space damage-number pool for large enemy counts.
/// Create one instance in the scene and assign a 3D TextMeshPro prefab.
/// </summary>
[DisallowMultipleComponent]
public sealed class DamageNumberPool : MonoBehaviour
{
    public static DamageNumberPool Instance { get; private set; }

    [Header("Pool")]
    [SerializeField] private TextMeshPro damageNumberPrefab;
    [SerializeField, Min(1)] private int poolCapacity = 96;

    [Header("Spawn")]
    [SerializeField, Min(0f)] private float verticalSpawnOffset = 0.04f;
    [SerializeField] private Vector2 horizontalSpawnJitter = new Vector2(0.14f, 0.12f);

    [Header("Animation")]
    [SerializeField, Min(0.1f)] private float lifetime = 0.72f;
    [SerializeField, Min(0f)] private float riseDistance = 1.05f;
    [SerializeField, Min(0f)] private float lateralDrift = 0.28f;
    [SerializeField, Range(0.01f, 0.5f)] private float popDuration = 0.16f;
    [SerializeField, Range(0.1f, 0.95f)] private float fadeStart = 0.58f;
    [SerializeField, Range(0.1f, 1f)] private float initialScale = 0.72f;

    [Header("Impact Styling")]
    [SerializeField, Range(0.05f, 1f)] private float heavyHitHealthFraction = 0.35f;
    [SerializeField, Min(0.1f)] private float heavyHitSizeMultiplier = 1.12f;
    [SerializeField, Min(0.1f)] private float lethalSizeMultiplier = 1.28f;
    [SerializeField] private Color normalColor = new Color(1f, 0.94f, 0.78f, 1f);
    [SerializeField] private Color heavyHitColor = new Color(1f, 0.57f, 0.19f, 1f);
    [SerializeField] private Color critColor = new Color(1f, 0.82f, 0.28f, 1f);
    [SerializeField] private Color lethalColor = new Color(1f, 0.82f, 0.28f, 1f);

    private PooledNumber[] numbers;
    private int nextNumberIndex;
    private Vector3 prefabScale;

    private sealed class PooledNumber
    {
        public TextMeshPro text;
        public Transform cachedTransform;
        public bool active;
        public float age;
        public float duration;
        public float sizeMultiplier;
        public float roll;
        public Vector3 startPosition;
        public Vector3 lateralOffset;
        public Color color;
    }

    private void Awake()
    {
        if (damageNumberPrefab == null)
        {
            Debug.LogError("DamageNumberPool is missing its 3D TextMeshPro prefab.", this);
            enabled = false;
            return;
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Only one DamageNumberPool should exist in a scene.", this);
            enabled = false;
            return;
        }

        Instance = this;

        poolCapacity = Mathf.Max(1, poolCapacity);
        prefabScale = damageNumberPrefab.transform.localScale;

        numbers = new PooledNumber[poolCapacity];
        for (int i = 0; i < numbers.Length; i++)
        {
            TextMeshPro text = Instantiate(damageNumberPrefab, transform);
            text.name = "Pooled Damage Number " + i;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.gameObject.SetActive(false);

            numbers[i] = new PooledNumber
            {
                text = text,
                cachedTransform = text.transform
            };
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Shows the actual damage applied to an enemy. Calls are ignored when no pool
    /// is present, so enemies do not need their own references to this component.
    /// </summary>
    public static void ShowDamage(
    Vector3 worldPosition,
    float damage,
    float enemyMaxHealth,
    bool lethalHit,
    int criticalHits)
    {
        if (Instance == null || !Instance.isActiveAndEnabled)
            return;

        if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            return;

        Instance.SpawnNumber(
            worldPosition,
            damage,
            enemyMaxHealth,
            lethalHit,
            criticalHits
        );
    }

    private void SpawnNumber(
    Vector3 worldPosition,
    float damage,
    float enemyMaxHealth,
    bool lethalHit,
    int criticalHits)
    {
        // A fixed-size ring means there are never runtime Instantiate/Destroy calls.
        // At capacity, the oldest slot is simply refreshed.
        PooledNumber number = numbers[nextNumberIndex];
        nextNumberIndex = (nextNumberIndex + 1) % numbers.Length;

        bool isCritical = criticalHits > 0;

        bool heavyHit = !lethalHit &&
                        !isCritical &&
                        enemyMaxHealth > 0f &&
                        damage >= enemyMaxHealth * heavyHitHealthFraction;

        number.age = 0f;
        number.duration = lifetime * (lethalHit ? 1.12f : 1f);

        number.sizeMultiplier = lethalHit
            ? lethalSizeMultiplier
            : isCritical || heavyHit
                ? heavyHitSizeMultiplier
                : 1f;

        // Critical styling takes priority over lethal and heavy-hit styling.
        number.color = isCritical
            ? critColor
            : lethalHit
                ? lethalColor
                : heavyHit
                    ? heavyHitColor
                    : normalColor;

        number.roll = Random.Range(-4f, 4f);

        Vector3 jitter = new Vector3(
            Random.Range(-horizontalSpawnJitter.x, horizontalSpawnJitter.x),
            0f,
            Random.Range(-horizontalSpawnJitter.y, horizontalSpawnJitter.y)
        );

        number.startPosition =
            worldPosition + jitter + Vector3.up * verticalSpawnOffset;

        Vector3 driftDirection = new Vector3(
            Random.Range(-1f, 1f),
            0f,
            Random.Range(-1f, 1f)
        );

        if (driftDirection.sqrMagnitude > 0.001f)
            driftDirection.Normalize();

        number.lateralOffset =
            driftDirection * Random.Range(lateralDrift * 0.55f, lateralDrift);

        // Critical markers appear underneath the damage number.
        string damageText = Mathf.RoundToInt(damage).ToString();

        if (criticalHits > 0)
        {
            string criticalMarks = new string('!', criticalHits);

            number.text.text = damageText + "\n" + criticalMarks;
        }
        else
        {
            number.text.text = damageText;
        }

        number.text.color = number.color;

        number.cachedTransform.position = number.startPosition;

        number.cachedTransform.localScale =
            prefabScale * initialScale * number.sizeMultiplier;

        number.active = true;

        if (!number.text.gameObject.activeSelf)
            number.text.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (numbers == null)
            return;

        float deltaTime = Time.deltaTime;
        Transform cameraTransform = Camera.main.transform;

        // One central update loop replaces one Update/coroutine per popup.
        for (int i = 0; i < numbers.Length; i++)
        {
            PooledNumber number = numbers[i];
            if (!number.active)
                continue;

            number.age += deltaTime;
            if (number.age >= number.duration)
            {
                number.active = false;
                number.text.gameObject.SetActive(false);
                continue;
            }

            float t = Mathf.Clamp01(number.age / number.duration);
            float easedRise = 1f - (1f - t) * (1f - t) * (1f - t);

            Vector3 position = number.startPosition +
                               number.lateralOffset * t +
                               Vector3.up * (riseDistance * easedRise);
            number.cachedTransform.position = position;

            // A quick overshoot on spawn gives each hit a crisp, arcade-like pop.
            float scale;
            float popT = Mathf.Clamp01(number.age / popDuration);
            if (number.age < popDuration)
            {
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                float offset = popT - 1f;
                float offsetSquared = offset * offset;
                float backOut = 1f + c3 * offsetSquared * offset +
                                c1 * offsetSquared;
                scale = Mathf.LerpUnclamped(initialScale, 1f, backOut);
            }
            else
            {
                scale = 1f;
            }

            number.cachedTransform.localScale = prefabScale * scale * number.sizeMultiplier;

            float alpha = t <= fadeStart
                ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, 1f, t));
            Color color = number.color;
            color.a *= alpha;
            number.text.color = color;

            if (cameraTransform != null)
            {
                Vector3 toCamera = cameraTransform.position - position;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    number.cachedTransform.rotation =
                        Quaternion.LookRotation(toCamera, cameraTransform.up) *
                        Quaternion.Euler(0f, 180f, number.roll);
                }
            }
        }
    }
}
