using System.Collections.Generic;
using UnityEngine;

namespace BlossomBreach
{
    /// <summary>Additive presentation motion that never competes with EnemyActor's world movement.</summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralCatMotion : MonoBehaviour
    {
        private readonly List<AnimatedPart> ears = new();
        private readonly List<AnimatedPart> tails = new();
        private readonly List<AnimatedScale> coreParts = new();
        private readonly List<AnimatedScale> happyEyes = new();
        private readonly List<AnimatedScale> cheeks = new();
        private readonly List<Renderer> renderers = new();
        private Transform rig;
        private Transform shield;
        private EnemyActor actor;
        private OptionalEnemyModelAdapter externalModel;
        private EnemyKind kind;
        private Vector3 baseScale;
        private float phase;
        private float recoil;
        private float purificationTime = -1f;
        private float shieldBreakTime = -1f;

        public void Configure(Transform visualRig, EnemyKind enemyKind)
        {
            rig = visualRig;
            kind = enemyKind;
            baseScale = rig.localScale;
            phase = Random.value * Mathf.PI * 2f;
            actor = GetComponent<EnemyActor>();
            externalModel = GetComponent<OptionalEnemyModelAdapter>();
            CollectRigParts();
        }

        public void ReactToHit(bool defeated)
        {
            ReactToHit(defeated, false);
        }

        public void ReactToHit(bool defeated, bool shieldBroken)
        {
            recoil = defeated ? 1.35f : 1f;
            if (shieldBroken && shield != null) shieldBreakTime = 0f;
            if (!defeated) externalModel?.PlayHit();
            if (!defeated)
            {
                var tint = kind == EnemyKind.Boss ? new Color(1f, 0.18f, 0.56f) : new Color(1f, 0.72f, 0.22f);
                FlowerBurst.Emit(transform.position + Vector3.up * 1.2f - transform.forward * 0.5f, tint, false);
            }
        }

        private void Awake()
        {
            actor = GetComponent<EnemyActor>();
        }

        private void Update()
        {
            if (rig == null) return;
            if (actor == null) actor = GetComponent<EnemyActor>();

            if (actor != null && actor.IsDefeated)
            {
                AnimatePurification();
                return;
            }

            var speed = kind switch
            {
                EnemyKind.Fast => 11f,
                EnemyKind.Boss => 4.2f,
                EnemyKind.Bomber => 5.2f,
                _ => 7f
            };
            var amplitude = kind == EnemyKind.Boss ? 0.035f : kind == EnemyKind.Fast ? 0.09f : 0.06f;
            var cycle = Time.time * speed + phase;
            var step = Mathf.Abs(Mathf.Sin(cycle));
            var waddle = Mathf.Sin(cycle * 0.5f);

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 5.8f);
            rig.localPosition = new Vector3(0f, step * amplitude, recoil * 0.10f);
            rig.localRotation = Quaternion.Euler(-recoil * 13f, waddle * (kind == EnemyKind.Fast ? 6f : 2.5f),
                waddle * (kind == EnemyKind.Bomber ? 6f : 2.2f));
            rig.localScale = Vector3.Scale(baseScale, new Vector3(1f + recoil * 0.04f, 1f - recoil * 0.06f, 1f));

            AnimateBossCore(cycle);
            AnimateShieldBreak();

            for (var i = 0; i < ears.Count; i++)
                ears[i].Transform.localRotation = ears[i].BaseRotation *
                    Quaternion.Euler(Mathf.Sin(cycle * 0.7f + i) * 4.5f, 0f, 0f);
            for (var i = 0; i < tails.Count; i++)
                tails[i].Transform.localRotation = tails[i].BaseRotation *
                    Quaternion.Euler(0f, Mathf.Sin(cycle * 0.45f + i) * 7f, 0f);
        }

        private void AnimatePurification()
        {
            if (purificationTime < 0f)
            {
                purificationTime = 0f;
                ApplyPurifiedLook();
                FlowerBurst.Emit(transform.position + Vector3.up * 1.25f, new Color(1f, 0.72f, 0.28f), true);
            }
            purificationTime += Time.deltaTime;
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 3f);
            var t = Mathf.Clamp01(purificationTime / 1.05f);
            ApplyPurifiedPose(t);
        }

        private void LateUpdate()
        {
            if (purificationTime >= 0f && actor != null && actor.IsDefeated)
                ApplyPurifiedPose(Mathf.Clamp01(purificationTime / 1.05f));
        }

        private void ApplyPurifiedPose(float t)
        {
            var settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.58f));
            var joyBounce = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 0.055f;
            var gentleWag = Mathf.Sin(t * Mathf.PI * 5f) * (1f - t) * 5f;

            // EnemyActor retains its gameplay retirement motion. Keep the pet presentation upright in world space.
            rig.position = transform.position + Vector3.up * (-0.27f * settle + joyBounce);
            rig.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, gentleWag);
            var farewell = Mathf.Lerp(1f, 0.76f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, t)));
            rig.localScale = baseScale * (Mathf.Lerp(1f, 0.88f, settle) * farewell);

            for (var i = 0; i < happyEyes.Count; i++)
                happyEyes[i].Transform.localScale = Vector3.Scale(happyEyes[i].BaseScale,
                    new Vector3(1.14f, 0.24f, 0.72f));
            for (var i = 0; i < cheeks.Count; i++)
                cheeks[i].Transform.localScale = cheeks[i].BaseScale * Mathf.Lerp(1f, 1.55f, settle);
        }

        private void ApplyPurifiedLook()
        {
            foreach (var importedAnimator in rig.GetComponentsInChildren<Animator>(true))
                importedAnimator.enabled = false;

            var kindnessTint = new Color(1f, 0.78f, 0.72f);
            for (var i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterial == null) continue;
                var material = renderer.sharedMaterial;
                var baseColor = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                var name = renderer.gameObject.name;
                var purified = name == "Pupil" ? new Color(0.08f, 0.05f, 0.10f)
                    : name == "Cheek" ? new Color(1f, 0.35f, 0.52f)
                    : Color.Lerp(baseColor, kindnessTint, 0.46f);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                if (material.HasProperty("_BaseColor")) block.SetColor("_BaseColor", purified);
                if (material.HasProperty("_Color")) block.SetColor("_Color", purified);
                if (material.HasProperty("_EmissionColor") && (name.Contains("Core") || name.Contains("Spark")))
                    block.SetColor("_EmissionColor", new Color(1f, 0.28f, 0.54f) * 3.2f);
                renderer.SetPropertyBlock(block);
            }

            ProceduralCatFactory.MeshPart("Purified Heart", ProceduralGeometry.Heart, rig,
                new Vector3(0f, kind == EnemyKind.Boss ? 2.95f : 2.48f, -0.22f),
                Vector3.one * (kind == EnemyKind.Boss ? 0.48f : 0.34f), Quaternion.identity,
                new Color(1f, 0.25f, 0.50f), true);
        }

        private void AnimateBossCore(float cycle)
        {
            if (kind != EnemyKind.Boss || coreParts.Count == 0 || actor == null) return;
            var target = actor.IsBossCoreOpen ? 1f + Mathf.Sin(cycle * 0.82f) * 0.14f : 0.54f;
            for (var i = 0; i < coreParts.Count; i++)
            {
                var part = coreParts[i];
                part.Transform.localScale = Vector3.Lerp(part.Transform.localScale,
                    part.BaseScale * target, Time.deltaTime * 10f);
            }
        }

        private void AnimateShieldBreak()
        {
            if (shield == null || shieldBreakTime < 0f) return;
            shieldBreakTime += Time.deltaTime;
            var t = Mathf.Clamp01(shieldBreakTime / 0.42f);
            shield.localPosition = new Vector3(-t * 0.70f, t * 0.55f, t * 0.12f);
            shield.localRotation = Quaternion.Euler(t * 35f, t * 80f, t * 55f);
            shield.localScale = Vector3.one * (1f - t * 0.82f);
            if (t >= 1f)
            {
                foreach (var collider in shield.GetComponentsInChildren<Collider>()) collider.enabled = false;
                shield.gameObject.SetActive(false);
                shieldBreakTime = -1f;
            }
        }

        private void CollectRigParts()
        {
            ears.Clear();
            tails.Clear();
            coreParts.Clear();
            happyEyes.Clear();
            cheeks.Clear();
            renderers.Clear();
            foreach (var child in rig.GetComponentsInChildren<Transform>(true))
            {
                var importedBone = OptionalEnemyModelAdapter.IsExternalTransform(child);
                if (!importedBone && child.name.Contains("Ear")) ears.Add(new AnimatedPart(child));
                if (!importedBone && (child.name.Contains("Tail") || child.name.Contains("Scarf")))
                    tails.Add(new AnimatedPart(child));
                if (!importedBone && child.name.Contains("Core")) coreParts.Add(new AnimatedScale(child));
                if (!importedBone && (child.name == "Eye White" || child.name == "Iris" ||
                                      child.name == "Pupil" || child.name == "Eye Glint"))
                    happyEyes.Add(new AnimatedScale(child));
                if (!importedBone && child.name == "Cheek") cheeks.Add(new AnimatedScale(child));
                if (child.name == "Rose Shield") shield = child;
            }
            renderers.AddRange(rig.GetComponentsInChildren<Renderer>(true));
        }

        private readonly struct AnimatedPart
        {
            public AnimatedPart(Transform transform)
            {
                Transform = transform;
                BaseRotation = transform.localRotation;
            }

            public Transform Transform { get; }
            public Quaternion BaseRotation { get; }
        }

        private readonly struct AnimatedScale
        {
            public AnimatedScale(Transform transform)
            {
                Transform = transform;
                BaseScale = transform.localScale;
            }

            public Transform Transform { get; }
            public Vector3 BaseScale { get; }
        }
    }
}
