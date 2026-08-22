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
        private static readonly Dictionary<Material, Material> TunedMaterials = new();
        private static int activeExternalInstances;
        private static Material contactShadowMaterial;

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
            ConfigureRenderers(modelRenderers, kind);
            NormalizeModel(model.transform, visualRig, modelRenderers, spec.TargetHeight);
            HideProceduralBody(visualRig, model.transform, kind);
            CreateContactShadow(enemyRoot.transform, kind);

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

        private static void ConfigureRenderers(Renderer[] renderers, EnemyKind kind)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
                var materials = renderers[i].sharedMaterials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = GetTunedMaterial(materials[materialIndex], kind);
                renderers[i].sharedMaterials = materials;
                if (renderers[i] is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = false;
                    skinned.quality = SkinQuality.Bone2;
                }
            }
        }

        private static Material GetTunedMaterial(Material source, EnemyKind kind)
        {
            if (source == null) return null;
            if (TunedMaterials.TryGetValue(source, out var cached) && cached != null) return cached;

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var tuned = urp != null ? new Material(urp) : new Material(source.shader);
            tuned.name = $"{source.name} Premium";
            tuned.enableInstancing = true;
            tuned.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;

            var baseTexture = ReadTexture(source, "_BaseMap", "_MainTex");
            if (tuned.HasProperty("_BaseMap") && baseTexture != null) tuned.SetTexture("_BaseMap", baseTexture);
            if (tuned.HasProperty("_MainTex") && baseTexture != null) tuned.SetTexture("_MainTex", baseTexture);
            CopyTexture(source, tuned, "_BumpMap", "_BumpMap");
            CopyTexture(source, tuned, "_MetallicGlossMap", "_MetallicGlossMap");
            CopyTexture(source, tuned, "_OcclusionMap", "_OcclusionMap");

            var sourceColor = ReadColor(source);
            var grade = kind switch
            {
                EnemyKind.Scout => new Color(1f, 0.96f, 0.88f, 1f),
                EnemyKind.Armored => new Color(0.91f, 0.95f, 1f, 1f),
                EnemyKind.Boss => new Color(0.94f, 0.89f, 1f, 1f),
                _ => new Color(1f, 0.97f, 0.92f, 1f)
            };
            var gradedColor = sourceColor * grade;
            gradedColor.a = sourceColor.a;
            if (tuned.HasProperty("_BaseColor")) tuned.SetColor("_BaseColor", gradedColor);
            if (tuned.HasProperty("_Color")) tuned.SetColor("_Color", gradedColor);

            var smoothness = kind switch
            {
                EnemyKind.Armored => 0.54f,
                EnemyKind.Boss => 0.43f,
                EnemyKind.Scout => 0.38f,
                _ => 0.34f
            };
            if (tuned.HasProperty("_Smoothness")) tuned.SetFloat("_Smoothness", smoothness);
            if (tuned.HasProperty("_Glossiness")) tuned.SetFloat("_Glossiness", smoothness);
            if (tuned.HasProperty("_BumpScale")) tuned.SetFloat("_BumpScale", 0.82f);
            if (tuned.HasProperty("_OcclusionStrength")) tuned.SetFloat("_OcclusionStrength", 0.92f);
            if (tuned.HasProperty("_SpecularHighlights")) tuned.SetFloat("_SpecularHighlights", 1f);
            if (tuned.HasProperty("_EnvironmentReflections")) tuned.SetFloat("_EnvironmentReflections", 1f);

            TunedMaterials[source] = tuned;
            return tuned;
        }

        private static Texture ReadTexture(Material source, string primary, string fallback)
        {
            if (source.HasProperty(primary))
            {
                var texture = source.GetTexture(primary);
                if (texture != null) return texture;
            }
            return source.HasProperty(fallback) ? source.GetTexture(fallback) : source.mainTexture;
        }

        private static Color ReadColor(Material source)
        {
            if (source.HasProperty("_BaseColor")) return source.GetColor("_BaseColor");
            if (source.HasProperty("_Color")) return source.GetColor("_Color");
            return source.color;
        }

        private static void CopyTexture(Material source, Material target, string targetProperty,
            string sourceProperty)
        {
            if (!target.HasProperty(targetProperty) || !source.HasProperty(sourceProperty)) return;
            var texture = source.GetTexture(sourceProperty);
            if (texture != null) target.SetTexture(targetProperty, texture);
        }

        private static void CreateContactShadow(Transform enemyRoot, EnemyKind kind)
        {
            var shadowRoot = new GameObject("Soft Contact Shadow").transform;
            shadowRoot.SetParent(enemyRoot, false);
            shadowRoot.localPosition = new Vector3(0f, 0.025f, 0.03f);
            var size = kind == EnemyKind.Boss ? 1.35f : kind == EnemyKind.Armored ? 1.05f : 0.88f;
            CreateShadowDisc(shadowRoot, new Vector3(size, 0.012f, size * 0.63f));
            CreateShadowDisc(shadowRoot, new Vector3(size * 0.66f, 0.014f, size * 0.40f));
        }

        private static void CreateShadowDisc(Transform parent, Vector3 scale)
        {
            var disc = ProceduralCatFactory.Part("Contact Shadow Disc", PrimitiveType.Cylinder, parent,
                Vector3.zero, scale, new Color(0.12f, 0.08f, 0.20f));
            var renderer = disc.GetComponent<Renderer>();
            renderer.sharedMaterial = GetContactShadowMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material GetContactShadowMaterial()
        {
            if (contactShadowMaterial != null) return contactShadowMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            contactShadowMaterial = new Material(shader)
            {
                name = "Shared Soft Contact Shadow",
                enableInstancing = true,
                hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor,
                renderQueue = (int)RenderQueue.Transparent
            };
            var color = new Color(0.10f, 0.06f, 0.16f, 0.18f);
            if (contactShadowMaterial.HasProperty("_BaseColor"))
                contactShadowMaterial.SetColor("_BaseColor", color);
            if (contactShadowMaterial.HasProperty("_Color")) contactShadowMaterial.SetColor("_Color", color);
            if (contactShadowMaterial.HasProperty("_Surface")) contactShadowMaterial.SetFloat("_Surface", 1f);
            if (contactShadowMaterial.HasProperty("_Blend")) contactShadowMaterial.SetFloat("_Blend", 0f);
            if (contactShadowMaterial.HasProperty("_SrcBlend"))
                contactShadowMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (contactShadowMaterial.HasProperty("_DstBlend"))
                contactShadowMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (contactShadowMaterial.HasProperty("_ZWrite")) contactShadowMaterial.SetFloat("_ZWrite", 0f);
            if (contactShadowMaterial.HasProperty("_Cull")) contactShadowMaterial.SetFloat("_Cull", 0f);
            contactShadowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return contactShadowMaterial;
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
