using UnityEngine;
using UnityEngine.Rendering;

namespace BlossomBreach
{
    /// <summary>Short-lived, mesh-petal hit effect with no texture or billboard dependency.</summary>
    [DisallowMultipleComponent]
    public sealed class FlowerBurst : MonoBehaviour
    {
        private Light glow;
        private float glowLifetime;
        private float glowAge;
        private Transform pulseRing;
        private float ringLifetime;

        public static void Emit(Vector3 position, Color color, bool defeated = false)
        {
            var effect = new GameObject(defeated ? "Final Purification Burst" : "Kindness Impact Burst");
            effect.transform.position = position;
            effect.AddComponent<FlowerBurst>().Play(color, defeated);
        }

        public void Play(Color color, bool defeated = false)
        {
            var petals = ConfigureEmitter(gameObject, ProceduralGeometry.Petal, color,
                defeated ? 20 : 10, defeated, true, 1f);
            var heartObject = new GameObject("Heart Particles");
            heartObject.transform.SetParent(transform, false);
            var heartColor = defeated ? new Color(1f, 0.28f, 0.55f) : Color.Lerp(color, new Color(1f, 0.32f, 0.58f), 0.68f);
            var hearts = ConfigureEmitter(heartObject, ProceduralGeometry.Heart, heartColor,
                defeated ? 18 : 6, defeated, false, 1.16f);
            ConfigureHeartTrails(hearts, heartColor, defeated);
            pulseRing = CreatePulseRing(transform, heartColor, defeated);
            ringLifetime = defeated ? 0.72f : 0.28f;

            if (defeated)
            {
                glow = gameObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = Color.Lerp(color, new Color(1f, 0.48f, 0.62f), 0.35f);
                glow.intensity = 1.45f;
                glow.range = 2.8f;
                glow.shadows = LightShadows.None;
            }
            glowLifetime = defeated ? 0.66f : 0.24f;
            glowAge = 0f;
            petals.Play(true);
            hearts.Play(true);
        }

        private static ParticleSystem ConfigureEmitter(GameObject host, Mesh mesh, Color color, int count,
            bool finalPurification, bool destroyRoot, float sizeMultiplier)
        {
            var particles = host.GetComponent<ParticleSystem>();
            if (particles == null) particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.duration = finalPurification ? 1.15f : 0.52f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = finalPurification
                ? new ParticleSystem.MinMaxCurve(0.72f, 1.28f)
                : new ParticleSystem.MinMaxCurve(0.34f, 0.66f);
            main.startSpeed = finalPurification
                ? new ParticleSystem.MinMaxCurve(2.3f, 4.7f)
                : new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
            main.startSize = finalPurification
                ? new ParticleSystem.MinMaxCurve(0.16f * sizeMultiplier, 0.31f * sizeMultiplier)
                : new ParticleSystem.MinMaxCurve(0.10f * sizeMultiplier, 0.20f * sizeMultiplier);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = finalPurification ? 0.42f : 0.28f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = finalPurification ? 28 : 16;
            main.stopAction = destroyRoot ? ParticleSystemStopAction.Destroy : ParticleSystemStopAction.None;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = finalPurification ? 0.26f : 0.11f;
            shape.radiusThickness = 1f;

            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-3.2f, 3.2f);
            rotation.y = new ParticleSystem.MinMaxCurve(-4.5f, 4.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.22f, 0.24f, 1f));

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = ProceduralPalette.Get(color, true);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return particles;
        }

        private static void ConfigureHeartTrails(ParticleSystem particles, Color color, bool finalPurification)
        {
            var trails = particles.trails;
            trails.enabled = true;
            trails.ratio = finalPurification ? 0.62f : 0.30f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(finalPurification ? 0.28f : 0.14f);
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = true;
            trails.worldSpace = true;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = ProceduralPalette.Get(Color.Lerp(color, Color.white, 0.28f), true);
        }

        private static Transform CreatePulseRing(Transform parent, Color color, bool finalPurification)
        {
            var ring = new GameObject("Purification Ring").transform;
            ring.SetParent(parent, false);
            var count = finalPurification ? 12 : 8;
            var radius = finalPurification ? 0.34f : 0.18f;
            for (var i = 0; i < count; i++)
            {
                var angle = i * Mathf.PI * 2f / count;
                ProceduralCatFactory.MeshPart("Ring Heart", ProceduralGeometry.Heart, ring,
                    new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f),
                    Vector3.one * (finalPurification ? 0.13f : 0.09f),
                    Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f), color, true);
            }
            ring.localScale = Vector3.one * 0.35f;
            return ring;
        }

        private void Update()
        {
            glowAge += Time.deltaTime;
            if (pulseRing != null && ringLifetime > 0f)
            {
                var progress = Mathf.Clamp01(glowAge / ringLifetime);
                pulseRing.localScale = Vector3.one * Mathf.Lerp(0.35f, 2.7f, 1f - (1f - progress) * (1f - progress));
                if (progress >= 1f) pulseRing.gameObject.SetActive(false);
            }
            if (glow == null || glowLifetime <= 0f) return;
            glow.intensity = 1.45f * Mathf.Clamp01(1f - glowAge / glowLifetime);
            if (glowAge >= glowLifetime) glow.enabled = false;
        }
    }
}
