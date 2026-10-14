using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// Writes Logs/material_report.txt: every material in the imported packs grouped by shader, with whether the
    /// shader runs under the current render pipeline.
    public static class SkyfallMaterialReport
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var mats = AssetDatabase.FindAssets("t:Material", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/Skyfall/") && !p.StartsWith("Assets/Settings/"))
                .Select(p => (path: p, mat: AssetDatabase.LoadAssetAtPath<Material>(p)))
                .Where(x => x.mat != null)
                .ToList();
            foreach (var g in mats.GroupBy(x => x.mat.shader ? x.mat.shader.name : "null").OrderBy(g => g.Key))
            {
                var sh = g.First().mat.shader;
                bool ok = sh != null && sh.isSupported && !sh.name.StartsWith("Hidden/InternalErrorShader");
                sb.AppendLine($"## {g.Key}  supported={ok}  count={g.Count()}  file={(sh ? AssetDatabase.GetAssetPath(sh) : "")}");
                foreach (var x in g.Take(400))
                {
                    var top = x.path.Split('/')[1];
                    sb.AppendLine($"  {x.path}");
                }
            }
            sb.AppendLine("## Prefab counts by folder");
            foreach (var root in new[] { "Assets/Hovl Studio", "Assets/Matthew Guz", "Assets/Polytope Studio", "Assets/LightningBolt" })
            {
                var prefabs = Directory.GetFiles(root, "*.prefab", SearchOption.AllDirectories);
                sb.AppendLine($"{root}: {prefabs.Length}");
                foreach (var p in prefabs)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\', '/'));
                    if (go == null) { sb.AppendLine($"  (null) {p}"); continue; }
                    var ps = go.GetComponentsInChildren<ParticleSystem>(true);
                    float dur = ps.Length > 0 ? ps.Max(s => s.main.duration + s.main.startLifetime.constantMax) : 0f;
                    bool loop = ps.Any(s => s.main.loop);
                    var rends = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
                    var b = rends.Length > 0 ? rends[0].bounds : new Bounds();
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    var comps = go.GetComponentsInChildren<MonoBehaviour>(true).Select(m => m ? m.GetType().Name : "MISSING").Distinct();
                    sb.AppendLine($"  {p.Replace('\\', '/')} ps={ps.Length} dur={dur:0.0} loop={loop} size={b.size.x:0.0}x{b.size.y:0.0}x{b.size.z:0.0} scripts={string.Join(",", comps)}");
                }
            }
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "material_report.txt"), sb.ToString());
            Debug.Log("SKYFALL_TEST material report written");
        }
    }
}
