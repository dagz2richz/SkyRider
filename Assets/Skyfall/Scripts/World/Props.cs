using UnityEngine;

namespace Skyfall
{
    /// Builders for medieval scenery made from primitives.
    public static class Props
    {
        public static readonly Color STONE = new Color(0.46f, 0.44f, 0.41f);
        public static readonly Color STONE_DARK = new Color(0.32f, 0.31f, 0.3f);
        public static readonly Color PLASTER = new Color(0.66f, 0.6f, 0.5f);
        public static readonly Color TIMBER = new Color(0.26f, 0.18f, 0.12f);
        public static readonly Color CHAR = new Color(0.07f, 0.06f, 0.05f);
        public static readonly Color THATCH = new Color(0.42f, 0.35f, 0.2f);
        public static readonly Color STEEL = new Color(0.55f, 0.57f, 0.6f);
        public static readonly Color LEATHER = new Color(0.3f, 0.2f, 0.13f);
        public static readonly Color BLOOD = new Color(0.22f, 0.02f, 0.02f);
        public static readonly Color BARK = new Color(0.18f, 0.15f, 0.12f);
        static readonly Color[] TABARDS = { new Color(0.55f, 0.08f, 0.08f), new Color(0.1f, 0.2f, 0.5f), new Color(0.15f, 0.35f, 0.15f), new Color(0.5f, 0.4f, 0.1f) };

        static Material Mt(Color c, float rough = 0.9f, float metal = 0f) => FX.Lit(c, rough, metal);

        static MeshRenderer Bx(Transform p, Vector3 size, Vector3 pos, Material mat, Vector3 rot = default) =>
            FX.Part(p, MeshGen.Box(), mat, pos, size, rot);

        static Transform NewNode(string name) => new GameObject(name).transform;

        public static Transform RuinedHouse(Rng rng)
        {
            var n = NewNode("RuinedHouse");
            float w = rng.Range(5f, 7.5f);
            float d = rng.Range(4f, 6f);
            bool stoneBase = rng.Randf() < 0.5f;
            var wallMat = stoneBase ? Mt(STONE) : Mt(PLASTER);
            const float t = 0.45f;
            // Four walls, each broken off at a random height, one with a gap.
            int gapWall = rng.RangeInt(0, 3);
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side % 2 == 0;
                float length = alongX ? w : d;
                var offset = alongX ? new Vector3(0, 0, d * 0.5f * (side == 0 ? 1 : -1)) : new Vector3(w * 0.5f * (side == 1 ? 1 : -1), 0, 0);
                int pieces = (int)(length / 0.9f);
                for (int i = 0; i < pieces; i++)
                {
                    if (side == gapWall && i > pieces * 0.35f && i < pieces * 0.65f) continue;
                    float hgt = rng.Randf() < 0.7f ? rng.Range(0.6f, 3.6f) : rng.Range(2.6f, 3.8f);
                    float along = -length * 0.5f + (i + 0.5f) * (length / pieces);
                    var pos = offset + (alongX ? new Vector3(along, hgt * 0.5f, 0) : new Vector3(0, hgt * 0.5f, along));
                    var size = alongX ? new Vector3(length / pieces + 0.02f, hgt, t) : new Vector3(t, hgt, length / pieces + 0.02f);
                    var mat = wallMat;
                    if (rng.Randf() < 0.25f) mat = Mt(CHAR);
                    Bx(n, size, pos, mat);
                }
            }
            // Timber frame posts at the corners.
            foreach (int cx in new[] { -1, 1 })
                foreach (int cz in new[] { -1, 1 })
                {
                    float hgt = rng.Range(1.5f, 4.2f);
                    Bx(n, new Vector3(0.35f, hgt, 0.35f), new Vector3(cx * w * 0.5f, hgt * 0.5f, cz * d * 0.5f), Mt(TIMBER));
                }
            // Collapsed roof slab leaning inside.
            float rx = rng.Range(0.3f, 0.6f), rz = rng.Range(-0.2f, 0.2f);
            var roofPos = new Vector3(rng.Range(-0.5f, 0.5f), 1.0f, rng.Range(-0.5f, 0.5f));
            var roofMat = Mt(rng.Randf() < 0.5f ? THATCH : CHAR);
            Bx(n, new Vector3(w * 0.8f, 0.18f, d * 0.6f), roofPos, roofMat, new Vector3(rx, rng.Range(-0.3f, 0.3f), rz));
            // Charred beams and rubble.
            int beams = rng.RangeInt(3, 6);
            for (int i = 0; i < beams; i++)
            {
                float beamLen = rng.Range(2f, 4.5f);
                Bx(n, new Vector3(0.22f, 0.22f, beamLen), new Vector3(rng.Range(-w * 0.4f, w * 0.4f), rng.Range(0.2f, 1.4f), rng.Range(-d * 0.4f, d * 0.4f)),
                    Mt(CHAR), new Vector3(rng.Range(-0.6f, 0.6f), rng.Randf() * M.TAU, rng.Range(-0.4f, 0.4f)));
            }
            int rubble = rng.RangeInt(6, 12);
            for (int i = 0; i < rubble; i++)
            {
                float s = rng.Range(0.3f, 0.8f);
                var r = new Vector3(rng.Range(-w * 0.7f, w * 0.7f), s * 0.3f, rng.Range(-d * 0.7f, d * 0.7f));
                Bx(n, new Vector3(s, s * 0.6f, s * 0.8f), r, Mt(STONE_DARK), new Vector3(rng.Randf(), rng.Randf() * M.TAU, rng.Randf()));
            }
            return n;
        }

        public static Transform DeadSoldier(Rng rng)
        {
            var n = NewNode("DeadSoldier");
            var tab = TABARDS[rng.Next() % TABARDS.Length];
            var lying = FX.Node(n, "Body", new Vector3(0, 0.25f, 0), new Vector3(0, 0, M.PI * 0.5f * (rng.Randf() < 0.5f ? 1 : -1)));
            // Blood pool.
            FX.Part(n, MeshGen.Disc(), Mt(BLOOD, 0.3f), new Vector3(0, 0.03f, 0), new Vector3(rng.Range(0.7f, 1.2f), 1, rng.Range(0.9f, 1.4f)));
            // Body (lying along X once rotated).
            FX.Part(lying, MeshGen.Capsule(), Mt(tab), Vector3.zero, new Vector3(0.36f, 0.42f, 0.28f));
            FX.Part(lying, MeshGen.Capsule(), Mt(STEEL, 0.4f, 0.7f), new Vector3(0, 0.15f, 0), new Vector3(0.38f, 0.22f, 0.3f));
            FX.Part(lying, MeshGen.Sphere(), Mt(new Color(0.72f, 0.6f, 0.5f)), new Vector3(0, 0.95f, 0), Vector3.one * 0.3f);
            FX.Part(lying, MeshGen.Sphere(), Mt(STEEL, 0.35f, 0.8f), new Vector3(0, 1.0f, 0), new Vector3(0.34f, 0.24f, 0.34f));
            // Legs splayed.
            FX.Part(lying, MeshGen.Capsule(), Mt(LEATHER), new Vector3(0.12f, -0.75f, 0), new Vector3(0.14f, 0.36f, 0.14f), new Vector3(0, 0, 0.2f));
            FX.Part(lying, MeshGen.Capsule(), Mt(LEATHER), new Vector3(-0.12f, -0.75f, 0), new Vector3(0.14f, 0.36f, 0.14f), new Vector3(0, 0, -0.35f));
            // Arm flung out.
            FX.Part(lying, MeshGen.Capsule(), Mt(tab), new Vector3(0, 0.4f, 0.35f), new Vector3(0.12f, 0.3f, 0.12f), new Vector3(1.2f, 0, 0));
            // Shield nearby.
            if (rng.Randf() < 0.7f)
            {
                var sh = FX.Part(n, MeshGen.Cylinder(), Mt(M.Darken(tab, 0.2f)), new Vector3(rng.Range(0.6f, 1.2f), 0.08f, rng.Range(-0.8f, 0.8f)),
                    new Vector3(0.9f, 0.08f, 0.9f), new Vector3(rng.Range(-0.2f, 0.2f), 0, rng.Range(-0.2f, 0.2f)));
                FX.Part(sh.transform, MeshGen.Sphere(), Mt(STEEL, 0.4f, 0.8f), new Vector3(0, 0.6f, 0), new Vector3(0.25f, 0.8f, 0.25f));
            }
            // Broken spear or sword.
            if (rng.Randf() < 0.6f)
                Bx(n, new Vector3(0.06f, 0.06f, 2.2f), new Vector3(rng.Range(-1f, 1f), 0.05f, rng.Range(-1f, 1f)), Mt(TIMBER), new Vector3(0, rng.Randf() * M.TAU, 0));
            return n;
        }

        public static Transform BladeInGround(Rng rng)
        {
            var n = NewNode("Blade");
            var holder = FX.Node(n, "Tilt", Vector3.zero, new Vector3(rng.Range(-0.35f, 0.35f), 0, rng.Range(-0.35f, 0.35f)));
            if (rng.Randf() < 0.5f)
            {
                Bx(holder, new Vector3(0.12f, 1.1f, 0.03f), new Vector3(0, 0.45f, 0), Mt(STEEL, 0.3f, 0.85f));
                Bx(holder, new Vector3(0.45f, 0.06f, 0.08f), new Vector3(0, 1.0f, 0), Mt(STEEL, 0.4f, 0.7f));
                Bx(holder, new Vector3(0.06f, 0.28f, 0.06f), new Vector3(0, 1.17f, 0), Mt(LEATHER));
            }
            else
            {
                Bx(holder, new Vector3(0.06f, 2.4f, 0.06f), new Vector3(0, 1.1f, 0), Mt(TIMBER));
                FX.Part(holder, MeshGen.Cylinder(0f, 0.5f, 4), Mt(STEEL, 0.3f, 0.8f), new Vector3(0, 2.4f, 0), new Vector3(0.14f, 0.4f, 0.14f));
            }
            return n;
        }

        public static Transform Banner(Rng rng)
        {
            var n = NewNode("Banner");
            var tab = TABARDS[rng.Next() % TABARDS.Length];
            var holder = FX.Node(n, "Tilt", Vector3.zero, new Vector3(rng.Range(-0.4f, 0.4f), 0, rng.Range(-0.4f, 0.4f)));
            Bx(holder, new Vector3(0.1f, 4.0f, 0.1f), new Vector3(0, 2.0f, 0), Mt(TIMBER));
            Bx(holder, new Vector3(1.2f, 0.08f, 0.08f), new Vector3(0.6f, 3.85f, 0), Mt(TIMBER));
            Bx(holder, new Vector3(1.1f, 1.8f, 0.03f), new Vector3(0.62f, 2.9f, 0), Mt(tab), new Vector3(0, rng.Range(-0.3f, 0.3f), 0));
            // Tattered strips.
            for (int i = 0; i < 3; i++)
                Bx(holder, new Vector3(0.28f, rng.Range(0.3f, 0.7f), 0.03f), new Vector3(0.25f + i * 0.37f, 1.85f, 0), Mt(tab));
            Bx(holder, new Vector3(0.4f, 0.4f, 0.035f), new Vector3(0.62f, 3.1f, 0), Mt(new Color(0.85f, 0.75f, 0.3f), 0.5f, 0.5f), new Vector3(0, 0, M.PI * 0.25f));
            return n;
        }

        public static Transform Campfire(Rng rng)
        {
            var n = NewNode("Campfire");
            FX.Part(n, MeshGen.Disc(), Mt(CHAR), new Vector3(0, 0.03f, 0), new Vector3(1f, 1, 1f));
            for (int i = 0; i < 8; i++)
            {
                float a = M.TAU * i / 8f;
                FX.Part(n, MeshGen.Sphere(6), Mt(STONE_DARK), new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)), new Vector3(0.35f, 0.25f, 0.3f));
            }
            for (int i = 0; i < 3; i++)
                Bx(n, new Vector3(0.12f, 0.12f, 1.2f), new Vector3(0, 0.15f, 0), Mt(CHAR), new Vector3(0.2f, M.TAU * i / 3f, 0));
            FX.Part(n, MeshGen.Sphere(6), FX.Glow(new Color(1f, 0.35f, 0.05f), 1.5f), new Vector3(0, 0.12f, 0), new Vector3(0.5f, 0.15f, 0.5f));
            var smokeNode = FX.Node(n, "Smoke", new Vector3(0, 0.4f, 0), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.Particles(smokeNode, new FX.ParticleSpec
            {
                amount = 16, lifetime = 5f, size = 1.4f, scaleMin = 0.8f, scaleMax = 2.5f, speedMin = 1f, speedMax = 1.8f, spread = 12f,
                gravity = new Vector3(0.4f, 0.2f, 0.1f), startColor = new Color(0.25f, 0.23f, 0.22f, 0.55f), endColor = new Color(0.4f, 0.4f, 0.4f, 0f),
                additive = false,
            });
            return n;
        }

        public static Transform BrokenCart(Rng rng)
        {
            var n = NewNode("Cart");
            var bed = FX.Node(n, "Bed", new Vector3(0, 0.7f, 0), new Vector3(0, 0, rng.Range(0.15f, 0.35f)));
            Bx(bed, new Vector3(2.6f, 0.12f, 1.4f), Vector3.zero, Mt(TIMBER));
            Bx(bed, new Vector3(2.6f, 0.5f, 0.08f), new Vector3(0, 0.3f, 0.68f), Mt(TIMBER));
            Bx(bed, new Vector3(0.08f, 0.5f, 1.4f), new Vector3(-1.28f, 0.3f, 0), Mt(TIMBER));
            Bx(bed, new Vector3(1.8f, 0.08f, 0.08f), new Vector3(2.0f, -0.1f, 0.4f), Mt(TIMBER));
            // One wheel attached, one fallen flat.
            var wheel = Mt(M.Lighten(TIMBER, 0.1f));
            FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 14), wheel, new Vector3(0.3f, 0.5f, 0.85f), new Vector3(1f, 0.1f, 1f), new Vector3(M.PI * 0.5f, 0, 0));
            FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 14), wheel, new Vector3(-0.6f, 0.06f, -1.6f), new Vector3(1f, 0.1f, 1f));
            int barrels = rng.RangeInt(2, 4);
            for (int i = 0; i < barrels; i++)
                FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 8), Mt(new Color(0.4f, 0.3f, 0.18f)), new Vector3(rng.Range(-2f, 2f), 0.25f, rng.Range(-2f, 2f)),
                    new Vector3(0.45f, 0.6f, 0.45f), new Vector3(M.PI * 0.5f, rng.Randf() * M.TAU, 0));
            return n;
        }

        public static Transform Well(Rng rng)
        {
            var n = NewNode("Well");
            for (int i = 0; i < 10; i++)
            {
                float a = M.TAU * i / 10f;
                Bx(n, new Vector3(0.5f, 0.9f, 0.35f), new Vector3(Mathf.Cos(a), 0.45f, Mathf.Sin(a)), Mt(STONE), new Vector3(0, -a, 0));
            }
            FX.Part(n, MeshGen.Disc(), Mt(new Color(0.02f, 0.03f, 0.04f)), new Vector3(0, 0.5f, 0), new Vector3(0.85f, 1, 0.85f));
            Bx(n, new Vector3(0.18f, 2.2f, 0.18f), new Vector3(1f, 1.1f, 0), Mt(TIMBER));
            Bx(n, new Vector3(0.18f, 1.4f, 0.18f), new Vector3(-1f, 0.7f, 0), Mt(CHAR));
            return n;
        }

        public static Transform Watchtower(Rng rng)
        {
            var n = NewNode("Watchtower");
            int rings = rng.RangeInt(5, 9);
            for (int r = 0; r < rings; r++)
            {
                const int count = 12;
                for (int i = 0; i < count; i++)
                {
                    if (r > 2 && rng.Randf() < (float)r / rings * 0.8f) continue;
                    float a = M.TAU * (i + (r % 2) * 0.5f) / count;
                    Bx(n, new Vector3(1.1f, 0.75f, 0.7f), new Vector3(Mathf.Cos(a) * 2.6f, 0.38f + r * 0.75f, Mathf.Sin(a) * 2.6f),
                        Mt((r + i) % 3 != 0 ? STONE : STONE_DARK), new Vector3(0, -a + M.PI * 0.5f, 0));
                }
            }
            for (int i = 0; i < 12; i++)
            {
                float s = rng.Range(0.4f, 0.9f);
                Bx(n, new Vector3(s, s * 0.7f, s), new Vector3(rng.Range(-5f, 5f), s * 0.3f, rng.Range(-5f, 5f)), Mt(STONE_DARK),
                    new Vector3(rng.Randf(), rng.Randf() * M.TAU, rng.Randf()));
            }
            return n;
        }

        public static Transform StoneCircle(Rng rng)
        {
            var n = NewNode("StoneCircle");
            int count = rng.RangeInt(7, 10);
            for (int i = 0; i < count; i++)
            {
                if (rng.Randf() < 0.15f) continue;
                float a = M.TAU * i / count;
                float h = rng.Range(2.5f, 4f);
                bool fallen = rng.Randf() < 0.2f;
                var rot = new Vector3(0, -a, fallen ? M.PI * 0.5f : rng.Range(-0.08f, 0.08f));
                float y = fallen ? 0.4f : h * 0.5f;
                Bx(n, new Vector3(1f, h, 0.6f), new Vector3(Mathf.Cos(a) * 6f, y, Mathf.Sin(a) * 6f), Mt(STONE_DARK), rot);
            }
            // Glowing rune altar at the center.
            Bx(n, new Vector3(2f, 0.8f, 1.2f), new Vector3(0, 0.4f, 0), Mt(STONE));
            Bx(n, new Vector3(1.4f, 0.05f, 0.6f), new Vector3(0, 0.82f, 0), FX.Glow(new Color(0.3f, 0.7f, 1f), 1.2f));
            return n;
        }

        public static Transform DeadTree(Rng rng)
        {
            var n = NewNode("DeadTree");
            float h = rng.Range(4f, 8f);
            var lean = new Vector3(rng.Range(-0.12f, 0.12f), 0, rng.Range(-0.12f, 0.12f));
            var top = new Vector3(lean.x * h, h, lean.z * h);
            FX.Bone(n, Vector3.zero, top, 0.32f, 0.06f, Mt(BARK));
            int branches = rng.RangeInt(3, 6);
            for (int i = 0; i < branches; i++)
            {
                float t = rng.Range(0.35f, 0.9f);
                var start = top * t;
                float a = rng.Randf() * M.TAU;
                float l = rng.Range(1.2f, 2.8f) * (1.2f - t);
                var end = start + new Vector3(Mathf.Cos(a) * l, rng.Range(0.4f, 1.4f), Mathf.Sin(a) * l);
                FX.Bone(n, start, end, 0.12f * (1.1f - t), 0.02f, Mt(BARK));
                if (rng.Randf() < 0.5f)
                {
                    var tip = end + new Vector3(Mathf.Cos(a + 0.8f) * l * 0.5f, 0.5f, Mathf.Sin(a + 0.8f) * l * 0.5f);
                    FX.Bone(n, end, tip, 0.04f, 0.01f, Mt(BARK));
                }
            }
            return n;
        }

        public static Transform Rock(Rng rng)
        {
            var n = NewNode("Rock");
            float s = rng.Range(0.6f, 2.6f);
            FX.Part(n, MeshGen.Sphere(7), Mt(Color.Lerp(STONE, STONE_DARK, rng.Randf())), new Vector3(0, s * 0.25f, 0),
                new Vector3(s * rng.Range(0.8f, 1.4f), s * rng.Range(0.5f, 0.9f), s * rng.Range(0.8f, 1.3f)),
                new Vector3(rng.Range(-0.3f, 0.3f), rng.Randf() * M.TAU, rng.Range(-0.3f, 0.3f)));
            if (rng.Randf() < 0.4f)
                FX.Part(n, MeshGen.Sphere(6), Mt(STONE_DARK), new Vector3(s * 0.6f, s * 0.1f, 0), Vector3.one * s * 0.45f);
            return n;
        }
    }
}
