using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlossomBreach
{
    /// <summary>
    /// Optional presentation-only bridge for optimized Meshy enemy models.
    /// Gameplay colliders and the procedural weak point remain authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OptionalEnemyModelAdapter : MonoBehaviour
    {
        private const int MaxExternalInstances = 4;
        private const int MaxTrianglesPerInstance = 20000;
        private const int MaxSharedMaterials = 4;
        private const string ExternalModelName = "Optional Meshy Model";

        private static readonly ModelSpec ScoutSpec = new ModelSpec(
            "Premium Scout", 2.15f,
            new[] { "Meshy/PremiumScout/PremiumScout_Optimized" },
            new[]
            {
                "Assets/ExternalAssets/Meshy/PremiumScoutOptimized/PremiumScout_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/PremiumScoutOptimized/PremiumScout_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/PremiumScoutOptimized/model.fbx",
                "Assets/ExternalAssets/Meshy/PremiumScout/PremiumScout_Optimized.fbx"
            });

        private static readonly ModelSpec ArmoredSpec = new ModelSpec(
            "Premium Armored", 2.25f,
            new[] { "Meshy/PremiumArmored/PremiumArmored_Optimized" },
            new[]
            {
                "Assets/ExternalAssets/Meshy/PremiumArmoredOptimized/PremiumArmored_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/PremiumArmoredOptimized/PremiumArmored_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/PremiumArmoredOptimized/model.fbx",
                "Assets/ExternalAssets/Meshy/PremiumArmored/PremiumArmored_Optimized.fbx"
            });

        private static readonly ModelSpec BomberSpec = new ModelSpec(
            "Acorn Bomber", 2.15f,
            new[] { "Meshy/AcornBomber/AcornBomber_Optimized", "Meshy/AcornBomber/AcornBomber" },
            new[]
            {
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/model.fbx",
                "Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber_Optimized.fbx"
            });

        private static readonly ModelSpec BossSpec = new ModelSpec(
            "Premium Boss", 2.85f,
            new[] { "Meshy/PremiumBoss/PremiumBoss_Optimized" },
            new[]
            {
                "Assets/ExternalAssets/Meshy/PremiumBossOptimized/PremiumBoss_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/PremiumBossOptimized/PremiumBoss_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/PremiumBossOptimized/model.fbx",
                "Assets/ExternalAssets/Meshy/PremiumBoss/PremiumBoss_Optimized.fbx"
            });

        private static readonly string[] RunStates = { "RUN_FORWARD", "Running", "CHARGE" };
        private static readonly string[] HitStates = { "HIT_RECOIL", "BeHit" };
        private static readonly string[] DeathStates = { "DEATH", "Dead", "DEATH_FALL_BACK" };

        private static readonly Dictionary<EnemyKind, GameObject> RegisteredPrefabs = new();
        private static int activeExternalInstances;

        private Animator animator;
        private bool ownsExternalSlot;
        private bool deathStarted;
        private float resumeRunAt = -1f;

        public static int ActiveExternalInstances => activeExternalInstances;

        /// <summary>Allows a bootstrap or scene author to provide a prefab without Resources.</summary>
        public static void RegisterBomberPrefab(GameObject prefab)
        {
            RegisterPrefab(EnemyKind.Bomber, prefab);
        }

        public static void RegisterPrefab(EnemyKind kind, GameObject prefab)
        {
            if (prefab == null) RegisteredPrefabs.Remove(kind);
            else RegisteredPrefabs[kind] = prefab;
        }

        public static bool TryAttachBomber(GameObject enemyRoot, Transform visualRig)
        {
            return TryAttach(enemyRoot, visualRig, EnemyKind.Bomber);
        }

        public static bool TryAttach(GameObject enemyRoot, Transform visualRig, EnemyKind kind)
        {
            if (enemyRoot == null || visualRig == null || activeExternalInstances >= MaxExternalInstances)
                return false;
            if (enemyRoot.GetComponent<OptionalEnemyModelAdapter>() != null || !TryGetSpec(kind, out var spec))
                return false;

            var prefab = ResolvePrefab(kind, spec);
            if (prefab == null) return false;

            GameObject model;
            try
            {
                model = Instantiate(prefab, visualRig, false);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Optional {spec.Label} could not be instantiated; using procedural fallback. {exception.Message}",
                    enemyRoot);
                return false;
            }

            model.name = $"{ExternalModelName} ({kind})";
            var modelRenderers = model.GetComponentsInChildren<Renderer>(true);
            if (modelRenderers.Length == 0 || !WithinRuntimeBudget(modelRenderers, spec.Label))
            {
                SafeDestroy(model);
                return false;
            }

            DisableImportedColliders(model);
            ConfigureRenderers(modelRenderers);
            NormalizeModel(model.transform, visualRig, modelRenderers, spec.TargetHeight);
            HideProceduralBody(visualRig, model.transform, kind);

            var adapter = enemyRoot.AddComponent<OptionalEnemyModelAdapter>();
            adapter.animator = model.GetComponentInChildren<Animator>(true);
            adapter.ownsExternalSlot = true;
            activeExternalInstances++;
            adapter.ConfigureAnimator();
            return true;
        }

        internal static bool IsExternalTransform(Transform candidate)
        {
            while (candidate != null)
            {
                if (candidate.name.StartsWith(ExternalModelName)) return true;
                candidate = candidate.parent;
            }
            return false;
        }

        public void PlayHit()
        {
            if (deathStarted || animator == null) return;
            if (TryPlay(HitStates, 0.035f)) resumeRunAt = Time.time + 0.24f;
        }

        public void PlayDeath()
        {
            if (deathStarted) return;
            deathStarted = true;
            resumeRunAt = -1f;
            TryPlay(DeathStates, 0.055f);
        }

        private void Update()
        {
            if (!deathStarted && resumeRunAt >= 0f && Time.time >= resumeRunAt)
            {
                resumeRunAt = -1f;
                TryPlay(RunStates, 0.08f);
            }
        }

        private void OnDestroy()
        {
            if (!ownsExternalSlot) return;
            ownsExternalSlot = false;
            activeExternalInstances = Mathf.Max(0, activeExternalInstances - 1);
        }

        private void ConfigureAnimator()
        {
            if (animator == null) return;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            TryPlay(RunStates, 0f);
        }

        private bool TryPlay(string[] aliases, float transitionSeconds)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            for (var i = 0; i < aliases.Length; i++)
            {
                var shortHash = Animator.StringToHash(aliases[i]);
                var fullHash = Animator.StringToHash($"Base Layer.{aliases[i]}");
                if (animator.HasState(0, shortHash))
                {
                    animator.CrossFadeInFixedTime(shortHash, transitionSeconds, 0);
                    return true;
                }
                if (animator.HasState(0, fullHash))
                {
                    animator.CrossFadeInFixedTime(fullHash, transitionSeconds, 0);
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetSpec(EnemyKind kind, out ModelSpec spec)
        {
            switch (kind)
            {
                case EnemyKind.Scout: spec = ScoutSpec; return true;
                case EnemyKind.Armored: spec = ArmoredSpec; return true;
                case EnemyKind.Bomber: spec = BomberSpec; return true;
                case EnemyKind.Boss: spec = BossSpec; return true;
                default: spec = default; return false;
            }
        }

        private static GameObject ResolvePrefab(EnemyKind kind, ModelSpec spec)
        {
            if (RegisteredPrefabs.TryGetValue(kind, out var registered) && registered != null) return registered;
            for (var i = 0; i < spec.ResourceCandidates.Length; i++)
            {
                var candidate = Resources.Load<GameObject>(spec.ResourceCandidates[i]);
                if (candidate != null) return candidate;
            }

#if UNITY_EDITOR
            for (var i = 0; i < spec.EditorCandidates.Length; i++)
            {
                var candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(spec.EditorCandidates[i]);
                if (candidate != null) return candidate;
            }
#endif
            return null;
        }

        private static bool WithinRuntimeBudget(Renderer[] renderers, string label)
        {
            var triangles = 0;
            var materials = new HashSet<Material>();
            for (var i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = null;
                if (renderers[i] is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                else if (renderers[i].TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                if (mesh != null)
                {
                    for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                        triangles += (int)mesh.GetIndexCount(subMesh) / 3;
                }

                var shared = renderers[i].sharedMaterials;
                for (var materialIndex = 0; materialIndex < shared.Length; materialIndex++)
                {
                    if (shared[materialIndex] != null) materials.Add(shared[materialIndex]);
                }
            }

            if (triangles <= MaxTrianglesPerInstance && materials.Count <= MaxSharedMaterials) return true;
            Debug.LogWarning($"Optional {label} exceeds mobile budget ({triangles} triangles, " +
                             $"{materials.Count} materials); using procedural fallback.");
            return false;
        }

        private static void ConfigureRenderers(Renderer[] renderers)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
                if (renderers[i] is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = false;
                    skinned.quality = SkinQuality.Bone2;
                }
            }
        }

        private static void NormalizeModel(Transform model, Transform rig, Renderer[] renderers, float targetHeight)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.localScale = Vector3.one;
            if (!TryGetBounds(renderers, out var bounds) || bounds.size.y < 0.001f) return;

            var uniformScale = Mathf.Clamp(targetHeight / bounds.size.y, 0.01f, 25f);
            model.localScale = Vector3.one * uniformScale;
            if (!TryGetBounds(renderers, out bounds)) return;
            var center = rig.InverseTransformPoint(bounds.center);
            model.localPosition += new Vector3(-center.x, targetHeight * 0.5f - center.y, -center.z);
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled) continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return found;
        }

        private static void HideProceduralBody(Transform rig, Transform externalModel, EnemyKind kind)
        {
            var renderers = rig.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].transform.IsChildOf(externalModel)) continue;
                var name = renderers[i].gameObject.name;
                var keepAsTarget = KeepProceduralRenderer(kind, name);
                renderers[i].enabled = keepAsTarget;
            }
        }

        private static bool KeepProceduralRenderer(EnemyKind kind, string name)
        {
            if (name == "Weak Point Core" || name == "Core Petal") return true;
            if (kind == EnemyKind.Armored)
                return name == "Rose Shield Zone" || name == "Rose Petal" || name == "Rose Heart";
            if (kind == EnemyKind.Bomber)
                return name == "Pollen Bomb" || name == "Bomb Band" || name == "Fuse" ||
                       name == "Fuse Spark" || name == "Warning Belt" || name == "Warning Stripe";
            return false;
        }

        private static void DisableImportedColliders(GameObject model)
        {
            var colliders = model.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        }

        private static void SafeDestroy(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private readonly struct ModelSpec
        {
            public ModelSpec(string label, float targetHeight, string[] resourceCandidates,
                string[] editorCandidates)
            {
                Label = label;
                TargetHeight = targetHeight;
                ResourceCandidates = resourceCandidates;
                EditorCandidates = editorCandidates;
            }

            public string Label { get; }
            public float TargetHeight { get; }
            public string[] ResourceCandidates { get; }
            public string[] EditorCandidates { get; }
        }
    }
}
