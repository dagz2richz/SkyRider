using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// The warlord's prize: one of the dragons the player does not own, chained down on the boss's island. Walking up to it
    /// breaks the chains (a short struggle, then a flash); the dragon roars, flies to the hero and is unlocked for good.
    /// If the hero never comes, the dragon tears loose by itself after a while and flies to them, so a run cannot stall.
    public class DragonCaptive : MonoBehaviour
    {
        /// How close the hero must be (meters, flat) for the chains to start breaking.
        public const float FREE_RADIUS = 6f;
        const float STRUGGLE_TIME = 1.4f;
        const float AUTO_FREE_AFTER = 45f;
        /// The procedural dragon is 12 units long at scale 1; the captive is drawn at this share of that.
        const float SCALE = 0.5f;
        const float DRAGON_LENGTH = 12f;

        static readonly string[] SK_IDLE = { "Idle", "Idle01" };
        static readonly string[] SK_ROAR = { "Scream", "Fly Fireball Shoot", "Idle" };
        static readonly string[] SK_FLY = { "Fly Forward", "Fly Float", "Idle" };

        Game game;
        public string dragonId;
        /// Test hook: keeps the chains from breaking.
        public bool hold;
        float t;
        float struggle = -1f;
        bool snapped;
        Vector3 home;
        Transform body;
        CharacterRig skin;
        DragonModel fallback;
        readonly List<Material> mats = new List<Material>();
        readonly List<GameObject> chainParts = new List<GameObject>();
        Light glow;

        public bool Freeing => struggle >= 0f;

        public static DragonCaptive Spawn(Game g, string dragonId, Vector3 pos)
        {
            var c = new GameObject("DragonCaptive_" + dragonId).AddComponent<DragonCaptive>();
            c.game = g;
            c.dragonId = dragonId;
            c.transform.SetParent(g.transform, true);
            c.transform.position = pos;
            c.home = pos;
            c.Build(Data.DRAGONS[dragonId]);
            FX.Vfx("buff", pos, 1.5f, 2.5f, null, 0f);
            Sfx.Play("roar", pos, -6f, 0f);
            return c;
        }

        void OnDestroy()
        {
            foreach (var m in mats) if (m) Destroy(m);
        }

        void Build(Data.DragonDef d)
        {
            body = FX.Node(transform, "Body");
            var prefab = Skins.Lib ? Skins.Lib.Dragon(d.skin) : null;
            if (prefab != null)
            {
                // The same pack model and tint as the dragon partner, measured in a neutral pose and scaled to match.
                var holder = FX.Node(body, "DragonSkin");
                var go = Instantiate(prefab, holder, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                skin = holder.gameObject.AddComponent<CharacterRig>();
                skin.Init(go, null);
                float len = Mathf.Max(skin.Bounds().size.z, 0.01f);
                holder.localScale = Vector3.one * (DRAGON_LENGTH / len);
                if (d.skinTint != Color.white) Tint(go.transform, d.skinTint);
                skin.Loop(SK_IDLE);
            }
            else
            {
                var go = new GameObject("DragonModel");
                go.transform.SetParent(body, false);
                fallback = go.AddComponent<DragonModel>().Setup(d);
                fallback.fold = 1f;
            }
            body.localScale = Vector3.one * SCALE;
            body.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Four iron posts with glowing runes, chained to the dragon's back, wings and tail.
            var iron = FX.NewLit(new Color(0.16f, 0.15f, 0.16f), 0.6f, 0.7f, 0f);
            var rune = FX.Glow(new Color(1f, 0.35f, 0.12f), 2.4f);
            var cyl = MeshGen.Cylinder(0.5f, 0.5f, 8);
            var link = MeshGen.Torus(0.7f, 1f, 12, 5);
            float r = 4.4f;
            for (int i = 0; i < 4; i++)
            {
                float a = M.TAU * (i + 0.5f) / 4f;
                var post = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                var top = post + new Vector3(0f, 2.6f, 0f);
                chainParts.Add(FX.Part(transform, cyl, iron, post + new Vector3(0, 1.3f, 0), new Vector3(0.45f, 2.6f, 0.45f)).gameObject);
                chainParts.Add(FX.Part(transform, MeshGen.Sphere(8), rune, top, Vector3.one * 0.38f).gameObject);
                var to = new Vector3(Mathf.Cos(a) * 0.9f, 1.3f, Mathf.Sin(a) * 0.9f);
                var dir = to - top;
                int links = 7;
                for (int k = 1; k < links; k++)
                {
                    var p = top + dir * (k / (float)links) + new Vector3(0f, -0.35f * Mathf.Sin(M.PI * k / links), 0f);
                    var lk = FX.Part(transform, link, iron, p, Vector3.one * 0.3f);
                    lk.transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 0f : 90f);
                    chainParts.Add(lk.gameObject);
                }
                chainParts.Add(FX.Part(transform, MeshGen.Torus(0.75f, 1f, 16, 6), iron, to, Vector3.one * 0.7f, new Vector3(M.PI * 0.5f, 0, 0)).gameObject);
            }
            // The warlord's seal on the ground and a faint ember glow.
            var seal = FX.Part(transform, MeshGen.Ring(), FX.Glow(new Color(1f, 0.3f, 0.1f, 0.5f), 1.6f), new Vector3(0, 0.12f, 0), new Vector3(r, 0.2f, r));
            FX.NoShadow(seal);
            chainParts.Add(seal.gameObject);
            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0, 3f, 0);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.5f, 0.25f);
            glow.intensity = 2.5f;
            glow.range = 14f;
            glow.shadows = LightShadows.None;
        }

        void Tint(Transform root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.materials;
                foreach (var m in ms)
                {
                    if (m == null) continue;
                    mats.Add(m);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * tint);
                }
                r.materials = ms;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || game == null || game.player == null || game.ended) return;
            t += dt;
            if (snapped) return;
            var pp = game.player.Pos;
            float flat = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(home.x, home.z));
            bool near = flat < FREE_RADIUS && Mathf.Abs(pp.y - home.y) < 8f;
            if (struggle < 0f && !hold && (near || t > AUTO_FREE_AFTER))
            {
                struggle = 0f;
                if (skin) skin.Action(STRUGGLE_TIME, SK_ROAR);
                Sfx.Play("roar", home, -3f, 0f);
            }
            // Idle: a slow breath. Struggling: it thrashes harder and the runes flare.
            float amp = struggle >= 0f ? 0.25f + struggle * 0.35f : 0.05f;
            body.localPosition = new Vector3(Mathf.Sin(t * (struggle >= 0f ? 28f : 1.3f)) * amp * 0.4f, Mathf.Abs(Mathf.Sin(t * 1.3f)) * 0.08f, 0f);
            if (glow) glow.intensity = struggle >= 0f ? 2.5f + struggle * 6f : 2.5f + Mathf.Sin(t * 2f) * 0.6f;
            if (struggle < 0f) return;
            struggle += dt;
            if (Random.value < dt * 20f) FX.Burst(home + new Vector3(Random.Range(-3f, 3f), Random.Range(0.5f, 3f), Random.Range(-3f, 3f)), new Color(1f, 0.5f, 0.2f), 4, 5f, 0.18f);
            if (struggle >= STRUGGLE_TIME) StartCoroutine(Break());
        }

        /// The chains snap: a flash, the posts shatter, the dragon takes off toward the hero and is unlocked.
        System.Collections.IEnumerator Break()
        {
            snapped = true;
            var at = home + new Vector3(0, 2f, 0);
            FX.Flash(at, 9f, new Color(1f, 0.8f, 0.5f, 0.85f), 0.5f);
            FX.Shockwave(home + new Vector3(0, 0.3f, 0), 10f, new Color(1f, 0.6f, 0.25f, 0.9f), 0.5f);
            FX.Burst(at, new Color(1f, 0.6f, 0.25f), 40, 12f, 0.3f);
            Sfx.Play("roar", 4f, 0f);
            game.Shake(0.7f);
            foreach (var c in chainParts) if (c) Destroy(c);
            chainParts.Clear();
            if (glow) glow.intensity = 6f;
            if (skin) skin.Action(1.2f, SK_ROAR);
            yield return Tw.Wait(0.9f);
            if (this == null || game == null || game.ended) yield break;
            // Up and over to the hero, shrinking into the bond.
            if (skin) skin.Loop(SK_FLY);
            var from = transform.position;
            float k = 0f;
            while (k < 1f && game != null && game.player != null)
            {
                k += Time.deltaTime / 1.1f;
                var to = game.player.Center;
                var p = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k));
                p.y += Mathf.Sin(k * M.PI) * 6f;
                transform.position = p;
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, k * k);
                transform.rotation = Quaternion.LookRotation(M.Flat(to - from) + new Vector3(0.001f, 0f, 0f));
                yield return null;
            }
            if (game != null)
            {
                FX.Flash(transform.position, 4f, new Color(1f, 1f, 1f, 0.6f), 0.4f);
                game.FreeDragon(dragonId);
            }
            Destroy(gameObject);
        }
    }
}
