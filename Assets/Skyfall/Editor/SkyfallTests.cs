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
    public static partial class SkyfallTests
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
            AudioListener.volume = 0f;
            EditorApplication.isPlaying = true;
        }

        /// -executeMethod Skyfall.EditorTools.SkyfallTests.KitReport: logs every Ruins kit piece's size and triangle count.
        public static void KitReport()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SkinLibrary>("Assets/Skyfall/Resources/Skyfall/SkinLibrary.asset");
            foreach (var mf in lib.ruinsKit.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = mf.sharedMesh.bounds;
                var m = mf.transform.localToWorldMatrix;
                var wb = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    wb.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                Debug.Log($"SKYFALL_TEST kit {mf.name} size={wb.size:F2} tris={mf.sharedMesh.triangles.Length / 3} readable={mf.sharedMesh.isReadable} subs={mf.sharedMesh.subMeshCount} rot={mf.transform.localRotation.eulerAngles} scale={mf.transform.localScale}");
            }
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
            // Test runs are silent: the game sets the master volume when it starts, so hold it at zero every frame.
            AudioListener.volume = 0f;
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
                if (stage == 0 && Arg("-skyfallShop", "") != "")
                {
                    ShopTest(game);
                    return;
                }
                if (stage == 0 && frame > 60 && (Arg("-skyfallVfxSheet", "") == "" || frame > 30 + 12 * Skins.Lib.vfx.Count))
                {
                    // Loadout from the command line: -skyfallLoadout hero,dragon,spell,passive
                    var lo = Arg("-skyfallLoadout", "knight,blue,chain_lightning,blood_aura").Split(',');
                    tag = lo[0];
                    Snap($"snap_{tag}_1_menu");
                    stage = 1;
                    frame = 0;
                    ApplyGear();
                    game.StartRun(lo[2], lo[3], lo[0], lo[1]);
                    LogStats(game, "start");
                    if (Arg("-skyfallIslandView", "") == "" && Arg("-skyfallFinale", "") == "" && Arg("-skyfallSolidTest", "") == "" && Arg("-skyfallKitView", "") == "") game.timeElapsed = 110f;   // skip ahead: the field fills with ogres and the first boss arrives soon
                    game.player.hp = game.player.maxHp = 5000f;   // keep the hero alive for the whole test
                }
                else if (stage == 1 && Arg("-skyfallFinale", "") != "")
                {
                    Finale(game);
                }
                else if (stage == 1 && Arg("-skyfallBowView", "") != "")
                {
                    BowView(game);
                }
                else if (stage == 1 && Arg("-skyfallKitView", "") != "")
                {
                    KitView(game);
                }
                else if (stage == 1 && Arg("-skyfallSolidTest", "") != "")
                {
                    SolidTest(game);
                }
                else if (stage == 1 && Arg("-skyfallIslandView", "") != "")
                {
                    IslandView(game);
                }
                else if (stage == 1)
                {
                    game.player.hp = game.player.maxHp;
                    TimeFrame(game);
                    if (frame == 200 && Arg("-skyfallStress", "") != "")
                    {
                        // Performance check: 300 tough ogres around the hero.
                        int n = int.Parse(Arg("-skyfallStress", "300"));
                        string[] kinds = { "runt", "ogre", "ogre", "brute", "hulk", "archer" };
                        for (int i = 0; i < n; i++)
                        {
                            float a = i * 2.399f, r = 6f + 22f * Mathf.Sqrt(i / (float)n);
                            var p = game.player.Pos + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                            var e = game.SpawnEnemy(kinds[i % kinds.Length], p);
                            e.hp = e.maxHp = 1e7f;
                        }
                    }
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
                            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
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
                            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
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
                    if (frame == 1480 && Arg("-skyfallBank", "") != "")
                    {
                        // Stand-in for walking over the loot: pull every coin onto the hero so it is collected.
                        int n = 0;
                        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                            if (t.name == "Coin") { t.position = game.player.Center; n++; }
                        Debug.Log($"SKYFALL_TEST vacuumed {n} coins; runGold before={game.runGold}");
                    }
                    if (frame == 1500)
                    {
                        Snap($"snap_{tag}_3_wide");
                        Log("wide", game);
                        Debug.Log($"SKYFALL_TEST perf: avg frame {perfMs / Mathf.Max(1, perfFrames):0.00} ms over {perfFrames} frames, avg enemies {perfEnemies / Mathf.Max(1, perfFrames):0}");
                        if (Arg("-skyfallBank", "") != "") Bank(game);
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

        /// -skyfallGear "sword=5,javelin=3,bow=5,helm=2,chest=5,gloves=5,legs=5,boots=5,gold=1000": sets shop levels in the TEST save
        /// (refused unless -skyfallSaveFile is given, so the real save is never touched).
        static void ApplyGear()
        {
            var gear = Arg("-skyfallGear", "");
            if (gear == "") return;
            if (Arg("-skyfallSaveFile", "") == "")
            {
                Debug.LogError("SKYFALL_TEST -skyfallGear needs -skyfallSaveFile; ignored");
                return;
            }
            foreach (var kv in gear.Split(','))
            {
                var p = kv.Split('=');
                int v = p[1] == "all" ? 1 : int.Parse(p[1]);
                switch (p[0])
                {
                    case "sword": Save.SetWeaponLevel("sword", v); break;
                    case "javelin": Save.SetWeaponLevel("blood_lance", v); break;
                    case "bow": Save.SetWeaponLevel("bow", v); break;
                    case "gold": Save.gold = v; break;
                    case "alldragons": Save.unlocked_dragons = new System.Collections.Generic.List<string>(Data.DRAGON_ORDER); break;
                    default: Save.SetArmorTier(p[0], v); break;
                }
            }
            Save.Write();
        }

        static void LogStats(Game game, string what)
        {
            var p = game.player;
            Debug.Log($"SKYFALL_TEST stats {what} {p.heroId}: weapon={p.Weapon.shopName} wLv={p.swordLevel} dmg/hit={p.WeaponBaseDamage * p.WeaponDamageMult:0.0} interval={p.AttackInterval:0.000}s shots={p.ShotCount} " +
                      $"armor={p.Armor:0.0} defLv={p.shieldLevel} block={p.BlockChance:0.00} dodge={p.DodgeChance:0.00} crit={p.CritChance:0.00} move={p.MoveMult:0.00} maxHp={p.maxHp:0.0} " +
                      $"armorTiers=[helm {Save.armor_helm}, chest {Save.armor_chest}, gloves {Save.armor_gloves}, legs {Save.armor_legs}, boots {Save.armor_boots}] gold={Save.gold}");
        }

        /// -skyfallBank 1: ends the run by death at the end of the test and logs gold before and after banking.
        static void Bank(Game game)
        {
            int before = Save.gold, run = game.runGold;
            game.OnPlayerDied();
            Debug.Log($"SKYFALL_TEST bank: runGold={run} saveGold before={before} after={Save.gold} ended={game.ended}");
            Debug.Log($"SKYFALL_TEST save file: {System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, Arg("-skyfallSaveFile", "skyfall_save.json"))).Replace("\n", " ")}");
        }

        /// -skyfallShop 1: opens the shop on the title screen, queues purchases the way a click would, and logs the results.
        /// IMGUI is not in snapshots, so this mostly proves the draw code runs (no exceptions) and purchases save.
        static void ShopTest(Game game)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var hud = game.hud;
            var ht = typeof(Hud);
            if (frame == 40)
            {
                Save.gold = 1000;
                ht.GetMethod("ShowShop", flags).Invoke(hud, new object[] { System.Enum.ToObject(ht.GetNestedType("Menu", System.Reflection.BindingFlags.NonPublic), 1) });
                Debug.Log($"SKYFALL_TEST shop opened: gold={Save.gold} sword={Save.sword_level} bow={Save.bow_level} helm={Save.armor_helm}");
            }
            void Buy(string what) => ht.GetField("pendingBuy", flags).SetValue(hud, what);
            if (frame == 60) Buy("w:bow");
            if (frame == 70) ht.GetField("shopArmorTab", flags).SetValue(hud, true);
            if (frame == 80) Buy("a:helm");
            if (frame == 90) Buy("a:helm");
            if (frame == 100) Buy("a:boots");
            if (frame == 110) Buy("w:sword");   // not affordable any more or still ok: logged either way
            if (frame == 130)
            {
                Debug.Log($"SKYFALL_TEST shop after: gold={Save.gold} sword={Save.sword_level} bow={Save.bow_level} helm={Save.armor_helm} boots={Save.armor_boots} msg={ht.GetField("shopMsg", flags).GetValue(hud)}");
                Debug.Log("SKYFALL_TEST shop file: " + System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, Arg("-skyfallSaveFile", "skyfall_save.json"))).Replace("\n", " "));
                Finish(0);
            }
        }

        /// -skyfallBowView 1 (use with the pyromancer): close-ups of the bow in her hand and a burst of arrows in flight.
        static void BowView(Game game)
        {
            var pl = game.player;
            pl.hp = pl.maxHp = 99999f;
            if (frame == 1) game.timeElapsed = 110f;
            if (frame == 100)
            {
                var hand = pl.HandPosition;
                var rg = pl.GetComponentInChildren<CharacterRig>();
                if (rg) foreach (var t in rg.model.GetComponentsInChildren<Transform>(true)) Debug.Log($"SKYFALL_TEST rigpart {t.name} active={t.gameObject.activeSelf} renderer={t.GetComponent<Renderer>() != null} pos={t.position - pl.Pos}");
                Debug.Log($"SKYFALL_TEST bow: hand={hand} player={pl.Pos} foes={game.enemies.Count} coinsGold={game.runGold}");
                pl.camDistTarget = 2.6f; pl.camPitch = 0.05f; pl.camYaw = Mathf.PI * 0.85f;
            }
            if (frame == 130) Snap("bow_1_front");
            if (frame == 131) { pl.camYaw = Mathf.PI * 0.5f; }
            if (frame == 160) Snap("bow_2_side");
            if (frame == 161) { pl.camYaw = 0.5f; pl.camDistTarget = 2.8f; pl.camPitch = 0.1f; }
            if (frame == 190) Snap("bow_3_back");
            if (frame == 191) { pl.camYaw = Mathf.PI * 0.8f; pl.camDistTarget = 7f; pl.camPitch = 0.25f; }
            if (frame >= 220 && frame <= 232 && frame % 3 == 1) Snap($"bow_4_arrows_{frame}");
            if (frame == 236)
            {
                // A look at the gold: a mini-boss style pile and one coin of each size, 8 m ahead (outside the magnet radius).
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var ahead = pl.Pos + pl.CamFwd * 8f;
                typeof(Game).GetMethod("DropGoldPile", flags).Invoke(game, new object[] { ahead, 150, 10, 2.5f, false });
                var drop = typeof(Game).GetMethod("DropCoin", flags);
                int k = 0;
                foreach (int v in new[] { 1, 4, 8 })
                    drop.Invoke(game, new object[] { ahead + pl.CamRight * (3.5f + 1.2f * k++), v, false });
                pl.camDistTarget = 5.5f; pl.camPitch = 0.55f;
            }
            if (frame == 250) Snap("coins_pile");
            if (frame == 252)
            {
                int arrows = Object.FindObjectsByType<FireArrow>(FindObjectsInactive.Exclude).Length;
                Debug.Log($"SKYFALL_TEST bow: arrows in flight={arrows} kills={game.kills} runGold={game.runGold} " + $"dmgDealt={game.damageDealt:0}");
                LogStats(game, "end");
                Finish(0);
            }
        }

        /// -skyfallIslandView 1: photographs the floating islands from the ground and from the boss island's top.
        static void IslandView(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            pl.hp = pl.maxHp = 99999f;
            var bc = tr.BossIslandCenter;
            if (frame == 1)
            {
                Debug.Log($"SKYFALL_TEST islands={tr.islands.Count} boss center={bc} radius={tr.BossIslandRadius} ground={tr.Height(bc.x, bc.z):0.0}");
                foreach (var i in tr.islands)
                    Debug.Log($"SKYFALL_TEST island big={i.big} pos={i.center} r={i.radius:0.0} depth={i.depth:0.0} groundBelow={tr.Height(i.center.x, i.center.z):0.0}");
            }
            void LookFrom(Vector3 stand, Vector3 look, float dist, float pitch)
            {
                pl.Pos = stand;
                pl.velocity = Vector3.zero;
                var d = look - stand; d.y = 0;
                pl.camYaw = Mathf.Atan2(-d.x, -d.z);
                pl.camDistTarget = dist;
                pl.camPitch = pitch;
            }
            Vector3 toward = new Vector3(bc.x, 0, bc.z).normalized;
            if (frame < 60) LookFrom(new Vector3(0, tr.Height(0, 0), 0), bc, 30f, 0.1f);
            else if (frame < 100)
            {
                var s = bc - toward * 70f; s.y = tr.Height(s.x, s.z);
                LookFrom(s, bc, 20f, 0.05f);
                if (frame == 99) Snap("islands_1_far");
            }
            else if (frame < 140)
            {
                var s = bc - toward * 45f; s.y = tr.Height(s.x, s.z);
                LookFrom(s, bc, 12f, 0.5f);
                if (frame == 139) Snap("islands_2_chain");
            }
            else if (frame < 180)
            {
                var side = new Vector3(-toward.z, 0, toward.x);
                var s = bc + side * 62f; s.y = tr.Height(s.x, s.z);
                LookFrom(s, bc, 8f, 0.12f);
                if (frame == 179) Snap("islands_3_side");
            }
            else if (frame == 180)
            {
                pl.Pos = bc + new Vector3(0, 1f, 0);
                pl.velocity = Vector3.zero;
                pl.camDistTarget = 14f; pl.camPitch = 0.35f;
            }
            else if (frame == 300)
            {
                Snap("islands_4_top");
                Debug.Log($"SKYFALL_TEST on top: pos={pl.Pos} expectedTop={bc.y} dy={pl.Pos.y - bc.y:0.00}");
                pl.camYaw += 2.5f; pl.camDistTarget = 26f; pl.camPitch = 0.55f;
            }
            else if (frame == 330) { Snap("islands_5_top_wide"); Finish(0); }
        }

        /// -skyfallFinale 1 (use with -skyfallSaveFile test_save.json): mini boss at 4:00, the 0:00 clear-out, the island boss,
        /// the egg drop and pickup, and the victory. Never touches the real save when a separate save file is given.
        static void Finale(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            pl.hp = pl.maxHp = 99999f;
            var bc = tr.BossIslandCenter;
            void LookAt(Vector3 stand, Vector3 look, float dist, float pitch)
            {
                pl.Pos = stand;
                pl.velocity = Vector3.zero;
                var d = look - stand; d.y = 0;
                pl.camYaw = Mathf.Atan2(-d.x, -d.z);
                pl.camDistTarget = dist;
                pl.camPitch = pitch;
            }
            void Marker(string what)
            {
                bool ok = game.hud.IslandMarker(out var pos, out bool on, out float m, out float ang);
                Debug.Log($"SKYFALL_TEST marker {what}: visible={ok} pos={pos} onScreen={on} meters={m:0} angle={ang:0}");
            }
            if (frame == 1)
            {
                game.timeElapsed = 235f;
                Debug.Log($"SKYFALL_TEST save before: unlocked=[{string.Join(",", Save.unlocked_dragons)}] runLength={Data.RUN_LENGTH}");
            }
            if (frame < 400) pl.Pos = new Vector3(pl.Pos.x, tr.Height(pl.Pos.x, pl.Pos.z), pl.Pos.z);
            if (frame == 420)
            {
                Log("mini", game);
                foreach (var b in game.bosses) Debug.Log($"SKYFALL_TEST mini boss {b.displayName} hp={b.hp:0} pos={b.Pos} dist={Vector3.Distance(b.Pos, pl.Pos):0}");
                pl.camYaw += 0.5f; pl.camDistTarget = 16f;
                Snap("finale_1_mini");
                game.timeElapsed = 478.5f;
            }
            if (frame == 520)
            {
                Log("finale", game);
                Debug.Log($"SKYFALL_TEST finalStarted={game.finalStarted} foes={game.enemies.Count} kills={game.kills} bossName={(game.bosses.Count > 0 ? game.bosses[0].displayName : "none")}");
                if (game.bosses.Count > 0)
                {
                    var b = game.bosses[0];
                    Debug.Log($"SKYFALL_TEST final boss pos={b.Pos} islandCenter={bc} horiz={Vector2.Distance(new Vector2(b.Pos.x, b.Pos.z), new Vector2(bc.x, bc.z)):0.0} islandBound={b.islandBound}");
                }
                Marker("from spawn");
                var toward = new Vector3(bc.x, 0, bc.z).normalized;
                var s = bc - toward * 65f; s.y = tr.Height(s.x, s.z);
                LookAt(s, bc, 14f, 0.1f);
            }
            if (frame == 560) { Marker("ground facing island"); Snap("finale_2_beacon"); }
            if (frame == 570)
            {
                var toward = new Vector3(bc.x, 0, bc.z).normalized;
                var s = bc - toward * 65f; s.y = tr.Height(s.x, s.z);
                LookAt(s, bc - toward * 200f, 14f, 0.1f);   // turn away: the arrow should flip to the edge
            }
            if (frame == 590) { Marker("ground facing away"); }
            if (frame == 600)
            {
                // Stranded below the island: the boss must stay on it.
                var side = new Vector3(-bc.z, 0, bc.x).normalized;
                var s = bc + side * (tr.BossIslandRadius + 14f); s.y = tr.Height(s.x, s.z);
                LookAt(s, bc, 14f, 0.15f);
                if (game.bosses.Count > 0)
                {
                    // Edge test: put the boss near the rim facing the stranded player and shove him outward hard.
                    var b = game.bosses[0];
                    var outDir = new Vector3(s.x - bc.x, 0, s.z - bc.z).normalized;
                    b.Pos = bc + outDir * 15f;
                    game.DamageEnemy(b, 1f, outDir, 4000f);
                }
            }
            if ((frame == 610 || frame == 700) && game.bosses.Count > 0)
            {
                var b = game.bosses[0];
                Debug.Log($"SKYFALL_TEST stranded: boss horiz={Vector2.Distance(new Vector2(b.Pos.x, b.Pos.z), new Vector2(bc.x, bc.z)):0.0} limit~{tr.BossIslandRadius - 3f:0.0} y={b.Pos.y:0.0} top={bc.y:0.0}");
                Snap("finale_3_edge");
            }
            if (frame == 710)
            {
                var toward = new Vector3(bc.x, 0, bc.z).normalized;
                LookAt(bc + new Vector3(-toward.x, 0, -toward.z) * (tr.BossIslandRadius * 0.6f) + Vector3.up, bc, 22f, 0.3f);
            }
            if (frame > 710 && frame < 900 && game.bosses.Count > 0)
            {
                var b = game.bosses[0];
                if (frame % 30 == 0)
                    Debug.Log($"SKYFALL_TEST on island: boss horiz={Vector2.Distance(new Vector2(b.Pos.x, b.Pos.z), new Vector2(bc.x, bc.z)):0.0} onIsland={game.PlayerOnBossIsland()} foes={game.enemies.Count}");
                if (frame % 30 == 0) Marker("player on island");
                if (frame == 800) Snap("finale_4_fight");
                if (frame == 840)
                {
                    b.hp = 1f;
                    game.DamageEnemy(b, 5f);
                    Debug.Log("SKYFALL_TEST boss killed");
                    var e0 = Object.FindFirstObjectByType<DragonEgg>();
                    if (e0) e0.hold = true;
                }
            }
            if (frame >= 841)
            {
                var egg = Object.FindFirstObjectByType<DragonEgg>();
                if (egg != null && frame < 905 && frame % 10 == 2 && frame != 902)
                {
                    var rend = egg.GetComponentInChildren<MeshRenderer>();
                    var mc = rend.sharedMaterial.GetVector("_BaseColor");
                    var want = Data.DRAGONS[egg.dragonId].body;
                    Debug.Log($"SKYFALL_TEST egg for {egg.dragonId} at {egg.transform.position} shellColor(lin)={mc} wantLin={FX.Lin(want)}");
                }
                if (egg != null && frame == 902) egg.hold = false;
                if (frame == 900 && egg != null)
                {
                    var ct = pl.cam.transform;
                    ct.position = egg.transform.position + new Vector3(2.2f, 0.4f, 4.5f);
                    ct.LookAt(egg.transform.position);
                    Snap("finale_5_egg");
                }
                if (game.ended || frame > 1400)
                {
                    Debug.Log($"SKYFALL_TEST victory ended={game.ended} hatched={game.hatchedDragon} unlocked=[{string.Join(",", Save.unlocked_dragons)}] wins={Save.wins} bossesSlain={game.bossesSlain}");
                    Debug.Log($"SKYFALL_TEST save file: {System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, Arg("-skyfallSaveFile", "skyfall_save.json"))).Replace("\n", " ")}");
                    Finish(0);
                }
            }
        }

        static double perfMs, perfEnemies, perfLast;
        static int perfFrames;

        /// Averages the real time per frame between frames 300 and 1500 of the normal run (the field is full of ogres by then).
        static void TimeFrame(Game game)
        {
            double now = EditorApplication.timeSinceStartup;
            if (frame > 300 && frame <= 1500 && perfLast > 0)
            {
                perfMs += (now - perfLast) * 1000.0;
                perfEnemies += game.enemies.Count;
                perfFrames++;
            }
            perfLast = now;
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
                      $"t={game.timeElapsed:0} bosses={game.bosses.Count} spawned={game.bossesSpawned} dmg={game.damageDealt:0} runGold={game.runGold} saveGold={Save.gold}");

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
