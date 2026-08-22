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

            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Color.Lerp(color, new Color(1f, 0.48f, 0.62f), 0.35f);
            glow.intensity = defeated ? 1.85f : 0.85f;
            glow.range = defeated ? 3.1f : 1.55f;
            glow.shadows = LightShadows.None;
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

        private void Update()
        {
            if (glow == null || glowLifetime <= 0f) return;
            glowAge += Time.deltaTime;
            glow.intensity *= Mathf.Clamp01(1f - glowAge / glowLifetime);
            if (glowAge >= glowLifetime) glow.enabled = false;
        }
    }
}
