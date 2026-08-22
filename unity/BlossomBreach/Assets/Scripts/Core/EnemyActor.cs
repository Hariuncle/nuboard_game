using UnityEngine;

namespace BlossomBreach
{
    [DisallowMultipleComponent]
    public sealed class EnemyActor : MonoBehaviour
    {
        [Header("Approach")]
        [SerializeField] private float laneSway = 0.24f;
        [SerializeField] private float laneSwayFrequency = 1.7f;
        [SerializeField, Min(0.1f)] private float chargeWarningDistance = 0.35f;

        [Header("Defeat")]
        [SerializeField] private float defeatedLifetime = 1.15f;
        [SerializeField] private float fallAwaySpeed = 2.8f;
        [SerializeField] private float fallRotationSpeed = 150f;

        private BlossomGame _game;
        private float _speed;
        private float _laneCenter;
        private float _swayPhase;
        private float _farViewportY;
        private float _nearViewportY;
        private Vector3 _baseRootScale;
        private float _aliveTime;
        private float _defeatedTime;
        private float _staggerRemaining;
        private float _forcedCoreOpenUntil;
        private float _chargeElapsed;
        private float _chargeDuration;
        private int _health;
        private int _maxHealth;
        private int _shield;
        private bool _configured;
        private bool _breached;
        private bool _isCharging;

        public EnemyKind Kind { get; private set; }
        public bool IsDefeated { get; private set; }
        public bool IsBossCoreOpen { get; private set; }
        public bool IsCharging => _isCharging;
        public float ChargeNormalized => !_isCharging || _chargeDuration <= 0f
            ? 0f
            : Mathf.Clamp01(_chargeElapsed / _chargeDuration);
        public EnemyThreatStage ThreatStage { get; private set; }
        public BossPattern CurrentBossPattern { get; private set; }
        public float HealthNormalized => _maxHealth <= 0 ? 0f : Mathf.Clamp01((float)_health / _maxHealth);
        public int ShieldRemaining => Mathf.Max(0, _shield);

        public void Configure(BlossomGame game, EnemyKind kind, float speed, float swayPhase)
        {
            Configure(game, kind, speed, swayPhase, 0.83f, 0.485f);
        }

        public void Configure(
            BlossomGame game,
            EnemyKind kind,
            float speed,
            float swayPhase,
            float farViewportY,
            float nearViewportY)
        {
            _game = game;
            Kind = kind;
            _speed = Mathf.Max(0.1f, speed);
            _laneCenter = transform.position.x;
            _swayPhase = swayPhase;
            _farViewportY = Mathf.Clamp01(farViewportY);
            _nearViewportY = Mathf.Clamp01(nearViewportY);
            _baseRootScale = transform.localScale;
            _aliveTime = 0f;
            _defeatedTime = 0f;
            _staggerRemaining = 0f;
            _forcedCoreOpenUntil = 0f;
            _chargeElapsed = 0f;
            _chargeDuration = GameRules.ChargeWarningDuration(kind);
            _breached = false;
            _isCharging = false;
            IsDefeated = false;
            ThreatStage = EnemyThreatStage.Distant;
            CurrentBossPattern = BossPattern.GuardedCore;

            switch (kind)
            {
                case EnemyKind.Fast:
                    _health = 1;
                    _shield = 0;
                    laneSway = Mathf.Min(laneSway, 0.32f);
                    laneSwayFrequency = Mathf.Max(laneSwayFrequency, 2.5f);
                    break;
                case EnemyKind.Armored:
                    _health = 3;
                    _shield = 2;
                    break;
                case EnemyKind.Bomber:
                    _health = 2;
                    _shield = 0;
                    break;
                case EnemyKind.Boss:
                    _health = 14;
                    _shield = 0;
                    break;
                default:
                    _health = 1;
                    _shield = 0;
                    break;
            }

            _maxHealth = _health;
            _configured = true;
            EnsureHitTarget();
            UpdateBossCoreState();
            _game?.ReportThreatStage(this, ThreatStage, _chargeDuration);
            if (Kind == EnemyKind.Boss)
            {
                _game?.ReportBossPattern(this, CurrentBossPattern);
            }
        }

        public EnemyHitResult ReceiveHit(HitZone zone, bool powerShot)
        {
            if (!_configured || IsDefeated || zone != null && zone.Owner != this)
            {
                return EnemyHitResult.Rejected;
            }

            bool weakPoint = zone != null && zone.weakPoint;
            bool shieldZone = zone != null && zone.shield;
            bool shieldBroken = false;

            if (Kind == EnemyKind.Armored && _shield > 0)
            {
                if (!shieldZone && !powerShot)
                {
                    return EnemyHitResult.Rejected;
                }

                _shield -= powerShot ? 2 : 1;
                shieldBroken = _shield <= 0;
                int shieldScore = shieldBroken ? 90 : 30;
                ReactToHit(false, shieldBroken);
                return new EnemyHitResult(true, false, shieldBroken, false, shieldScore);
            }

            if (Kind == EnemyKind.Boss && weakPoint && !IsBossCoreOpen && !powerShot)
            {
                return EnemyHitResult.Rejected;
            }

            if (Kind == EnemyKind.Boss && !weakPoint && !powerShot)
            {
                return EnemyHitResult.Rejected;
            }

            int damage = GameRules.HitDamage(weakPoint, powerShot);
            _health -= damage;
            bool defeated = _health <= 0;
            int score = GameRules.BaseScore(Kind);

            if (Kind == EnemyKind.Bomber && !weakPoint && !defeated)
            {
                // A poorly placed body shot lights the fuse and makes the bomber more urgent.
                _speed *= 1.3f;
            }

            if (weakPoint && !defeated)
            {
                Stagger(Kind == EnemyKind.Boss ? 0.42f : 0.28f);
                if (Kind == EnemyKind.Boss)
                {
                    _forcedCoreOpenUntil = Mathf.Max(_forcedCoreOpenUntil, _aliveTime + 0.55f);
                }
            }

            if (!defeated)
            {
                score = Mathf.Max(20, score / (Kind == EnemyKind.Boss ? 10 : 3));
            }

            if (weakPoint)
            {
                score += defeated ? GameRules.BaseScore(Kind) / 2 : 75;
            }

            if (defeated)
            {
                BeginDefeat();
            }

            ReactToHit(defeated);

            return new EnemyHitResult(true, weakPoint, false, defeated, score);
        }

        public EnemyHitResult ReceiveExplosion(int damage)
        {
            if (!_configured || IsDefeated || damage <= 0)
            {
                return EnemyHitResult.Rejected;
            }

            bool shieldBroken = _shield > 0;
            _shield = 0;
            _health -= damage;
            bool defeated = _health <= 0;
            if (defeated)
            {
                BeginDefeat();
            }
            else
            {
                Stagger(0.48f);
            }

            ReactToHit(defeated, shieldBroken);
            int score = defeated ? Mathf.Max(40, GameRules.BaseScore(Kind) / 2) : 20;
            return new EnemyHitResult(true, false, shieldBroken, defeated, score);
        }

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            if (IsDefeated)
            {
                AnimateDefeat();
                return;
            }

            if (_game != null && !_game.IsRunning)
            {
                return;
            }

            _aliveTime += Time.deltaTime;
            UpdateBossCoreState();

            Vector3 position = transform.position;
            if (_isCharging)
            {
                _chargeElapsed += Time.unscaledDeltaTime;
            }
            else if (_staggerRemaining > 0f)
            {
                _staggerRemaining = Mathf.Max(0f, _staggerRemaining - Time.deltaTime);
            }
            else
            {
                float patternSpeed = Kind == EnemyKind.Boss
                    ? GameRules.BossApproachSpeedMultiplier(CurrentBossPattern)
                    : 1f;
                position.z -= _speed * patternSpeed * Time.deltaTime;
            }

            if (!_isCharging && _game != null &&
                position.z <= _game.BreachZ + chargeWarningDistance)
            {
                position.z = _game.BreachZ + chargeWarningDistance;
                BeginChargeWarning();
            }

            float approachProgress = _game != null
                ? GameRules.ApproachProgress(position.z, _game.SpawnZ, _game.BreachZ)
                : 0f;
            float sway = Mathf.Sin(_aliveTime * laneSwayFrequency + _swayPhase) * laneSway;
            position.x = GameRules.ConvergedLaneX(
                _laneCenter,
                sway,
                GameRules.EaseInFinalQuarter(approachProgress));
            if (_game != null)
            {
                float viewportY = GameRules.ApproachViewportY(
                    position.z,
                    _game.SpawnZ,
                    _game.BreachZ,
                    _farViewportY,
                    _nearViewportY);
                position.y = _game.WorldHeightForViewportY(
                    position.x,
                    position.z,
                    viewportY,
                    position.y);
            }
            transform.position = position;
            UpdateChargePulse();

            if (!_breached && _isCharging && _chargeElapsed >= _chargeDuration)
            {
                CompleteBreach();
            }
            else if (!_isCharging)
            {
                SetThreatStage(GameRules.ThreatStageAt(approachProgress));
            }
        }

        private void UpdateBossCoreState()
        {
            if (Kind != EnemyKind.Boss)
            {
                IsBossCoreOpen = false;
                return;
            }

            BossPattern nextPattern = GameRules.BossPatternAt(_aliveTime);
            if (nextPattern != CurrentBossPattern)
            {
                CurrentBossPattern = nextPattern;
                _game?.ReportBossPattern(this, CurrentBossPattern);
            }

            IsBossCoreOpen = _aliveTime < _forcedCoreOpenUntil ||
                GameRules.IsBossCoreOpen(CurrentBossPattern, _aliveTime);
        }

        private void BeginDefeat()
        {
            IsDefeated = true;
            _defeatedTime = 0f;
            _isCharging = false;
            transform.localScale = _baseRootScale;

            foreach (Collider targetCollider in GetComponentsInChildren<Collider>())
            {
                targetCollider.enabled = false;
            }

            _game?.RegisterDefeat(this);
        }

        private void AnimateDefeat()
        {
            _defeatedTime += Time.deltaTime;
            transform.position += Vector3.forward * (fallAwaySpeed * Time.deltaTime);
            transform.Rotate(Vector3.right, fallRotationSpeed * Time.deltaTime, Space.Self);

            if (_defeatedTime >= defeatedLifetime)
            {
                Destroy(gameObject);
            }
        }

        private void BeginChargeWarning()
        {
            _isCharging = true;
            _chargeElapsed = 0f;
            _chargeDuration = GameRules.ChargeWarningDuration(Kind);
            SetThreatStage(EnemyThreatStage.Charging);
        }

        private void CompleteBreach()
        {
            if (_breached || _game == null)
            {
                return;
            }

            _breached = true;
            transform.localScale = _baseRootScale;
            _game.RegisterBreach(this);
            Destroy(gameObject);
        }

        private void SetThreatStage(EnemyThreatStage stage)
        {
            if (ThreatStage == stage)
            {
                return;
            }

            ThreatStage = stage;
            _game?.ReportThreatStage(this, ThreatStage, _chargeDuration);
        }

        private void UpdateChargePulse()
        {
            if (!_isCharging)
            {
                transform.localScale = Vector3.Lerp(
                    transform.localScale,
                    _baseRootScale,
                    Time.unscaledDeltaTime * 12f);
                return;
            }

            float frequency = Kind == EnemyKind.Fast ? 10f
                : Kind == EnemyKind.Bomber ? 8.5f
                : Kind == EnemyKind.Boss ? 5f
                : 6.5f;
            float progress = ChargeNormalized;
            float pulse = Mathf.Sin(_chargeElapsed * frequency * Mathf.PI * 2f);
            float scale = 1f + pulse * (0.025f + progress * 0.025f) + progress * 0.055f;
            transform.localScale = _baseRootScale * scale;
        }

        private void EnsureHitTarget()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            Collider bodyCollider = GetComponent<Collider>();
            if (colliders.Length == 0)
            {
                CapsuleCollider body = gameObject.AddComponent<CapsuleCollider>();
                body.center = new Vector3(0f, 0.75f, 0f);
                body.height = Kind == EnemyKind.Boss ? 2.8f : 1.6f;
                body.radius = Kind == EnemyKind.Boss ? 0.8f : 0.45f;
                bodyCollider = body;
            }

            if (bodyCollider == null)
            {
                return;
            }

            HitZone bodyZone = bodyCollider.GetComponent<HitZone>();
            if (bodyZone == null)
            {
                bodyZone = bodyCollider.gameObject.AddComponent<HitZone>();
            }

            if (Kind == EnemyKind.Armored)
            {
                // The visible armored silhouette is itself a shield hit proxy. Without
                // this marker only the small rose collider could break the shield, while
                // hits on the otherwise valid root capsule were rejected as body shots.
                bodyZone.shield = true;
            }
        }

        private void ReactToHit(bool defeated, bool shieldBroken = false)
        {
            ProceduralCatMotion motion = GetComponent<ProceduralCatMotion>();
            if (motion != null)
            {
                motion.ReactToHit(defeated, shieldBroken);
            }
        }

        private void Stagger(float seconds)
        {
            _staggerRemaining = Mathf.Max(_staggerRemaining, seconds);
            if (_isCharging)
            {
                _isCharging = false;
                _chargeElapsed = 0f;
                transform.localScale = _baseRootScale;
                SetThreatStage(EnemyThreatStage.Near);
            }
            transform.position += Vector3.forward * (Kind == EnemyKind.Boss ? 0.28f : 0.48f);
        }
    }
}
