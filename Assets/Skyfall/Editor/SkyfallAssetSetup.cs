using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// Prepares the imported Asset Store packs for the game: converts their built-in-pipeline materials to URP
    /// and fills Resources/Skyfall/SkinLibrary.asset with the models, effects, sounds and sky the game uses.
    /// Run from the menu (Skyfall > Set Up Asset Packs) or the command line (-executeMethod ...SkyfallAssetSetup.Run).
    /// Safe to run again after adding packs.
    public static class SkyfallAssetSetup
    {
        const string LibraryPath = "Assets/Skyfall/Resources/Skyfall/SkinLibrary.asset";
        const string Hovl = "Assets/Hovl Studio/Magic effects pack/Prefabs/";
        const string Guz = "Assets/Matthew Guz/Hits Effects FREE/Prefab/";
        const string Nature = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/";
        const string Dragons = "Assets/FourEvilDragonsPBR/Prefab/";

        static readonly string[] CharacterFolders =
        {
            "Assets/FourEvilDragonsPBR", "Assets/RPGHero", "Assets/WizardPolyArt", "Assets/Mini Legion Grunt PBR HP Polyart",
            "Assets/Mini Legion Rock Golem PBR HP Polyart", "Assets/Ogre",
        };

        static readonly string[] EffectFolders = { "Assets/Hovl Studio", "Assets/Matthew Guz" };

        const string PolytopeUrpPackage = "Assets/Polytope Studio/Lowpoly_Environments/URP/PT_Nature_Free_URP_17.unitypackage";
        const string PolytopeShader = "Assets/Polytope Studio/Lowpoly_Environments/Sources/Shaders/PT_Rock_Shader.shader";

        [MenuItem("Skyfall/Set Up Asset Packs")]
        public static void Run()
        {
            // Polytope ships its URP shaders as a package inside the pack; install it if the built-in ones are still there.
            if (File.Exists(PolytopeShader) && !File.ReadAllText(PolytopeShader).Contains("UniversalPipeline") && File.Exists(PolytopeUrpPackage))
            {
#pragma warning disable 618
                AssetDatabase.ImportPackage(PolytopeUrpPackage, false);
#pragma warning restore 618
                Debug.LogWarning("Skyfall: installing Polytope's URP shaders. When the import finishes, run Skyfall > Set Up Asset Packs once more.");
            }
            int converted = ConvertMaterials();
            int nature = PrepareNatureMaterials();
            converted += PrepareRuinsKit();
            BuildLibrary();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"SKYFALL_TEST asset setup done: {converted} materials converted to URP, {nature} nature materials prepared, library at {LibraryPath}");
        }

        const string KitFolder = "Assets/Ruins_creation_kit";
        const string KitModel = "Assets/Ruins_creation_kit/ruins_creation_kit.FBX";

        /// Ruins kit: converts its legacy materials to URP Lit (keeping texture + normal map) and maps the FBX's
        /// embedded materials onto them by name, since the model ships without linked materials.
        static int PrepareRuinsKit()
        {
            if (!AssetDatabase.IsValidFolder(KitFolder)) return 0;
            var upgraders = Upgraders();
            var known = new HashSet<string>(upgraders.Select(u => u.OldShaderPath));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var byName = new Dictionary<string, Material>();
            int n = 0;
            foreach (var mat in MaterialsIn(KitFolder + "/Materials"))
            {
                string sh = mat.shader ? mat.shader.name : "";
                if (known.Contains(sh)) { MaterialUpgrader.Upgrade(mat, upgraders, MaterialUpgrader.UpgradeFlags.None); n++; }
                else if (!sh.StartsWith("Universal Render Pipeline/") && lit != null)
                {
                    Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                    Color col = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                    mat.shader = lit;
                    mat.SetTexture("_BaseMap", main);
                    mat.SetColor("_BaseColor", col);
                    if (bump) { mat.SetTexture("_BumpMap", bump); mat.EnableKeyword("_NORMALMAP"); }
                    mat.SetFloat("_Smoothness", 0.12f);
                    mat.SetFloat("_Metallic", 0f);
                    n++;
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                byName[mat.name] = mat;
            }
            // FBX material name -> kit material.
            var map = new Dictionary<string, string>
            {
                ["walls"] = "wall_01", ["walls_break"] = "ruins_creation_kit-walls_break", ["01_ground"] = "ruins_creation_kit-01_ground",
                ["worked_Stone"] = "dif_smoothStone_01", ["worked_Stone_2"] = "dif_smoothStone_02", ["woodRoof"] = "dif_woodRoof_01",
                ["old_wood"] = "dif_oldWood_01", ["wood_planks"] = "dif_dirtyPlanks_02", ["messBrick"] = "dif_messBrick",
                ["metal"] = "dif_metal_01", ["iron"] = "dif_metal_01", ["pew_01"] = "dif_dirtyWood_01", ["tomb_01"] = "dif_smoothStone_01",
                ["domeOutside"] = "dif_coupole_01", ["domeInside"] = "dif_coupole_01",
                ["vitrail_01"] = "dif_vitrail_01", ["vitrail_02"] = "dif_vitrail_02", ["vitrail_03"] = "dif_vitrail_03", ["vitrail_04"] = "dif_vitrail_04",
            };
            if (AssetImporter.GetAtPath(KitModel) is ModelImporter imp)
            {
                bool changed = false;
                foreach (var emb in AssetDatabase.LoadAllAssetsAtPath(KitModel).OfType<Material>().ToList())
                {
                    if (!map.TryGetValue(emb.name, out var target) || !byName.TryGetValue(target, out var kitMat)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), emb.name);
                    if (imp.GetExternalObjectMap().TryGetValue(id, out var cur) && cur == kitMat) continue;
                    imp.AddRemap(id, kitMat);
                    changed = true;
                }
                if (changed) imp.SaveAndReimport();
            }
            return n;
        }

        // ---------------------------------------------------------------- materials

        static List<MaterialUpgrader> Upgraders()
        {
            var list = new List<MaterialUpgrader>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<IMaterialUpgradersProvider>())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                // Skip URP's 2D converters: they would also claim "Standard" and URP Lit materials and turn them
                // into 2D sprite-mesh materials, which is wrong for this 3D game.
                if (t.Name.Contains("2D")) continue;
                try
                {
                    var provider = (IMaterialUpgradersProvider)Activator.CreateInstance(t, true);
                    list.AddRange(provider.GetUpgraders());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Skyfall: skipped upgrader provider {t.Name}: {e.Message}");
                }
            }
            return list;
        }

        static IEnumerable<Material> MaterialsIn(params string[] folders) =>
            AssetDatabase.FindAssets("t:Material", folders.Where(AssetDatabase.IsValidFolder).ToArray())
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null);

        static int ConvertMaterials()
        {
            var upgraders = Upgraders();
            var known = new HashSet<string>(upgraders.Select(u => u.OldShaderPath));
            int count = 0;
            // Repair: materials an earlier setup run sent to the 2D mesh shader still hold their original Standard
            // properties, so put them back on Standard and convert them properly below.
            var standard = Shader.Find("Standard");
            var standardSpec = Shader.Find("Standard (Specular setup)");
            foreach (var mat in MaterialsIn(CharacterFolders.Concat(EffectFolders).ToArray()))
            {
                if (mat.shader == null || !mat.shader.name.StartsWith("Universal Render Pipeline/2D/")) continue;
                bool spec = (mat.HasProperty("_SpecGlossMap") && mat.GetTexture("_SpecGlossMap") != null) ||
                            (mat.HasProperty("_WorkflowMode") && mat.GetFloat("_WorkflowMode") < 0.5f);
                mat.shader = spec ? standardSpec : standard;
                EditorUtility.SetDirty(mat);
                Debug.Log("Skyfall: repaired 2D material " + AssetDatabase.GetAssetPath(mat));
            }
            foreach (var mat in MaterialsIn(CharacterFolders.Concat(EffectFolders).ToArray()))
            {
                string shader = mat.shader ? mat.shader.name : "";
                if (known.Contains(shader))
                {
                    MaterialUpgrader.Upgrade(mat, upgraders, MaterialUpgrader.UpgradeFlags.None);
                    count++;
                }
                EditorUtility.SetDirty(mat);
            }
            // Character materials: keep the emission variant so hit flashes and status glows can tint them.
            foreach (var mat in MaterialsIn(CharacterFolders))
            {
                if (mat.shader == null || !mat.shader.name.StartsWith("Universal Render Pipeline/")) continue;
                RelinkMissingAlbedo(mat);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    if (!mat.HasProperty("_EmissionMap") || mat.GetTexture("_EmissionMap") == null)
                        mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }
            return count;
        }

        /// Some pack materials (the Usurper dragons) ship without their color texture linked even though it sits next
        /// to their other maps. Link the "Albedo" texture from the same folder as the material's normal/other maps.
        static void RelinkMissingAlbedo(Material mat)
        {
            if (!mat.HasProperty("_BaseMap") || mat.GetTexture("_BaseMap") != null) return;
            string dir = null;
            foreach (var prop in new[] { "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_EmissionMap" })
            {
                if (!mat.HasProperty(prop)) continue;
                var t = mat.GetTexture(prop);
                if (t == null) continue;
                dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(t))?.Replace('\\', '/');
                if (dir != null) break;
            }
            if (dir == null) return;
            var albedo = AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p).IndexOf("Albedo", StringComparison.OrdinalIgnoreCase) >= 0);
            if (albedo == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
            mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            Debug.Log($"Skyfall: linked missing albedo {albedo} to {AssetDatabase.GetAssetPath(mat)}");
        }

        static int PrepareNatureMaterials()
        {
            int n = 0;
            foreach (var mat in MaterialsIn("Assets/Polytope Studio"))
            {
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- library

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning("Skyfall: missing asset " + path);
            return a;
        }

        static GameObject[] Prefabs(params string[] paths) => paths.Select(Load<GameObject>).Where(g => g != null).ToArray();

        static AudioClip[] Clips(params string[] paths) => paths.Select(Load<AudioClip>).Where(c => c != null).ToArray();

        static void BuildLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SkinLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<SkinLibrary>();
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            // Heroes (the Blood Huntress has no model in these packs and keeps her procedural look).
            lib.knight = Load<GameObject>("Assets/RPGHero/Prefabs/RPGHeroPBR.prefab");
            lib.pyromancer = Load<GameObject>("Assets/WizardPolyArt/Prefabs/PolyArtWizardStandardMat.prefab");
            lib.huntress = null;

            // Dragons: Azure Wyrm = Usurper (blue), Crimson Drake = Terror Bringer (red), Night Wyrm = Usurper (black).
            lib.dragonBlue = Load<GameObject>(Dragons + "DragonUsurper/Blue.prefab");
            lib.dragonRed = Load<GameObject>(Dragons + "DragonTerrorBringer/Red.prefab");
            lib.dragonBlack = Load<GameObject>(Dragons + "DragonUsurper/Black.prefab");

            // Ogres borrow the Grunt's humanoid animations (the ogre pack has a rig but no animations).
            lib.ogres = Prefabs("Assets/Ogre/Prefabs/PBR/Ogre Generic.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Green.prefab",
                "Assets/Ogre/Prefabs/PBR/Ogre Blue.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Generic Warpainted.prefab");
            lib.bossOgres = Prefabs("Assets/Ogre/Prefabs/PBR/Ogre Green Warpainted.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Blue Warpainted.prefab",
                "Assets/Ogre/Prefabs/PBR/Ogre Generic Warpainted.prefab");
            lib.ogreWeapon = Load<GameObject>("Assets/Ogre/Prefabs/PBR/Weapon.prefab");
            lib.ogreController = Load<RuntimeAnimatorController>("Assets/Mini Legion Grunt PBR HP Polyart/Animations/Grunt.controller");
            lib.archer = Load<GameObject>("Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPBR.prefab");
            lib.shaman = Load<GameObject>("Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPolyart.prefab");
            lib.golem = Load<GameObject>("Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/PBR_Golem.prefab");
            lib.golemKing = Load<GameObject>("Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/Polyart_Golem.prefab");

            // Scenery.
            lib.deadTrees = Prefabs(Nature + "Trees/PT_Pine_Tree_03_dead.prefab", Nature + "Trees/PT_Pine_Tree_03_dead_cut.prefab",
                Nature + "Trees/PT_Fruit_Tree_01_dead.prefab", Nature + "Trees/PT_Fruit_Tree_01_dead_cut.prefab");
            lib.stumps = Prefabs(Nature + "Trees/PT_Fruit_Tree_01_stump.prefab", Nature + "Trees/PT_Pine_Tree_03_stump.prefab",
                Nature + "Trees/PT_Fruit_Tree_01_logs.prefab", Nature + "Trees/PT_Pine_Tree_03_logs.prefab");
            lib.rocks = Prefabs(Nature + "Rocks/PT_Generic_Rock_01.prefab", Nature + "Rocks/PT_Ore_Rock_01.prefab",
                Nature + "Rocks/PT_River_Rock_Pile_02.prefab", Nature + "Rocks/PT_Ore_Rock_01_split.prefab");
            lib.standingStones = Prefabs(Nature + "Rocks/PT_Menhir_Rock_02.prefab");
            lib.shrubs = Prefabs(Nature + "Shrubs/PT_Generic_Shrub_01_dead.prefab", Nature + "Shrubs/PT_Generic_Shrub_01_dead.prefab",
                Nature + "Shrubs/PT_Generic_Shrub_01_green.prefab");
            lib.grass = Prefabs(Nature + "Plants/PT_Grass_02.prefab", Nature + "Plants/PT_Grass_02.prefab", Nature + "Flowers/PT_Poppy_02.prefab");

            lib.ruinsKit = Load<GameObject>(KitModel);

            // Sky.
            lib.skybox = Load<Material>(SkyPath());

            // Effects.
            lib.vfx = new List<SkinLibrary.Named>();
            void V(string name, string path)
            {
                var p = Load<GameObject>(path);
                if (p) lib.vfx.Add(new SkinLibrary.Named { name = name, prefab = p });
            }
            V("hit", Guz + "Basic Hit .prefab");
            V("hit_fire", Guz + "Fire Hit .prefab");
            V("hit_ice", Guz + "Ice Hit .prefab");
            V("hit_lightning", Guz + "Lightning Hit Blue.prefab");
            V("hit_magic", Guz + "Magic Hit 2.prefab");
            V("explosion", Hovl + "Hits and explosions/Explosion.prefab");
            V("snow_hit", Hovl + "Hits and explosions/Snow hit.prefab");
            V("electro_hit", Hovl + "Hits and explosions/Electro hit.prefab");
            V("stones_hit", Hovl + "Hits and explosions/Stones hit.prefab");
            V("ground_explosion", Hovl + "AoE effects/Ground AOE explosion.prefab");
            V("red_explosion", Hovl + "AoE effects/Red energy explosion.prefab");
            V("dark_explosion", Hovl + "AoE effects/Smoke AOE explosion.prefab");
            V("magic_circle", Hovl + "Magic circles/Magic circle.prefab");
            V("healing", Hovl + "Character auras/Healing.prefab");
            V("buff", Hovl + "Character auras/Buff.prefab");
            V("portal_red", Hovl + "Portals/Portal red.prefab");
            V("dust_puff", Hovl + "Smoke effects/Dust puff.prefab");

            // Sounds (pickup and click keep their crisp synthesized blips).
            lib.sounds = new List<SkinLibrary.SoundSet>();
            void S(string name, params string[] paths)
            {
                var c = Clips(paths);
                if (c.Length > 0) lib.sounds.Add(new SkinLibrary.SoundSet { name = name, clips = c });
            }
            const string Leo = "Assets/Leohpaz/RPG_Essentials_Free/";
            const string Med = "Assets/Medieval Action - FX Pack 2.0/";
            const string Amp = "Assets/A.M.P. DEMO/";
            const string Frost = "Assets/Free Frost Mage - SFX/";
            const string Roar = "Assets/Monster Roars/";
            const string Free = "Assets/Free Pack/";
            S("hit", Leo + "10_Battle_SFX/15_Impact_flesh_02.wav", Leo + "10_Battle_SFX/77_flesh_02.wav", Free + "Bloody punch.wav");
            S("clang", Med + "Sword 3.wav", Med + "Sword 5.wav", Free + "Metal impact 5.wav");
            S("block", Leo + "10_Battle_SFX/39_Block_03.wav", Med + "Sword 1.wav");
            S("parry", Med + "Sword 2.wav", Med + "Sword 4.wav");
            S("heal", Leo + "8_Buffs_Heals_SFX/02_Heal_02.wav");
            S("slash", Med + "Sword Swish 1.wav", Med + "Sword Swish 2.wav", Leo + "10_Battle_SFX/22_Slash_04.wav");
            S("bow", Med + "Bow.wav");
            S("arrow_hit", Med + "Arrow 1.wav", Med + "Arrow 2.wav", Med + "Arrow 3.wav");
            S("boom", Amp + "AMP_Fire_Impact_01.wav", Amp + "AMP_Earth_Impact_01.wav", Leo + "8_Atk_Magic_SFX/04_Fire_explosion_04_medium.wav");
            S("whoosh", Amp + "AMP_Wind_Cast_02.wav", Free + "Magic Spell_Simple Swoosh_6.wav", Leo + "8_Atk_Magic_SFX/25_Wind_01.wav");
            S("frost", Frost + "ice_bolt_07.wav", Frost + "ice_bolt_21.wav", Frost + "forst_nova_short_05.wav", Frost + "ice_blast_04.wav");
            S("zap", Amp + "AMP_Lightning_Impact_06.wav", Leo + "8_Atk_Magic_SFX/18_Thunder_02.wav");
            S("hurt", Leo + "12_Player_Movement_SFX/61_Hit_03.wav", Leo + "10_Battle_SFX/03_Claw_03.wav");
            S("death", Leo + "10_Battle_SFX/69_Enemy_death_01.wav");
            S("levelup", Amp + "AMP_Buff_01.wav", Amp + "AMP_Buff_08.wav");
            S("roar", Roar + "01. Primal Roar.wav", Roar + "02. Ferocious Roar.wav", Roar + "06. Battle Roar.wav", Roar + "08. Titan Roar.wav", Roar + "10. Attack Roar.wav");
            S("flame", Amp + "AMP_Fire_Cast_04.wav");

            EditorUtility.SetDirty(lib);
        }

        /// The skybox used by the game. Override with -skyfallSky "Assets/.../Some.mat".
        static string SkyPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-skyfallSky") return args[i + 1];
            return "Assets/Fantasy Skybox FREE/Panoramics/FS013/FS013_Sunset.mat";
        }
    }
}
