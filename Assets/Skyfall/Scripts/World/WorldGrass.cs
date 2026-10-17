using UnityEngine;

namespace Skyfall
{
    /// Short GPU-instanced grass (Point Grass Renderer package) over the ground and the floating islands: lush in the
    /// Verdant Marches, dry and sparse in the Burning Fields, thin grey tufts in the Sunken Crypts. Needs
    /// SkinLibrary.grassMaterial; without it (or without GPU support) the land is simply bare.
    public partial class WorldTerrain
    {
        GrassField grass;

        /// Blades per square meter, the look and the scorch tolerance for a stage.
        static void GrassStyleFor(int stage, out GrassField.Style style, out float density, out float maxScorch)
        {
            switch (stage)
            {
                case 2:    // lush green
                    style = new GrassField.Style
                    {
                        bottom = new Color(0.08f, 0.30f, 0.08f), top = new Color(0.34f, 0.62f, 0.16f), subsurface = new Color(0.36f, 0.52f, 0.10f),
                        height = 0.42f, width = 0.07f, bright = 1f
                    };
                    density = 0.5f; maxScorch = 1f;
                    break;
                case 3:    // dead grey-green tufts
                    style = new GrassField.Style
                    {
                        bottom = new Color(0.10f, 0.13f, 0.10f), top = new Color(0.34f, 0.38f, 0.30f), subsurface = new Color(0.24f, 0.28f, 0.18f),
                        height = 0.30f, width = 0.06f, bright = 0.9f
                    };
                    density = 0.07f; maxScorch = 0.5f;
                    break;
                default:   // dry, scorched golden-brown
                    style = new GrassField.Style
                    {
                        bottom = new Color(0.30f, 0.22f, 0.09f), top = new Color(0.66f, 0.52f, 0.24f), subsurface = new Color(0.55f, 0.40f, 0.15f),
                        height = 0.34f, width = 0.065f, bright = 1f
                    };
                    density = 0.12f; maxScorch = 0.42f;
                    break;
            }
        }

        void BuildGrass(int seed)
        {
            if (Lib == null || Lib.grassMaterial == null) return;
            GrassStyleFor(stage, out var style, out float density, out float maxScorch);
            grass = GrassField.Create(transform, Lib.grassMaterial, style);
            if (grass == null) return;
            // Its own random stream so the grass never shifts where the scenery goes.
            var g = new Rng(seed + 4421);
            var patch = new Noise(seed + 31, 0.045f, 3);

            // Ground: random points over the playable land, thinned by slope, height, water, scorch and clutter.
            float lim = PLAY_LIMIT + 6f;
            int attempts = Mathf.Min((int)(density * 4f * lim * lim), 110000);
            for (int i = 0; i < attempts; i++)
            {
                float x = g.Range(-lim, lim), z = g.Range(-lim, lim);
                float h = Height(x, z);
                if (h < WATER_LEVEL + 0.7f || h > 26f) continue;
                float slope = 1f - NormalAt(x, z).y;
                if (g.Randf() > 1f - M.Smoothstep(0.08f, 0.24f, slope)) continue;
                // Clumps and bare patches.
                float clump = M.Smoothstep(-0.35f, 0.4f, patch.Get(x, z));
                if (g.Randf() > clump) continue;
                if (scorch.Get(x, z) > maxScorch) continue;
                if (stage == 2 && NearVillage(new Vector3(x, h, z), 11f)) continue;
                if (PointInObstacle(new Vector3(x, h, z), 1.2f)) continue;
                grass.Add(new Vector3(x, h - 0.03f, z), g.Range(0.6f, 1.2f), g.Randf());
            }

            // Floating islands: their flat green tops, clear of the rim and the ruins.
            foreach (var isl in islands)
            {
                int n = (int)(density * 0.9f * Mathf.PI * isl.radius * isl.radius);
                for (int i = 0; i < n; i++)
                {
                    float a = g.Randf() * M.TAU;
                    float r = isl.EdgeRadius(a) * 0.93f * Mathf.Sqrt(g.Randf());
                    var p = OnIsland(isl, r, a);
                    p.y = isl.center.y;
                    if (PointInObstacle(p, 1.2f)) continue;
                    grass.Add(p + new Vector3(0, -0.03f, 0), g.Range(0.6f, 1.2f), g.Randf());
                }
            }
            grass.Finish();
        }
    }
}
