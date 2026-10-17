using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Shared materials, part builders and short-lived visual effects.
    /// Colors are given in sRGB like the Godot original and converted to linear for the shaders.
    public static class FX
    {
        public static Transform root;
        public static int liveBursts;

        static Shader litShader, unlitShader;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Texture2D softDot;

        public static Shader LitShader => litShader ? litShader : (litShader = Shader.Find("Skyfall/Lit"));
        public static Shader UnlitShader => unlitShader ? unlitShader : (unlitShader = Shader.Find("Skyfall/Unlit"));

        // ---------------------------------------------------------------- color helpers

        /// sRGB color (optionally HDR-scaled) to the linear vector the shaders expect.
        public static Vector4 Lin(Color c, float mult = 1f)
        {
            var l = new Color(c.r, c.g, c.b).linear;
            return new Vector4(l.r * mult, l.g * mult, l.b * mult, c.a);
        }

        public static void SetLin(Material m, string prop, Color c, float mult = 1f) => m.SetVector(prop, Lin(c, mult));

        static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        // ---------------------------------------------------------------- materials

        /// Lit material (shared, cached). rough/metal like Godot; emission > 0 makes it glow in its own color.
        public static Material Lit(Color color, float rough = 0.85f, float metal = 0f, float emission = 0f)
        {
            string key = $"l{ColorUtility.ToHtmlStringRGBA(color)}_{rough:F2}_{metal:F2}_{emission:F2}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = NewLit(color, rough, metal, emission);
            mats[key] = m;
            return m;
        }

        /// A fresh (unshared) lit material.
        public static Material NewLit(Color color, float rough = 0.85f, float metal = 0f, float emission = 0f)
        {
            var m = new Material(LitShader);
            SetLin(m, "_BaseColor", color);
            m.SetFloat("_Smoothness", 1f - rough);
            m.SetFloat("_Metallic", metal);
            SetLin(m, "_EmissionColor", emission > 0f ? color : Color.black, emission);
            if (color.a < 1f) MakeTransparent(m, false);
            return m;
        }

        /// Unlit glowing material (shared, cached). Energy above 1 blooms.
        public static Material Glow(Color color, float energy = 2f)
        {
            string key = $"g{ColorUtility.ToHtmlStringRGBA(color)}_{energy:F2}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(UnlitShader);
            SetLin(m, "_Color", color, energy);
            m.SetFloat("_Cull", (float)CullMode.Off);
            if (color.a < 1f) MakeTransparent(m, false);
            mats[key] = m;
            return m;
        }

        /// A fresh (unshared) transparent unlit material, so it can be faded on its own.
        public static Material FadeMat(Color color, bool additive = true)
        {
            var m = new Material(UnlitShader);
            SetLin(m, "_Color", color);
            m.SetFloat("_Cull", (float)CullMode.Off);
            MakeTransparent(m, additive);
            return m;
        }

        /// Sets only the alpha of a fade material's color.
        public static void SetAlpha(Material m, float a)
        {
            var c = m.GetVector("_Color");
            c.w = a;
            m.SetVector("_Color", c);
        }

        public static Texture2D SoftDot
        {
            get
            {
                if (softDot) return softDot;
                const int N = 64;
                softDot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                softDot.SetPixels32(px);
                softDot.Apply();
                return softDot;
            }
        }

        /// Particle material: soft round sprite, vertex colored, additive or alpha blended, with an HDR boost.
        public static Material ParticleMat(bool additive = true, float boost = 1f)
        {
            string key = $"p{additive}_{boost:F2}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(UnlitShader);
            m.SetVector("_Color", new Vector4(boost, boost, boost, 1f));
            m.SetTexture("_MainTex", SoftDot);
            m.SetFloat("_Cull", (float)CullMode.Off);
            MakeTransparent(m, additive);
            mats[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- builders

        /// Adds a mesh part under parent. rot is Euler radians (Godot convention).
        public static MeshRenderer Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scl, Vector3 rot = default)
        {
            var go = new GameObject(mesh ? mesh.name : "Part");
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = M.Euler(rot);
            t.localScale = scl;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (mat && mat.renderQueue >= (int)RenderQueue.Transparent) r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        public static Transform Node(Transform parent, string name, Vector3 pos = default, Vector3 rot = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = M.Euler(rot);
            return t;
        }

        /// A limb/bone spanning a -> b (local to parent), tapering from radius rA to rB.
        public static MeshRenderer Bone(Transform parent, Vector3 a, Vector3 b, float rA, float rB, Material mat)
        {
            var mesh = MeshGen.Cylinder(Mathf.Clamp(rB / Mathf.Max(rA, 0.001f), 0f, 4f) * 0.5f, 0.5f, 8);
            var d = b - a;
            var r = Part(parent, mesh, mat, (a + b) * 0.5f, new Vector3(rA * 2f, d.magnitude, rA * 2f));
            r.transform.localRotation = M.AlongY(d);
            return r;
        }

        public static void NoShadow(Renderer r) { if (r) r.shadowCastingMode = ShadowCastingMode.Off; }

        // ---------------------------------------------------------------- particles

        public class ParticleSpec
        {
            public int amount = 16;
            public float lifetime = 0.5f;
            public float size = 0.5f;
            public float scaleMin = 1f, scaleMax = 1f;
            public float speedMin, speedMax;
            public float spread = 0f;                 // cone half-angle in degrees; >= 180 means all directions
            public Vector3 gravity = Vector3.zero;
            public Color startColor = Color.white, endColor = new Color(1, 1, 1, 0);
            public Color? midColor;
            public float midAt = 0.4f;
            public bool additive = true;
            public float boost = 1f;
            public bool oneShot;
            public bool worldSpace = true;
            public bool emitting = true;
            public Vector3 boxExtents;                // > 0 makes a box emitter
            public float sphereRadius;                // > 0 makes a sphere emitter
            public float radialSpeed;                 // pulls (negative) or pushes particles from the emitter
            public bool stretch;                      // stretched billboards (rain)
            public float stretchScale = 0.04f;        // how long stretched particles get per unit of speed
            public float drag;                        // > 0: particles burst out fast and brake (the "punch" of an impact)
            public bool shrink;                       // particles shrink to nothing over their lifetime
        }

        /// Creates a particle system emitting along the transform's local +Z.
        public static ParticleSystem Particles(Transform parent, ParticleSpec s)
        {
            var go = new GameObject("Particles");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.05f, s.lifetime);
            main.loop = !s.oneShot;
            main.startLifetime = s.lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speedMin, Mathf.Max(s.speedMin, s.speedMax));
            main.startSize = new ParticleSystem.MinMaxCurve(s.size * s.scaleMin, s.size * s.scaleMax);
            main.simulationSpace = s.worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.Max(8, s.amount * 2);
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            if (s.oneShot)
            {
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)s.amount) });
            }
            else em.rateOverTime = s.amount / Mathf.Max(0.05f, s.lifetime);

            var sh = ps.shape;
            sh.enabled = true;
            if (s.boxExtents != Vector3.zero)
            {
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = s.boxExtents * 2f;
            }
            else if (s.sphereRadius > 0f || s.spread >= 179f)
            {
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = Mathf.Max(0.01f, s.sphereRadius);
                sh.radiusThickness = s.sphereRadius > 0f ? 1f : 1f;
            }
            else
            {
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = Mathf.Clamp(s.spread, 0f, 89f);
                sh.radius = 0.02f;
            }

            if (s.gravity != Vector3.zero)
            {
                var f = ps.forceOverLifetime;
                f.enabled = true;
                f.space = ParticleSystemSimulationSpace.World;
                f.x = new ParticleSystem.MinMaxCurve(s.gravity.x);
                f.y = new ParticleSystem.MinMaxCurve(s.gravity.y);
                f.z = new ParticleSystem.MinMaxCurve(s.gravity.z);
            }

            if (s.radialSpeed != 0f)
            {
                var v = ps.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.Local;
                v.x = new ParticleSystem.MinMaxCurve(0f);
                v.y = new ParticleSystem.MinMaxCurve(0f);
                v.z = new ParticleSystem.MinMaxCurve(0f);
                v.radial = new ParticleSystem.MinMaxCurve(s.radialSpeed);
            }

            if (s.drag > 0f)
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.drag = s.drag;
                lim.multiplyDragByParticleSize = false;
                lim.multiplyDragByParticleVelocity = true;
            }

            if (s.shrink)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f)));
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(MakeGradient(s));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = ParticleMat(s.additive, s.boost);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (s.stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = s.stretchScale;
                r.lengthScale = 2f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;

            if (s.emitting) ps.Play();
            return ps;
        }

        static Gradient MakeGradient(ParticleSpec s)
        {
            var g = new Gradient();
            var cols = new List<GradientColorKey>();
            var alphas = new List<GradientAlphaKey>();
            cols.Add(new GradientColorKey(Clamp01(s.startColor), 0f));
            alphas.Add(new GradientAlphaKey(s.startColor.a, 0f));
            if (s.midColor.HasValue)
            {
                cols.Add(new GradientColorKey(Clamp01(s.midColor.Value), s.midAt));
                alphas.Add(new GradientAlphaKey(s.midColor.Value.a, s.midAt));
            }
            cols.Add(new GradientColorKey(Clamp01(s.endColor), 1f));
            alphas.Add(new GradientAlphaKey(s.endColor.a, 1f));
            g.SetKeys(cols.ToArray(), alphas.ToArray());
            return g;
        }

        static Color Clamp01(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        /// Recolors a particle system's gradient.
        public static void SetParticleColors(ParticleSystem ps, Color start, Color end, Color? mid = null, float midAt = 0.2f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(MakeGradient(new ParticleSpec { startColor = start, endColor = end, midColor = mid, midAt = midAt }));
        }

        // ---------------------------------------------------------------- effects

        static Transform FxNode(string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.position = pos;
            return t;
        }

        public static void Flash(Vector3 pos, float radius, Color color, float dur = 0.3f)
        {
            if (root == null) return;
            var m = FadeMat(color);
            var r = Part(root, MeshGen.Sphere(14), m, Vector3.zero, Vector3.one * radius * 0.5f);
            r.transform.position = pos;
            NoShadow(r);
            float a0 = color.a;
            var t = r.transform;
            Tw.Run(Tw.Seq(
                // Snaps open (most of the growth in the first fifth) and fades fast, so it reads as a hard flash.
                Tw.Lerp(dur, k =>
                {
                    t.localScale = Vector3.one * Mathf.Lerp(radius * 0.8f, radius * 2.2f, Tw.Apply(Ease.OutQuint, k));
                    SetAlpha(m, Mathf.Min(1f, a0 * 1.25f) * (1f - k) * (1f - k));
                }, t),
                Tw.Call(() => { if (t) Object.Destroy(t.gameObject); Object.Destroy(m); })));
        }

        public static void Shockwave(Vector3 pos, float radius, Color color, float dur = 0.4f, bool additive = true)
        {
            if (root == null) return;
            var m = FadeMat(color, additive);
            var r = Part(root, MeshGen.Ring(), m, Vector3.zero, new Vector3(0.3f, 2f, 0.3f));
            r.transform.position = pos;
            NoShadow(r);
            float a0 = color.a;
            var t = r.transform;
            Tw.Run(Tw.Seq(
                // Rips outward (out-quint) to a little past its radius, thinning as it goes.
                Tw.Lerp(dur * 0.85f, k =>
                {
                    float e = Tw.Apply(Ease.OutQuint, k);
                    t.localScale = Vector3.Lerp(new Vector3(0.3f, 3.2f, 0.3f), new Vector3(radius * 1.1f, 1.6f, radius * 1.1f), e);
                    SetAlpha(m, Mathf.Min(1f, a0 * 1.2f) * (1f - k * k));
                }, t),
                Tw.Call(() => { if (t) Object.Destroy(t.gameObject); Object.Destroy(m); })));
        }

        public static void Bolt(Vector3 a, Vector3 b, Color color, float width = 0.18f, float dur = 0.18f, int segs = 7)
        {
            if (root == null) return;
            var holder = FxNode("Bolt", Vector3.zero);
            var m = FadeMat(color);
            var prev = a;
            var d = b - a;
            for (int i = 1; i <= segs; i++)
            {
                float t = (float)i / segs;
                var p = a + d * t;
                if (i < segs)
                    p += new Vector3(M.RandRange(-1f, 1f), M.RandRange(-1f, 1f), M.RandRange(-1f, 1f)) * d.magnitude * 0.06f;
                var sd = p - prev;
                var r = Part(holder, MeshGen.Box(), m, (p + prev) * 0.5f, new Vector3(width, sd.magnitude, width));
                r.transform.localRotation = M.AlongY(sd);
                NoShadow(r);
                prev = p;
            }
            float a0 = color.a;
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k => SetAlpha(m, a0 * (1f - k)), holder),
                Tw.Call(() => { if (holder) Object.Destroy(holder.gameObject); Object.Destroy(m); })));
        }

        /// An impact burst in two layers: hot streaking sparks that shoot out fast and brake hard (white-hot at the
        /// start, cooling to the color), and chunkier blobs of the color that pop out, fall and shrink away.
        public static void Burst(Vector3 pos, Color color, int amount = 14, float speed = 6f, float size = 0.25f)
        {
            if (root == null || liveBursts > 40) return;
            liveBursts++;
            var holder = FxNode("Burst", pos);
            Particles(holder, new ParticleSpec
            {
                amount = Mathf.Max(4, amount * 2 / 3), lifetime = 0.32f, size = size * 0.55f, scaleMin = 0.7f, scaleMax = 1.2f,
                speedMin = speed * 1.6f, speedMax = speed * 3.2f, spread = 180f, drag = 7f, stretch = true, stretchScale = 0.06f,
                startColor = Color.Lerp(color, Color.white, 0.7f), midColor = color, midAt = 0.3f, endColor = M.WithA(color, 0f),
                boost = 2.4f, oneShot = true, shrink = true,
            });
            Particles(holder, new ParticleSpec
            {
                amount = amount, lifetime = 0.5f, size = size * 1.15f, scaleMin = 0.6f, scaleMax = 1.5f,
                speedMin = speed * 0.6f, speedMax = speed * 1.5f, spread = 180f, gravity = new Vector3(0, -12, 0), drag = 3f,
                startColor = Color.Lerp(color, Color.white, 0.25f), endColor = M.WithA(color, 0f), boost = 1.8f, oneShot = true, shrink = true,
            });
            Tw.After(0.8f, () =>
            {
                liveBursts--;
                if (holder) Object.Destroy(holder.gameObject);
            });
        }

        // ---------------------------------------------------------------- Asset Store effects

        /// Enemy-caused explosions, death puffs and impacts are always this red.
        public static readonly Color EnemyRed = new Color(0.9f, 0.1f, 0.08f);

        public static int liveVfx;
        static readonly Dictionary<string, float> vfxLast = new Dictionary<string, float>();

        /// Plays a one-shot effect prefab from the skin library (does nothing when the packs are not set up).
        /// minGap throttles how often the same effect can start, so big fights stay readable and fast.
        /// With a tint the effect is recolored: hue moves to the tint's (keeping each color's brightness and some of its saturation),
        /// in the particles' start colors, color-over-lifetime gradients, trails, lights and the particle materials' colors.
        public static bool Vfx(string name, Vector3 pos, float scale = 1f, float life = 2f, Quaternion? rot = null, float minGap = 0.04f, Color? tint = null)
        {
            var lib = Skins.Lib;
            if (lib == null || root == null || liveVfx >= 45) return false;
            var prefab = lib.Vfx(name);
            if (prefab == null) return false;
            float now = Time.time;
            if (vfxLast.TryGetValue(name, out float last) && now - last < minGap && now >= last) return false;
            vfxLast[name] = now;
            var go = Object.Instantiate(prefab, pos, rot ?? prefab.transform.rotation, root);
            go.transform.localScale = prefab.transform.localScale * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                // Ground crater decals in these packs are very large and pile up across a busy battlefield.
                if (ps.name == "Crater")
                {
                    ps.gameObject.SetActive(false);
                    continue;
                }
                var m = ps.main;
                m.loop = false;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            if (tint.HasValue) TintEffect(go, tint.Value);
            liveVfx++;
            Tw.After(life, () =>
            {
                liveVfx--;
                if (go) Object.Destroy(go);
            });
            return true;
        }

        // ---------------------------------------------------------------- effect tinting

        static readonly Dictionary<(int, int), Material> tintedMats = new Dictionary<(int, int), Material>();
        static readonly string[] COLOR_PROPS = { "_Color", "_BaseColor", "_TintColor", "_MainColor", "_EmissionColor", "_Emission", "_Tint", "_CoreColor", "_EdgeColor", "_FresnelColor" };

        /// Moves a color's hue to the tint's. Saturation is pulled toward the tint's so grays and whites (dust, white-hot cores)
        /// also turn the color, and brightness is scaled by the tint's value (a darker tint gives a deeper color).
        public static Color Retint(Color c, Color tint)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            Color.RGBToHSV(tint, out float th, out float ts, out float tv);
            float ns = Mathf.Clamp01(Mathf.Lerp(s, ts, 0.7f));
            var r = Color.HSVToRGB(th, ns, Mathf.Min(v, 4f) * Mathf.Lerp(1f, tv, 0.85f), true);
            r.a = c.a;
            return r;
        }

        static Gradient RetintGradient(Gradient g, Color tint)
        {
            var ng = new Gradient { mode = g.mode };
            var ck = g.colorKeys;
            for (int i = 0; i < ck.Length; i++) ck[i].color = Retint(ck[i].color, tint);
            ng.SetKeys(ck, g.alphaKeys);
            return ng;
        }

        static ParticleSystem.MinMaxGradient RetintMinMax(ParticleSystem.MinMaxGradient g, Color tint)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Retint(g.color, tint));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Retint(g.colorMin, tint), Retint(g.colorMax, tint));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(RetintGradient(g.gradient, tint));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(RetintGradient(g.gradientMin, tint), RetintGradient(g.gradientMax, tint));
                default: return new ParticleSystem.MinMaxGradient(RetintGradient(g.gradient, tint));
            }
        }

        static Material TintedMaterial(Material src, Color tint)
        {
            var key = (src.GetHashCode(), ColorUtility.ToHtmlStringRGBA(tint).GetHashCode());
            if (tintedMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(src) { name = src.name + "_tint" };
            foreach (var prop in COLOR_PROPS)
                if (m.HasProperty(prop)) m.SetColor(prop, Retint(m.GetColor(prop), tint));
            tintedMats[key] = m;
            return m;
        }

        /// Recolors every particle system, light and renderer material under an effect instance (see Vfx).
        public static void TintEffect(GameObject go, Color tint)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = RetintMinMax(main.startColor, tint);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = RetintMinMax(col.color, tint);
                var tr = ps.trails;
                if (tr.enabled)
                {
                    tr.colorOverLifetime = RetintMinMax(tr.colorOverLifetime, tint);
                    tr.colorOverTrail = RetintMinMax(tr.colorOverTrail, tint);
                }
                var cbs = ps.colorBySpeed;
                if (cbs.enabled) cbs.color = RetintMinMax(cbs.color, tint);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) { mats[i] = TintedMaterial(mats[i], tint); changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.color = Retint(l.color, tint);
        }

        /// Attaches a looping effect prefab to a parent (for auras and circles); the caller destroys it.
        public static GameObject VfxLoop(string name, Transform parent, Vector3 localPos, float scale = 1f)
        {
            var lib = Skins.Lib;
            if (lib == null) return null;
            var prefab = lib.Vfx(name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = prefab.transform.localScale * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return go;
        }

        // Floating combat text is drawn by the HUD in screen space (fixed size, always on top).
        public class FloatText
        {
            public Vector3 pos;
            public string text;
            public Color color;
            public bool big;
            public float t;
            public float rise, riseDur, fadeDelay, fadeDur;
        }

        public static readonly List<FloatText> texts = new List<FloatText>();

        public static void Number(Vector3 pos, float value, Color? color = null, bool big = false)
        {
            if (root == null || texts.Count > 45) return;
            texts.Add(new FloatText
            {
                pos = pos + new Vector3(M.RandRange(-0.5f, 0.5f), 0.5f, M.RandRange(-0.5f, 0.5f)),
                text = Mathf.RoundToInt(value).ToString(), color = color ?? Color.white, big = big,
                rise = 1.6f, riseDur = 0.6f, fadeDelay = 0.25f, fadeDur = 0.6f,
            });
        }

        public static void Text(Vector3 pos, string msg, Color? color = null)
        {
            if (root == null || texts.Count > 45) return;
            texts.Add(new FloatText
            {
                pos = pos, text = msg, color = color ?? Color.white, big = true,
                rise = 1.4f, riseDur = 0.7f, fadeDelay = 0.3f, fadeDur = 0.5f,
            });
        }

        public static void TickTexts(float dt)
        {
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                var f = texts[i];
                f.t += dt;
                if (f.t >= f.fadeDelay + f.fadeDur) texts.RemoveAt(i);
            }
        }

        public static void Telegraph(Vector3 pos, float radius, float dur, Color? colorIn = null)
        {
            if (root == null) return;
            var color = colorIn ?? new Color(1f, 0.15f, 0.1f, 0.45f);
            var holder = FxNode("Telegraph", pos + new Vector3(0, 0.15f, 0));
            var om = FadeMat(new Color(color.r, color.g, color.b, 0.8f), false);
            var outer = Part(holder, MeshGen.Ring(), om, Vector3.zero, new Vector3(radius, 1f, radius));
            NoShadow(outer);
            var fm = FadeMat(color, false);
            var fill = Part(holder, MeshGen.Disc(), fm, Vector3.zero, new Vector3(0.05f, 1f, 0.05f));
            NoShadow(fill);
            var ft = fill.transform;
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k => ft.localScale = Vector3.Lerp(new Vector3(0.05f, 1f, 0.05f), new Vector3(radius, 1f, radius), k), holder),
                Tw.Call(() => { if (holder) Object.Destroy(holder.gameObject); Object.Destroy(om); Object.Destroy(fm); })));
        }

        /// A ground strip from `from` along flat direction dir: an outline that fills from the start to the far end over dur.
        public static void TelegraphLine(Vector3 from, Vector3 dir, float length, float halfWidth, float dur, Color? colorIn = null)
        {
            if (root == null) return;
            var color = colorIn ?? new Color(1f, 0.15f, 0.1f, 0.45f);
            var holder = FxNode("TelegraphLine", from + new Vector3(0, 0.2f, 0));
            holder.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.0001f ? dir : Vector3.forward, Vector3.up);
            var om = FadeMat(new Color(color.r, color.g, color.b, 0.25f), false);
            NoShadow(Part(holder, MeshGen.Box(), om, new Vector3(0, 0, length * 0.5f), new Vector3(halfWidth * 2f, 0.04f, length)));
            var fm = FadeMat(color, false);
            var pivot = Node(holder, "Fill");
            NoShadow(Part(pivot, MeshGen.Box(), fm, new Vector3(0, 0.02f, 0.5f), new Vector3(halfWidth * 2f, 0.04f, 1f)));
            pivot.localScale = new Vector3(1f, 1f, 0.01f * length);
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k => pivot.localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, k * length)), holder),
                Tw.Call(() => { if (holder) Object.Destroy(holder.gameObject); Object.Destroy(om); Object.Destroy(fm); })));
        }
    }
}
