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

        /// A fresh (unshared) transparent unlit material, so it can be faded on its own (optionally textured).
        public static Material FadeMat(Color color, bool additive = true, Texture2D tex = null)
        {
            var m = new Material(UnlitShader);
            SetLin(m, "_Color", color);
            if (tex) m.SetTexture("_MainTex", tex);
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

        static readonly Dictionary<string, Texture2D> fxTex = new Dictionary<string, Texture2D>();

        /// An effect texture from Resources/Skyfall/FX (the Unity AI generated rings, flashes, sparks and decals), or null
        /// when it is missing, in which case callers keep their plain look.
        public static Texture2D Tex(string name)
        {
            if (!fxTex.TryGetValue(name, out var t))
            {
                t = Resources.Load<Texture2D>("Skyfall/FX/" + name);
                fxTex[name] = t;
            }
            return t;
        }

        /// Particle material: a sprite (soft round dot by default), vertex colored, additive or alpha blended, with an HDR boost.
        public static Material ParticleMat(bool additive = true, float boost = 1f, Texture2D tex = null)
        {
            string key = $"p{additive}_{boost:F2}_{(tex ? tex.name : "dot")}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(UnlitShader);
            m.SetVector("_Color", new Vector4(boost, boost, boost, 1f));
            m.SetTexture("_MainTex", tex ? tex : SoftDot);
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
            public bool grow;                         // particles swell (fast, then easing) over their lifetime
            public Texture2D tex;                     // sprite; null = soft round dot
            public bool spin;                         // random start rotation and a slow spin (smoke, embers)
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
            else if (s.grow)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f, 0f, 3f), new Keyframe(0.35f, 0.9f), new Keyframe(1f, 1.15f)));
            }

            if (s.spin)
            {
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var rol = ps.rotationOverLifetime;
                rol.enabled = true;
                rol.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(MakeGradient(s));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = ParticleMat(s.additive, s.boost, s.tex);
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

        /// A flat textured quad lying on the ground plane at pos (size = full width), yawed by yaw degrees.
        static MeshRenderer FlatQuad(Transform parent, Vector3 pos, float size, Material m, float yaw)
        {
            var r = Part(parent, MeshGen.Quad(), m, Vector3.zero, Vector3.one * size);
            r.transform.position = pos;
            r.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            NoShadow(r);
            return r;
        }

        /// Terrain height under pos when pos is within maxAbove of it (on the ground, not up on an island), else null.
        static float? GroundUnder(Vector3 pos, float maxAbove = 2.5f)
        {
            var g = Game.I;
            if (g == null || g.terrain == null) return null;
            float h = g.terrain.Height(pos.x, pos.z);
            return pos.y - h < maxAbove && pos.y - h > -1f ? h : (float?)null;
        }

        public static void Flash(Vector3 pos, float radius, Color color, float dur = 0.3f)
        {
            if (root == null) return;
            // A starburst sprite over a soft glow carries the flash; the plain sphere is only the fallback without it.
            var star = Tex("fx_flash_star");
            if (star)
            {
                var holder = FxNode("Flash", pos);
                Particles(holder, new ParticleSpec
                {
                    amount = 1, lifetime = dur * 1.3f, size = radius * 2.2f, oneShot = true, grow = true,
                    startColor = M.WithA(color, Mathf.Min(1f, color.a * 0.8f)), endColor = M.WithA(color, 0f), boost = 1.6f,
                });
                Particles(holder, new ParticleSpec
                {
                    amount = 1, lifetime = dur * 1.15f, size = radius * 2.6f, oneShot = true, grow = true, spin = true, tex = star,
                    startColor = M.WithA(Color.Lerp(color, Color.white, 0.55f), Mathf.Min(1f, color.a * 1.2f)), midColor = color, midAt = 0.35f,
                    endColor = M.WithA(color, 0f), boost = 2.2f,
                });
                Tw.After(dur * 1.3f + 0.3f, () => { if (holder) Object.Destroy(holder.gameObject); });
                return;
            }
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
            // Ground waves kick up a ring of dust.
            if (radius >= 3f && GroundUnder(pos) is float gh)
                Vfx("dust_ground", new Vector3(pos.x, gh + 0.05f, pos.z), Mathf.Clamp(radius / 6f, 0.5f, 2.5f), 2f, null, 0.15f);
            var ring = Tex("fx_shockwave_ring");
            if (ring)
            {
                // A flat wispy energy ring (its band sits at 0.29 of the quad) that rips outward like the mesh ring did.
                var qm = FadeMat(color, additive, ring);
                var q = FlatQuad(root, pos, 1f, qm, M.RandRange(0f, 360f));
                var qt = q.transform;
                float qa = color.a;
                Tw.Run(Tw.Seq(
                    Tw.Lerp(dur * 0.85f, k =>
                    {
                        float e = Tw.Apply(Ease.OutQuint, k);
                        qt.localScale = Vector3.one * Mathf.Lerp(1f, radius * 1.1f / 0.29f, e);
                        SetAlpha(qm, Mathf.Min(1f, qa * 1.3f) * (1f - k * k));
                    }, qt),
                    Tw.Call(() => { if (qt) Object.Destroy(qt.gameObject); Object.Destroy(qm); })));
                return;
            }
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

        static void JagLine(LineRenderer lr, Vector3 a, Vector3 b, int segs, float jag)
        {
            var d = b - a;
            for (int i = 0; i <= segs; i++)
            {
                var p = a + d * ((float)i / segs);
                if (i > 0 && i < segs)
                    p += new Vector3(M.RandRange(-1f, 1f), M.RandRange(-1f, 1f), M.RandRange(-1f, 1f)) * d.magnitude * jag;
                lr.SetPosition(i, p);
            }
        }

        static LineRenderer BoltLine(Transform holder, Material m, float width, int segs)
        {
            var go = new GameObject("BoltLine");
            go.transform.SetParent(holder, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = m;
            lr.positionCount = segs + 1;
            lr.widthMultiplier = width;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        public static void Bolt(Vector3 a, Vector3 b, Color color, float width = 0.18f, float dur = 0.18f, int segs = 7)
        {
            if (root == null) return;
            var tex = Tex("fx_bolt");
            if (tex)
            {
                // The LightningBolt pack's glow strip around a thin white-hot core, re-forking a few times while it fades.
                var h = FxNode("Bolt", Vector3.zero);
                int n = Mathf.Max(segs, 9);
                var gm = FadeMat(color, true, tex);
                var cm = FadeMat(Color.Lerp(color, Color.white, 0.75f));
                var glow = BoltLine(h, gm, width * 3.2f, n);
                var core = BoltLine(h, cm, width * 0.45f, n);
                float ga = color.a;
                int lastStep = -1;
                Tw.Run(Tw.Seq(
                    Tw.Lerp(dur * 1.3f, k =>
                    {
                        int step = (int)(k * 3f);
                        if (step != lastStep)
                        {
                            lastStep = step;
                            JagLine(glow, a, b, n, 0.07f);
                            for (int i = 0; i <= n; i++) core.SetPosition(i, glow.GetPosition(i));
                        }
                        SetAlpha(gm, Mathf.Min(1f, ga * 1.3f) * (1f - k));
                        SetAlpha(cm, (1f - k) * (1f - k));
                    }, h),
                    Tw.Call(() => { if (h) Object.Destroy(h.gameObject); Object.Destroy(gm); Object.Destroy(cm); })));
                return;
            }
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
                boost = 2.4f, oneShot = true, shrink = true, tex = Tex("fx_spark"),
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

        static readonly Queue<Transform> scorches = new Queue<Transform>();

        /// A burnt mark left on the ground by an explosion (only on the terrain, not up on islands), with a few embers and a
        /// smoke puff rising from it. It lingers, then fades; the oldest marks go first once 20 are down.
        public static void Scorch(Vector3 pos, float radius)
        {
            if (root == null || !(GroundUnder(pos) is float gh)) return;
            var tex = Tex("fx_scorch_decal");
            if (tex == null) return;
            while (scorches.Count >= 20)
            {
                var old = scorches.Dequeue();
                if (old) Object.Destroy(old.gameObject);
            }
            var at = new Vector3(pos.x, gh + 0.06f + scorches.Count * 0.002f, pos.z);
            var holder = FxNode("Scorch", at);
            scorches.Enqueue(holder);
            var m = FadeMat(new Color(1f, 1f, 1f, 0f), false, tex);
            FlatQuad(holder, at, radius * 2.4f, m, M.RandRange(0f, 360f));
            var ember = Tex("fx_ember");
            if (ember)
                Particles(holder, new ParticleSpec
                {
                    amount = 7, lifetime = 1.4f, size = 0.32f, scaleMin = 0.5f, scaleMax = 1.2f, speedMin = 0.6f, speedMax = 2.2f, spread = 35f,
                    sphereRadius = radius * 0.5f, gravity = new Vector3(0, 1.2f, 0), drag = 1f, tex = ember, boost = 2f, oneShot = true, shrink = true,
                    startColor = new Color(1f, 0.75f, 0.35f), endColor = new Color(1f, 0.3f, 0.05f, 0f),
                });
            var smoke = Tex("fx_smoke_puff");
            if (smoke)
                Particles(holder, new ParticleSpec
                {
                    amount = 4, lifetime = 1.8f, size = radius * 0.9f, scaleMin = 0.7f, scaleMax = 1.2f, speedMin = 0.4f, speedMax = 1.2f, spread = 25f,
                    sphereRadius = radius * 0.3f, gravity = new Vector3(0, 0.8f, 0), additive = false, tex = smoke, oneShot = true, grow = true, spin = true,
                    startColor = new Color(0.25f, 0.23f, 0.22f, 0.55f), endColor = new Color(0.4f, 0.38f, 0.36f, 0f),
                });
            Tw.Run(Tw.Seq(
                Tw.Lerp(0.15f, k => SetAlpha(m, 0.85f * k), holder),
                Tw.Wait(6f),
                Tw.Lerp(3f, k => SetAlpha(m, 0.85f * (1f - k)), holder),
                Tw.Call(() => { if (holder) Object.Destroy(holder.gameObject); Object.Destroy(m); })));
        }

        /// A ring of ice crystals that bursts outward on the ground (Frost Bolt's impact).
        public static void FrostRing(Vector3 pos, float radius, float dur = 0.6f)
        {
            if (root == null) return;
            var tex = Tex("fx_frost_ring");
            if (tex == null) return;
            float y = GroundUnder(pos, 4f) is float gh ? gh + 0.1f : pos.y;
            var m = FadeMat(new Color(0.8f, 0.95f, 1f, 1f), true, tex);
            SetLin(m, "_Color", new Color(0.8f, 0.95f, 1f), 1.6f);
            var t = FlatQuad(root, new Vector3(pos.x, y, pos.z), 1f, m, M.RandRange(0f, 360f)).transform;
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k =>
                {
                    // The crystal band sits at 0.3 of the quad.
                    t.localScale = Vector3.one * Mathf.Lerp(radius * 0.3f, radius, Tw.Apply(Ease.OutQuint, k)) / 0.3f;
                    SetAlpha(m, k < 0.5f ? 1f : (1f - k) * 2f);
                }, t),
                Tw.Call(() => { if (t) Object.Destroy(t.gameObject); Object.Destroy(m); })));
        }

        // ---------------------------------------------------------------- Asset Store effects

        static readonly Dictionary<Material, Material> legacyFixed = new Dictionary<Material, Material>();

        /// Some packs (Polygonal Particles) ship built-in particle shaders that URP draws pink: swap those for the game's own
        /// unlit shader with the same texture, tint and blending.
        static void FixLegacyMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    var src = ms[i];
                    if (src == null || src.shader == null) continue;
                    string sn = src.shader.name;
                    if (!sn.StartsWith("Legacy Shaders/Particles") && !sn.StartsWith("Mobile/Particles")) continue;
                    if (!legacyFixed.TryGetValue(src, out var m) || !m)
                    {
                        m = new Material(UnlitShader) { name = src.name + "_urp" };
                        if (src.HasProperty("_MainTex")) m.SetTexture("_MainTex", src.GetTexture("_MainTex"));
                        // Legacy particle tints are at half strength (the shaders double them).
                        var tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") * 2f : Color.white;
                        m.SetVector("_Color", new Vector4(tint.r, tint.g, tint.b, Mathf.Min(1f, tint.a)));
                        m.SetFloat("_Cull", (float)CullMode.Off);
                        MakeTransparent(m, sn.Contains("Additive"));
                        legacyFixed[src] = m;
                    }
                    ms[i] = m;
                    changed = true;
                }
                if (changed) r.sharedMaterials = ms;
            }
        }

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
            FixLegacyMaterials(go);
            // Explosions on the ground leave a scorch mark.
            if (name.Contains("explosion")) Scorch(pos, Mathf.Clamp(scale * 2.2f, 1.2f, 7f));
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
            FixLegacyMaterials(go);
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
            // Outline: a turning rune circle (its outer edge sits at 0.317 of the quad), or the plain ring without it.
            var runes = Tex("fx_telegraph_runes");
            var om = FadeMat(new Color(color.r, color.g, color.b, runes ? 1f : 0.8f), runes != null, runes);
            Transform spinner = null;
            if (runes)
            {
                spinner = FlatQuad(holder, holder.position + new Vector3(0, 0.02f, 0), radius / 0.317f, om, M.RandRange(0f, 360f)).transform;
                SetLin(om, "_Color", color, 1.5f);
                SetAlpha(om, 1f);
            }
            else NoShadow(Part(holder, MeshGen.Ring(), om, Vector3.zero, new Vector3(radius, 1f, radius)));
            var fm = FadeMat(color, false);
            var fill = Part(holder, MeshGen.Disc(), fm, Vector3.zero, new Vector3(0.05f, 1f, 0.05f));
            NoShadow(fill);
            var ft = fill.transform;
            Tw.Run(Tw.Seq(
                Tw.Lerp(dur, k =>
                {
                    ft.localScale = Vector3.Lerp(new Vector3(0.05f, 1f, 0.05f), new Vector3(radius, 1f, radius), k);
                    if (spinner) spinner.Rotate(0f, 50f * Time.deltaTime, 0f, Space.World);
                }, holder),
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
