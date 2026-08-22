using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlossomBreach
{
    [DisallowMultipleComponent]
    public sealed class BlossomGame : MonoBehaviour
    {
        public enum ShotOutcome
        {
            Miss,
            Blocked,
            BossCoreBlocked,
            Hit,
            Critical,
            ShieldBreak,
            Defeat
        }

        public readonly struct ShotSignal
        {
            public ShotSignal(
                ShotOutcome outcome,
                Vector3 worldPoint,
                bool hasEnemy,
                EnemyKind enemyKind,
                bool powerShot)
            {
                Outcome = outcome;
                WorldPoint = worldPoint;
                HasEnemy = hasEnemy;
                EnemyKind = enemyKind;
                PowerShot = powerShot;
            }

            public ShotOutcome Outcome { get; }
            public Vector3 WorldPoint { get; }
            public bool HasEnemy { get; }
            public EnemyKind EnemyKind { get; }
            public bool PowerShot { get; }
        }

        private const float StatePublishInterval = 0.2f;

        [Header("References")]
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Transform enemyRoot;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Arena")]
        [SerializeField] private float spawnZ = 24f;
        [SerializeField] private float breachZ = -2.25f;
        [SerializeField] private float laneWidth = 2.25f;
        [SerializeField] private float enemyHeight = 0f;

        [Header("Screen-space Approach")]
        [SerializeField, Range(0f, 1f)] private float farViewportYMin = 0.78f;
        [SerializeField, Range(0f, 1f)] private float farViewportYMax = 0.88f;
        [SerializeField, Range(0f, 1f)] private float nearViewportYMin = 0.36f;
        [SerializeField, Range(0f, 1f)] private float nearViewportYMax = 0.48f;

        [Header("Tuning")]
        [SerializeField] private int startingPurity = 100;
        [SerializeField] private float scoutSpeed = 3.45f;
        [SerializeField] private float fastSpeed = 5.12f;
        [SerializeField] private float armoredSpeed = 2.55f;
        [SerializeField] private float bomberSpeed = 3.05f;
        [SerializeField] private float bossSpeed = 1.53f;
        [SerializeField] private float rayDistance = 100f;
        [SerializeField] private float comboHoldSeconds = 1.75f;
        [SerializeField] private float comboDecayStepSeconds = 0.55f;
        [SerializeField, Range(1, 6)] private int maxActiveEnemies = 4;

        private readonly List<EnemyActor> _activeEnemies = new List<EnemyActor>();
        private float _elapsed;
        private float _nextSpawnAt;
        private bool _bossSpawned;
        private bool _sessionRunning;
        private bool _timerExpired;
        private bool _waveWasActive;
        private float _nextComboDecayAt;
        private float _nextStatePublishAt;

        public event Action StateChanged;
        public event Action<ShotSignal> ShotResolved;

        public int Score { get; private set; }
        public int Purity { get; private set; }
        public float TimeRemaining { get; private set; }
        public int Chapter { get; private set; }
        public int Combo { get; private set; }
        public int OverdriveShots { get; private set; }
        public Vector2 AimViewport { get; private set; } = new Vector2(0.5f, 0.5f);
        public float SpawnZ => spawnZ;
        public float BreachZ => breachZ;
        private int EffectiveActiveCap => Mathf.Clamp(maxActiveEnemies, 1, 6);
        public bool IsRunning => _sessionRunning;
        public int ActiveEnemyCount => CountActiveEnemies();
        public bool BossActive => TryGetBoss(out _);
        public float BossHealthNormalized => TryGetBoss(out EnemyActor boss) ? boss.HealthNormalized : 0f;

        private void Awake()
        {
            if (gameplayCamera == null)
            {
                gameplayCamera = Camera.main;
            }

            if (enemyRoot == null)
            {
                enemyRoot = transform;
            }
        }

        private void Start()
        {
            Restart();
        }

        private void Update()
        {
            if (!_sessionRunning)
            {
                return;
            }

            int previousChapter = Chapter;
            if (!_timerExpired)
            {
                _elapsed = Mathf.Min(GameRules.SessionDuration, _elapsed + Time.deltaTime);
            }

            TimeRemaining = Mathf.Max(0f, GameRules.SessionDuration - _elapsed);
            Chapter = GameRules.ChapterAt(_elapsed);
            if (TimeRemaining <= 0f)
            {
                _timerExpired = true;
            }

            if (previousChapter != Chapter)
            {
                _nextSpawnAt = _elapsed + (Chapter == 3 ? 1.2f : 0.9f);

                if (Chapter == 2)
                {
                    SpawnEnemy(EnemyKind.Armored);
                }
                else if (Chapter == 3 && !_bossSpawned)
                {
                    _bossSpawned = SpawnEnemy(EnemyKind.Boss);
                }
            }

            if (Chapter == 3 && !_bossSpawned && CountActiveEnemies() < EffectiveActiveCap)
            {
                _bossSpawned = SpawnEnemy(EnemyKind.Boss);
            }

            bool waveActive = !_timerExpired && GameRules.IsWaveActive(_elapsed);
            if (waveActive && !_waveWasActive)
            {
                _nextSpawnAt = Mathf.Max(_nextSpawnAt, _elapsed + 0.12f);
            }

            while (waveActive && _elapsed >= _nextSpawnAt && _sessionRunning)
            {
                SpawnEnemy(ChooseEnemyKind());
                _nextSpawnAt += GameRules.SpawnIntervalFor(Chapter);
            }

            if (!waveActive)
            {
                _nextSpawnAt = _elapsed + 0.12f;
            }

            _waveWasActive = waveActive;

            if (Combo > 0 && Time.unscaledTime >= _nextComboDecayAt)
            {
                Combo--;
                _nextComboDecayAt = Time.unscaledTime + Mathf.Max(0.1f, comboDecayStepSeconds);
            }

            if (_timerExpired && _bossSpawned && !BossActive)
            {
                _sessionRunning = false;
                NotifyStateChanged();
                return;
            }

            if (Time.unscaledTime >= _nextStatePublishAt)
            {
                NotifyStateChanged();
            }
        }

        public void SetAim(Vector2 viewport)
        {
            Vector2 clamped = new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y));
            if (clamped == AimViewport)
            {
                return;
            }

            AimViewport = clamped;
            if (Time.unscaledTime >= _nextStatePublishAt)
            {
                NotifyStateChanged();
            }
        }

        public void Fire()
        {
            if (!_sessionRunning || gameplayCamera == null)
            {
                return;
            }

            bool powerShot = OverdriveShots > 0;
            if (powerShot)
            {
                OverdriveShots--;
            }

            Ray ray = gameplayCamera.ViewportPointToRay(new Vector3(AimViewport.x, AimViewport.y, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, hitMask, QueryTriggerInteraction.Collide))
            {
                ApplyMissPenalty(25, 2);
                PublishShot(new ShotSignal(
                    ShotOutcome.Miss,
                    ray.GetPoint(Mathf.Min(rayDistance, 18f)),
                    false,
                    EnemyKind.Scout,
                    powerShot));
                NotifyStateChanged();
                return;
            }

            HitZone zone = hit.collider.GetComponent<HitZone>();
            EnemyActor enemy = zone != null
                ? zone.Owner
                : hit.collider.GetComponentInParent<EnemyActor>();

            if (enemy == null)
            {
                ApplyMissPenalty(25, 2);
                PublishShot(new ShotSignal(
                    ShotOutcome.Miss,
                    hit.point,
                    false,
                    EnemyKind.Scout,
                    powerShot));
                NotifyStateChanged();
                return;
            }

            EnemyHitResult result = enemy.ReceiveHit(zone, powerShot);
            if (!result.Accepted)
            {
                ApplyMissPenalty(10, 1);
                ShotOutcome blockedOutcome = enemy.Kind == EnemyKind.Boss &&
                    zone != null && zone.weakPoint
                        ? ShotOutcome.BossCoreBlocked
                        : ShotOutcome.Blocked;
                PublishShot(new ShotSignal(
                    blockedOutcome,
                    hit.point,
                    true,
                    enemy.Kind,
                    powerShot));
                NotifyStateChanged();
                return;
            }

            Score += result.Score;
            if (!powerShot)
            {
                Combo++;
                _nextComboDecayAt = Time.unscaledTime + Mathf.Max(0.1f, comboHoldSeconds);

                if (GameRules.GrantsOverdrive(Combo))
                {
                    OverdriveShots = GameRules.OverdriveShotGrant;
                    Combo = 0;
                    _nextComboDecayAt = float.PositiveInfinity;
                }
            }

            ShotOutcome acceptedOutcome = result.Defeated
                ? ShotOutcome.Defeat
                : result.ShieldBroken
                    ? ShotOutcome.ShieldBreak
                    : result.Critical
                        ? ShotOutcome.Critical
                        : ShotOutcome.Hit;
            PublishShot(new ShotSignal(
                acceptedOutcome,
                hit.point,
                true,
                enemy.Kind,
                powerShot));
            NotifyStateChanged();
        }

        public void Restart()
        {
            EnemyActor[] sceneEnemies = enemyRoot != null
                ? enemyRoot.GetComponentsInChildren<EnemyActor>(true)
                : Array.Empty<EnemyActor>();

            for (int i = sceneEnemies.Length - 1; i >= 0; i--)
            {
                EnemyActor enemy = sceneEnemies[i];
                if (enemy != null)
                {
                    // Destroy is deferred until end of frame; stop retired actors from
                    // moving or breaching the freshly reset session in the meantime.
                    enemy.enabled = false;
                    Destroy(enemy.gameObject);
                }
            }

            _activeEnemies.Clear();
            Score = 0;
            Purity = Mathf.Max(1, startingPurity);
            TimeRemaining = GameRules.SessionDuration;
            Chapter = 1;
            Combo = 0;
            OverdriveShots = 0;
            AimViewport = new Vector2(0.5f, 0.5f);
            _elapsed = 0f;
            _nextSpawnAt = 0.65f;
            _bossSpawned = false;
            _sessionRunning = true;
            _timerExpired = false;
            _waveWasActive = true;
            _nextComboDecayAt = float.PositiveInfinity;
            NotifyStateChanged();
        }

        public void RegisterBreach(EnemyActor enemy)
        {
            if (enemy == null)
            {
                return;
            }

            _activeEnemies.Remove(enemy);
            int breachDamage = GameRules.BreachDamage(enemy.Kind);
            Purity = Mathf.Max(0, Purity - breachDamage);
            Score = GameRules.ValueAfterPenalty(Score, breachDamage * 10);
            Combo = 0;

            if (Purity <= 0 || _timerExpired && enemy.Kind == EnemyKind.Boss)
            {
                _sessionRunning = false;
            }

            NotifyStateChanged();
        }

        public void RegisterDefeat(EnemyActor enemy)
        {
            if (enemy == null)
            {
                return;
            }

            _activeEnemies.Remove(enemy);
            if (_timerExpired && enemy.Kind == EnemyKind.Boss)
            {
                _sessionRunning = false;
            }

            if (enemy.Kind != EnemyKind.Bomber)
            {
                return;
            }

            EnemyActor[] chainTargets = _activeEnemies.ToArray();
            foreach (EnemyActor target in chainTargets)
            {
                if (target == null || target.IsDefeated ||
                    Vector3.SqrMagnitude(target.transform.position - enemy.transform.position) > 10.24f)
                {
                    continue;
                }

                EnemyHitResult chainResult = target.ReceiveExplosion(3);
                if (chainResult.Accepted)
                {
                    Score += chainResult.Score;
                }
            }
        }

        private EnemyKind ChooseEnemyKind()
        {
            float roll = UnityEngine.Random.value;
            if (Chapter == 1)
            {
                return _elapsed < 7f || roll < 0.68f ? EnemyKind.Scout : EnemyKind.Fast;
            }

            if (Chapter == 2)
            {
                if (roll < 0.4f) return EnemyKind.Scout;
                if (roll < 0.65f) return EnemyKind.Fast;
                if (roll < 0.87f) return EnemyKind.Armored;
                return EnemyKind.Bomber;
            }

            if (roll < 0.25f) return EnemyKind.Scout;
            if (roll < 0.5f) return EnemyKind.Fast;
            if (roll < 0.8f) return EnemyKind.Armored;
            return EnemyKind.Bomber;
        }

        private bool SpawnEnemy(EnemyKind kind)
        {
            if (CountActiveEnemies() >= EffectiveActiveCap)
            {
                return false;
            }

            GameObject enemyObject = ProceduralCatFactory.Create(kind, enemyRoot);
            if (enemyObject == null)
            {
                Debug.LogWarning($"{nameof(ProceduralCatFactory)} returned no object for {kind}.", this);
                return false;
            }

            float lane = ChooseOpenLane();
            float farViewportY = UnityEngine.Random.Range(
                Mathf.Min(farViewportYMin, farViewportYMax),
                Mathf.Max(farViewportYMin, farViewportYMax));
            float nearViewportY = UnityEngine.Random.Range(
                Mathf.Min(nearViewportYMin, nearViewportYMax),
                Mathf.Max(nearViewportYMin, nearViewportYMax));
            float laneX = lane * laneWidth;
            float spawnHeight = WorldHeightForViewportY(laneX, spawnZ, farViewportY, enemyHeight);
            enemyObject.transform.position = new Vector3(laneX, spawnHeight, spawnZ);
            enemyObject.transform.rotation = Quaternion.identity;

            EnemyActor actor = enemyObject.GetComponent<EnemyActor>();
            if (actor == null)
            {
                actor = enemyObject.AddComponent<EnemyActor>();
            }

            actor.Configure(
                this,
                kind,
                SpeedFor(kind),
                UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                farViewportY,
                nearViewportY);
            _activeEnemies.Add(actor);
            return true;
        }

        internal float WorldHeightForViewportY(
            float worldX,
            float worldZ,
            float viewportY,
            float fallbackHeight)
        {
            if (gameplayCamera == null)
            {
                return fallbackHeight;
            }

            // Intersect the viewport row's left/right rays with the requested Z plane,
            // then solve the viewport X that preserves the actor's world-space lane.
            // This keeps screen Y deterministic without turning depth movement into a
            // camera-relative approximation or making enemies cross lanes.
            Ray leftRay = gameplayCamera.ViewportPointToRay(new Vector3(0f, viewportY, 0f));
            Ray rightRay = gameplayCamera.ViewportPointToRay(new Vector3(1f, viewportY, 0f));
            if (!TryIntersectZPlane(leftRay, worldZ, out Vector3 leftPoint) ||
                !TryIntersectZPlane(rightRay, worldZ, out Vector3 rightPoint))
            {
                return fallbackHeight;
            }

            float rowWidth = rightPoint.x - leftPoint.x;
            float viewportX = Mathf.Abs(rowWidth) < 0.001f
                ? 0.5f
                : (worldX - leftPoint.x) / rowWidth;
            Ray targetRay = gameplayCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
            return TryIntersectZPlane(targetRay, worldZ, out Vector3 targetPoint)
                ? targetPoint.y
                : fallbackHeight;
        }

        private static bool TryIntersectZPlane(Ray ray, float worldZ, out Vector3 point)
        {
            if (Mathf.Abs(ray.direction.z) < 0.0001f)
            {
                point = default;
                return false;
            }

            float distance = (worldZ - ray.origin.z) / ray.direction.z;
            if (distance <= 0f)
            {
                point = default;
                return false;
            }

            point = ray.GetPoint(distance);
            return true;
        }

        private float ChooseOpenLane()
        {
            int laneIndex = UnityEngine.Random.Range(-1, 2);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool occupiedNearSpawn = false;
                float laneX = laneIndex * laneWidth;

                foreach (EnemyActor enemy in _activeEnemies)
                {
                    if (enemy != null && !enemy.IsDefeated &&
                        enemy.transform.position.z > spawnZ - 3f &&
                        Mathf.Abs(enemy.transform.position.x - laneX) < laneWidth * 0.5f)
                    {
                        occupiedNearSpawn = true;
                        break;
                    }
                }

                if (!occupiedNearSpawn)
                {
                    return laneIndex;
                }

                laneIndex = laneIndex == 1 ? -1 : laneIndex + 1;
            }

            return laneIndex;
        }

        private float SpeedFor(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Fast: return fastSpeed;
                case EnemyKind.Armored: return armoredSpeed;
                case EnemyKind.Bomber: return bomberSpeed;
                case EnemyKind.Boss: return bossSpeed;
                default: return scoutSpeed;
            }
        }

        private void ApplyMissPenalty(int scorePenalty, int comboPenalty)
        {
            Score = GameRules.ValueAfterPenalty(Score, scorePenalty);
            Combo = GameRules.ValueAfterPenalty(Combo, comboPenalty);
            _nextComboDecayAt = Combo > 0
                ? Time.unscaledTime + Mathf.Max(0.1f, comboDecayStepSeconds)
                : float.PositiveInfinity;
        }

        private bool TryGetBoss(out EnemyActor boss)
        {
            foreach (EnemyActor enemy in _activeEnemies)
            {
                if (enemy != null && enemy.Kind == EnemyKind.Boss && !enemy.IsDefeated)
                {
                    boss = enemy;
                    return true;
                }
            }

            boss = null;
            return false;
        }

        private int CountActiveEnemies()
        {
            int count = 0;
            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                EnemyActor enemy = _activeEnemies[i];
                if (enemy == null || enemy.IsDefeated)
                {
                    _activeEnemies.RemoveAt(i);
                    continue;
                }

                count++;
            }

            return count;
        }

        private void NotifyStateChanged()
        {
            _nextStatePublishAt = Time.unscaledTime + StatePublishInterval;
            StateChanged?.Invoke();
        }

        private void PublishShot(ShotSignal signal)
        {
            ShotResolved?.Invoke(signal);
        }
    }
}
