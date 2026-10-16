using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// Writes Logs/ruins_report.txt: every piece of the Ruins Creation Kit model with its size and materials.
    public static class SkyfallKitReport
    {
        public static void Run()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ruins_creation_kit/ruins_creation_kit.FBX");
            var sb = new StringBuilder();
            if (go == null) { Debug.LogWarning("SKYFALL_TEST ruins kit missing"); return; }
            sb.AppendLine($"root {go.name} scale={go.transform.localScale} children={go.transform.childCount}");
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var t = mf.transform;
                var mr = mf.GetComponent<MeshRenderer>();
                var b = mf.sharedMesh.bounds;
                var s = Vector3.Scale(b.size, t.lossyScale);
                string mats = mr ? string.Join(",", mr.sharedMaterials.Select(m => m ? m.name + "[" + m.shader.name + "]" : "null")) : "";
                sb.AppendLine($"{t.name} pos={t.position} rot={t.eulerAngles} size={s.x:0.00}x{s.y:0.00}x{s.z:0.00} center={b.center} tris={mf.sharedMesh.triangles.Length / 3} mats={mats}");
            }
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ruins_report.txt"), sb.ToString());
            Debug.Log("SKYFALL_TEST ruins report written");
        }
    }
}
