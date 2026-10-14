using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Procedural primitive meshes matching Godot's defaults (sphere radius 0.5, box size 1, cylinder height 1,
    /// capsule radius 0.5 / height 2, torus in the XZ plane, disc radius 1). All meshes carry white vertex colors.
    public static class MeshGen
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            public int Add(Vector3 p, Vector3 nrm, Color? col = null)
            {
                v.Add(p);
                n.Add(nrm);
                c.Add(col ?? Color.white);
                return v.Count - 1;
            }

            /// Adds a triangle, flipping it if needed so its front face points along its vertex normals.
            public void Tri(int a, int b, int d)
            {
                var cross = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
                var avg = n[a] + n[b] + n[d];
                if (Vector3.Dot(cross, avg) < 0f) { t.Add(a); t.Add(d); t.Add(b); }
                else { t.Add(a); t.Add(b); t.Add(d); }
            }

            public void Quad(int a, int b, int d, int e) { Tri(a, b, d); Tri(a, d, e); }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (!cache.TryGetValue(key, out var m) || m == null)
            {
                m = make();
                cache[key] = m;
            }
            return m;
        }

        public static Mesh Sphere(int segments = 12)
        {
            return Cached("sphere" + segments, () =>
            {
                var b = new Builder();
                int rings = Mathf.Max(segments / 2, 3);
                for (int r = 0; r <= rings; r++)
                {
                    float phi = Mathf.PI * r / rings;               // 0 = top
                    float y = Mathf.Cos(phi), s = Mathf.Sin(phi);
                    for (int i = 0; i <= segments; i++)
                    {
                        float th = M.TAU * i / segments;
                        var nrm = new Vector3(s * Mathf.Cos(th), y, s * Mathf.Sin(th));
                        b.Add(nrm * 0.5f, nrm);
                    }
                }
                int row = segments + 1;
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < segments; i++)
                    {
                        int a = r * row + i, c = a + row;
                        if (r > 0) b.Tri(a, a + 1, c);
                        if (r < rings - 1) b.Tri(a + 1, c + 1, c);
                    }
                return b.Build("Sphere");
            });
        }

        public static Mesh Box()
        {
            return Cached("box", () =>
            {
                var b = new Builder();
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    var u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                    var w = Vector3.Cross(d, u);
                    var c = d * 0.5f;
                    int i0 = b.Add(c + (-u - w) * 0.5f, d);
                    int i1 = b.Add(c + (u - w) * 0.5f, d);
                    int i2 = b.Add(c + (u + w) * 0.5f, d);
                    int i3 = b.Add(c + (-u + w) * 0.5f, d);
                    b.Quad(i0, i1, i2, i3);
                }
                return b.Build("Box");
            });
        }

        /// Cylinder or cone of height 1 centered on the origin (top/bottom are radii).
        public static Mesh Cylinder(float top = 0.5f, float bottom = 0.5f, int segments = 10)
        {
            return Cached($"cyl{top:F2}_{bottom:F2}_{segments}", () => BuildCylinder(top, bottom, segments, 1f, "Cylinder"));
        }

        static Mesh BuildCylinder(float top, float bottom, int segments, float height, string name)
        {
            var b = new Builder();
            float h = height * 0.5f;
            float slope = (bottom - top) / height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = M.TAU * i / segments, a1 = M.TAU * (i + 1) / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var n0 = (d0 + Vector3.up * slope).normalized;
                var n1 = (d1 + Vector3.up * slope).normalized;
                int bt0 = b.Add(d0 * bottom + Vector3.down * h, n0);
                int bt1 = b.Add(d1 * bottom + Vector3.down * h, n1);
                int tp0 = b.Add(d0 * top + Vector3.up * h, n0);
                int tp1 = b.Add(d1 * top + Vector3.up * h, n1);
                if (top > 0.0001f) b.Quad(bt0, bt1, tp1, tp0);
                else b.Tri(bt0, bt1, tp0);
            }
            if (top > 0.0001f) Cap(b, top, h, Vector3.up, segments);
            if (bottom > 0.0001f) Cap(b, bottom, -h, Vector3.down, segments);
            return b.Build(name);
        }

        static void Cap(Builder b, float r, float y, Vector3 nrm, int segments)
        {
            int center = b.Add(new Vector3(0, y, 0), nrm);
            int first = -1, prev = -1;
            for (int i = 0; i <= segments; i++)
            {
                float a = M.TAU * i / segments;
                int idx = b.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), nrm);
                if (prev >= 0) b.Tri(center, prev, idx);
                else first = idx;
                prev = idx;
            }
        }

        /// Capsule with radius 0.5 and total height 2 (like Godot's default CapsuleMesh).
        public static Mesh Capsule()
        {
            return Cached("capsule", () =>
            {
                var b = new Builder();
                int seg = 12, rings = 4;
                const float r = 0.5f, half = 0.5f;
                // Latitude rows from the top pole to the bottom pole; the equator row is doubled to form the cylinder.
                var rows = new List<(float phi, float yoff)>();
                for (int i = 0; i <= rings; i++) rows.Add((Mathf.PI * 0.5f * i / rings, half));
                for (int i = 0; i <= rings; i++) rows.Add((Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / rings, -half));
                foreach (var (phi, yoff) in rows)
                {
                    float y = Mathf.Cos(phi), s = Mathf.Sin(phi);
                    for (int i = 0; i <= seg; i++)
                    {
                        float th = M.TAU * i / seg;
                        var nrm = new Vector3(s * Mathf.Cos(th), y, s * Mathf.Sin(th));
                        b.Add(nrm * r + Vector3.up * yoff, nrm);
                    }
                }
                int row = seg + 1;
                for (int k = 0; k < rows.Count - 1; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = k * row + i, c = a + row;
                        if (k > 0) b.Tri(a, a + 1, c);
                        if (k < rows.Count - 2) b.Tri(a + 1, c + 1, c);
                    }
                return b.Build("Capsule");
            });
        }

        /// Torus lying in the XZ plane, from inner to outer radius.
        public static Mesh Torus(float inner = 0.9f, float outer = 1.0f, int rings = 48, int ringSegments = 6)
        {
            return Cached($"torus{inner:F3}_{outer:F3}_{rings}_{ringSegments}", () =>
            {
                var b = new Builder();
                float R = (inner + outer) * 0.5f, tr = (outer - inner) * 0.5f;
                for (int i = 0; i <= rings; i++)
                {
                    float a = M.TAU * i / rings;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    for (int j = 0; j <= ringSegments; j++)
                    {
                        float bth = M.TAU * j / ringSegments;
                        var nrm = d * Mathf.Cos(bth) + Vector3.up * Mathf.Sin(bth);
                        b.Add(d * R + nrm * tr, nrm);
                    }
                }
                int row = ringSegments + 1;
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < ringSegments; j++)
                    {
                        int a = i * row + j;
                        b.Quad(a, a + 1, a + row + 1, a + row);
                    }
                return b.Build("Torus");
            });
        }

        public static Mesh Ring() => Torus(0.9f, 1.0f, 48, 6);

        /// Flat cylinder of radius 1 and height 0.04.
        public static Mesh Disc()
        {
            return Cached("disc", () => BuildCylinder(1f, 1f, 40, 0.04f, "Disc"));
        }

        /// A single quad in the XY plane facing -Z and +Z (for particles).
        public static Mesh Quad()
        {
            return Cached("quad", () =>
            {
                var m = new Mesh { name = "Quad" };
                m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
                m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
                m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                m.RecalculateBounds();
                return m;
            });
        }

        /// Builds a mesh from a flat triangle list with per-vertex normals (front faces follow the normals).
        public static Mesh FromTriangles(IList<Vector3> verts, IList<Vector3> normals, IList<Color> colors = null, string name = "Mesh")
        {
            var b = new Builder();
            for (int i = 0; i < verts.Count; i++)
                b.Add(verts[i], normals[i], colors != null ? colors[i] : Color.white);
            for (int i = 0; i + 2 < verts.Count; i += 3) b.Tri(i, i + 1, i + 2);
            return b.Build(name);
        }

        /// Builds a mesh from raw arrays without reordering triangles.
        public static Mesh Raw(Vector3[] verts, Vector3[] normals, Color[] colors, int[] tris, string name)
        {
            var m = new Mesh { name = name };
            if (verts.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = verts;
            m.normals = normals;
            m.colors = colors;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
    }
}
