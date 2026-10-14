using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// A procedurally assembled dragon: scaled body, 4 legs, 2 membrane wings, horned head, spiked tail.
    /// Faces -Z. Feet rest at y = 0 at scale 1.
    public class DragonModel : MonoBehaviour
    {
        static Shader scaleShader;

        Color bodyColor = new Color(0.1f, 0.42f, 1.0f);
        Color edgeColor = new Color(0.02f, 0.07f, 0.3f);
        Color bellyColor = new Color(0.62f, 0.82f, 1.0f);
        Color membraneColor = new Color(0.1f, 0.3f, 0.75f);
        Color hornColor = new Color(0.92f, 0.88f, 0.78f);
        Color eyeColor = new Color(1.0f, 0.85f, 0.25f);
        bool frontLegs = true;

        Material scaleMat, bellyMat, hornMat, membraneMat;

        Joint body, neckRoot, head, jaw;
        public Transform mouth;
        public Transform seat;
        Joint wingL, wingR;
        readonly List<(Joint pivot, float phase, bool front)> legs = new List<(Joint, float, bool)>();
        readonly List<Joint> tailSegs = new List<Joint>();

        float t, walkT;
        public float fold = 1f;     // 1 = wings folded, 0 = spread
        public float jawOpen;

        public DragonModel Setup(Data.DragonDef d, bool withFrontLegs = true)
        {
            bodyColor = d.body;
            edgeColor = d.edge;
            bellyColor = d.belly;
            membraneColor = d.membrane;
            hornColor = d.horn;
            eyeColor = d.eye;
            frontLegs = withFrontLegs;
            Build();
            return this;
        }

        void OnDestroy()
        {
            if (scaleMat) Destroy(scaleMat);
            if (bellyMat) Destroy(bellyMat);
            if (membraneMat) Destroy(membraneMat);
        }

        Material MakeScaleMat(Color c, float density, float bellyAmt)
        {
            if (scaleShader == null) scaleShader = Shader.Find("Skyfall/DragonScales");
            var m = new Material(scaleShader);
            FX.SetLin(m, "_BaseColor", c);
            FX.SetLin(m, "_EdgeColor", edgeColor);
            FX.SetLin(m, "_BellyColor", bellyColor);
            FX.SetLin(m, "_SheenColor", M.Lighten(c, 0.4f));
            m.SetFloat("_ScaleDensity", density);
            m.SetFloat("_BellyAmount", bellyAmt);
            return m;
        }

        MeshRenderer P(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scl, Vector3 rot = default) =>
            FX.Part(parent, mesh, mat, pos, scl, rot);

        void Build()
        {
            scaleMat = MakeScaleMat(bodyColor, 6.5f, 1f);
            bellyMat = MakeScaleMat(bellyColor, 2.2f, 0f);
            FX.SetLin(bellyMat, "_EdgeColor", M.Darken(bellyColor, 0.45f));
            hornMat = FX.Lit(hornColor, 0.45f, 0f);
            membraneMat = FX.NewLit(M.Darken(membraneColor, 0.15f), 0.6f);
            membraneMat.SetFloat("_Cull", (float)CullMode.Off);
            FX.SetLin(membraneMat, "_Backlight", M.Lighten(membraneColor, 0.3f));
            membraneMat.SetFloat("_Rim", 0.4f);

            body = new Joint(FX.Node(transform, "Body"));
            var sph = MeshGen.Sphere(20);
            var bt = body.t;

            // Torso.
            P(bt, sph, scaleMat, new Vector3(0, 1.75f, 0.15f), new Vector3(2.1f, 1.75f, 3.6f));
            P(bt, sph, scaleMat, new Vector3(0, 1.9f, -1.05f), new Vector3(1.95f, 1.85f, 2.0f));
            P(bt, sph, scaleMat, new Vector3(0, 1.7f, 1.35f), new Vector3(1.8f, 1.6f, 1.9f));
            P(bt, sph, bellyMat, new Vector3(0, 1.28f, 0f), new Vector3(1.55f, 0.9f, 3.3f));

            // Neck: a curve of overlapping segments rising forward.
            neckRoot = new Joint(FX.Node(bt, "Neck", new Vector3(0, 2.15f, -1.65f)));
            var p0 = Vector3.zero;
            var p1 = new Vector3(0, 1.0f, -0.7f);
            var p2 = new Vector3(0, 1.45f, -1.75f);
            const int segs = 7;
            for (int i = 0; i < segs; i++)
            {
                float tt = (float)i / (segs - 1);
                var pos = Vector3.Lerp(Vector3.Lerp(p0, p1, tt), Vector3.Lerp(p1, p2, tt), tt);
                float r = Mathf.Lerp(1.05f, 0.62f, tt);
                P(neckRoot.t, sph, scaleMat, pos, new Vector3(r, r, r * 1.15f));
                P(neckRoot.t, sph, bellyMat, pos + new Vector3(0, -r * 0.18f, -r * 0.12f), new Vector3(r * 0.75f, r * 0.7f, r * 0.95f));
                // Spine spikes.
                P(neckRoot.t, MeshGen.Cylinder(0f, 0.5f, 6), hornMat, pos + new Vector3(0, r * 0.48f, r * 0.05f), new Vector3(0.12f, 0.35f, 0.2f), new Vector3(-0.5f, 0, 0));
            }

            // Head.
            head = new Joint(FX.Node(neckRoot.t, "Head", p2 + new Vector3(0, 0.12f, -0.35f)));
            var ht = head.t;
            P(ht, sph, scaleMat, new Vector3(0, 0.05f, 0), new Vector3(0.95f, 0.8f, 1.05f));
            P(ht, MeshGen.Capsule(), scaleMat, new Vector3(0, -0.05f, -0.68f), new Vector3(0.62f, 0.42f, 0.5f), new Vector3(M.PI * 0.5f, 0, 0));
            foreach (int s in new[] { -1, 1 })
            {
                // Brow ridges.
                P(ht, sph, scaleMat, new Vector3(s * 0.26f, 0.28f, -0.22f), new Vector3(0.32f, 0.18f, 0.55f), new Vector3(0, s * 0.25f, 0));
                // Eyes.
                P(ht, sph, FX.Glow(eyeColor, 2.2f), new Vector3(s * 0.33f, 0.17f, -0.3f), new Vector3(0.14f, 0.11f, 0.18f));
                // Main horns sweeping back.
                P(ht, MeshGen.Cylinder(0f, 0.5f, 8), hornMat, new Vector3(s * 0.3f, 0.42f, 0.3f), new Vector3(0.2f, 1.1f, 0.2f), new Vector3(1.15f, 0, s * -0.25f));
                P(ht, MeshGen.Cylinder(0f, 0.5f, 6), hornMat, new Vector3(s * 0.42f, 0.18f, 0.28f), new Vector3(0.12f, 0.65f, 0.12f), new Vector3(1.3f, 0, s * -0.7f));
                // Cheek frills.
                P(ht, MeshGen.Cylinder(0f, 0.5f, 4), hornMat, new Vector3(s * 0.45f, -0.05f, 0.15f), new Vector3(0.08f, 0.45f, 0.18f), new Vector3(1.4f, 0, s * -1.1f));
                // Nostrils.
                P(ht, sph, FX.Lit(new Color(0.02f, 0.02f, 0.05f)), new Vector3(s * 0.12f, 0.1f, -1.08f), new Vector3(0.07f, 0.05f, 0.07f));
            }
            // Snout horn.
            P(ht, MeshGen.Cylinder(0f, 0.5f, 6), hornMat, new Vector3(0, 0.22f, -0.82f), new Vector3(0.1f, 0.32f, 0.1f), new Vector3(0.6f, 0, 0));
            // Jaw with teeth.
            jaw = new Joint(FX.Node(ht, "Jaw", new Vector3(0, -0.2f, -0.1f)));
            P(jaw.t, MeshGen.Capsule(), bellyMat, new Vector3(0, -0.06f, -0.55f), new Vector3(0.5f, 0.36f, 0.22f), new Vector3(M.PI * 0.5f, 0, 0));
            for (int i = 0; i < 5; i++)
                foreach (int s in new[] { -1, 1 })
                    P(jaw.t, MeshGen.Cylinder(0f, 0.5f, 4), hornMat, new Vector3(s * 0.16f, 0.08f, -0.25f - i * 0.16f), new Vector3(0.05f, 0.14f, 0.05f));
            mouth = FX.Node(ht, "Mouth", new Vector3(0, -0.1f, -1.15f));

            // Rider seat sits at the base of the neck.
            seat = FX.Node(bt, "Seat", new Vector3(0, 3.05f, -1.0f));

            // Back spikes.
            for (int i = 0; i < 7; i++)
            {
                float z = -0.6f + i * 0.45f;
                float h = 0.55f - Mathf.Abs(i - 2.5f) * 0.06f;
                P(bt, MeshGen.Cylinder(0f, 0.5f, 6), hornMat, new Vector3(0, 2.62f - i * 0.03f, z), new Vector3(0.16f, h, 0.3f), new Vector3(-0.45f, 0, 0));
            }

            // Tail: chained segments so sway propagates.
            var parent = bt;
            var tpos = new Vector3(0, 1.75f, 2.2f);
            for (int i = 0; i < 12; i++)
            {
                var seg = new Joint(FX.Node(parent, "Tail" + i, tpos));
                float r = Mathf.Lerp(0.85f, 0.14f, i / 11f);
                float segLen = 0.3f + r * 0.55f;
                P(seg.t, sph, scaleMat, new Vector3(0, 0, segLen * 0.5f), new Vector3(r * 1.1f, r, segLen * 2f));
                if (i % 2 == 0)
                    P(seg.t, MeshGen.Cylinder(0f, 0.5f, 6), hornMat, new Vector3(0, r * 0.45f, 0.2f), new Vector3(0.1f, r * 0.7f + 0.12f, 0.18f), new Vector3(-0.6f, 0, 0));
                tailSegs.Add(seg);
                parent = seg.t;
                tpos = new Vector3(0, i < 4 ? -0.05f : 0.02f, segLen);
            }
            // Spade tip.
            var spade = FX.Node(parent, "Spade", new Vector3(0, 0, 0.35f));
            P(spade, sph, scaleMat, Vector3.zero, new Vector3(0.5f, 0.07f, 0.55f));
            P(spade, MeshGen.Cylinder(0f, 0.5f, 4), hornMat, new Vector3(0, 0, 0.45f * 0.55f), new Vector3(0.25f, 0.55f, 0.035f), new Vector3(M.PI * 0.5f, 0, 0));

            // Legs.
            if (frontLegs)
            {
                MakeLeg(new Vector3(0.78f, 1.55f, -1.1f), true, 0f);
                MakeLeg(new Vector3(-0.78f, 1.55f, -1.1f), true, M.PI);
            }
            MakeLeg(new Vector3(0.82f, 1.65f, 1.25f), false, M.PI);
            MakeLeg(new Vector3(-0.82f, 1.65f, 1.25f), false, 0f);

            // Wings.
            wingR = MakeWing(1);
            wingL = MakeWing(-1);
        }

        void MakeLeg(Vector3 at, bool front, float phase)
        {
            var pivot = new Joint(FX.Node(body.t, front ? "FrontLeg" : "HindLeg", at));
            var pt = pivot.t;
            float s = Mathf.Sign(at.x);
            Vector3 knee, ankle, foot;
            float topR;
            if (front)
            {
                knee = new Vector3(s * 0.08f, -0.72f, 0.22f);
                ankle = new Vector3(s * 0.05f, -1.32f, 0.02f);
                foot = new Vector3(s * 0.05f, -1.5f, -0.12f);
                topR = 0.3f;
            }
            else
            {
                P(pt, MeshGen.Sphere(16), scaleMat, new Vector3(s * 0.08f, -0.15f, 0f), new Vector3(0.85f, 1.1f, 1.15f));
                knee = new Vector3(s * 0.1f, -0.75f, -0.35f);
                ankle = new Vector3(s * 0.08f, -1.3f, 0.15f);
                foot = new Vector3(s * 0.08f, -1.6f, -0.08f);
                topR = 0.36f;
            }
            FX.Bone(pt, Vector3.zero, knee, topR, topR * 0.7f, scaleMat);
            P(pt, MeshGen.Sphere(10), scaleMat, knee, Vector3.one * topR * 1.45f);
            FX.Bone(pt, knee, ankle, topR * 0.68f, topR * 0.5f, scaleMat);
            P(pt, MeshGen.Sphere(10), scaleMat, ankle, Vector3.one * topR);
            P(pt, MeshGen.Sphere(10), scaleMat, foot, new Vector3(0.42f, 0.2f, 0.55f));
            foreach (int c in new[] { -1, 0, 1 })
                P(pt, MeshGen.Cylinder(0f, 0.5f, 5), hornMat, foot + new Vector3(c * 0.13f, -0.05f, -0.3f), new Vector3(0.08f, 0.28f, 0.08f), new Vector3(-M.PI * 0.5f, 0, 0));
            legs.Add((pivot, phase, front));
        }

        readonly Dictionary<Joint, int> wingSide = new Dictionary<Joint, int>();

        Joint MakeWing(int side)
        {
            var pivot = new Joint(FX.Node(body.t, side > 0 ? "WingR" : "WingL", new Vector3(0.7f * side, 2.55f, -0.65f)));
            var pt = pivot.t;
            var m = new Vector3(side, 1, 1);
            var shoulder = Vector3.zero;
            var elbow = Vector3.Scale(new Vector3(1.7f, 0.55f, -0.15f), m);
            var wrist = Vector3.Scale(new Vector3(3.3f, 0.85f, 0.25f), m);
            var f1 = Vector3.Scale(new Vector3(6.4f, 0.45f, 0.7f), m);
            var f2 = Vector3.Scale(new Vector3(5.5f, 0.1f, 2.1f), m);
            var f3 = Vector3.Scale(new Vector3(3.9f, -0.2f, 3.0f), m);
            var rootBack = Vector3.Scale(new Vector3(0.35f, -0.25f, 2.6f), m);
            FX.Bone(pt, shoulder, elbow, 0.24f, 0.17f, scaleMat);
            FX.Bone(pt, elbow, wrist, 0.17f, 0.12f, scaleMat);
            P(pt, MeshGen.Sphere(8), scaleMat, elbow, Vector3.one * 0.36f);
            P(pt, MeshGen.Sphere(8), scaleMat, wrist, Vector3.one * 0.28f);
            foreach (var f in new[] { f1, f2, f3 })
                FX.Bone(pt, wrist, f, 0.09f, 0.03f, scaleMat);
            P(pt, MeshGen.Cylinder(0f, 0.5f, 5), hornMat, wrist + new Vector3(0.1f * side, 0.25f, -0.15f), new Vector3(0.1f, 0.45f, 0.1f), new Vector3(-0.4f, 0, -0.3f * side));
            // Membrane: fan from the wrist with scalloped edges between fingers.
            var m12 = Vector3.Lerp(Vector3.Lerp(f1, f2, 0.5f), wrist, 0.22f);
            var m23 = Vector3.Lerp(Vector3.Lerp(f2, f3, 0.5f), wrist, 0.22f);
            var m3r = Vector3.Lerp(Vector3.Lerp(f3, rootBack, 0.5f), wrist, 0.18f);
            var ring = new[] { elbow, f1, m12, f2, m23, f3, m3r, rootBack, shoulder };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var down = new Vector3(0, -0.04f, 0);
            for (int i = 0; i < ring.Length - 1; i++)
            {
                verts.Add(wrist + down); verts.Add(ring[i] + down); verts.Add(ring[i + 1] + down);
            }
            verts.Add(shoulder + down); verts.Add(elbow + down); verts.Add(wrist + down);
            for (int i = 0; i < verts.Count; i++) norms.Add(Vector3.up);
            var mesh = MeshGen.FromTriangles(verts, norms, null, "WingMembrane");
            var mr = P(pt, mesh, membraneMat, Vector3.zero, Vector3.one);
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            wingSide[pivot] = side;
            return pivot;
        }

        /// speed: horizontal speed; flying: in the air with wings out; vy: vertical velocity.
        public void Animate(float delta, float speed, bool flying, float vy)
        {
            t += delta;
            fold = Mathf.MoveTowards(fold, flying ? 0f : 1f, delta * 3f);
            float moving = Mathf.Clamp(speed / 10f, 0f, 1.5f);
            walkT += delta * (2f + speed * 0.9f);

            // Wings.
            float flapSpeed = vy > 0.5f ? 7.5f : 4.5f;
            float flap = Mathf.Sin(t * flapSpeed) * 0.75f;
            var spreadRot = new Vector3(0, 0, flap + 0.1f);
            var foldedRot = new Vector3(0.12f, -1.3f, 0.02f + Mathf.Sin(t * 1.5f) * 0.03f);
            foreach (var w in new[] { wingR, wingL })
            {
                float sd = wingSide[w];
                var rot = Vector3.Lerp(spreadRot, foldedRot, fold);
                w.Rot = new Vector3(rot.x, rot.y * sd, rot.z * sd);
                w.t.localScale = new Vector3(Mathf.Lerp(1f, 0.55f, fold), 1f, Mathf.Lerp(1f, 0.6f, fold));
            }

            // Legs: trot on the ground, tucked in flight.
            foreach (var (pivot, phase, front) in legs)
            {
                float walk = Mathf.Sin(walkT + phase) * 0.55f * Mathf.Min(moving, 1f);
                float tuck = front ? -0.7f : -1.0f;
                pivot.RotX = Mathf.Lerp(tuck, walk, fold);
            }

            // Body bob / pitch.
            float bob = Mathf.Abs(Mathf.Sin(walkT)) * 0.08f * Mathf.Min(moving, 1f) * fold;
            var bp = body.t.localPosition;
            bp.y = bob + (1f - fold) * Mathf.Sin(t * flapSpeed - 1f) * 0.12f;
            body.t.localPosition = bp;
            body.RotX = Mathf.Lerp(body.RotX, Mathf.Clamp(-vy * 0.03f, -0.35f, 0.35f) * (1f - fold), delta * 4f);

            // Neck and head sway.
            neckRoot.RotX = Mathf.Sin(t * 1.3f) * 0.04f - (1f - fold) * 0.15f;
            head.Rot = new Vector3(Mathf.Sin(t * 1.3f + 0.6f) * 0.06f + (1f - fold) * 0.2f, Mathf.Sin(t * 0.7f) * 0.08f, 0f);
            jawOpen = Mathf.MoveTowards(jawOpen, 0f, delta * 1.5f);
            jaw.RotX = jawOpen * 0.5f + 0.03f;

            // Tail sway.
            for (int i = 0; i < tailSegs.Count; i++)
                tailSegs[i].Rot = new Vector3(Mathf.Sin(t * 1.7f - i * 0.3f) * 0.03f + (1f - fold) * 0.02f,
                    Mathf.Sin(t * 2.2f - i * 0.45f) * (0.08f + moving * 0.04f), 0f);
        }
    }
}
