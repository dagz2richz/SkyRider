using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Procedural heightmap world with water and stage scenery: ruins and battlefields (stage 1), forest villages
    /// (stage 2) and crypt ruins (stage 3). Maps are 30% bigger than the original 400 m square.
    public partial class WorldTerrain : MonoBehaviour
    {
        public const float HALF = 260f;
        public const int RES = 260;
        public const float WATER_LEVEL = -6.5f;
        public const float PLAY_LIMIT = 214.5f;
        public const int PROP_LAYER = 8;

        Noise noise, ridge, detail;
        public Noise scorch;
        Rng rng;
        float[] heights;
        float cell;
        Transform propsRoot;
        /// Stage this land belongs to (1..3): its palette and scenery.
        public int stage = 1;
        Data.StageDef Def => Data.STAGES[Mathf.Clamp(stage - 1, 0, Data.STAGES.Length - 1)];

        public void Generate(int seed, int stageNum = 1)
        {
            stage = stageNum;
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
            packRoot = new GameObject("PackProps").transform;
            packRoot.SetParent(transform, false);
            if (stage == 2) ScatterForest();
            else if (stage == 3) ScatterCrypts();
            else
            {
                ScatterProps();
                ScatterGreenery();
            }
            // Floating islands only from the world where the dragon can first carry you; world 1 fights on the ground.
            if (stage >= Data.FLIGHT_STAGE) BuildIslands();
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
            var pal = Def;
            var grass = pal.grass;
            var grass2 = pal.grass2;
            var dry = pal.dry;
            var dirt = pal.dirt;
            var rock = pal.rock;
            var snow = new Color(0.86f, 0.88f, 0.92f);
            var ash = pal.ash;
            var mud = pal.mud;
            float scorchAmt = pal.scorch;
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
                    c = Color.Lerp(c, ash, M.Smoothstep(0.35f, 0.6f, s) * 0.85f * scorchAmt);
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
            var wc = stage == 2 ? new Color(0.1f, 0.24f, 0.24f, 0.82f) : stage == 3 ? new Color(0.08f, 0.1f, 0.16f, 0.85f) : new Color(0.12f, 0.22f, 0.26f, 0.82f);
            var mat = FX.NewLit(wc, 0.08f, 0.3f);
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

        // ---------------------------------------------------------------- Asset Store scenery

        Transform packRoot;

        /// Places a pack prop scaled so its largest dimension is targetSize (0 keeps the prefab's size).
        bool PlacePack(GameObject[] options, Vector3 p, float targetSize, bool align = false) => PlacePackT(options, p, targetSize, align) != null;

        /// Like PlacePack, returning the placed prop (or null).
        Transform PlacePackT(GameObject[] options, Vector3 p, float targetSize, bool align = false)
        {
            var prefab = Skins.Pick(options, rng);
            if (prefab == null) return null;
            var t = Skins.SpawnProp(prefab, packRoot, p, rng.Randf() * 360f, 1f);
            if (targetSize > 0f)
            {
                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (size > 0.01f) t.localScale *= targetSize / size;
                }
            }
            if (align) t.rotation = Quaternion.FromToRotation(Vector3.up, NormalAt(p.x, p.z)) * t.rotation;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) r.gameObject.layer = PROP_LAYER;
            return t;
        }

        SkinLibrary Lib => Skins.Lib;

        void PlaceTree(Vector3 p)
        {
            if (Lib && PlacePack(Lib.deadTrees, p - new Vector3(0, 0.2f, 0), rng.Range(6f, 10f))) return;
            Place(Props.DeadTree(rng), p - new Vector3(0, 0.2f, 0));
        }

        void PlaceRock(Vector3 p)
        {
            if (Lib && PlacePack(Lib.rocks, p - new Vector3(0, 0.25f, 0), rng.Range(0.8f, 3.4f), true)) return;
            Place(Props.Rock(rng), p - new Vector3(0, 0.3f, 0), true);
        }

        void PlaceStoneCircle(Vector3 c)
        {
            if (Lib == null || Lib.standingStones == null || Lib.standingStones.Length == 0)
            {
                Place(Props.StoneCircle(rng), c - new Vector3(0, 0.3f, 0));
                return;
            }
            int count = rng.RangeInt(7, 10);
            for (int i = 0; i < count; i++)
            {
                if (rng.Randf() < 0.15f) continue;
                float a = M.TAU * i / count;
                var p = c + new Vector3(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f);
                p.y = Height(p.x, p.z) - 0.3f;
                PlacePack(Lib.standingStones, p, rng.Range(2.6f, 4.2f));
            }
            // Glowing rune altar at the center.
            var altar = new GameObject("Altar").transform;
            Props.StoneCircleAltar(altar);
            Place(altar, c - new Vector3(0, 0.3f, 0));
        }

        /// Grass, shrubs and stumps from the nature pack (nothing without it).
        void ScatterGreenery()
        {
            if (Lib == null) return;
            for (int i = 0; i < 420; i++)
            {
                var p = Spot(6f, 0.22f);
                if (M.IsInf(p) || p.y > 24f || scorch.Get(p.x, p.z) > 0.4f) continue;
                PlacePack(Lib.grass, p - new Vector3(0, 0.05f, 0), rng.Range(0.9f, 1.8f), true);
            }
            for (int i = 0; i < 140; i++)
            {
                var p = Spot(8f, 0.3f);
                if (M.IsInf(p)) continue;
                PlacePack(Lib.shrubs, p - new Vector3(0, 0.1f, 0), rng.Range(1.2f, 2.2f));
            }
            for (int i = 0; i < 60; i++)
            {
                var p = Spot(8f, 0.3f);
                if (M.IsInf(p)) continue;
                PlacePack(Lib.stumps, p - new Vector3(0, 0.05f, 0), rng.Range(0.8f, 1.6f));
            }
        }

        void ScatterProps()
        {
            // Ruined villages.
            for (int v = 0; v < 7; v++)
            {
                var center = Spot(30f, 0.12f);
                if (M.IsInf(center)) continue;
                bool kit = KitAvailable;
                if (kit) BuildKitVillage(center);
                int houses = kit ? rng.RangeInt(2, 3) : rng.RangeInt(4, 7);
                for (int h = 0; h < houses; h++)
                {
                    var p = Spot(16f, 0.12f, center, 22f);
                    if (M.IsInf(p)) continue;
                    if (kit) BuildKitHouse(p);
                    else Place(Props.RuinedHouse(rng), p - new Vector3(0, 0.3f, 0));
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
                if (M.IsInf(p)) continue;
                if (KitAvailable) BuildKitTower(p);
                else Place(Props.Watchtower(rng), p - new Vector3(0, 0.5f, 0));
            }
            // Standing stones.
            for (int i = 0; i < 4; i++)
            {
                var c = Spot(35f, 0.12f);
                if (!M.IsInf(c)) PlaceStoneCircle(c);
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
                    if (!M.IsInf(p)) PlaceTree(p);
                }
            }
            // Rocks everywhere.
            for (int i = 0; i < 140; i++)
            {
                var p = Spot(8f, 0.6f);
                if (!M.IsInf(p)) PlaceRock(p);
            }
        }
    }
}
