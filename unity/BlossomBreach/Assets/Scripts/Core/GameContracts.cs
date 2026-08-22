using UnityEngine;

namespace BlossomBreach
{
    public enum EnemyKind
    {
        Scout,
        Fast,
        Armored,
        Bomber,
        Boss
    }

    public enum EnemyThreatStage
    {
        Distant,
        Advancing,
        Near,
        Charging
    }

    public enum BossPattern
    {
        GuardedCore,
        PetalBurst,
        Pounce
    }

    public readonly struct ThreatSignal
    {
        public ThreatSignal(EnemyActor actor, EnemyThreatStage stage, float chargeDuration)
        {
            Actor = actor;
            Stage = stage;
            ChargeDuration = chargeDuration;
        }

        public EnemyActor Actor { get; }
        public EnemyThreatStage Stage { get; }
        public float ChargeDuration { get; }
    }

    public readonly struct PurificationRewardSignal
    {
        public PurificationRewardSignal(int streak, int score, int overdriveShots)
        {
            Streak = streak;
            Score = score;
            OverdriveShots = overdriveShots;
        }

        public int Streak { get; }
        public int Score { get; }
        public int OverdriveShots { get; }
    }

    public readonly struct BossPatternSignal
    {
        public BossPatternSignal(EnemyActor actor, BossPattern pattern)
        {
            Actor = actor;
            Pattern = pattern;
        }

        public EnemyActor Actor { get; }
        public BossPattern Pattern { get; }
    }

    /// <summary>
    /// Marks a collider as a special target area. Procedural factories may leave both
    /// flags false to create an ordinary body hit zone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitZone : MonoBehaviour
    {
        [Tooltip("Critical target area, such as a head or boss core.")]
        public bool weakPoint;

        [Tooltip("Shield target area. Armored enemies must lose this before body damage.")]
        public bool shield;

        private EnemyActor _owner;

        public EnemyActor Owner
        {
            get
            {
                if (_owner == null)
                {
                    _owner = GetComponentInParent<EnemyActor>();
                }

                return _owner;
            }
        }

        private void OnTransformParentChanged()
        {
            _owner = null;
        }
    }

    public readonly struct EnemyHitResult
    {
        public static readonly EnemyHitResult Rejected =
            new EnemyHitResult(false, false, false, false, 0);

        public EnemyHitResult(
            bool accepted,
            bool critical,
            bool shieldBroken,
            bool defeated,
            int score)
        {
            Accepted = accepted;
            Critical = critical;
            ShieldBroken = shieldBroken;
            Defeated = defeated;
            Score = score;
        }

        public bool Accepted { get; }
        public bool Critical { get; }
        public bool ShieldBroken { get; }
        public bool Defeated { get; }
        public int Score { get; }
    }

    /// <summary>
    /// Side-effect-free rules kept separate from MonoBehaviours so their boundary
    /// behavior can be covered by ordinary edit-mode tests when Unity is available.
    /// </summary>
    public static class GameRules
    {
        public const float SessionDuration = 60f;
        public const int HitsForOverdrive = 5;
        public const int OverdriveShotGrant = 3;

        public static int ChapterAt(float elapsedSeconds)
        {
            if (elapsedSeconds >= 40f)
            {
                return 3;
            }

            return elapsedSeconds >= 20f ? 2 : 1;
        }

        public static float SpawnIntervalFor(int chapter)
        {
            switch (Mathf.Clamp(chapter, 1, 3))
            {
                case 1: return 1.18f;
                case 2: return 0.86f;
                default: return 0.68f;
            }
        }

        public static bool IsWaveActive(float elapsedSeconds)
        {
            int chapter = ChapterAt(elapsedSeconds);
            float chapterStart = (chapter - 1) * 20f;
            float localTime = Mathf.Max(0f, elapsedSeconds - chapterStart);
            float cycleLength = chapter == 1 ? 6.5f : chapter == 2 ? 6f : 5.5f;
            float activeLength = chapter == 1 ? 4.8f : chapter == 2 ? 4.65f : 4.45f;
            return Mathf.Repeat(localTime, cycleLength) < activeLength;
        }

        public static bool GrantsOverdrive(int comboAfterHit)
        {
            return comboAfterHit >= HitsForOverdrive;
        }

        public static float ChargeWarningDuration(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Fast: return 0.8f;
                case EnemyKind.Bomber: return 0.9f;
                case EnemyKind.Armored: return 1.15f;
                case EnemyKind.Boss: return 1.2f;
                default: return 1f;
            }
        }

        public static EnemyThreatStage ThreatStageAt(float approachProgress)
        {
            if (approachProgress >= 0.9f)
            {
                return EnemyThreatStage.Near;
            }

            return approachProgress >= 0.55f
                ? EnemyThreatStage.Advancing
                : EnemyThreatStage.Distant;
        }

        public static bool GrantsPurificationStreakReward(int streak)
        {
            return streak > 0 && streak % 3 == 0;
        }

        public static int PurificationStreakReward(int streak)
        {
            return GrantsPurificationStreakReward(streak) ? 180 + streak * 40 : 0;
        }

        public static BossPattern BossPatternAt(float aliveSeconds)
        {
            float cycle = Mathf.Repeat(Mathf.Max(0f, aliveSeconds), 9.6f);
            if (cycle < 3.4f)
            {
                return BossPattern.GuardedCore;
            }

            return cycle < 6.6f ? BossPattern.PetalBurst : BossPattern.Pounce;
        }

        public static bool IsBossCoreOpen(BossPattern pattern, float aliveSeconds)
        {
            float cycle = Mathf.Repeat(Mathf.Max(0f, aliveSeconds), 9.6f);
            switch (pattern)
            {
                case BossPattern.PetalBurst:
                    return Mathf.Repeat(cycle - 3.4f, 0.78f) >= 0.38f;
                case BossPattern.Pounce:
                    float pounceTime = cycle - 6.6f;
                    return pounceTime >= 0.72f && pounceTime <= 1.9f;
                default:
                    return cycle >= 1.45f;
            }
        }

        public static float BossApproachSpeedMultiplier(BossPattern pattern)
        {
            switch (pattern)
            {
                case BossPattern.PetalBurst: return 0.94f;
                case BossPattern.Pounce: return 1.22f;
                default: return 1f;
            }
        }

        public static float ApproachProgress(float worldZ, float spawnZ, float breachZ)
        {
            float approachLength = spawnZ - breachZ;
            if (Mathf.Abs(approachLength) < 0.001f)
            {
                return 1f;
            }

            return Mathf.Clamp01((spawnZ - worldZ) / approachLength);
        }

        public static float ApproachViewportY(
            float worldZ,
            float spawnZ,
            float breachZ,
            float farViewportY,
            float nearViewportY)
        {
            return Mathf.Lerp(
                farViewportY,
                nearViewportY,
                EaseInFinalQuarter(ApproachProgress(worldZ, spawnZ, breachZ)));
        }

        public static float EaseInFinalQuarter(float approachProgress)
        {
            float progress = Mathf.Clamp01(approachProgress);
            if (progress <= 0.75f)
            {
                return progress;
            }

            float finalQuarter = (progress - 0.75f) * 4f;
            // C1-continuous at 75%: briefly settles, then accelerates into the close-up.
            float eased = finalQuarter -
                0.6f * finalQuarter * finalQuarter * (1f - finalQuarter);
            return 0.75f + eased * 0.25f;
        }

        public static float ConvergedLaneX(float laneCenter, float sway, float approachProgress)
        {
            // At four metres from the camera, stronger convergence keeps large actors
            // inside the frame. Outer lanes remain on their original side of centre.
            float convergence = Mathf.Lerp(1f, 0.44f, Mathf.Clamp01(approachProgress));
            return laneCenter * convergence + sway;
        }

        public static int ValueAfterPenalty(int currentValue, int penalty)
        {
            return Mathf.Max(0, currentValue - Mathf.Max(0, penalty));
        }

        public static int HitDamage(bool weakPoint, bool powerShot)
        {
            int damage = weakPoint ? 2 : 1;
            return powerShot ? damage + 2 : damage;
        }

        public static int BreachDamage(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Fast: return 6;
                case EnemyKind.Armored: return 14;
                case EnemyKind.Bomber: return 24;
                case EnemyKind.Boss: return 30;
                default: return 8;
            }
        }

        public static int BaseScore(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Fast: return 130;
                case EnemyKind.Armored: return 180;
                case EnemyKind.Bomber: return 260;
                case EnemyKind.Boss: return 1200;
                default: return 100;
            }
        }
    }
}
