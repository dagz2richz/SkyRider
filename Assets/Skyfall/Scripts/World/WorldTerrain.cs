using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Procedural heightmap world with water and medieval ruins/battlefields.
    public class WorldTerrain : MonoBehaviour
    {
        public const float HALF = 200f;
        public const int RES = 200;
        public const float WATER_LEVEL = -6.5f;
        public const float PLAY_LIMIT = 165f;
        public const int PROP_LAYER = 8;

        Noise noise, ridge, detail;
        public Noise scorch;
        Rng rng;
        float[] heights;
        float cell;
        Transform propsRoot;

        public void Generate(int seed)
        {
            rng = new Rng(seed);
            noise = new Noise(seed, 0.0065f, 4);
            ridge = new Noise(seed + 3, 0.004f, 5);
            detail = new Noise(seed + 7, 0.06f, 5);
            scorch = new Noise(seed + 13, 0.018f, 5);
            cell = HALF * 2f / RES;
            BakeHeights();
            BuildMesh();
            BuildWater();
            propsRoot = new GameObject("Props").transform;
            propsRoot.SetParent(transform, false);
            ScatterProps();
            // Merge the static scenery into big batches so thousands of small parts render cheaply.
            StaticBatchingUtility.Combine(propsRoot.gameObject);
        }

        // ---------------------------------------------------------------- heights

        float RawHeight(float x, float z)
        {
            float h = noise.Get(x, z) * 20f;
            // Ridged hills carve long valleys between them.
            float r = 1f - Mathf.Abs(ridge.Get(x, z));
            h += (r * r - 0.45f) * 22f;
            h += detail.Get(x, z) * 0.9f;
            // Gentle, flat start area.
            float d = new Vector2(x, z).magnitude;
            h = Mathf.Lerp(h, 2f, (1f - M.Smoothstep(10f, 38f, d)) * 0.85f);
            // Mountains ring the edge of the world.
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h += M.Smoothstep(PLAY_LIMIT - 20f, HALF, edge) * 55f;
            return h;
        }

        void BakeHeights()
        {
            heights = new float[(RES + 1) * (RES + 1)];
            for (int iz = 0; iz <= RES; iz++)
                for (int ix = 0; ix <= RES; ix++)
                    heights[iz * (RES + 1) + ix] = RawHeight(-HALF + ix * cell, -HALF + iz * cell);
        }

        public float Height(float x, float z)
        {
            float fx = Mathf.Clamp((x + HALF) / cell, 0f, RES - 0.001f);
            float fz = Mathf.Clamp((z + HALF) / cell, 0f, RES - 0.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * (RES + 1) + ix;
            float h00 = heights[i], h10 = heights[i + 1];
            float h01 = heights[i + RES + 1], h11 = heights[i + RES + 2];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        public Vector3 NormalAt(float x, float z)
        {
            const float e = 1.5f;
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        // ---------------------------------------------------------------- mesh

        void BuildMesh()
        {
            int n = RES + 1;
            var verts = new Vector3[n * n];
            var norms = new Vector3[n * n];
            var cols = new Color[n * n];
            var grass = new Color(0.27f, 0.34f, 0.15f);
            var grass2 = new Color(0.36f, 0.38f, 0.17f);
            var dry = new Color(0.47f, 0.41f, 0.25f);
            var dirt = new Color(0.33f, 0.26f, 0.18f);
            var rock = new Color(0.40f, 0.39f, 0.37f);
            var snow = new Color(0.86f, 0.88f, 0.92f);
            var ash = new Color(0.10f, 0.09f, 0.08f);
            var mud = new Color(0.22f, 0.19f, 0.14f);
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float x = -HALF + ix * cell, z = -HALF + iz * cell;
                    float h = heights[iz * n + ix];
                    float hl = heights[iz * n + Mathf.Max(ix - 1, 0)];
                    float hr = heights[iz * n + Mathf.Min(ix + 1, RES)];
                    float hd = heights[Mathf.Max(iz - 1, 0) * n + ix];
                    float hu = heights[Mathf.Min(iz + 1, RES) * n + ix];
                    var nrm = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                    float slope = 1f - nrm.y;
                    float dn = detail.Get(x * 2f, z * 2f);
                    var c = Color.Lerp(grass, grass2, Mathf.Clamp01(dn * 0.5f + 0.5f));
                    c = Color.Lerp(c, dry, M.Smoothstep(4f, 14f, h) * 0.7f);
                    c = Color.Lerp(c, dirt, M.Smoothstep(0.12f, 0.28f, slope));
                    c = Color.Lerp(c, rock, M.Smoothstep(0.28f, 0.5f, slope));
                    c = Color.Lerp(c, rock, M.Smoothstep(22f, 32f, h) * 0.8f);
                    c = Color.Lerp(c, snow, M.Smoothstep(40f, 52f, h) * M.Smoothstep(0.55f, 0.3f, slope));
                    c = Color.Lerp(c, mud, M.Smoothstep(WATER_LEVEL + 2.5f, WATER_LEVEL, h));
                    float s = scorch.Get(x, z);
                    c = Color.Lerp(c, ash, M.Smoothstep(0.35f, 0.6f, s) * 0.85f);
                    c = M.Mul(c, 0.92f + dn * 0.1f);
                    verts[iz * n + ix] = new Vector3(x, h, z);
                    norms[iz * n + ix] = nrm;
                    var lin = new Color(c.r, c.g, c.b).linear;
                    cols[iz * n + ix] = new Color(lin.r, lin.g, lin.b, 1f);
                }
            var tris = new int[RES * RES * 6];
            int k = 0;
            for (int iz = 0; iz < RES; iz++)
                for (int ix = 0; ix < RES; ix++)
                {
                    int a = iz * n + ix, b = a + 1, c2 = a + n, d = c2 + 1;
                    // Unity front faces are clockwise seen from above.
                    tris[k++] = a; tris[k++] = c2; tris[k++] = b;
                    tris[k++] = b; tris[k++] = c2; tris[k++] = d;
                }
            var mesh = MeshGen.Raw(verts, norms, cols, tris, "Terrain");
            var go = new GameObject("TerrainMesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FX.Lit(Color.white, 0.95f);
        }

        void BuildWater()
        {
            var mat = FX.NewLit(new Color(0.12f, 0.22f, 0.26f, 0.82f), 0.08f, 0.3f);
            var r = FX.Part(transform, MeshGen.Box(), mat, new Vector3(0, WATER_LEVEL, 0), new Vector3(HALF * 2f, 0.01f, HALF * 2f));
            r.gameObject.name = "Water";
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- placement

        Vector3 Spot(float minCenter, float maxSlope = 0.22f, Vector3? around = null, float spread = 0f)
        {
            for (int i = 0; i < 30; i++)
            {
                float x, z;
                if (around.HasValue)
                {
                    x = around.Value.x + rng.Range(-spread, spread);
                    z = around.Value.z + rng.Range(-spread, spread);
                }
                else
                {
                    x = rng.Range(-PLAY_LIMIT, PLAY_LIMIT);
                    z = rng.Range(-PLAY_LIMIT, PLAY_LIMIT);
                }
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > PLAY_LIMIT + 10f) continue;
                if (new Vector2(x, z).magnitude < minCenter) continue;
                float h = Height(x, z);
                if (h < WATER_LEVEL + 1f) continue;
                if (1f - NormalAt(x, z).y > maxSlope) continue;
                return new Vector3(x, h, z);
            }
            return M.INF;
        }

        void Place(Transform node, Vector3 p, bool align = false)
        {
            node.SetParent(propsRoot, false);
            node.localPosition = p;
            var rot = Quaternion.Euler(0f, rng.Randf() * 360f, 0f);
            if (align)
            {
                var nrm = NormalAt(p.x, p.z);
                rot = Quaternion.FromToRotation(Vector3.up, nrm) * rot;
            }
            node.localRotation = rot;
            foreach (var r in node.GetComponentsInChildren<Renderer>()) r.gameObject.layer = PROP_LAYER;
            node.gameObject.layer = PROP_LAYER;
        }

        void ScatterProps()
        {
            // Ruined villages.
            for (int v = 0; v < 7; v++)
            {
                var center = Spot(30f, 0.12f);
                if (M.IsInf(center)) continue;
                int houses = rng.RangeInt(4, 7);
                for (int h = 0; h < houses; h++)
                {
                    var p = Spot(16f, 0.16f, center, 22f);
                    if (!M.IsInf(p)) Place(Props.RuinedHouse(rng), p - new Vector3(0, 0.3f, 0));
                }
                int carts = rng.RangeInt(1, 3);
                for (int c = 0; c < carts; c++)
                {
                    var p = Spot(16f, 0.2f, center, 26f);
                    if (!M.IsInf(p)) Place(Props.BrokenCart(rng), p, true);
                }
                var wp = Spot(16f, 0.15f, center, 10f);
                if (!M.IsInf(wp)) Place(Props.Well(rng), wp - new Vector3(0, 0.2f, 0));
            }
            // Battlefields: dead soldiers, fallen banners, blades in the earth.
            for (int b = 0; b < 10; b++)
            {
                var center = Spot(18f, 0.2f);
                if (M.IsInf(center)) continue;
                int n1 = rng.RangeInt(10, 18);
                for (int s = 0; s < n1; s++)
                {
                    var p = Spot(8f, 0.3f, center, 16f);
                    if (!M.IsInf(p)) Place(Props.DeadSoldier(rng), p, true);
                }
                int n2 = rng.RangeInt(3, 6);
                for (int s = 0; s < n2; s++)
                {
                    var p = Spot(8f, 0.3f, center, 18f);
                    if (!M.IsInf(p)) Place(Props.BladeInGround(rng), p);
                }
                int n3 = rng.RangeInt(1, 3);
                for (int s = 0; s < n3; s++)
                {
                    var p = Spot(8f, 0.3f, center, 16f);
                    if (!M.IsInf(p)) Place(Props.Banner(rng), p);
                }
                var cp = Spot(8f, 0.2f, center, 12f);
                if (!M.IsInf(cp)) Place(Props.Campfire(rng), cp, true);
            }
            // Scattered dead bodies along the roads.
            for (int i = 0; i < 60; i++)
            {
                var p = Spot(10f, 0.3f);
                if (!M.IsInf(p)) Place(Props.DeadSoldier(rng), p, true);
            }
            // Ruined watchtowers on hills.
            for (int i = 0; i < 8; i++)
            {
                var p = Spot(40f, 0.18f);
                if (!M.IsInf(p)) Place(Props.Watchtower(rng), p - new Vector3(0, 0.5f, 0));
            }
            // Standing stones.
            for (int i = 0; i < 4; i++)
            {
                var c = Spot(35f, 0.12f);
                if (!M.IsInf(c)) Place(Props.StoneCircle(rng), c - new Vector3(0, 0.3f, 0));
            }
            // Dead forests.
            for (int f = 0; f < 12; f++)
            {
                var center = Spot(20f, 0.3f);
                if (M.IsInf(center)) continue;
                int n = rng.RangeInt(6, 14);
                for (int t = 0; t < n; t++)
                {
                    var p = Spot(10f, 0.35f, center, 18f);
                    if (!M.IsInf(p)) Place(Props.DeadTree(rng), p - new Vector3(0, 0.2f, 0));
                }
            }
            // Rocks everywhere.
            for (int i = 0; i < 140; i++)
            {
                var p = Spot(8f, 0.6f);
                if (!M.IsInf(p)) Place(Props.Rock(rng), p - new Vector3(0, 0.3f, 0), true);
            }
        }
    }
}
