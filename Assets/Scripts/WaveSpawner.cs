
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns enemies in waves. Each wave is a list of groups that spawn one
/// after another; a new wave starts once the previous one is cleared
/// (or after a fixed delay, if waitForClear is off).
///
/// Spawned enemies follow the path (the waypoints, then the goal). Walkers use
/// EnemyMover; flyers use GhostMover (rise, cruise, spiral in).
/// </summary>
public class WaveSpawner : MonoBehaviour
{
    public enum SpawnerState
    {
        Idle,
        Countdown,
        Spawning,
        WaitingForClear,
        Finished
    }

    [Serializable]
    public class EnemyGroup
    {
        [Tooltip("Leave empty to use the spawner's Default Enemy Prefab.")]
        public GameObject enemyPrefab;
        [Min(1)] public int count = 5;
        [Min(0f)] public float spawnInterval = 1f;
        [Tooltip("Pause after this group finishes spawning.")]
        [Min(0f)] public float delayAfterGroup = 1f;
    }

    [Serializable]
    public class Wave
    {
        public string name = "Wave";
        public List<EnemyGroup> groups = new List<EnemyGroup>();
    }

    [Header("Enemies")]
    [SerializeField] private GameObject defaultEnemyPrefab;
    [SerializeField] private Transform spawnedEnemiesParent;

    [Header("Spawning")]
    [Tooltip("Enemies spawn at a random one of these points.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Random sideways offset around the spawn point, in metres.")]
    [SerializeField, Min(0f)] private float spawnSpread = 1.5f;

    [Header("Path")]
    [Tooltip("Optional points the enemies walk through before the goal.")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private Transform goal;

    [Header("Timing")]
    [SerializeField] private bool startOnPlay = true;
    [SerializeField, Min(0f)] private float firstWaveDelay = 5f;
    [SerializeField, Min(0f)] private float timeBetweenWaves = 8f;

    [Tooltip("Wait until every enemy of a wave is dead before counting down to the next.")]
    [SerializeField] private bool waitForClear = true;

    [Header("Endless")]
    [Tooltip("After the last wave, keep repeating it with more enemies.")]
    [SerializeField] private bool endless = true;
    [Tooltip("Extra enemies per group for each repeat of the last wave (0.25 = +25%).")]
    [SerializeField, Min(0f)] private float endlessCountGrowth = 0.25f;

    [Header("Waves")]
    [SerializeField] private List<Wave> waves = new List<Wave>
    {
        new Wave { name = "Wave 1", groups = { new EnemyGroup { count = 5, spawnInterval = 1.5f } } },
        new Wave { name = "Wave 2", groups = { new EnemyGroup { count = 8, spawnInterval = 1.2f } } },
        new Wave
        {
            name = "Wave 3",
            groups =
            {
                new EnemyGroup { count = 6, spawnInterval = 1f, delayAfterGroup = 3f },
                new EnemyGroup { count = 6, spawnInterval = 0.6f }
            }
        },
        new Wave { name = "Wave 4", groups = { new EnemyGroup { count = 15, spawnInterval = 0.8f } } },
        new Wave
        {
            name = "Wave 5",
            groups =
            {
                new EnemyGroup { count = 10, spawnInterval = 0.5f, delayAfterGroup = 4f },
                new EnemyGroup { count = 12, spawnInterval = 0.4f }
            }
        },
    };

    private readonly List<GameObject> aliveEnemies = new List<GameObject>();
    private Transform[] path;
    private Coroutine runRoutine;

    private SpawnerState state = SpawnerState.Idle;
    private int currentWave;          // 1-based; 0 before the first wave
    private float countdown;
    private int enemiesKilled;
    private int enemiesLeaked;

    public SpawnerState State => state;
    public int CurrentWave => currentWave;
    public int WaveCount => waves.Count;
    public float Countdown => countdown;
    public int EnemiesAlive => aliveEnemies.Count;
    public int EnemiesKilled => enemiesKilled;
    public int EnemiesLeaked => enemiesLeaked;

    /// <summary>Raised when a wave starts spawning (1-based wave number).</summary>
    public event Action<int> WaveStarted;

    /// <summary>Raised when every enemy of a wave is gone (1-based wave number).</summary>
    public event Action<int> WaveCleared;

    /// <summary>Raised when an enemy reaches the goal.</summary>
    public event Action<GameObject> EnemyReachedGoal;

    /// <summary>Raised after the last wave is cleared (never in endless mode).</summary>
    public event Action AllWavesCompleted;

    private void Awake()
    {
        BuildPath();
    }

    private void Start()
    {
        if (startOnPlay)
            StartWaves();
    }

    public void StartWaves()
    {
        if (runRoutine != null)
            return;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("WaveSpawner: Assign at least one spawn point.", this);
            return;
        }

        if (waves.Count == 0)
        {
            Debug.LogError("WaveSpawner: No waves configured.", this);
            return;
        }

        runRoutine = StartCoroutine(RunWaves());
    }

    public void StopWaves()
    {
        if (runRoutine != null)
        {
            StopCoroutine(runRoutine);
            runRoutine = null;
        }

        state = SpawnerState.Idle;
    }

    private IEnumerator RunWaves()
    {
        yield return CountdownRoutine(firstWaveDelay);

        int repeat = 0;

        while (true)
        {
            int waveIndex = Mathf.Min(currentWave, waves.Count - 1);
            bool isRepeat = currentWave >= waves.Count;

            if (isRepeat)
                repeat++;

            currentWave++;
            Wave wave = waves[waveIndex];

            state = SpawnerState.Spawning;
            Debug.Log($"WaveSpawner: Wave {currentWave} started.");
            WaveStarted?.Invoke(currentWave);

            float countScale = 1f + endlessCountGrowth * repeat;

            foreach (EnemyGroup group in wave.groups)
            {
                int count = Mathf.CeilToInt(group.count * countScale);

                for (int i = 0; i < count; i++)
                {
                    SpawnEnemy(group.enemyPrefab != null ? group.enemyPrefab : defaultEnemyPrefab);

                    if (i < count - 1 && group.spawnInterval > 0f)
                        yield return new WaitForSeconds(group.spawnInterval);
                }

                if (group.delayAfterGroup > 0f)
                    yield return new WaitForSeconds(group.delayAfterGroup);
            }

            if (waitForClear)
            {
                state = SpawnerState.WaitingForClear;

                while (PruneAndCountAlive() > 0)
                    yield return null;

                Debug.Log($"WaveSpawner: Wave {currentWave} cleared.");
                WaveCleared?.Invoke(currentWave);
            }

            bool lastWave = currentWave >= waves.Count;

            if (lastWave && !endless)
            {
                // Without waitForClear, still wait so "completed" means all gone.
                while (PruneAndCountAlive() > 0)
                    yield return null;

                state = SpawnerState.Finished;
                runRoutine = null;
                Debug.Log("WaveSpawner: All waves completed.");
                AllWavesCompleted?.Invoke();
                yield break;
            }

            yield return CountdownRoutine(timeBetweenWaves);
        }
    }

    private IEnumerator CountdownRoutine(float seconds)
    {
        state = SpawnerState.Countdown;
        countdown = seconds;

        while (countdown > 0f)
        {
            countdown -= Time.deltaTime;
            yield return null;
        }

        countdown = 0f;
    }

    private void SpawnEnemy(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("WaveSpawner: No enemy prefab to spawn.", this);
            return;
        }

        Transform point = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];

        Vector3 offset = point.right * UnityEngine.Random.Range(-spawnSpread, spawnSpread);

        GameObject enemy = Instantiate(
            prefab,
            point.position + offset,
            point.rotation,
            spawnedEnemiesParent
        );

        aliveEnemies.Add(enemy);

        if (enemy.TryGetComponent(out TargetHealth health))
            health.Died += OnEnemyDied;

        if (enemy.TryGetComponent(out EnemyMover mover))
        {
            mover.SetPath(path);
            mover.ReachedGoal += m => OnEnemyReachedGoal(m.gameObject);
        }
        else if (enemy.TryGetComponent(out GhostMover ghost))
        {
            ghost.SetPath(path);
            ghost.ReachedGoal += g => OnEnemyReachedGoal(g.gameObject);
        }
    }

    private void OnEnemyDied(TargetHealth health)
    {
        enemiesKilled++;
        aliveEnemies.Remove(health.gameObject);
    }

    private void OnEnemyReachedGoal(GameObject enemy)
    {
        enemiesLeaked++;
        aliveEnemies.Remove(enemy);

        Debug.Log($"WaveSpawner: {enemy.name} reached the goal.");
        EnemyReachedGoal?.Invoke(enemy);
    }

    private int PruneAndCountAlive()
    {
        // Catches enemies destroyed some other way.
        aliveEnemies.RemoveAll(enemy => enemy == null);
        return aliveEnemies.Count;
    }

    private void BuildPath()
    {
        List<Transform> points = new List<Transform>();

        if (waypoints != null)
        {
            foreach (Transform waypoint in waypoints)
            {
                if (waypoint != null)
                    points.Add(waypoint);
            }
        }

        if (goal != null)
            points.Add(goal);

        path = points.ToArray();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f);

        if (spawnPoints != null)
        {
            foreach (Transform point in spawnPoints)
            {
                if (point == null)
                    continue;

                Gizmos.DrawWireSphere(point.position, 0.5f);
                Gizmos.DrawLine(
                    point.position - point.right * spawnSpread,
                    point.position + point.right * spawnSpread
                );
            }
        }

        // Draw the path from the first spawn point through to the goal.
        Vector3? previous = spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[0] != null
            ? spawnPoints[0].position
            : (Vector3?)null;

        Gizmos.color = new Color(1f, 0.8f, 0.2f);

        if (waypoints != null)
        {
            foreach (Transform waypoint in waypoints)
            {
                if (waypoint == null)
                    continue;

                if (previous.HasValue)
                    Gizmos.DrawLine(previous.Value, waypoint.position);

                Gizmos.DrawWireSphere(waypoint.position, 0.3f);
                previous = waypoint.position;
            }
        }

        if (goal != null)
        {
            if (previous.HasValue)
                Gizmos.DrawLine(previous.Value, goal.position);

            Gizmos.color = new Color(0.3f, 1f, 0.4f);
            Gizmos.DrawWireCube(goal.position + Vector3.up, new Vector3(2f, 2f, 2f));
        }
    }
}
