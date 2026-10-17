using System.Collections.Generic;
using MicahW.PointGrass;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Short ground grass drawn with the Point Grass Renderer's GPU shader: every blade is one entry in a per-chunk compute
    /// buffer and each visible chunk is a single instanced draw call (no GameObject per blade). Only chunks near the player and
    /// inside the camera view are drawn, thinned out with distance, so hundreds of enemies cost the same as on bare ground.
    public class GrassField : MonoBehaviour
    {
        public const float CHUNK = 24f;
        const int GRID = 24;                 // covers the whole 520 m world
        const float ORIGIN = -WorldTerrain.HALF - 4f;

        /// Look of one stage's grass.
        public struct Style
        {
            public Color bottom, top, subsurface;
            public float height, width, bright;
        }

        class Chunk
        {
            public readonly List<PointGrassCommon.MeshPoint> points = new List<PointGrassCommon.MeshPoint>();
            public ComputeBuffer buffer;
            public MaterialPropertyBlock block;
            public Bounds bounds;
            public float minX, maxX, minZ, maxZ;
            public int count;
        }

        Chunk[] chunks;
        readonly List<Chunk> live = new List<Chunk>();
        Material mat;
        Mesh blade;
        float height;
        /// Chunks farther than this from the player are skipped; density thins out from the half-way mark.
        public float drawRadius = 78f;
        Camera cam;
        readonly Plane[] planes = new Plane[6];

        /// Starts a grass field from the library's grass material (null when the Point Grass package, the material or GPU
        /// support is missing: the game then simply has no grass).
        public static GrassField Create(Transform parent, Material template, Style style)
        {
            if (template == null) return null;
            if (!SystemInfo.supportsInstancing || SystemInfo.graphicsShaderLevel < 45 || !SystemInfo.supportsComputeShaders) return null;
            var go = new GameObject("Grass");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<GrassField>();
            f.Init(template, style);
            return f;
        }

        void Init(Material template, Style style)
        {
            PointGrassCommon.FindPropertyIDs();
            if (!PointGrassCommon.BladeMeshesGenerated) PointGrassCommon.GenerateGrassMeshes();
            blade = PointGrassCommon.grassMeshFlat;
            mat = new Material(template) { name = "GrassStage" };
            mat.SetColor("_BottomCol", style.bottom);
            mat.SetColor("_TopCol", style.top);
            mat.SetColor("_SubsurfaceCol", style.subsurface);
            mat.SetFloat("_Height", style.height);
            mat.SetFloat("_Width", style.width);
            mat.SetFloat("_TopBright", style.bright);
            mat.SetFloat("_Smoothness", 0.05f);
            height = style.height;
            chunks = new Chunk[GRID * GRID];
            // The package's wind component feeds the shader's global wind parameters (a slow rolling breeze).
            var wind = gameObject.AddComponent<PointGrassWind>();
            wind.windScale = 0.12f;
            wind.noiseRange = new Vector2(-0.4f, 1f);
            wind.windDirection = new Vector3(0.6f, 0f, 0.3f);
            wind.windScroll = new Vector3(2.2f, 0f, 1.1f);
            wind.RefreshValues();
        }

        static int CellOf(float v) => Mathf.Clamp((int)((v - ORIGIN) / CHUNK), 0, GRID - 1);

        /// Adds one blade at a world position; len scales the blade's height (about 0.6..1.2) and r is a 0..1 random value.
        public void Add(Vector3 p, float len, float r)
        {
            int ix = CellOf(p.x), iz = CellOf(p.z);
            int i = iz * GRID + ix;
            var c = chunks[i];
            if (c == null)
            {
                c = new Chunk { minX = ORIGIN + ix * CHUNK, minZ = ORIGIN + iz * CHUNK };
                c.maxX = c.minX + CHUNK;
                c.maxZ = c.minZ + CHUNK;
                chunks[i] = c;
            }
            c.points.Add(new PointGrassCommon.MeshPoint(p, Vector3.up, Color.white, new Vector4(0f, 0f, len, r)));
        }

        /// Uploads the points to the GPU; call once after the last Add.
        public void Finish()
        {
            var shuffle = new System.Random(7);
            for (int i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                if (c == null) continue;
                if (c.points.Count == 0) { chunks[i] = null; continue; }
                c.count = c.points.Count;
                // Shuffle so a prefix of the buffer is an even sample (ground and island blades interleave).
                for (int k = c.count - 1; k > 0; k--)
                {
                    int j = shuffle.Next(k + 1);
                    (c.points[k], c.points[j]) = (c.points[j], c.points[k]);
                }
                c.buffer = new ComputeBuffer(c.count, PointGrassCommon.MeshPoint.stride);
                c.buffer.SetData(c.points);
                var b = new Bounds(c.points[0].position, Vector3.zero);
                for (int k = 1; k < c.count; k++) b.Encapsulate(c.points[k].position);
                b.Expand(new Vector3(1f, height * 2.5f, 1f));
                c.bounds = b;
                c.block = new MaterialPropertyBlock();
                c.block.SetBuffer(PointGrassCommon.ID_PointBuff, c.buffer);
                c.block.SetMatrix(PointGrassCommon.ID_MatrixL2W, Matrix4x4.identity);
                c.block.SetMatrix(PointGrassCommon.ID_MatrixW2L, Matrix4x4.identity);
                c.points.Clear();
                c.points.TrimExcess();
                live.Add(c);
            }
        }

        public int BladeCount
        {
            get { int n = 0; foreach (var c in live) n += c.count; return n; }
        }

        void LateUpdate()
        {
            if (live.Count == 0 || mat == null || blade == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var focus = Game.I != null && Game.I.player != null ? Game.I.player.Pos : cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            float fadeFrom = drawRadius * 0.45f;
            for (int i = 0; i < live.Count; i++)
            {
                var c = live[i];
                // Distance on the ground plane from the player to the chunk's rectangle.
                float dx = Mathf.Max(Mathf.Max(c.minX - focus.x, focus.x - c.maxX), 0f);
                float dz = Mathf.Max(Mathf.Max(c.minZ - focus.z, focus.z - c.maxZ), 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= drawRadius) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                // Points are in random order, so drawing a prefix thins the chunk evenly.
                float keep = 1f - M.Smoothstep(fadeFrom, drawRadius, d);
                int n = Mathf.CeilToInt(c.count * keep);
                if (n <= 0) continue;
                Graphics.DrawMeshInstancedProcedural(blade, 0, mat, c.bounds, n, c.block, ShadowCastingMode.Off, true, 0, null);
            }
        }

        void OnDestroy()
        {
            foreach (var c in live) if (c.buffer != null) c.buffer.Release();
            live.Clear();
            if (mat) Destroy(mat);
        }
    }
}
