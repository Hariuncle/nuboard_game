using UnityEngine;

namespace BlossomBreach
{
    /// <summary>Creates the playable meadow backdrop from reusable Unity primitives.</summary>
    [DisallowMultipleComponent]
    public sealed class MeadowEnvironment : MonoBehaviour
    {
        private const string GeneratedName = "Generated Meadow";

        public static GameObject Build()
        {
            return Build(null);
        }

        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("Meadow Environment");
            root.transform.SetParent(parent, false);
            root.AddComponent<MeadowEnvironment>();
            return root;
        }

        private void Awake()
        {
            BuildContents();
        }

        private void BuildContents()
        {
            if (transform.Find(GeneratedName) != null) return;
            var generated = new GameObject(GeneratedName).transform;
            generated.SetParent(transform, false);

            BuildGround(generated);
            BuildPonds(generated);
            BuildFlowerClusters(generated);
            BuildGardenArch(generated);
            BuildBoundaryShrubs(generated);
            ConfigureAtmosphere();
        }

        private static void BuildGround(Transform parent)
        {
            ProceduralCatFactory.Part("Meadow Ground", PrimitiveType.Cube, parent,
                new Vector3(0f, -0.28f, 8f), new Vector3(22f, 0.45f, 34f),
                new Color(0.36f, 0.67f, 0.27f), false, Quaternion.identity, true);
            ProceduralCatFactory.Part("Soft Path", PrimitiveType.Cube, parent,
                new Vector3(0f, -0.045f, 7f), new Vector3(4.5f, 0.025f, 28f),
                new Color(0.78f, 0.69f, 0.47f));

            for (var i = 0; i < 18; i++)
            {
                var z = -5f + i * 1.55f;
                var x = Mathf.Sin(i * 2.19f) * 1.25f;
                ProceduralCatFactory.Part("Path Stone", PrimitiveType.Sphere, parent,
                    new Vector3(x, 0.005f, z), new Vector3(0.54f, 0.07f, 0.38f),
                    Color.Lerp(new Color(0.72f, 0.67f, 0.56f), Color.white, i % 3 * 0.05f));
            }
        }

        private static void BuildPonds(Transform parent)
        {
            BuildPond(parent, new Vector3(-6.6f, -0.01f, 7f), new Vector3(3.4f, 0.04f, 2.2f));
            BuildPond(parent, new Vector3(6.9f, -0.01f, 15f), new Vector3(2.7f, 0.04f, 3.6f));
        }

        private static void BuildPond(Transform parent, Vector3 position, Vector3 scale)
        {
            ProceduralCatFactory.Part("Pond", PrimitiveType.Cylinder, parent, position, scale,
                new Color(0.20f, 0.65f, 0.67f), true);
            for (var i = 0; i < 7; i++)
            {
                var angle = i * Mathf.PI * 2f / 7f;
                var radius = new Vector3(scale.x * 0.56f, 0f, scale.z * 0.56f);
                ProceduralCatFactory.Part("Pond Stone", PrimitiveType.Sphere, parent,
                    position + new Vector3(Mathf.Cos(angle) * radius.x, 0.03f, Mathf.Sin(angle) * radius.z),
                    new Vector3(0.67f, 0.18f, 0.55f), new Color(0.53f, 0.57f, 0.47f));
            }
            for (var i = 0; i < 3; i++)
            {
                var offset = new Vector3((i - 1) * 0.66f, 0.09f, Mathf.Sin(i * 3f) * 0.42f);
                ProceduralCatFactory.Part("Lily Pad", PrimitiveType.Cylinder, parent, position + offset,
                    new Vector3(0.46f, 0.025f, 0.46f), new Color(0.22f, 0.53f, 0.25f));
            }
        }

        private static void BuildFlowerClusters(Transform parent)
        {
            var flowerColors = new[]
            {
                new Color(1f, 0.39f, 0.56f), new Color(1f, 0.78f, 0.23f),
                new Color(0.64f, 0.43f, 0.91f), new Color(1f, 0.66f, 0.82f),
                new Color(0.42f, 0.72f, 1f)
            };
            var random = new System.Random(1837);
            for (var cluster = 0; cluster < 15; cluster++)
            {
                var side = cluster % 2 == 0 ? -1f : 1f;
                var center = new Vector3(side * (3.5f + (float)random.NextDouble() * 6.1f), 0f,
                    -6f + (float)random.NextDouble() * 29f);
                var count = 3 + random.Next(4);
                for (var i = 0; i < count; i++)
                {
                    var offset = new Vector3(((float)random.NextDouble() - 0.5f) * 1.6f, 0f,
                        ((float)random.NextDouble() - 0.5f) * 1.4f);
                    BuildFlower(parent, center + offset, flowerColors[(cluster + i) % flowerColors.Length],
                        0.72f + (float)random.NextDouble() * 0.45f);
                }
            }
        }

        private static void BuildFlower(Transform parent, Vector3 position, Color color, float size)
        {
            var stemHeight = 0.35f * size;
            ProceduralCatFactory.Part("Flower Stem", PrimitiveType.Capsule, parent,
                position + Vector3.up * stemHeight * 0.5f,
                new Vector3(0.035f, stemHeight * 0.5f, 0.035f), new Color(0.22f, 0.53f, 0.21f));
            var head = position + Vector3.up * stemHeight;
            for (var p = 0; p < 5; p++)
            {
                var a = p * Mathf.PI * 0.4f;
                ProceduralCatFactory.MeshPart("Flower Petal", ProceduralGeometry.Petal, parent,
                    head + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f * size,
                    new Vector3(0.24f, 0.12f, 0.18f) * size,
                    Quaternion.Euler(90f, -a * Mathf.Rad2Deg, 0f), color);
            }
            ProceduralCatFactory.Part("Flower Center", PrimitiveType.Sphere, parent, head,
                Vector3.one * (0.13f * size), new Color(1f, 0.82f, 0.23f), true);
        }

        private static void BuildGardenArch(Transform parent)
        {
            var arch = new GameObject("Flower Gate").transform;
            arch.SetParent(parent, false);
            arch.localPosition = new Vector3(0f, 0f, 22.5f);
            var stone = new Color(0.76f, 0.72f, 0.63f);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var y = 0; y < 5; y++)
                {
                    ProceduralCatFactory.Part("Arch Stone", PrimitiveType.Sphere, arch,
                        new Vector3(side * 2.55f, 0.42f + y * 0.72f, 0f),
                        new Vector3(0.72f, 0.48f, 0.58f), Color.Lerp(stone, Color.white, y % 2 * 0.08f));
                }
            }
            for (var i = 0; i < 9; i++)
            {
                var angle = Mathf.Lerp(0f, Mathf.PI, i / 8f);
                var p = new Vector3(Mathf.Cos(angle) * 2.55f, 3.33f + Mathf.Sin(angle) * 1.70f, 0f);
                ProceduralCatFactory.Part("Arch Crown", PrimitiveType.Sphere, arch, p,
                    new Vector3(0.64f, 0.43f, 0.58f), Color.Lerp(stone, Color.white, i % 2 * 0.06f));
            }

            var vine = new Color(0.18f, 0.48f, 0.24f);
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f;
                ProceduralCatFactory.MeshPart("Arch Leaf", ProceduralGeometry.Petal, arch,
                    new Vector3(Mathf.Cos(angle) * 2.85f, 3.2f + Mathf.Sin(angle) * 1.92f, -0.42f),
                    new Vector3(0.34f, 0.56f, 0.20f), Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg), vine);
            }
        }

        private static void BuildBoundaryShrubs(Transform parent)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 9; i++)
                {
                    var z = -5f + i * 3.5f;
                    var green = Color.Lerp(new Color(0.16f, 0.45f, 0.22f),
                        new Color(0.46f, 0.68f, 0.22f), i % 3 * 0.18f);
                    ProceduralCatFactory.Part("Meadow Shrub", PrimitiveType.Sphere, parent,
                        new Vector3(side * (9.2f + Mathf.Sin(i) * 0.5f), 0.35f, z),
                        new Vector3(1.45f, 0.78f, 1.15f), green);
                }
            }
        }

        private static void ConfigureAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.009f;
            RenderSettings.fogColor = new Color(0.38f, 0.32f, 0.52f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.34f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.43f, 0.48f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.18f, 0.23f);

            var lights = Object.FindObjectsByType<Light>();
            var hasSun = false;
            var hasRim = false;
            foreach (var candidate in lights)
            {
                if (candidate.name == "Meadow Rim Light") hasRim = true;
                else if (candidate.type == LightType.Directional) hasSun = true;
            }

            if (!hasSun)
            {
                var sun = new GameObject("Meadow Sun", typeof(Light));
                sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                var light = sun.GetComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.82f, 0.57f);
                light.intensity = 1.28f;
                light.shadows = LightShadows.Soft;
            }

            if (!hasRim)
            {
                var rim = new GameObject("Meadow Rim Light", typeof(Light));
                rim.transform.rotation = Quaternion.Euler(24f, 148f, 0f);
                var light = rim.GetComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.42f, 0.34f, 0.92f);
                light.intensity = 0.52f;
                light.shadows = LightShadows.None;
            }
        }
    }
}
