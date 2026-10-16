using UnityEngine;

namespace Skyfall
{
    /// A power-up lying in the world: a glowing, spinning, bobbing model (cyan wings = Swiftness, red sword = Fury,
    /// violet horseshoe magnet = Lodestone) with a light that magnets to the player like an XP gem and is collected on touch.
    public class PowerUpPickup : MonoBehaviour
    {
        const float LIFE = 90f;

        public string id = "speed";
        Game game;
        Transform body, label;
        Light glow;
        bool pulled;
        float age, phase;

        public static PowerUpPickup Spawn(Game g, string id, Vector3 pos)
        {
            var p = new GameObject("PowerUp_" + id).AddComponent<PowerUpPickup>();
            p.game = g;
            p.id = id;
            p.phase = M.RandF() * M.TAU;
            p.transform.position = pos;
            var d = Data.POWERUPS[id];
            p.body = FX.Node(p.transform, "Body", new Vector3(0, 1f, 0));
            switch (id)
            {
                case "speed": p.BuildWings(d.color); break;
                case "damage": p.BuildSword(d.color); break;
                default: p.BuildMagnet(d.color); break;
            }
            // A ground ring, a light and a few drifting sparks make it easy to spot in a crowd.
            FX.NoShadow(FX.Part(p.transform, MeshGen.Ring(), FX.Glow(new Color(d.color.r, d.color.g, d.color.b, 0.6f), 1.6f), new Vector3(0, 0.05f, 0), new Vector3(0.9f, 0.4f, 0.9f)));
            p.glow = new GameObject("Light").AddComponent<Light>();
            p.glow.transform.SetParent(p.transform, false);
            p.glow.transform.localPosition = new Vector3(0, 1.2f, 0);
            p.glow.type = LightType.Point;
            p.glow.color = d.color;
            p.glow.intensity = 1.8f;
            p.glow.range = 6f;
            p.glow.shadows = LightShadows.None;
            var sparks = FX.Node(p.transform, "Sparks", new Vector3(0, 0.6f, 0), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.Particles(sparks, new FX.ParticleSpec
            {
                amount = 10, lifetime = 1.1f, size = 0.16f, speedMin = 0.5f, speedMax = 1.2f, spread = 35f, startColor = Color.Lerp(d.color, Color.white, 0.4f),
                endColor = M.WithA(d.color, 0f), boost = 1.8f, worldSpace = true,
            });
            return p;
        }

        // ---------------------------------------------------------------- models

        static Material G(Color c, float energy = 2.6f) => FX.Glow(c, energy);

        void BuildWings(Color c)
        {
            var hot = Color.Lerp(c, Color.white, 0.7f);
            FX.NoShadow(FX.Part(body, MeshGen.Sphere(12), G(hot, 3f), Vector3.zero, Vector3.one * 0.3f));
            FX.NoShadow(FX.Part(body, MeshGen.Sphere(12), G(M.WithA(c, 0.35f), 1.6f), Vector3.zero, Vector3.one * 0.62f));
            // Three feathers per side, fanned out and up like a wing.
            float[] deg = { 78f, 55f, 100f };
            float[] len = { 0.95f, 0.78f, 0.62f };
            foreach (int s in new[] { -1, 1 })
                for (int i = 0; i < 3; i++)
                {
                    float a = deg[i] * Mathf.Deg2Rad;
                    var dir = new Vector2(s * Mathf.Sin(a), Mathf.Cos(a));
                    var feather = Color.Lerp(c, Color.white, 0.15f * i);
                    FX.NoShadow(FX.Part(body, MeshGen.Cylinder(0f, 0.5f, 6), G(feather, 2.4f), new Vector3(dir.x * len[i] * 0.5f + s * 0.12f, dir.y * len[i] * 0.5f, 0f),
                        new Vector3(0.2f, len[i], 0.04f), new Vector3(0, 0, -s * a)));
                }
        }

        void BuildSword(Color c)
        {
            var hot = Color.Lerp(c, Color.white, 0.55f);
            var tilt = FX.Node(body, "Tilt", Vector3.zero, new Vector3(0, 0, 0.45f));
            FX.NoShadow(FX.Part(tilt, MeshGen.Box(), G(c, 2.8f), new Vector3(0, 0.15f, 0), new Vector3(0.2f, 1.05f, 0.06f)));
            FX.NoShadow(FX.Part(tilt, MeshGen.Cylinder(0f, 0.5f, 4), G(c, 2.8f), new Vector3(0, 0.82f, 0), new Vector3(0.2f, 0.34f, 0.06f)));
            FX.NoShadow(FX.Part(tilt, MeshGen.Box(), G(hot, 3.2f), new Vector3(0, 0.2f, 0), new Vector3(0.06f, 1.0f, 0.075f)));
            FX.Part(tilt, MeshGen.Box(), FX.Lit(new Color(0.95f, 0.75f, 0.3f), 0.3f, 0.9f, 0.5f), new Vector3(0, -0.4f, 0), new Vector3(0.58f, 0.09f, 0.12f));
            FX.Part(tilt, MeshGen.Box(), FX.Lit(new Color(0.25f, 0.06f, 0.06f), 0.6f), new Vector3(0, -0.62f, 0), new Vector3(0.09f, 0.36f, 0.09f));
            FX.Part(tilt, MeshGen.Sphere(8), FX.Lit(new Color(0.95f, 0.75f, 0.3f), 0.3f, 0.9f, 0.5f), new Vector3(0, -0.84f, 0), Vector3.one * 0.14f);
            // A red halo behind the blade.
            FX.NoShadow(FX.Part(body, MeshGen.Ring(), G(M.WithA(c, 0.5f), 1.6f), Vector3.zero, new Vector3(0.75f, 0.4f, 0.75f), new Vector3(M.PI * 0.5f, 0, 0)));
        }

        /// The Lodestone: a horseshoe magnet in glowing violet with silver poles, crackling between its tips.
        void BuildMagnet(Color c)
        {
            var tilt = FX.Node(body, "Tilt", Vector3.zero, new Vector3(0, 0, 0.3f));
            var arm = G(c, 2.6f);
            var silver = FX.Lit(new Color(0.85f, 0.88f, 0.95f), 0.2f, 0.95f, 0.6f);
            // The bend: a half ring of segments.
            const int segs = 9;
            const float r = 0.36f;
            for (int i = 0; i <= segs; i++)
            {
                float a = M.PI * i / segs;
                var p = new Vector3(Mathf.Cos(a) * r, -Mathf.Sin(a) * r, 0f);
                FX.NoShadow(FX.Part(tilt, MeshGen.Box(), arm, p, new Vector3(0.2f, 0.16f, 0.2f), new Vector3(0, 0, a)));
            }
            foreach (int s in new[] { -1, 1 })
            {
                FX.NoShadow(FX.Part(tilt, MeshGen.Box(), arm, new Vector3(s * r, 0.22f, 0), new Vector3(0.2f, 0.44f, 0.2f)));
                FX.NoShadow(FX.Part(tilt, MeshGen.Box(), silver, new Vector3(s * r, 0.53f, 0), new Vector3(0.22f, 0.18f, 0.22f)));
            }
            // A crackle between the poles.
            FX.NoShadow(FX.Part(tilt, MeshGen.Box(), G(Color.Lerp(c, Color.white, 0.6f), 3.5f), new Vector3(0, 0.6f, 0), new Vector3(0.56f, 0.03f, 0.03f)));
            FX.NoShadow(FX.Part(body, MeshGen.Ring(), G(M.WithA(c, 0.5f), 1.6f), Vector3.zero, new Vector3(0.8f, 0.4f, 0.8f), new Vector3(M.PI * 0.5f, 0, 0)));
        }

        /// Starts flying to the player right away (the Lodestone pulls other pickups too).
        public void Pull() => pulled = true;

        // ---------------------------------------------------------------- behavior

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f || game == null || game.player == null) return;
            var player = game.player;
            if (game.ended) { Destroy(gameObject); return; }
            age += delta;
            // Fade out with a flicker in the last 10 seconds.
            bool show = age < LIFE - 10f || ((int)(age * 6f) % 2 == 0);
            body.gameObject.SetActive(show);
            if (label) label.gameObject.SetActive(show);
            if (age >= LIFE) { Destroy(gameObject); return; }

            float bob = 1f + Mathf.Sin(age * 3f + phase) * 0.22f;
            body.localPosition = new Vector3(0, bob, 0);
            body.rotation = Quaternion.Euler(0, (age * 120f + phase * 57f) % 360f, 0);
            if (label)
            {
                label.localPosition = new Vector3(0, bob + 0.85f, 0);
                var cam = player.cam;
                if (cam) label.rotation = Quaternion.LookRotation(label.position - cam.transform.position, Vector3.up);
            }

            var c = player.Center;
            var to = c - body.position;
            float d = to.magnitude;
            if (!pulled && d < player.MagnetRadius) pulled = true;
            if (pulled) transform.position += to.normalized * Mathf.Min(d, delta * (18f + 30f / Mathf.Max(d, 0.5f)));
            if (d < 1.6f && game.running)
            {
                player.GrantPowerUp(id);
                Destroy(gameObject);
            }
        }
    }
}
