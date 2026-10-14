using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// Command-line checks: compiles every pass of the Skyfall shaders and reports errors, and plays the game
    /// headless for a few seconds while saving camera snapshots to Logs/.
    /// Usage: Unity.exe -batchmode -projectPath ... -executeMethod Skyfall.EditorTools.SkyfallTests.Run
    [InitializeOnLoad]
    public static class SkyfallTests
    {
        const string Key = "SkyfallTestsRunning";
        static int frame;
        static int stage;

        static SkyfallTests()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            CheckShaders();
            EditorSceneManager.OpenScene(SkyfallSetup.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Tick;
            Time.captureDeltaTime = 1f / 60f;
            EditorApplication.isPlaying = true;
        }

        public static void CheckShaders()
        {
            foreach (var name in new[] { "Skyfall/Lit", "Skyfall/Unlit", "Skyfall/DragonScales", "Skyfall/Sky" })
            {
                var sh = Shader.Find(name);
                if (sh == null)
                {
                    Debug.LogError($"SKYFALL_TEST shader missing: {name}");
                    continue;
                }
                var mat = new Material(sh);
                for (int p = 0; p < sh.passCount; p++) ShaderUtil.CompilePass(mat, p, true);
                var msgs = ShaderUtil.GetShaderMessages(sh);
                int errors = 0;
                foreach (var m in msgs)
                {
                    Debug.Log($"SKYFALL_TEST shader {name}: [{m.severity}] {m.message} (line {m.line}) {m.file}");
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors++;
                }
                Debug.Log($"SKYFALL_TEST shader {name}: {sh.passCount} passes, {errors} errors");
                Object.DestroyImmediate(mat);
            }
        }

        static void Snap(string name)
        {
            var game = Game.I;
            if (game == null || game.player == null || game.player.cam == null)
            {
                Debug.LogError("SKYFALL_TEST no game/camera to snapshot");
                return;
            }
            var cam = game.player.cam;
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"SKYFALL_TEST snapshot saved: {path}");
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            frame++;
            Time.captureDeltaTime = 1f / 60f;
            var game = Game.I;
            try
            {
                if (stage == 0 && Arg("-skyfallSkyPreview", "") != "")
                {
                    // Photograph each candidate skybox (comma-separated material paths).
                    var skies = Arg("-skyfallSkyPreview", "").Split(',');
                    int idx = (frame - 30) / 4;
                    if (frame >= 30 && idx < skies.Length)
                    {
                        if ((frame - 30) % 4 == 0)
                        {
                            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(skies[idx]);
                            game.player.camPitch = -0.15f;
                        }
                        if ((frame - 30) % 4 == 2) Snap("sky_" + Path.GetFileNameWithoutExtension(skies[idx]));
                        return;
                    }
                    if (frame >= 30) Finish(0);
                    return;
                }
                if (stage == 0 && Arg("-skyfallVfxSheet", "") != "" && Skins.Lib != null)
                {
                    // Photograph each effect on its own, a few frames after it starts.
                    var names = Skins.Lib.vfx;
                    int idx = (frame - 30) / 12, sub = (frame - 30) % 12;
                    if (frame >= 30 && idx < names.Count)
                    {
                        var cam = game.player.cam.transform;
                        if (sub == 0)
                        {
                            var p = cam.position + cam.forward * 9f;
                            p.y = game.terrain.Height(p.x, p.z) + 1f;
                            FX.Vfx(names[idx].name, p, 1f, 0.18f, null, 0f);
                        }
                        if (sub == 6) Snap("vfx_" + names[idx].name);
                        return;
                    }
                }
                if (stage == 0 && frame > 60 && (Arg("-skyfallVfxSheet", "") == "" || frame > 30 + 12 * Skins.Lib.vfx.Count))
                {
                    // Loadout from the command line: -skyfallLoadout hero,dragon,spell,passive
                    var lo = Arg("-skyfallLoadout", "knight,blue,chain_lightning,blood_aura").Split(',');
                    tag = lo[0];
                    Snap($"snap_{tag}_1_menu");
                    stage = 1;
                    frame = 0;
                    game.StartRun(lo[2], lo[3], lo[0], lo[1]);
                    game.timeElapsed = 110f;   // skip ahead: the field fills with ogres and the first boss arrives soon
                    game.player.hp = game.player.maxHp = 5000f;   // keep the hero alive for the whole test
                }
                else if (stage == 1)
                {
                    game.player.hp = game.player.maxHp;
                    if (frame % 300 == 0) game.pet.RideCommand();                       // E: dragon signature on the ground
                    if (frame == 400)
                        typeof(Player).GetMethod("SummonDragon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(game.player, null);                                 // Q: take flight
                    if (frame == 500) game.pet.RideCommand();                           // E from the air: leap down and slam
                    if (frame == 900)
                    {
                        Snap($"snap_{tag}_2_battle");
                        // -skyfallProbe x,y : list renderers drawn near that pixel of the snapshot (1280x720, y from top).
                        if (Arg("-skyfallProbeDragon", "") != "")
                        {
                            var petPos = game.pet.transform.position;
                            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                                if (r.transform.IsChildOf(game.pet.transform) && r.GetComponentInParent<CharacterRig>() != null) continue;
                                if (Vector3.Distance(r.bounds.center, petPos) > 7f) continue;
                                string path = r.name;
                                for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                                var m = r.sharedMaterial;
                                Debug.Log($"SKYFALL_PROBE {path} mat={(m ? m.name : "none")} shader={(m && m.shader ? m.shader.name : "none")} size={r.bounds.size} type={r.GetType().Name}");
                            }
                        }
                        var probe = Arg("-skyfallProbe", "");
                        if (probe != "")
                        {
                            var xy = probe.Split(',');
                            var target = new Vector2(float.Parse(xy[0]) * Screen.width / 1280f, (720f - float.Parse(xy[1])) * Screen.height / 720f);
                            var cam = game.player.cam;
                            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                                var sp = cam.WorldToScreenPoint(r.bounds.center);
                                var spScaled = new Vector2(sp.x * 1280f / Screen.width, sp.y * 720f / Screen.height);
                                var t = new Vector2(float.Parse(xy[0]), 720f - float.Parse(xy[1]));
                                if (sp.z > 0 && (spScaled - t).magnitude < 70f)
                                {
                                    string path = r.name;
                                    for (var p = r.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                                    var m = r.sharedMaterial;
                                    Debug.Log($"SKYFALL_PROBE {path} mat={(m ? m.name : "none")} shader={(m && m.shader ? m.shader.name : "none")} size={r.bounds.size}");
                                }
                            }
                        }
                        Log("battle", game);
                        game.player.camYaw += 2.2f;
                        game.player.camDistTarget = 16f;
                        game.player.camPitch = 0.45f;
                    }
                    if (frame == 1500)
                    {
                        Snap($"snap_{tag}_3_wide");
                        Log("wide", game);
                        Finish(0);
                    }
                }
                if (game != null && game.leveling)
                {
                    // Auto-pick the first level-up card so the run keeps going.
                    var hudField = typeof(Hud).GetField("levelOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var opts = hudField.GetValue(game.hud) as System.Collections.Generic.List<Game.LevelOption>;
                    if (opts != null && opts.Count > 0)
                    {
                        hudField.SetValue(game.hud, null);
                        game.ChooseOption(opts[0]);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("SKYFALL_TEST exception: " + e);
                Finish(1);
            }
        }

        static string tag = "run";

        static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        static void Log(string what, Game game) =>
            Debug.Log($"SKYFALL_TEST {tag} {what}: enemies={game.enemies.Count} kills={game.kills} lvl={game.player.level} " +
                      $"t={game.timeElapsed:0} bosses={game.bosses.Count} spawned={game.bossesSpawned} dmg={game.damageDealt:0}");

        static void Finish(int code)
        {
            Time.captureDeltaTime = 0f;
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}
