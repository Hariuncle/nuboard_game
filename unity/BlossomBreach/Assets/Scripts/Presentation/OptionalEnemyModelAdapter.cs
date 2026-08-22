using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlossomBreach
{
    /// <summary>
    /// Optional presentation-only bridge for an optimized Meshy Acorn Bomber model.
    /// Gameplay colliders and the procedural weak point remain authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OptionalEnemyModelAdapter : MonoBehaviour
    {
        private const int MaxExternalInstances = 4;
        private const int MaxTrianglesPerInstance = 12000;
        private const int MaxSharedMaterials = 4;
        private const float TargetModelHeight = 2.15f;

        private static readonly string[] ResourceCandidates =
        {
            "Meshy/AcornBomber/AcornBomber_Optimized",
            "Meshy/AcornBomber/AcornBomber"
        };

        private static readonly string[] RunStates = { "RUN_FORWARD", "Running", "CHARGE" };
        private static readonly string[] HitStates = { "HIT_RECOIL", "BeHit" };
        private static readonly string[] DeathStates = { "DEATH", "Dead", "DEATH_FALL_BACK" };

        private static GameObject registeredBomberPrefab;
        private static int activeExternalInstances;

        private Animator animator;
        private bool ownsExternalSlot;
        private bool deathStarted;
        private float resumeRunAt = -1f;

        public static int ActiveExternalInstances => activeExternalInstances;

        /// <summary>Allows a bootstrap or scene author to provide a prefab without Resources.</summary>
        public static void RegisterBomberPrefab(GameObject prefab)
        {
            registeredBomberPrefab = prefab;
        }

        public static bool TryAttachBomber(GameObject enemyRoot, Transform visualRig)
        {
            if (enemyRoot == null || visualRig == null || activeExternalInstances >= MaxExternalInstances)
                return false;

            var prefab = ResolveBomberPrefab();
            if (prefab == null) return false;

            GameObject model;
            try
            {
                model = Instantiate(prefab, visualRig, false);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Optional Acorn Bomber could not be instantiated; using procedural fallback. {exception.Message}",
                    enemyRoot);
                return false;
            }

            model.name = "Optional Meshy Model";
            var modelRenderers = model.GetComponentsInChildren<Renderer>(true);
            if (modelRenderers.Length == 0 || !WithinRuntimeBudget(modelRenderers))
            {
                SafeDestroy(model);
                return false;
            }

            DisableImportedColliders(model);
            ConfigureRenderers(modelRenderers);
            NormalizeModel(model.transform, visualRig, modelRenderers);
            HideProceduralBomberBody(visualRig, model.transform);

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
                if (candidate.name == "Optional Meshy Model") return true;
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

        private static GameObject ResolveBomberPrefab()
        {
            if (registeredBomberPrefab != null) return registeredBomberPrefab;
            for (var i = 0; i < ResourceCandidates.Length; i++)
            {
                var candidate = Resources.Load<GameObject>(ResourceCandidates[i]);
                if (candidate != null) return candidate;
            }

#if UNITY_EDITOR
            var editorCandidates = new[]
            {
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/AcornBomber_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/AcornBomberOptimized/model.fbx",
                "Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber_Optimized.prefab",
                "Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber_Optimized.fbx",
                "Assets/ExternalAssets/Meshy/AcornBomber/AcornBomber.fbx"
            };
            for (var i = 0; i < editorCandidates.Length; i++)
            {
                var candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(editorCandidates[i]);
                if (candidate != null) return candidate;
            }
#endif
            return null;
        }

        private static bool WithinRuntimeBudget(Renderer[] renderers)
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
            Debug.LogWarning($"Optional Acorn Bomber exceeds mobile budget ({triangles} triangles, " +
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

        private static void NormalizeModel(Transform model, Transform rig, Renderer[] renderers)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.localScale = Vector3.one;
            if (!TryGetBounds(renderers, out var bounds) || bounds.size.y < 0.001f) return;

            var uniformScale = Mathf.Clamp(TargetModelHeight / bounds.size.y, 0.01f, 25f);
            model.localScale = Vector3.one * uniformScale;
            if (!TryGetBounds(renderers, out bounds)) return;
            var center = rig.InverseTransformPoint(bounds.center);
            model.localPosition += new Vector3(-center.x, TargetModelHeight * 0.5f - center.y, -center.z);
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

        private static void HideProceduralBomberBody(Transform rig, Transform externalModel)
        {
            var renderers = rig.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].transform.IsChildOf(externalModel)) continue;
                var name = renderers[i].gameObject.name;
                var keepAsTarget = name == "Weak Point Core" || name == "Core Petal" || name == "Fuse Spark";
                renderers[i].enabled = keepAsTarget;
            }
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
    }
}
