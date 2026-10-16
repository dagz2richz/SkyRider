using UnityEngine;

namespace Skyfall
{
    /// The Mountain King's reward: a glowing egg in the color of a dragon the player does not own yet. It bobs and spins,
    /// then flies to the player (when near, or after a couple of seconds) and unlocks that dragon.
    public class DragonEgg : MonoBehaviour
    {
        Game game;
        public string dragonId;
        /// Test hook: keeps the egg from flying to the player.
        public bool hold;
        float t;
        bool pulled;
        Vector3 home;
        Transform shell;
        static Mesh eggMesh;

        /// Egg-shaped mesh: a sphere with a narrower top.
        static Mesh EggMesh()
        {
            if (eggMesh) return eggMesh;
            var src = MeshGen.Sphere(24);
            var v = src.vertices;
            var n = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float y = v[i].y;                                    // -0.5 .. 0.5
                float k = y > 0f ? 1f - 0.34f * (y / 0.5f) : 1f;    // pointy top, round bottom
                v[i] = new Vector3(v[i].x * k, y * 1.35f, v[i].z * k);
            }
            eggMesh = new Mesh { name = "Egg", vertices = v, triangles = src.triangles, colors = src.colors };
            eggMesh.RecalculateNormals();
            eggMesh.RecalculateBounds();
            return eggMesh;
        }

        public static DragonEgg Spawn(Game g, string dragonId, Vector3 pos)
        {
            var e = new GameObject("DragonEgg_" + dragonId).AddComponent<DragonEgg>();
            e.game = g;
            e.dragonId = dragonId;
            e.transform.SetParent(g.transform, true);
            e.home = pos + new Vector3(0, 1.5f, 0);
            e.transform.position = e.home;
            var d = Data.DRAGONS[dragonId];
            var body = d.body;
            var edge = M.Lighten(d.breath, 0.15f);
            e.shell = FX.Node(e.transform, "Shell");
            // Glossy shell in the dragon's body color, with a lighter band of scales and speckles of its breath color.
            FX.Part(e.shell, EggMesh(), FX.NewLit(body, 0.18f, 0.35f, 0.25f), Vector3.zero, Vector3.one * 1.6f);
            var scale = FX.Lit(M.Lighten(body, 0.25f), 0.3f, 0.5f);
            var speck = FX.Glow(edge, 2.2f);
            var sph = MeshGen.Sphere(8);
            var rng = new System.Random(dragonId.GetHashCode());
            for (int i = 0; i < 26; i++)
            {
                float y = (float)rng.NextDouble() * 1.7f - 0.75f;           // -0.75..0.95 (egg height ~1.08 each side)
                float a = (float)rng.NextDouble() * M.TAU;
                float rad = 0.5f * 1.6f * Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow(y / (0.675f * 1.6f), 2f))) * (y > 0 ? 1f - 0.34f * (y / (0.675f * 1.6f)) : 1f);
                var p = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                bool glow = i % 3 == 0;
                FX.Part(e.shell, sph, glow ? speck : scale, p, Vector3.one * (glow ? 0.07f : 0.16f), Vector3.zero);
            }
            // Soft aura, light and sparkles.
            FX.NoShadow(FX.Part(e.transform, MeshGen.Sphere(12), FX.FadeMat(new Color(edge.r, edge.g, edge.b, 0.07f)), Vector3.zero, Vector3.one * 3.4f));
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(e.transform, false);
            light.type = LightType.Point;
            light.color = edge;
            light.intensity = 3.5f;
            light.range = 12f;
            light.shadows = LightShadows.None;
            FX.Particles(e.transform, new FX.ParticleSpec
            {
                amount = 18, lifetime = 1.4f, size = 0.22f, scaleMin = 0.5f, scaleMax = 1.2f, speedMin = 0.3f, speedMax = 1.2f,
                sphereRadius = 1.1f, gravity = new Vector3(0, 0.8f, 0), startColor = new Color(1f, 1f, 1f, 0.9f),
                midColor = edge, endColor = new Color(edge.r, edge.g, edge.b, 0f), boost = 2.5f,
            });
            FX.Vfx("buff", pos, 1.5f, 2.5f, null, 0f);
            Sfx.Play("levelup", pos, -6f, 0f);
            return e;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || game == null || game.player == null) return;
            t += dt;
            shell.localRotation = Quaternion.Euler(0, t * 50f, 8f * Mathf.Sin(t * 1.5f));
            var c = game.player.Center;
            float d = Vector3.Distance(c, transform.position);
            if (!pulled && !hold && (d < 9f || t > 2f)) pulled = true;
            if (pulled)
            {
                var to = c - transform.position;
                transform.position += to.normalized * Mathf.Min(d, dt * (14f + 28f / Mathf.Max(d, 0.5f) + t * 2f));
                if (d < 1.6f)
                {
                    game.CollectEgg(dragonId);
                    FX.Flash(transform.position, 4f, new Color(1f, 1f, 1f, 0.6f), 0.4f);
                    Destroy(gameObject);
                }
            }
            else transform.position = home + new Vector3(0, Mathf.Sin(t * 2f) * 0.25f, 0);
        }
    }
}
