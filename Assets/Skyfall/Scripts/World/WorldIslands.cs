using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Floating islands (a big boss island plus stepping stones) and the Ruins Creation Kit scenery.
    public partial class WorldTerrain
    {
        /// A floating island: flat walkable top at topY inside an irregular footprint, jagged rock below.
        public class Island
        {
            public Vector3 center;     // point on the top surface
            public float radius;       // mean top radius
            public float topY;
            public float depth;        // how far the rock hangs below the top
            public bool big;
            float p1, p2, p3;

            public void SetShape(Rng rng) { p1 = rng.Randf() * M.TAU; p2 = rng.Randf() * M.TAU; p3 = rng.Randf() * M.TAU; }

            /// Footprint radius toward angle a (radians, atan2(dz, dx)).
            public float EdgeRadius(float a) =>
                radius * (1f + 0.10f * Mathf.Sin(2f * a + p1) + 0.07f * Mathf.Sin(3f * a + p2) + 0.04f * Mathf.Sin(5f * a + p3));

            public bool Contains(float x, float z)
            {
                float dx = x - center.x, dz = z - center.z;
                float d2 = dx * dx + dz * dz;
                float max = radius * 1.25f;
                if (d2 > max * max) return false;
                return Mathf.Sqrt(d2) < EdgeRadius(Mathf.Atan2(dz, dx));
            }
        }

        public readonly List<Island> islands = new List<Island>();
        public Island bossIsland;
        /// A point on the big island's top surface (its middle).
        public Vector3 BossIslandCenter => bossIsland != null ? bossIsland.center : M.INF;
        public float BossIslandRadius => bossIsland != null ? bossIsland.radius : 0f;

        /// Highest walkable surface at (x, z) that is at or below fromY + 0.5: an island top when the caller is above
        /// that island, otherwise the terrain. Pass the height the caller was at BEFORE moving this frame.
        public float SurfaceHeight(float x, float z, float fromY)
        {
            float h = Height(x, z);
            for (int i = 0; i < islands.Count; i++)
            {
                var isl = islands[i];
                if (isl.topY > h && isl.topY <= fromY + 0.5f && isl.Contains(x, z)) h = isl.topY;
            }
            return h;
        }

        /// The island whose top surface is under (x, z) at height fromY, or null.
        public Island IslandAt(float x, float z, float fromY)
        {
            foreach (var isl in islands)
                if (isl.topY <= fromY + 0.5f && isl.Contains(x, z)) return isl;
            return null;
        }

        // ---------------------------------------------------------------- layout

        float GroundMax(Vector3 c, float r)
        {
            float m = Height(c.x, c.z);
            for (int i = 0; i < 16; i++)
            {
                float a = M.TAU * i / 16f;
                m = Mathf.Max(m, Height(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r));
                m = Mathf.Max(m, Height(c.x + Mathf.Cos(a) * r * 0.5f, c.z + Mathf.Sin(a) * r * 0.5f));
            }
            return m;
        }

        Island NewIsland(float x, float z, float radius, float topY, bool big)
        {
            var isl = new Island { radius = radius, big = big };
            isl.SetShape(rng);
            float gmax = GroundMax(new Vector3(x, 0, z), radius * 1.1f);
            isl.topY = Mathf.Max(topY, gmax + (big ? 10f : 4f));
            isl.center = new Vector3(x, isl.topY, z);
            isl.depth = Mathf.Clamp(Mathf.Min(radius * (big ? 0.8f : 1.1f), isl.topY - gmax - 1.5f), 3f, 40f);
            islands.Add(isl);
            return isl;
        }

        static Vector2 Polar(float r, float a) => new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);

        void BuildIslands()
        {
            var root = new GameObject("Islands").transform;
            root.SetParent(transform, false);

            // The big island: far from the start, over fairly low ground.
            float bestScore = float.MaxValue, bestAngle = 0f;
            const float dist = 120f, bigR = 25f;
            for (int i = 0; i < 12; i++)
            {
                float a = rng.Randf() * M.TAU;
                var c = Polar(dist, a);
                if (Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)) + bigR > PLAY_LIMIT) continue;
                float ground = Height(c.x, c.y);
                if (ground < WATER_LEVEL + 1.5f) continue;
                float score = GroundMax(new Vector3(c.x, 0, c.y), bigR * 1.1f) + rng.Randf() * 3f;
                if (score < bestScore) { bestScore = score; bestAngle = a; }
            }
            if (bestScore == float.MaxValue) bestAngle = M.PI * 0.25f;
            var bc = Polar(dist, bestAngle);
            bossIsland = NewIsland(bc.x, bc.y, bigR, Height(bc.x, bc.y) + 21f, true);
            float bigTop = bossIsland.topY;

            // Stepping stones: ground -> big island in hops of ~24 units, each a few units higher.
            var s = Polar(44f, bestAngle - 1.9f);
            float sr, prevTop = Height(s.x, s.y) + 7f;
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                // Curve through polar space so the chain sweeps around toward the big island.
                float r = Mathf.Lerp(44f, 82f, t), ang = Mathf.Lerp(bestAngle - 1.9f, bestAngle - 0.15f, t);
                var p = Polar(r, ang);
                sr = rng.Range(5.5f, 8.5f);
                float plan = Mathf.Lerp(Height(s.x, s.y) + 7f, bigTop - 5f, t);
                float top = Mathf.Clamp(plan, prevTop - 1f, prevTop + 9f);
                var last = NewIsland(p.x, p.y, sr, top, false);
                prevTop = last.topY;
            }
            // A few more around the big island for looks (on the far side from the approach).
            float[] offs = { 2.3f, 3.6f, 4.9f };
            float[] lift = { 4f, -2f, 7f };
            int extra = 0;
            for (int k = 0; k < offs.Length && extra < 2; k++)
            {
                var p = new Vector2(bc.x, bc.y) + Polar(bigR + 17f, bestAngle + offs[k] + Mathf.PI);
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > PLAY_LIMIT - 6f) continue;
                NewIsland(p.x, p.y, rng.Range(5f, 7.5f), bigTop + lift[k], false);
                extra++;
            }

            foreach (var isl in islands)
            {
                BuildIslandMesh(isl, root);
                DecorateIsland(isl, bestAngle);
            }
        }

        // ---------------------------------------------------------------- mesh

        void BuildIslandMesh(Island isl, Transform parent)
        {
            const int N = 44;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var cen = isl.center;
            var body = cen + new Vector3(0, -isl.depth * 0.55f, 0);
            var grassA = new Color(0.30f, 0.38f, 0.16f);
            var grassB = new Color(0.40f, 0.42f, 0.19f);
            var dirt = new Color(0.34f, 0.27f, 0.19f);
            var rockHi = new Color(0.45f, 0.42f, 0.38f);
            var rockLo = new Color(0.22f, 0.20f, 0.19f);
            Color Lin(Color c) { var l = c.linear; l.a = 1f; return l; }
            var tris = new List<int>();
            void Tri(int a, int b, int c)
            {
                var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                var cent = (verts[a] + verts[b] + verts[c]) / 3f;
                if (Vector3.Dot(n, cent - body) < 0f) { tris.Add(a); tris.Add(c); tris.Add(b); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            float[] ang = new float[N], edge = new float[N];
            for (int i = 0; i < N; i++) { ang[i] = M.TAU * i / N; edge[i] = isl.EdgeRadius(ang[i]); }

            // Top: a center vertex and rings out to the edge, all at topY.
            int centerIdx = verts.Count;
            verts.Add(cen);
            cols.Add(Lin(grassB));
            float[] ringF = { 0.35f, 0.7f, 0.93f, 1f };
            var ringStart = new int[ringF.Length];
            for (int r = 0; r < ringF.Length; r++)
            {
                ringStart[r] = verts.Count;
                for (int i = 0; i < N; i++)
                {
                    var p = cen + new Vector3(Mathf.Cos(ang[i]), 0, Mathf.Sin(ang[i])) * edge[i] * ringF[r];
                    verts.Add(p);
                    float n = Mathf.PerlinNoise(p.x * 0.2f + 31f, p.z * 0.2f + 17f);
                    var c = Color.Lerp(grassA, grassB, n);
                    if (r == ringF.Length - 1) c = Color.Lerp(c, dirt, 0.5f);
                    cols.Add(Lin(M.Mul(c, 0.92f + 0.14f * n)));
                }
            }
            for (int i = 0; i < N; i++) Tri(centerIdx, ringStart[0] + i, ringStart[0] + (i + 1) % N);
            for (int r = 0; r < ringF.Length - 1; r++)
                for (int i = 0; i < N; i++)
                {
                    int a = ringStart[r] + i, b = ringStart[r] + (i + 1) % N, c = ringStart[r + 1] + i, d = ringStart[r + 1] + (i + 1) % N;
                    Tri(a, c, b); Tri(b, c, d);
                }

            // Underside: rings that shrink to a point, jittered into jagged rock.
            int prev = ringStart[ringF.Length - 1];
            const int U = 6;
            for (int j = 1; j <= U; j++)
            {
                float f = j / (float)U;
                int start = verts.Count;
                for (int i = 0; i < N; i++)
                {
                    float rf = Mathf.Pow(1f - f, 0.8f) * (j == 1 ? 0.96f : 1f) * (1f + (rng.Randf() - 0.5f) * 0.34f);
                    float y = -(j == 1 ? 1.1f : 1.1f + isl.depth * Mathf.Pow(f, 1.1f)) + (rng.Randf() - 0.5f) * isl.depth * 0.16f * (j < U ? 1f : 0f);
                    verts.Add(cen + new Vector3(Mathf.Cos(ang[i]) * edge[i] * rf, y, Mathf.Sin(ang[i]) * edge[i] * rf));
                    float shade = Mathf.Clamp01(f * 1.1f + (rng.Randf() - 0.5f) * 0.25f);
                    var c = Color.Lerp(j == 1 ? dirt : rockHi, rockLo, shade);
                    cols.Add(Lin(c));
                }
                for (int i = 0; i < N; i++)
                {
                    int a = prev + i, b = prev + (i + 1) % N, c = start + i, d = start + (i + 1) % N;
                    Tri(a, c, b); Tri(b, c, d);
                }
                prev = start;
            }
            int tip = verts.Count;
            verts.Add(cen + new Vector3((rng.Randf() - 0.5f) * isl.radius * 0.15f, -isl.depth * 1.12f, (rng.Randf() - 0.5f) * isl.radius * 0.15f));
            cols.Add(Lin(rockLo));
            for (int i = 0; i < N; i++) Tri(prev + i, tip, prev + (i + 1) % N);

            var mesh = new Mesh { name = "Island" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(isl.big ? "BossIsland" : "Island");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = FX.Lit(Color.white, 0.95f);

            // Dangling rocks under the rim.
            int rocks = isl.big ? 7 : 3;
            for (int i = 0; i < rocks; i++)
            {
                float a = rng.Randf() * M.TAU;
                float rr = isl.EdgeRadius(a) * rng.Range(0.45f, 0.8f);
                float s = isl.radius * rng.Range(0.07f, 0.16f);
                float y = -isl.depth * rng.Range(0.25f, 0.65f) - 1f;
                var rock = FX.Part(go.transform, MeshGen.Sphere(6), FX.Lit(Color.Lerp(rockHi, rockLo, rng.Randf()), 0.95f),
                    cen + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), new Vector3(s, s * rng.Range(1.2f, 2.2f), s),
                    new Vector3(rng.Range(-0.4f, 0.4f), rng.Randf() * M.TAU, rng.Range(-0.4f, 0.4f)));
                rock.gameObject.name = "HangingRock";
            }
        }

        // ---------------------------------------------------------------- decoration

        Vector3 OnIsland(Island isl, float r, float a) =>
            isl.center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);

        void DecorateIsland(Island isl, float approach)
        {
            bool kit = KitAvailable;
            if (isl.big)
            {
                if (kit)
                {
                    // A ruined arena: broken pillars and wall segments in a ring, leaving the middle and the approach open.
                    string[] pillars = { "pillar_broke_01", "pillar_02", "pillar_broke_01", "pillar_broke_part_01" };
                    int count = 11;
                    for (int i = 0; i < count; i++)
                    {
                        float a = M.TAU * i / count + 0.2f;
                        float edge = isl.EdgeRadius(a);
                        var p = OnIsland(isl, edge * 0.8f, a);
                        if (i % 2 == 0) KitPiece(pillars[(i / 2) % pillars.Length], p, -a * Mathf.Rad2Deg + rng.Range(-15f, 15f), rng.Range(0.8f, 1.1f), 0.1f);
                        else KitPiece(rng.Randf() < 0.5f ? "destroyed_wall_01" : "destroyed_wall_02", p, -a * Mathf.Rad2Deg + 90f, rng.Range(0.8f, 1.0f), 0.1f);
                    }
                    // A temple at the back (away from the stepping stones) and a gate toward them.
                    float back = approach;
                    KitPiece("nef_01", OnIsland(isl, isl.EdgeRadius(back) * 0.62f, back), -back * Mathf.Rad2Deg + 90f, 0.9f, 0.1f);
                    KitPiece("entrance_01", OnIsland(isl, isl.EdgeRadius(back + 0.75f) * 0.7f, back + 0.75f), -(back + 0.75f) * Mathf.Rad2Deg, 0.6f, 0.1f);
                    KitPiece("dome_01", OnIsland(isl, isl.EdgeRadius(back - 1.0f) * 0.68f, back - 1.0f), rng.Randf() * 360f, 0.8f, 0.1f);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = rng.Randf() * M.TAU;
                        KitPiece("destroyed_floor_01", OnIsland(isl, isl.radius * rng.Range(0.3f, 0.5f), a), rng.Randf() * 360f, 1f, 0.35f);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = rng.Randf() * M.TAU;
                        KitPiece(i == 0 ? "tomb_01" : "pew_01", OnIsland(isl, isl.radius * rng.Range(0.55f, 0.7f), a), rng.Randf() * 360f, 1.2f, 0.05f);
                    }
                }
                else
                {
                    for (int i = 0; i < 7; i++)
                    {
                        float a = M.TAU * i / 7f;
                        PlaceOnIsland(Props.RuinedHouse(rng), OnIsland(isl, isl.EdgeRadius(a) * 0.78f, a));
                    }
                }
                ScatterOnIsland(isl, 26, 0.55f, 0.92f, true);
            }
            else
            {
                if (kit && rng.Randf() < 0.6f)
                    KitPiece(rng.Randf() < 0.5f ? "pillar_broke_01" : "destroyed_wall_02", OnIsland(isl, isl.radius * 0.45f, rng.Randf() * M.TAU), rng.Randf() * 360f, 0.8f, 0.1f);
                ScatterOnIsland(isl, 6, 0.25f, 0.8f, false);
            }
        }

        void ScatterOnIsland(Island isl, int count, float rMin, float rMax, bool trees)
        {
            for (int i = 0; i < count; i++)
            {
                float a = rng.Randf() * M.TAU;
                var p = OnIsland(isl, isl.EdgeRadius(a) * rng.Range(rMin, rMax), a);
                float roll = rng.Randf();
                if (trees && roll < 0.12f)
                {
                    if (!(Lib && PlacePack(Lib.deadTrees, p - new Vector3(0, 0.1f, 0), rng.Range(5f, 8f)))) PlaceOnIsland(Props.DeadTree(rng), p);
                }
                else if (roll < 0.4f)
                {
                    if (!(Lib && PlacePack(Lib.rocks, p - new Vector3(0, 0.2f, 0), rng.Range(0.8f, 2.6f)))) PlaceOnIsland(Props.Rock(rng), p);
                }
                else if (Lib && roll < 0.75f) PlacePack(Lib.shrubs, p - new Vector3(0, 0.05f, 0), rng.Range(1.2f, 2f));
                else if (Lib) PlacePack(Lib.grass, p - new Vector3(0, 0.05f, 0), rng.Range(0.9f, 1.8f));
            }
        }

        void PlaceOnIsland(Transform node, Vector3 p)
        {
            node.SetParent(propsRoot, false);
            node.localPosition = p;
            node.localRotation = Quaternion.Euler(0f, rng.Randf() * 360f, 0f);
            foreach (var r in node.GetComponentsInChildren<Renderer>()) r.gameObject.layer = PROP_LAYER;
            node.gameObject.layer = PROP_LAYER;
        }

        // ---------------------------------------------------------------- Ruins Creation Kit

        public bool KitAvailable => Lib != null && Lib.ruinsKit != null;
        Dictionary<string, Transform> kitChildren;

        Transform KitChild(string name)
        {
            if (kitChildren == null)
            {
                kitChildren = new Dictionary<string, Transform>();
                foreach (var t in Lib.ruinsKit.GetComponentsInChildren<Transform>(true))
                    if (t.GetComponent<MeshRenderer>()) kitChildren[t.name] = t;
            }
            kitChildren.TryGetValue(name, out var c);
            return c;
        }

        /// Places one piece of the Ruins kit: its base sits at pos.y minus sink, its footprint centered on pos.
        Transform KitPiece(string name, Vector3 pos, float yawDeg, float scale, float sink)
        {
            if (!KitAvailable) return null;
            var src = KitChild(name);
            if (src == null) return null;
            var holder = new GameObject("Ruin_" + name).transform;
            holder.SetParent(propsRoot, false);
            holder.position = pos;
            holder.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            holder.localScale = Vector3.one * scale;
            var piece = Object.Instantiate(src.gameObject, holder, false).transform;
            piece.localPosition = Vector3.zero;
            piece.localRotation = src.localRotation;
            piece.localScale = src.localScale;
            var rends = piece.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            piece.position += new Vector3(pos.x - b.center.x, pos.y - sink * scale - b.min.y, pos.z - b.center.z);
            foreach (var r in rends) r.gameObject.layer = PROP_LAYER;
            holder.gameObject.layer = PROP_LAYER;
            return holder;
        }

        bool GroundOk(Vector3 p, float maxSlope)
        {
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) > PLAY_LIMIT + 10f) return false;
            if (p.magnitude < 14f) return false;
            if (Height(p.x, p.z) < WATER_LEVEL + 1f) return false;
            return 1f - NormalAt(p.x, p.z).y <= maxSlope;
        }

        /// A ruined settlement built from kit pieces: a chapel or gatehouse, broken walls and pillars around it.
        void BuildKitVillage(Vector3 c)
        {
            string[] centers = { "nef_01", "entrance_02", "entrance_03", "corridor_01", "entrance_01" };
            string cp = centers[rng.RangeInt(0, centers.Length - 1)];
            KitPiece(cp, new Vector3(c.x, c.y, c.z), rng.Randf() * 360f, cp == "nef_01" ? 0.85f : 0.8f, 0.4f);
            int walls = rng.RangeInt(4, 6);
            float a0 = rng.Randf() * M.TAU;
            for (int i = 0; i < walls; i++)
            {
                float a = a0 + M.TAU * i / walls + rng.Range(-0.2f, 0.2f);
                float r = rng.Range(13f, 18f);
                var p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (!GroundOk(p, 0.16f)) continue;
                p.y = Height(p.x, p.z);
                KitPiece(rng.Randf() < 0.5f ? "destroyed_wall_01" : "destroyed_wall_02", p, -a * Mathf.Rad2Deg + 90f + rng.Range(-20f, 20f), rng.Range(0.85f, 1.1f), 0.6f);
            }
            string[] small = { "pillar_broke_01", "pillar_broke_part_01", "pillar_02", "tomb_01", "pew_01", "pew_02", "dome_01", "destroyed_floor_01", "pillars_01" };
            int n = rng.RangeInt(4, 6);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Randf() * M.TAU, r = rng.Range(7f, 20f);
                var p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (!GroundOk(p, 0.16f)) continue;
                p.y = Height(p.x, p.z);
                string name = small[rng.RangeInt(0, small.Length - 1)];
                KitPiece(name, p, rng.Randf() * 360f, name.StartsWith("pew") || name.StartsWith("tomb") ? 1.3f : rng.Range(0.8f, 1.05f), 0.4f);
            }
        }
    }
}
