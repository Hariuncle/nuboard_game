using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlossomBreach
{
    /// <summary>Builds readable, collider-aware cat enemies entirely from Unity meshes.</summary>
    public static class ProceduralCatFactory
    {
        public static GameObject Create(EnemyKind kind, Transform parent)
        {
            var root = new GameObject($"{kind} Cat");
            root.transform.SetParent(parent, false);
            var rig = new GameObject("Visual Rig").transform;
            rig.SetParent(root.transform, false);

            var bodyColor = kind switch
            {
                EnemyKind.Scout => new Color(0.92f, 0.70f, 0.43f),
                EnemyKind.Fast => new Color(0.48f, 0.78f, 0.85f),
                EnemyKind.Armored => new Color(0.52f, 0.58f, 0.66f),
                EnemyKind.Bomber => new Color(0.84f, 0.36f, 0.16f),
                EnemyKind.Boss => new Color(0.25f, 0.16f, 0.28f),
                _ => Color.gray
            };
            var accentColor = kind switch
            {
                EnemyKind.Scout => new Color(0.32f, 0.66f, 0.26f),
                EnemyKind.Fast => new Color(0.18f, 0.64f, 0.92f),
                EnemyKind.Armored => new Color(0.95f, 0.57f, 0.28f),
                EnemyKind.Bomber => new Color(1f, 0.62f, 0.08f),
                EnemyKind.Boss => new Color(0.96f, 0.18f, 0.55f),
                _ => new Color(0.55f, 0.72f, 0.80f)
            };

            var size = kind switch
            {
                EnemyKind.Fast => 0.82f,
                EnemyKind.Boss => 1.22f,
                _ => 1f
            };
            BuildCat(rig, bodyColor, accentColor, size);

            switch (kind)
            {
                case EnemyKind.Scout:
                    AddLeafHood(rig);
                    AddFlowerCore(rig, new Vector3(0f, 1.04f, -0.57f), 0.20f,
                        new Color(0.65f, 1f, 0.35f), false);
                    break;
                case EnemyKind.Fast:
                    AddFastAccessories(rig);
                    AddFlowerCore(rig, new Vector3(0f, 0.88f, -0.50f), 0.15f,
                        new Color(0.36f, 0.95f, 1f), true);
                    break;
                case EnemyKind.Armored:
                    AddLeafArmor(rig);
                    AddFlowerCore(rig, new Vector3(0f, 1.30f, -0.61f), 0.17f,
                        new Color(1f, 0.73f, 0.25f), true);
                    AddRoseShield(rig);
                    break;
                case EnemyKind.Bomber:
                    AddBomberAccessories(rig);
                    AddFlowerCore(rig, new Vector3(0f, 1.04f, -0.59f), 0.18f,
                        new Color(1f, 0.78f, 0.12f), true);
                    break;
                case EnemyKind.Boss:
                    AddThornCrown(rig);
                    AddFlowerCore(rig, new Vector3(0f, 1.20f, -0.73f), 0.28f,
                        new Color(1f, 0.18f, 0.56f), true);
                    AddBossMantle(rig);
                    break;
            }

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1.03f, 0f);
            capsule.radius = kind == EnemyKind.Boss ? 0.66f : 0.53f;
            capsule.height = kind == EnemyKind.Boss ? 2.25f : 1.9f;
            if (kind == EnemyKind.Bomber)
                OptionalEnemyModelAdapter.TryAttachBomber(root, rig);
            root.AddComponent<ProceduralCatMotion>().Configure(rig, kind);
            return root;
        }

        private static void BuildCat(Transform root, Color fur, Color accent, float size)
        {
            Part("Body", PrimitiveType.Capsule, root, new Vector3(0f, 0.93f, 0.06f) * size,
                new Vector3(0.68f, 0.78f, 0.55f) * size, fur);
            Part("Head", PrimitiveType.Sphere, root, new Vector3(0f, 1.70f, -0.03f) * size,
                new Vector3(0.91f, 0.78f, 0.75f) * size, fur);

            var earInside = Color.Lerp(fur, new Color(1f, 0.48f, 0.53f), 0.55f);
            for (var side = -1; side <= 1; side += 2)
            {
                MeshPart("Ear", ProceduralGeometry.Cone, root,
                    new Vector3(0.37f * side, 2.22f, -0.01f) * size,
                    new Vector3(0.34f, 0.56f, 0.25f) * size,
                    Quaternion.Euler(0f, 0f, -8f * side), fur);
                MeshPart("Inner Ear", ProceduralGeometry.Cone, root,
                    new Vector3(0.37f * side, 2.19f, -0.22f) * size,
                    new Vector3(0.18f, 0.36f, 0.08f) * size,
                    Quaternion.Euler(0f, 0f, -8f * side), earInside);

                Part("Eye White", PrimitiveType.Sphere, root,
                    new Vector3(0.24f * side, 1.79f, -0.67f) * size,
                    new Vector3(0.20f, 0.23f, 0.075f) * size, new Color(1f, 0.97f, 0.89f));
                Part("Iris", PrimitiveType.Sphere, root,
                    new Vector3(0.23f * side, 1.78f, -0.73f) * size,
                    new Vector3(0.125f, 0.165f, 0.045f) * size, Color.Lerp(accent, Color.white, 0.12f), true);
                Part("Pupil", PrimitiveType.Sphere, root,
                    new Vector3(0.23f * side, 1.77f, -0.77f) * size,
                    new Vector3(0.065f, 0.125f, 0.028f) * size, new Color(0.035f, 0.025f, 0.055f));
                Part("Eye Glint", PrimitiveType.Sphere, root,
                    new Vector3(0.19f * side, 1.86f, -0.80f) * size,
                    Vector3.one * (0.042f * size), Color.white, true);
                Part("Eyebrow", PrimitiveType.Capsule, root,
                    new Vector3(0.23f * side, 2.03f, -0.69f) * size,
                    new Vector3(0.026f, 0.17f, 0.025f) * size, Color.Lerp(fur, Color.black, 0.58f), false,
                    Quaternion.Euler(0f, 0f, 84f + side * 7f));
                Part("Muzzle", PrimitiveType.Sphere, root,
                    new Vector3(0.16f * side, 1.53f, -0.63f) * size,
                    new Vector3(0.28f, 0.19f, 0.14f) * size, new Color(1f, 0.86f, 0.67f));
                Part("Cheek", PrimitiveType.Sphere, root,
                    new Vector3(0.34f * side, 1.50f, -0.71f) * size,
                    new Vector3(0.12f, 0.055f, 0.035f) * size, new Color(1f, 0.48f, 0.58f), true);
                Part("Paw", PrimitiveType.Capsule, root,
                    new Vector3(0.32f * side, 0.26f, -0.25f) * size,
                    new Vector3(0.25f, 0.28f, 0.29f) * size, Color.Lerp(fur, Color.white, 0.25f));
                for (var toe = -1; toe <= 1; toe++)
                {
                    Part("Toe Bean", PrimitiveType.Sphere, root,
                        new Vector3((0.32f * side) + toe * 0.065f, 0.22f, -0.50f) * size,
                        new Vector3(0.052f, 0.038f, 0.028f) * size, new Color(0.67f, 0.25f, 0.34f));
                }

                for (var whisker = -1; whisker <= 1; whisker += 2)
                {
                    Part("Whisker", PrimitiveType.Capsule, root,
                        new Vector3(0.40f * side, (1.48f + 0.08f * whisker) * size, -0.69f * size),
                        new Vector3(0.018f, 0.26f, 0.018f) * size, Color.white,
                        false, Quaternion.Euler(0f, 0f, 70f * side + 7f * whisker));
                }
            }

            Part("Nose", PrimitiveType.Sphere, root, new Vector3(0f, 1.58f, -0.76f) * size,
                new Vector3(0.13f, 0.09f, 0.08f) * size, new Color(0.66f, 0.20f, 0.31f));
            for (var side = -1; side <= 1; side += 2)
            {
                Part("Smile", PrimitiveType.Capsule, root, new Vector3(0.075f * side, 1.45f, -0.76f) * size,
                    new Vector3(0.018f, 0.10f, 0.018f) * size, new Color(0.30f, 0.10f, 0.15f), false,
                    Quaternion.Euler(0f, 0f, 42f * side));
            }
            MeshPart("Chest Tuft", ProceduralGeometry.Cone, root, new Vector3(0f, 1.10f, -0.54f) * size,
                new Vector3(0.34f, 0.42f, 0.12f) * size, Quaternion.Euler(180f, 0f, 0f),
                Color.Lerp(fur, Color.white, 0.48f));

            Part("Tail Lower", PrimitiveType.Capsule, root, new Vector3(0.58f, 0.88f, 0.36f) * size,
                new Vector3(0.18f, 0.56f, 0.18f) * size, fur, false, Quaternion.Euler(8f, 0f, -48f));
            Part("Tail Tip", PrimitiveType.Capsule, root, new Vector3(0.91f, 1.29f, 0.38f) * size,
                new Vector3(0.16f, 0.43f, 0.16f) * size, Color.Lerp(fur, accent, 0.42f), false,
                Quaternion.Euler(0f, 0f, -28f));
        }

        private static void AddLeafHood(Transform root)
        {
            var leaf = new Color(0.20f, 0.54f, 0.25f);
            Part("Leaf Hood", PrimitiveType.Sphere, root, new Vector3(0f, 1.75f, 0.15f),
                new Vector3(1.01f, 0.85f, 0.68f), leaf);
            // Re-add the visible face over the hood's front edge.
            Part("Hood Brow", PrimitiveType.Capsule, root, new Vector3(0f, 2.12f, -0.50f),
                new Vector3(0.13f, 0.55f, 0.13f), leaf, false, Quaternion.Euler(0f, 0f, 90f));
            for (var i = -1; i <= 1; i++)
            {
                MeshPart("Hood Leaf", ProceduralGeometry.Cone, root,
                    new Vector3(i * 0.34f, 2.28f - Mathf.Abs(i) * 0.07f, -0.28f),
                    new Vector3(0.22f, 0.47f, 0.15f), Quaternion.Euler(0f, 0f, -18f * i),
                    Color.Lerp(leaf, new Color(0.53f, 0.82f, 0.26f), i == 0 ? 0.55f : 0.2f));
            }
            Part("Hood Heart Clasp", PrimitiveType.Sphere, root, new Vector3(0f, 1.26f, -0.61f),
                new Vector3(0.18f, 0.18f, 0.09f), new Color(0.82f, 0.95f, 0.30f), true);
        }

        private static void AddLeafArmor(Transform root)
        {
            var armor = new Color(0.19f, 0.40f, 0.31f);
            for (var side = -1; side <= 1; side += 2)
            {
                Part("Leaf Pauldron", PrimitiveType.Sphere, root,
                    new Vector3(0.57f * side, 1.15f, 0f), new Vector3(0.50f, 0.28f, 0.48f), armor,
                    false, Quaternion.Euler(0f, 0f, 14f * side));
                MeshPart("Armor Leaf", ProceduralGeometry.Cone, root,
                    new Vector3(0.56f * side, 1.48f, -0.10f), new Vector3(0.24f, 0.45f, 0.18f),
                    Quaternion.Euler(0f, 0f, -35f * side), armor);
            }
            Part("Armor Brow", PrimitiveType.Capsule, root, new Vector3(0f, 2.12f, -0.52f),
                new Vector3(0.09f, 0.55f, 0.08f), new Color(0.73f, 0.80f, 0.82f), true,
                Quaternion.Euler(0f, 0f, 90f));
            for (var side = -1; side <= 1; side += 2)
            {
                Part("Armor Rivet", PrimitiveType.Sphere, root, new Vector3(0.47f * side, 2.12f, -0.57f),
                    Vector3.one * 0.10f, new Color(1f, 0.70f, 0.30f), true);
            }
        }

        private static void AddFastAccessories(Transform root)
        {
            var blue = new Color(0.11f, 0.46f, 0.73f);
            Part("Wind Scarf", PrimitiveType.Capsule, root, new Vector3(0f, 1.24f, 0.05f),
                new Vector3(0.13f, 0.58f, 0.13f), blue, false, Quaternion.Euler(0f, 0f, 90f));
            Part("Trailing Scarf", PrimitiveType.Capsule, root, new Vector3(0.58f, 1.18f, 0.35f),
                new Vector3(0.11f, 0.54f, 0.11f), blue, false, Quaternion.Euler(10f, 0f, 68f));
            for (var side = -1; side <= 1; side += 2)
            {
                MeshPart("Wing Leaf", ProceduralGeometry.Petal, root,
                    new Vector3(0.53f * side, 1.02f, 0.18f), new Vector3(0.32f, 0.58f, 0.16f),
                    Quaternion.Euler(12f, 0f, 55f * side), new Color(0.62f, 0.94f, 0.88f), true);
            }
            MeshPart("Wind Crest", ProceduralGeometry.Cone, root, new Vector3(0f, 2.42f, -0.18f),
                new Vector3(0.20f, 0.52f, 0.15f), Quaternion.Euler(0f, 0f, -24f),
                new Color(0.32f, 0.86f, 1f), true);
        }

        private static void AddBomberAccessories(Transform root)
        {
            var bomb = Part("Pollen Bomb", PrimitiveType.Sphere, root, new Vector3(0.58f, 1.13f, 0.38f),
                Vector3.one * 0.61f, new Color(0.12f, 0.11f, 0.15f));
            Part("Bomb Band", PrimitiveType.Cylinder, bomb.transform, Vector3.zero,
                new Vector3(1.03f, 0.08f, 1.03f), new Color(0.96f, 0.58f, 0.08f), false,
                Quaternion.Euler(90f, 0f, 0f));
            Part("Fuse", PrimitiveType.Capsule, root, new Vector3(0.58f, 1.57f, 0.38f),
                new Vector3(0.055f, 0.24f, 0.055f), new Color(0.32f, 0.20f, 0.10f), false,
                Quaternion.Euler(0f, 0f, 18f));
            Part("Fuse Spark", PrimitiveType.Sphere, root, new Vector3(0.50f, 1.80f, 0.38f),
                Vector3.one * 0.15f, new Color(1f, 0.30f, 0.05f), true);
            for (var side = -1; side <= 1; side += 2)
            {
                MeshPart("Warning Ear Flag", ProceduralGeometry.Cone, root,
                    new Vector3(0.37f * side, 2.35f, -0.10f), new Vector3(0.15f, 0.34f, 0.12f),
                    Quaternion.Euler(0f, 0f, -12f * side), new Color(1f, 0.70f, 0.06f), true);
            }
            Part("Warning Belt", PrimitiveType.Capsule, root, new Vector3(0f, 0.88f, -0.48f),
                new Vector3(0.10f, 0.52f, 0.08f), new Color(0.16f, 0.13f, 0.18f), false,
                Quaternion.Euler(0f, 0f, 90f));
            for (var stripe = -1; stripe <= 1; stripe += 2)
            {
                Part("Warning Stripe", PrimitiveType.Capsule, root,
                    new Vector3(0.18f * stripe, 0.88f, -0.55f), new Vector3(0.035f, 0.12f, 0.025f),
                    new Color(1f, 0.72f, 0.08f), true, Quaternion.Euler(0f, 0f, 38f));
            }
        }

        private static void AddRoseShield(Transform root)
        {
            var shieldRoot = new GameObject("Rose Shield").transform;
            shieldRoot.SetParent(root, false);
            var shield = Part("Rose Shield Zone", PrimitiveType.Cylinder, shieldRoot,
                new Vector3(-0.35f, 0.98f, -0.78f), new Vector3(0.66f, 0.11f, 0.66f),
                new Color(0.26f, 0.45f, 0.27f), false, Quaternion.Euler(90f, 0f, 0f), true);
            var zone = shield.AddComponent<HitZone>();
            zone.shield = true;

            var rose = new Color(0.82f, 0.17f, 0.34f);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI * 0.25f;
                Part("Rose Petal", PrimitiveType.Sphere, shieldRoot,
                    new Vector3(-0.35f + Mathf.Cos(angle) * 0.31f, 0.98f + Mathf.Sin(angle) * 0.31f, -0.91f),
                    new Vector3(0.29f, 0.17f, 0.09f), Color.Lerp(rose, Color.white, i % 2 * 0.08f),
                    false, Quaternion.Euler(0f, 0f, i * 45f));
            }
            Part("Rose Heart", PrimitiveType.Sphere, shieldRoot, new Vector3(-0.35f, 0.98f, -1.01f),
                Vector3.one * 0.28f, new Color(1f, 0.48f, 0.46f), true);
        }

        private static void AddThornCrown(Transform root)
        {
            var vine = new Color(0.18f, 0.40f, 0.19f);
            for (var i = 0; i < 7; i++)
            {
                var angle = i * Mathf.PI * 2f / 7f;
                var p = new Vector3(Mathf.Cos(angle) * 0.54f, 2.54f, Mathf.Sin(angle) * 0.35f);
                Part("Crown Vine", PrimitiveType.Sphere, root, p,
                    new Vector3(0.26f, 0.16f, 0.18f), vine);
                MeshPart("Crown Thorn", ProceduralGeometry.Cone, root, p + Vector3.up * 0.31f,
                    new Vector3(0.13f, 0.38f, 0.13f), Quaternion.Euler(0f, 0f, Mathf.Cos(angle) * 15f),
                    new Color(0.37f, 0.60f, 0.22f));
            }
            Part("Crown Heart", PrimitiveType.Sphere, root, new Vector3(0f, 2.73f, -0.42f),
                new Vector3(0.22f, 0.25f, 0.12f), new Color(1f, 0.16f, 0.52f), true);
        }

        private static void AddBossMantle(Transform root)
        {
            var petal = new Color(0.55f, 0.09f, 0.30f);
            for (var i = 0; i < 9; i++)
            {
                var angle = i * Mathf.PI * 2f / 9f;
                Part("Mantle Petal", PrimitiveType.Sphere, root,
                    new Vector3(Mathf.Cos(angle) * 0.67f, 1.46f + Mathf.Sin(angle) * 0.15f,
                        0.18f + Mathf.Sin(angle) * 0.25f),
                    new Vector3(0.33f, 0.16f, 0.23f), petal, false,
                    Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg));
            }
        }

        private static void AddFlowerCore(Transform root, Vector3 position, float radius, Color color,
            bool emissive)
        {
            var core = Part("Weak Point Core", PrimitiveType.Sphere, root, position, Vector3.one * radius,
                color, emissive, Quaternion.identity, true);
            var zone = core.AddComponent<HitZone>();
            zone.weakPoint = true;

            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.PI / 3f;
                Part("Core Petal", PrimitiveType.Sphere, root,
                    position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), -0.01f) * radius * 1.30f,
                    new Vector3(radius * 0.78f, radius * 0.40f, radius * 0.30f),
                    Color.Lerp(color, Color.white, 0.16f), emissive, Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg));
            }
        }

        internal static GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 position,
            Vector3 scale, Color color, bool emissive = false, Quaternion rotation = default,
            bool keepCollider = false)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            part.transform.localScale = scale;
            ConfigureRenderer(part.GetComponent<Renderer>(), color, emissive);
            if (!keepCollider)
                DisableAndDestroy(part.GetComponent<Collider>());
            return part;
        }

        internal static GameObject MeshPart(string name, Mesh mesh, Transform parent, Vector3 position,
            Vector3 scale, Quaternion rotation, Color color, bool emissive = false)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureRenderer(part.GetComponent<Renderer>(), color, emissive);
            return part;
        }

        internal static void ConfigureRenderer(Renderer renderer, Color color, bool emissive = false)
        {
            renderer.sharedMaterial = ProceduralPalette.Get(color, emissive);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void DisableAndDestroy(Collider collider)
        {
            if (collider == null) return;
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
    }

    internal static class ProceduralPalette
    {
        private static readonly Dictionary<int, Material> Cache = new();

        internal static Material Get(Color color, bool emissive = false)
        {
            var c = (Color32)color;
            var key = c.r | c.g << 8 | c.b << 16 | c.a << 24;
            if (emissive) key ^= 0x5f3759df;
            if (Cache.TryGetValue(key, out var material) && material != null) return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader)
            {
                name = $"Procedural {(emissive ? "Glow " : string.Empty)}{ColorUtility.ToHtmlStringRGB(color)}",
                enableInstancing = true,
                hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", emissive ? 0.78f : 0.46f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.02f);
            if (material.HasProperty("_CoatMask")) material.SetFloat("_CoatMask", emissive ? 0.18f : 0.08f);
            if (emissive && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 3.2f);
            }
            Cache[key] = material;
            return material;
        }
    }

    internal static class ProceduralGeometry
    {
        private static Mesh cone;
        private static Mesh petal;
        private static Mesh heart;

        internal static Mesh Cone => cone != null ? cone : cone = CreateCone();
        internal static Mesh Petal => petal != null ? petal : petal = CreatePetal();
        internal static Mesh Heart => heart != null ? heart : heart = CreateHeart();

        private static Mesh CreateCone()
        {
            const int sides = 12;
            var vertices = new Vector3[sides + 2];
            var triangles = new int[sides * 6];
            vertices[0] = Vector3.up * 0.5f;
            vertices[sides + 1] = Vector3.down * 0.5f;
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2f / sides;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f);
                var next = (i + 1) % sides + 1;
                var t = i * 6;
                triangles[t] = 0;
                triangles[t + 1] = i + 1;
                triangles[t + 2] = next;
                triangles[t + 3] = sides + 1;
                triangles[t + 4] = next;
                triangles[t + 5] = i + 1;
            }
            return FinalizeMesh("Procedural Cone", vertices, triangles);
        }

        private static Mesh CreatePetal()
        {
            var vertices = new[]
            {
                new Vector3(0f, .55f, -.06f), new Vector3(.38f, .05f, -.06f),
                new Vector3(0f, -.55f, -.06f), new Vector3(-.38f, .05f, -.06f),
                new Vector3(0f, .55f, .06f), new Vector3(.38f, .05f, .06f),
                new Vector3(0f, -.55f, .06f), new Vector3(-.38f, .05f, .06f)
            };
            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
            };
            return FinalizeMesh("Procedural Petal", vertices, triangles);
        }

        private static Mesh CreateHeart()
        {
            var outline = new[]
            {
                new Vector2(0f, 0.24f), new Vector2(-0.22f, 0.50f),
                new Vector2(-0.52f, 0.34f), new Vector2(-0.48f, 0.02f),
                new Vector2(0f, -0.54f), new Vector2(0.48f, 0.02f),
                new Vector2(0.52f, 0.34f), new Vector2(0.22f, 0.50f)
            };
            var vertices = new Vector3[18];
            vertices[0] = new Vector3(0f, 0f, -0.07f);
            vertices[9] = new Vector3(0f, 0f, 0.07f);
            for (var i = 0; i < outline.Length; i++)
            {
                vertices[i + 1] = new Vector3(outline[i].x, outline[i].y, -0.07f);
                vertices[i + 10] = new Vector3(outline[i].x, outline[i].y, 0.07f);
            }

            var triangles = new int[outline.Length * 12];
            for (var i = 0; i < outline.Length; i++)
            {
                var next = (i + 1) % outline.Length;
                var t = i * 12;
                triangles[t] = 0;
                triangles[t + 1] = i + 1;
                triangles[t + 2] = next + 1;
                triangles[t + 3] = 9;
                triangles[t + 4] = next + 10;
                triangles[t + 5] = i + 10;
                triangles[t + 6] = i + 1;
                triangles[t + 7] = i + 10;
                triangles[t + 8] = next + 10;
                triangles[t + 9] = i + 1;
                triangles[t + 10] = next + 10;
                triangles[t + 11] = next + 1;
            }
            return FinalizeMesh("Procedural Heart", vertices, triangles);
        }

        private static Mesh FinalizeMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
