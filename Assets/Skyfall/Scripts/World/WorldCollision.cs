using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Custom collision for the player (no physics colliders): the solid bodies of the floating islands and the big
    /// pieces of the Ruins kit, kept as oriented boxes in a spatial grid.
    public partial class WorldTerrain
    {
        /// An upright oriented box on the ground plane: center (cx, cz), local x axis (ux, uz), half sizes, vertical span.
        public struct Obstacle
        {
            public float cx, cz, ux, uz, hx, hz, bottom, top;
            public string name;
        }

        public readonly List<Obstacle> obstacles = new List<Obstacle>();

        const float OB_CELL = 6f;
        const float OB_ORIGIN = -216f;
        const int OB_N = 72;
        List<int>[] obGrid;
        int[] obStamp = new int[0];
        int obQuery;

        void EnsureObstacleGrid()
        {
            if (obGrid == null) obGrid = new List<int>[OB_N * OB_N];
        }

        static int ObCell(float v) => Mathf.Clamp((int)((v - OB_ORIGIN) / OB_CELL), 0, OB_N - 1);

        void AddObstacle(Obstacle o)
        {
            EnsureObstacleGrid();
            int idx = obstacles.Count;
            obstacles.Add(o);
            float ex = Mathf.Abs(o.ux) * o.hx + Mathf.Abs(o.uz) * o.hz;
            float ez = Mathf.Abs(o.uz) * o.hx + Mathf.Abs(o.ux) * o.hz;
            int x0 = ObCell(o.cx - ex), x1 = ObCell(o.cx + ex), z0 = ObCell(o.cz - ez), z1 = ObCell(o.cz + ez);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * OB_N + x;
                    if (obGrid[c] == null) obGrid[c] = new List<int>();
                    obGrid[c].Add(idx);
                }
        }

        /// Visits every obstacle whose grid cells overlap the square around (x, z); returns the stamp to compare against.
        bool NextCandidate(ref int cellX, ref int cellZ, int x0, int x1, int z0, int z1, ref int k, out int idx)
        {
            idx = -1;
            while (cellZ <= z1)
            {
                var list = obGrid[cellZ * OB_N + cellX];
                if (list != null)
                {
                    while (k < list.Count)
                    {
                        int i = list[k++];
                        if (obStamp[i] == obQuery) continue;
                        obStamp[i] = obQuery;
                        idx = i;
                        return true;
                    }
                }
                k = 0;
                if (++cellX > x1) { cellX = x0; cellZ++; }
            }
            return false;
        }

        void BeginObstacleQuery()
        {
            if (obStamp.Length < obstacles.Count) obStamp = new int[obstacles.Count + 256];
            obQuery++;
        }

        // ---------------------------------------------------------------- ruins: queries

        /// Pushes a cylinder (feet at feetY, given height and radius) out of every ruin box it overlaps. Boxes whose top is
        /// within 0.5 of the feet (the player is on top or can step up) or entirely above the head do not block.
        public bool PushOutOfObstacles(ref Vector3 pos, float feetY, float height, float radius)
        {
            if (obGrid == null || obstacles.Count == 0) return false;
            bool any = false;
            for (int pass = 0; pass < 2; pass++)
            {
                BeginObstacleQuery();
                int x0 = ObCell(pos.x - radius), x1 = ObCell(pos.x + radius), z0 = ObCell(pos.z - radius), z1 = ObCell(pos.z + radius);
                int cx = x0, cz = z0, k = 0;
                bool moved = false;
                while (NextCandidate(ref cx, ref cz, x0, x1, z0, z1, ref k, out int i))
                {
                    var o = obstacles[i];
                    if (feetY >= o.top - 0.5f || feetY + height <= o.bottom) continue;
                    float dx = pos.x - o.cx, dz = pos.z - o.cz;
                    float u = dx * o.ux + dz * o.uz, v = -dx * o.uz + dz * o.ux;
                    float cu = Mathf.Clamp(u, -o.hx, o.hx), cv = Mathf.Clamp(v, -o.hz, o.hz);
                    float du = u - cu, dv = v - cv;
                    float d2 = du * du + dv * dv;
                    if (d2 >= radius * radius) continue;
                    float nu, nv, push;
                    if (d2 > 1e-6f)
                    {
                        float d = Mathf.Sqrt(d2);
                        nu = du / d; nv = dv / d; push = radius - d;
                    }
                    else
                    {
                        float pu = o.hx - Mathf.Abs(u), pv = o.hz - Mathf.Abs(v);
                        if (pu < pv) { nu = u >= 0f ? 1f : -1f; nv = 0f; push = pu + radius; }
                        else { nu = 0f; nv = v >= 0f ? 1f : -1f; push = pv + radius; }
                    }
                    pos.x += (nu * o.ux - nv * o.uz) * push;
                    pos.z += (nu * o.uz + nv * o.ux) * push;
                    moved = true;
                }
                if (!moved) break;
                any = true;
            }
            return any;
        }

        /// Highest ruin top under (x, z) that is at or below fromY + 0.5 (the player's "floor" on a wall), else -infinity.
        public float ObstacleTop(float x, float z, float fromY)
        {
            float best = float.NegativeInfinity;
            if (obGrid == null) return best;
            var list = obGrid[ObCell(z) * OB_N + ObCell(x)];
            if (list == null) return best;
            for (int k = 0; k < list.Count; k++)
            {
                var o = obstacles[list[k]];
                if (o.top > fromY + 0.5f || o.top <= best) continue;
                float dx = x - o.cx, dz = z - o.cz;
                float u = dx * o.ux + dz * o.uz, v = -dx * o.uz + dz * o.ux;
                if (Mathf.Abs(u) <= o.hx && Mathf.Abs(v) <= o.hz) best = o.top;
            }
            return best;
        }

        /// What a walker standing at (x, z) rests on: terrain, an island top or a ruin top, whichever is highest below fromY + 0.5.
        public float GroundHeight(float x, float z, float fromY) => Mathf.Max(SurfaceHeight(x, z, fromY), ObstacleTop(x, z, fromY));

        public bool PointInObstacle(Vector3 p, float margin)
        {
            if (obGrid == null) return false;
            var list = obGrid[ObCell(p.z) * OB_N + ObCell(p.x)];
            if (list == null) return false;
            for (int k = 0; k < list.Count; k++)
            {
                var o = obstacles[list[k]];
                if (p.y > o.top + margin || p.y < o.bottom - margin) continue;
                float dx = p.x - o.cx, dz = p.z - o.cz;
                float u = dx * o.ux + dz * o.uz, v = -dx * o.uz + dz * o.ux;
                if (Mathf.Abs(u) <= o.hx + margin && Mathf.Abs(v) <= o.hz + margin) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- islands: queries

        const float ISLAND_SHRINK = 0.97f;

        /// How far below the top surface the rock's underside is at normalized radius u (0 = center, 1 = rim);
        /// negative when there is no rock at that radius.
        public static float RimDepth(Island isl, float u)
        {
            if (u >= 1f) return -1f;
            if (u >= 0.96f) return 1.1f;
            return 1.1f + isl.depth * Mathf.Pow(1f - Mathf.Pow(Mathf.Max(u, 0f), 1.25f), 1.1f);
        }

        /// Smallest normalized radius at which the rock's underside is at least `room` below the top surface.
        static float RoomToU(Island isl, float room)
        {
            if (room < 1.1f) return 1f;
            float f = (room - 1.1f) / Mathf.Max(isl.depth, 0.01f);
            if (f >= 1f) return 0f;
            return Mathf.Pow(1f - Mathf.Pow(f, 1f / 1.1f), 0.8f);
        }

        /// Keeps a player cylinder out of the islands' rock: pushes it out sideways when it moves into the side, and stops it
        /// under the belly when it flies up into it (vy is zeroed then). `from` is where the player was before the move.
        public void CollideIslands(Vector3 from, ref Vector3 pos, float radius, float height, ref float vy)
        {
            for (int n = 0; n < islands.Count; n++)
            {
                var isl = islands[n];
                if (from.y >= isl.topY - 0.3f) continue;
                float tip = isl.topY - isl.depth * 1.15f - 1f;
                if (from.y + height < tip && pos.y + height < tip) continue;
                var c = isl.center;
                float dx = pos.x - c.x, dz = pos.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > isl.radius * 1.3f + radius + 1f) continue;
                float R = isl.EdgeRadius(Mathf.Atan2(dz, dx)) * ISLAND_SHRINK;
                // Sideways: the rock above the old head height is the hull the body must stay outside of.
                float uReq = RoomToU(isl, isl.topY - (from.y + height));
                float need = R * uReq + radius;
                if (dist < need)
                {
                    float nx, nz;
                    if (dist > 0.01f) { nx = dx / dist; nz = dz / dist; }
                    else
                    {
                        float fx = from.x - c.x, fz = from.z - c.z, fd = Mathf.Sqrt(fx * fx + fz * fz);
                        if (fd > 0.01f) { nx = fx / fd; nz = fz / fd; } else { nx = 1f; nz = 0f; }
                    }
                    pos.x = c.x + nx * need;
                    pos.z = c.z + nz * need;
                    dist = need;
                }
                // Upward: the underside is a ceiling.
                float u = Mathf.Max(0f, dist - radius) / R;
                if (u < 1f)
                {
                    float yU = isl.topY - RimDepth(isl, u);
                    if (pos.y + height > yU && from.y + height <= yU + 0.1f)
                    {
                        pos.y = yU - height;
                        if (vy > 0f) vy = 0f;
                    }
                }
            }
        }

        /// Whether a point (with a safety margin) is inside the rock of an island.
        public bool InsideIslandRock(Vector3 p, float margin)
        {
            for (int n = 0; n < islands.Count; n++)
            {
                var isl = islands[n];
                float d = isl.topY - p.y;
                if (d < -margin || d > isl.depth * 1.15f + 1f + margin) continue;
                float dx = p.x - isl.center.x, dz = p.z - isl.center.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > isl.radius * 1.3f + margin) continue;
                float R = isl.EdgeRadius(Mathf.Atan2(dz, dx)) * ISLAND_SHRINK;
                float u = Mathf.Max(0f, dist - margin) / R;
                if (u >= 1f) continue;
                if (d <= 0f || d <= RimDepth(isl, u) + margin) return true;
            }
            return false;
        }

        /// True where the camera must not be: inside island rock or a solid ruin.
        public bool CameraBlocked(Vector3 p) => InsideIslandRock(p, 0.35f) || PointInObstacle(p, 0.3f);

        /// Player collision against the world: island bodies, then ruin boxes. Returns true if the horizontal position changed.
        public bool CollidePlayer(Vector3 from, ref Vector3 pos, float radius, float height, ref float vy)
        {
            var before = pos;
            CollideIslands(from, ref pos, radius, height, ref vy);
            PushOutOfObstacles(ref pos, from.y, height, radius);
            return Mathf.Abs(pos.x - before.x) + Mathf.Abs(pos.z - before.z) > 1e-5f;
        }

        // ---------------------------------------------------------------- ruins: building the boxes from the meshes

        struct MeshData { public Vector3[] v; public int[] t; }
        readonly Dictionary<Mesh, MeshData> meshData = new Dictionary<Mesh, MeshData>();
        readonly List<Vector3> kitSamples = new List<Vector3>();

        /// Turns a placed Ruins kit piece into walk-blocking boxes: its surface is sampled, and every 0.4 m column of the
        /// piece's footprint that has surface between 1 and 2.2 m above the ground is solid (so walls, pillars and building
        /// shells block, while rubble, pews, low tombs and floor slabs stay walk-over, and hollow buildings stay open inside).
        /// Solid columns are merged into boxes whose top is the highest surface in them, so wall tops can be stood on.
        int RegisterKitObstacles(Transform holder, string name, float groundY, float baseY)
        {
            if (name.StartsWith("vitrail") || name.StartsWith("iron_gate")) return 0;
            float lo = groundY + 1f, hi = groundY + 2.2f;
            var inv = Quaternion.Inverse(holder.rotation);
            var origin = holder.position;
            kitSamples.Clear();
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                if (!meshData.TryGetValue(mesh, out var md))
                {
                    md = new MeshData { v = mesh.vertices, t = mesh.triangles };
                    meshData[mesh] = md;
                }
                var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < md.t.Length; i += 3)
                {
                    var a = m.MultiplyPoint3x4(md.v[md.t[i]]);
                    var b = m.MultiplyPoint3x4(md.v[md.t[i + 1]]);
                    var c = m.MultiplyPoint3x4(md.v[md.t[i + 2]]);
                    if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < lo) continue;
                    float e = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
                    int n = Mathf.Clamp(Mathf.CeilToInt(e / 0.3f), 1, 64);
                    for (int s = 0; s <= n; s++)
                        for (int t = 0; t <= n - s; t++)
                        {
                            var p = a + (b - a) * (s / (float)n) + (c - a) * (t / (float)n);
                            var l = inv * (p - origin);
                            kitSamples.Add(new Vector3(l.x, p.y, l.z));
                        }
                }
            }
            if (kitSamples.Count == 0) return 0;
            const float cs = 0.4f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in kitSamples)
            {
                if (s.x < minX) minX = s.x;
                if (s.x > maxX) maxX = s.x;
                if (s.z < minZ) minZ = s.z;
                if (s.z > maxZ) maxZ = s.z;
            }
            int W = Mathf.CeilToInt((maxX - minX) / cs) + 1, H = Mathf.CeilToInt((maxZ - minZ) / cs) + 1;
            var top = new float[W * H];
            var solid = new bool[W * H];
            var used = new bool[W * H];
            for (int i = 0; i < top.Length; i++) top[i] = float.NegativeInfinity;
            foreach (var s in kitSamples)
            {
                int ix = Mathf.Min((int)((s.x - minX) / cs), W - 1), iz = Mathf.Min((int)((s.z - minZ) / cs), H - 1);
                int c = iz * W + ix;
                if (s.y > top[c]) top[c] = s.y;
                if (s.y >= lo && s.y <= hi) solid[c] = true;
            }
            // The meshes are hollow shells (only their faces carry samples), so close gaps of up to 1.2 m between solid columns:
            // that fills the inside of walls and pillars while leaving rooms and doorways open.
            var orig = (bool[])solid.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                int outer = pass == 0 ? H : W, inner = pass == 0 ? W : H;
                for (int a = 0; a < outer; a++)
                {
                    int last = -1;
                    for (int b = 0; b < inner; b++)
                    {
                        int c = pass == 0 ? a * W + b : b * W + a;
                        if (!orig[c]) continue;
                        if (last >= 0 && b - last > 1 && b - last <= 4)
                        {
                            int cl = pass == 0 ? a * W + last : last * W + a;
                            for (int k = last + 1; k < b; k++)
                            {
                                int ck = pass == 0 ? a * W + k : k * W + a;
                                solid[ck] = true;
                                top[ck] = Mathf.Max(top[ck], Mathf.Max(top[cl], top[c]));
                            }
                        }
                        last = b;
                    }
                }
            }
            var rot = holder.rotation;
            var axis = rot * Vector3.right;
            int count = 0;
            for (int iz = 0; iz < H; iz++)
                for (int ix = 0; ix < W; ix++)
                {
                    int c0 = iz * W + ix;
                    if (!solid[c0] || used[c0]) continue;
                    float t0 = top[c0];
                    int w = 1;
                    while (ix + w < W && solid[c0 + w] && !used[c0 + w] && Mathf.Abs(top[c0 + w] - t0) < 0.6f) w++;
                    int h = 1;
                    for (; iz + h < H; h++)
                    {
                        bool ok = true;
                        for (int k = 0; k < w && ok; k++)
                        {
                            int c = (iz + h) * W + ix + k;
                            ok = solid[c] && !used[c] && Mathf.Abs(top[c] - t0) < 0.6f;
                        }
                        if (!ok) break;
                    }
                    float tmax = t0;
                    for (int r = 0; r < h; r++)
                        for (int k = 0; k < w; k++)
                        {
                            int c = (iz + r) * W + ix + k;
                            used[c] = true;
                            if (top[c] > tmax) tmax = top[c];
                        }
                    float lx = minX + (ix + w * 0.5f) * cs, lz = minZ + (iz + h * 0.5f) * cs;
                    var wc = rot * new Vector3(lx, 0f, lz);
                    AddObstacle(new Obstacle
                    {
                        cx = origin.x + wc.x, cz = origin.z + wc.z, ux = axis.x, uz = axis.z,
                        hx = Mathf.Max(0.2f, w * cs * 0.5f - 0.1f), hz = Mathf.Max(0.2f, h * cs * 0.5f - 0.1f),
                        bottom = baseY, top = tmax, name = name,
                    });
                    count++;
                }
            return count;
        }
    }
}
