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
                r.velocityScale = 0.04f;
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
                Tw.Lerp(dur, k =>
                {
                    t.localScale = Vector3.one * Mathf.Lerp(radius * 0.5f, radius * 2f, Tw.Apply(Ease.OutCubic, k));
                    SetAlpha(m, a0 * (1f - k));
                }, t),
                Tw.Call(() => { if (t) Object.Destroy(t.gameObject); Object.Destroy(m); })));
        }

        public static void Shockwave(Vector3 pos, float radius, Color color, float dur = 0.4f)
        {
            if (root == null) return;
            var m = FadeMat(color);
            var r = Part(root, MeshGen.Ring(), m, Vector3.zero, new Vector3(0.3f, 2f, 0.3f));
            r.transform.position = pos;
            NoShadow(r);
            float a0 = color.a;
            var t = r.transform;
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k =>
                {
                    float e = Tw.Apply(Ease.OutQuart, k);
                    t.localScale = Vector3.Lerp(new Vector3(0.3f, 2f, 0.3f), new Vector3(radius, 3f, radius), e);
                    SetAlpha(m, a0 * (1f - k));
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

        public static void Burst(Vector3 pos, Color color, int amount = 14, float speed = 6f, float size = 0.25f)
        {
            if (root == null || liveBursts > 40) return;
            liveBursts++;
            var holder = FxNode("Burst", pos);
            var ps = Particles(holder, new ParticleSpec
            {
                amount = amount, lifetime = 0.55f, size = size, scaleMin = 0.6f, scaleMax = 1.4f,
                speedMin = speed * 0.4f, speedMax = speed, spread = 180f, gravity = new Vector3(0, -9, 0),
                startColor = color, endColor = M.WithA(color, 0f), boost = 1.6f, oneShot = true,
            });
            Tw.After(0.8f, () =>
            {
                liveBursts--;
                if (holder) Object.Destroy(holder.gameObject);
            });
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
    }
}
