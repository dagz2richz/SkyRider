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
        /// The dedicated wyvern model (generated separately); the setup falls back to an evil dragon until it exists.
        const string WyvernPath = "Assets/Skyfall/Art/Wyvern/Wyvern.prefab";
        const string WyvernHighPolyPath = "Assets/Skyfall/Art/Wyvern/Wyvern_HighPoly.prefab";
        /// The game's own rigged hero models (humanoid FBX exported from ArtSource/Heroes) and the blue dragon
        /// (generic rig with its own clips, exported from ArtSource/Dragon). Each gets a prefab next to its FBX.
        const string HeroArt = "Assets/Skyfall/Art/Heroes/";
        const string BlueDragonFbx = "Assets/Skyfall/Art/BlueDragon/BlueDragon.fbx";
        const string MageControllerPath = "Assets/Skyfall/Resources/Skyfall/EmberMage.controller";

        static readonly string[] CharacterFolders =
        {
            "Assets/FourEvilDragonsPBR", "Assets/RPGHero", "Assets/WizardPolyArt", "Assets/Mini Legion Grunt PBR HP Polyart",
            "Assets/Mini Legion Rock Golem PBR HP Polyart", "Assets/Ogre", "Assets/Toon_RTS", "Assets/Toon_RTS_demo",
        };

        static readonly string[] EffectFolders = { "Assets/Hovl Studio", "Assets/Matthew Guz", "Assets/Polygonal Particles" };

        /// Scenery packs added for stages 2 and 3 (built-in materials converted to URP Lit).
        static readonly string[] SceneryFolders = { "Assets/BrokenVector", "Assets/Gridness Studios" };

        const string KI = "Assets/Kevin Iglesias/Human Animations/";
        const string HuntressControllerPath = "Assets/Skyfall/Resources/Skyfall/Huntress.controller";
        const string SkeletonControllerPath = "Assets/Skyfall/Resources/Skyfall/Skeleton.controller";
        const string HumanControllerPath = "Assets/Skyfall/Resources/Skyfall/RenegadeKnight.controller";
        const string KnightControllerPath = "Assets/Skyfall/Resources/Skyfall/Dragonguard.controller";
        const string Icons = "Assets/Basic_RPG_Icons/";
        const string AiIcons = "Assets/Skyfall/Art/Icons/";
        const string Quick = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";
        const string Slavic = "Assets/EmaceArt/Slavic World Free/Prefabs/";
        const string Trees = "Assets/BrokenVector/LowPolyTreePack/Prefabs/";
        const string Dungeon = "Assets/Gridness Studios/Elementary Dungeon Pack Lite/Prefabs/";

        const string PolytopeUrpPackage = "Assets/Polytope Studio/Lowpoly_Environments/URP/PT_Nature_Free_URP_17.unitypackage";
        const string PolytopeShader = "Assets/Polytope Studio/Lowpoly_Environments/Sources/Shaders/PT_Rock_Shader.shader";

        /// Path of the prefab with exactly this file name anywhere under folder (pack subfolders vary), or "" when missing.
        static string FindPrefab(string folder, string name)
        {
            if (!AssetDatabase.IsValidFolder(folder.TrimEnd('/'))) return "";
            foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { folder.TrimEnd('/') }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return "";
        }

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
            foreach (var mat in MaterialsIn(CharacterFolders.Concat(EffectFolders).Concat(SceneryFolders).ToArray()))
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

        /// The single remade model at path when it exists, else the pack's set.
        static GameObject[] AiOr(string path, GameObject[] fallback)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return p != null ? new[] { p } : fallback;
        }

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

            // Heroes. The Blood Huntress is the Kevin Iglesias female warrior with a polearm (recolored crimson in game),
            // driven by a controller built from that pack's female clips.
            lib.knight = Load<GameObject>("Assets/RPGHero/Prefabs/RPGHeroPBR.prefab");
            lib.pyromancer = Load<GameObject>("Assets/WizardPolyArt/Prefabs/PolyArtWizardStandardMat.prefab");
            lib.huntress = Load<GameObject>(KI + "Unity Demo Scenes/Human Melee Animations/Prefabs/Characters/HumanF_Dummy_Red - Polearm.prefab");
            lib.huntressController = BuildHuntressController();
            lib.knightController = BuildKnightController(lib.knight);
            lib.mageController = null;
            lib.artHeroes = false;
            // The game's own rigged heroes replace the pack models when all three have landed. The pack clips are
            // humanoid, so the same controllers drive them; the Ember Mage gets male clips with a left-hand cast.
            var artKnight = HeroArtPrefab("DragonguardKnight");
            var artHuntress = HeroArtPrefab("BloodHuntress");
            var artMage = HeroArtPrefab("EmberMage");
            if (artKnight && artHuntress && artMage)
            {
                lib.knight = artKnight;
                lib.huntress = artHuntress;
                lib.pyromancer = artMage;
                lib.mageController = BuildMageController();
                lib.artHeroes = true;
            }

            // Dragons: every dragon is the Usurper; the game tints the variants per dragon (albino is the base for the new ones).
            lib.dragonBlue = Load<GameObject>(Dragons + "DragonUsurper/Blue.prefab");
            lib.dragonRed = Load<GameObject>(Dragons + "DragonUsurper/Red.prefab");
            lib.dragonBlack = Load<GameObject>(Dragons + "DragonUsurper/Black.prefab");
            lib.dragonAlbino = Load<GameObject>(Dragons + "DragonUsurper/Albino.prefab");
            // The Azure Wyrm is the game's own blue dragon when it has been exported (its clips use the state names
            // DragonPet asks for, so it gets a controller of its own). The other dragons keep the Usurper.
            var blue = DragonArtPrefab(BlueDragonFbx);
            if (blue) lib.dragonBlue = blue;

            // The wyvern enemy: the dedicated model when it has landed, otherwise a stand-in evil dragon (the Terror Bringer,
            // NOT the Usurper that is the player's dragon) that the game scales down and tints.
            lib.wyvern = AssetDatabase.LoadAssetAtPath<GameObject>(WyvernPath);
            if (lib.wyvern == null) lib.wyvern = AssetDatabase.LoadAssetAtPath<GameObject>(WyvernHighPolyPath);   // until the rigged one lands
            lib.wyvernFallback = lib.wyvern == null;
            if (lib.wyvern == null) lib.wyvern = Load<GameObject>(Dragons + "DragonTerrorBringer/Grey.prefab");

            // Stage 2: Slavic villages among low-poly trees.
            lib.forestTrees = Prefabs(Trees + "Tree Type0 01.prefab", Trees + "Tree Type0 03.prefab", Trees + "Tree Type1 02.prefab", Trees + "Tree Type2 01.prefab",
                Trees + "Tree Type3 02.prefab", Trees + "Tree Type4 01.prefab", Trees + "Tree Type5 03.prefab", Trees + "Tree Type6 02.prefab", Trees + "Tree Type7 01.prefab",
                Slavic + "Nature/Tree/EA03_Nature_Tree_01b_PRE.prefab", Slavic + "Nature/Tree/EA03_Nature_Tree_02b_PRE.prefab", Slavic + "Nature/Tree/EA03_Nature_Tree_05a_PRE.prefab");
            lib.forestBushes = Prefabs(Slavic + "Nature/Bushes/EA03_Nature_Bush_01a_PRE.prefab", Slavic + "Nature/Bushes/EA03_Nature_Bush_02a_PRE.prefab",
                Slavic + "Nature/Bushes/EA03_Nature_Bush_03a_PRE.prefab", Slavic + "Nature/Bushes/EA03_Nature_Bush_05a_PRE.prefab", Slavic + "Nature/Plant/EA03_Nature_SweetFlag_01a_PRE.prefab");
            lib.forestRocks = Prefabs(Slavic + "Environment/Rock/EA03_Environment_Rock_Big_Head_01a_PRE.prefab", Slavic + "Environment/Rock/EA03_Environment_Rock_CatHead_01b_PRE.prefab",
                Slavic + "Environment/Rock/EA03_EnvRock_01a_PRE.prefab", Slavic + "Environment/Rock/EA03_Environment_Rock_Mini_Head_01a_PRE.prefab");
            lib.villageHouses = Prefabs(Slavic + "Town/Building/EA03_Town_House_Comp_01a_PRE.prefab", Slavic + "Town/Building/EA03_Town_House_Comp_02a_PRE.prefab",
                Slavic + "Town/Building/EA03_Town_House_Comp_03a_PRE.prefab", Slavic + "Town/Building/EA03_Town_House_Comp_03b_PRE.prefab",
                Slavic + "Village/OutBuilding/EA03_Village_OutBuilding_Shed_01a_PRE.prefab", Slavic + "Village/Village/AnimalBreeding/EA03_Village_AnimalBreeding_Hovel_01a_PRE.prefab");
            lib.villageProps = Prefabs(Slavic + "Fence/Plank2/EA03_Fence_Plank_02a_PRE.prefab", Slavic + "Fence/Plank2/EA03_Village_Fence_01a_PRE.prefab",
                Slavic + "Prop/Container/EA03_Prop_Container_Barrel_01d_PRE.prefab", Slavic + "Prop/Container/EA03_Prop_Container_Bale_01a_PRE.prefab",
                Slavic + "Prop/Container/EA03_Prop_Container_Basket_01a_PRE.prefab", Slavic + "Prop/Container/EA03_Prop_Container_Bag_02a_PRE.prefab");

            // Stage 3: crypt ruins from the dungeon pack.
            lib.cryptPieces = Prefabs(Dungeon + "Wall_Chiseled.prefab", Dungeon + "Wall_DoorStand.prefab", Dungeon + "Column_Half.prefab", Dungeon + "Wall_Chiseled.prefab");
            lib.cryptProps = Prefabs(Dungeon + "Barrel_C.prefab", Dungeon + "Cauldron.prefab", Dungeon + "Flag.004.prefab", Dungeon + "SquareChest_Bottom.prefab",
                Dungeon + "Pot.prefab", Dungeon + "Table.003.prefab", Dungeon + "Shelf_Framed.prefab", Dungeon + "Book_Holy_C.prefab");
            lib.cryptTorch = Load<GameObject>(Dungeon + "Torch.001.prefab");

            BuildIcons(lib);

            // Ogres borrow the Grunt's humanoid animations (the ogre pack has a rig but no animations).
            // The Unity AI remakes (Art/Enemies: the rigged, reduced models wrapped at the pack models' size) replace the
            // regular ogres, the archer, the golem mini boss, the skeletons and the knights; each falls back to its pack model.
            const string AiFoes = "Assets/Skyfall/Art/Enemies/";
            lib.ogres = AiOr(AiFoes + "Ogre.prefab", Prefabs("Assets/Ogre/Prefabs/PBR/Ogre Generic.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Green.prefab",
                "Assets/Ogre/Prefabs/PBR/Ogre Blue.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Generic Warpainted.prefab"));
            lib.bossOgres = AiOr(AiFoes + "Ogre.prefab", Prefabs("Assets/Ogre/Prefabs/PBR/Ogre Green Warpainted.prefab", "Assets/Ogre/Prefabs/PBR/Ogre Blue Warpainted.prefab",
                "Assets/Ogre/Prefabs/PBR/Ogre Generic Warpainted.prefab"));
            lib.ogreWeapon = Load<GameObject>("Assets/Ogre/Prefabs/PBR/Weapon.prefab");
            lib.ogreController = Load<RuntimeAnimatorController>("Assets/Mini Legion Grunt PBR HP Polyart/Animations/Grunt.controller");
            lib.archer = Load<GameObject>(AiFoes + "Grunt.prefab") ?? Load<GameObject>("Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPBR.prefab");
            lib.shaman = Load<GameObject>("Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPolyart.prefab");
            lib.golem = Load<GameObject>(AiFoes + "RockGolem.prefab") ?? Load<GameObject>("Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/PBR_Golem.prefab");
            lib.golemKing = Load<GameObject>("Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/Polyart_Golem.prefab");
            // Stage 2 and 3 final bosses: Ulgrim is a warpainted ogre, Skarnath a Soul Eater dragon (not the Usurper the player's dragons use).
            lib.forestBoss = Load<GameObject>("Assets/Ogre/Prefabs/PBR/Ogre Green Warpainted.prefab");
            lib.cryptBoss = Load<GameObject>(Dragons + "DragonSoulEaterPBR/DragonSoulEaterPurplePBR.prefab");

            // The swarm: Toon RTS skeletons (humanoid rig, so they share the Grunt's clips and their own) and Toon RTS
            // knights (generic rig, driven by the knight's own clips).
            // Both FBX models come with an untextured embedded material; use the textured one shipped beside them.
            RemapMaterials("Assets/Toon_RTS/unded_Demo/model/UD_demo_character.FBX", "Assets/Toon_RTS/unded_Demo/model/Materials/Undead_RTS_units_demo.mat");
            RemapMaterials("Assets/Toon_RTS_demo/models/ToonRTS_demo_Knight.FBX", "Assets/Toon_RTS_demo/models/Materials/DemoMat.mat");
            // The Toon RTS packs only ship idle, walk and one or two attacks, so both swarm foes borrow the Kevin
            // Iglesias male clips for running, a second attack, flinching and dying. The knight's rig is imported as
            // humanoid (its own clips copy its avatar) so those clips retarget onto it.
            const string UD = "Assets/Toon_RTS/unded_Demo/animation/", WK = "Assets/Toon_RTS_demo/animations/", KnightFbx = "Assets/Toon_RTS_demo/models/ToonRTS_demo_Knight.FBX";
            string m = KI + "Animations/Male/";
            MakeHumanoid(KnightFbx, WK + "WK_heavy_infantry_04_charge.FBX", WK + "WK_heavy_infantry_05_combat_idle.FBX",
                WK + "WK_heavy_infantry_06_combat_walk.FBX", WK + "WK_heavy_infantry_08_attack_B.FBX");
            lib.skeleton = Load<GameObject>(AiFoes + "Skeleton.prefab") ?? Load<GameObject>("Assets/Toon_RTS/unded_Demo/prefab/UD_demo_character.prefab");
            lib.skeletonController = BuildController(SkeletonControllerPath, new (string, string)[]
            {
                ("Idle", UD + "UD_infantry_05_combat_idle.FBX"),
                ("Walk", UD + "UD_infantry_06_combat_walk.FBX"),
                ("Run", m + "Movement/Run/HumanM@Run01_Forward.fbx"),
                ("Attack01", UD + "UD_infantry_07_attack_A.FBX"),
                ("Attack02", m + "Combat/1H/HumanM@Attack1H01_R.fbx"),
                ("GetHit", m + "Combat/HumanM@CombatDamage01.fbx"),
                ("Die", m + "Combat/HumanM@Death01.fbx"),
            });
            lib.humanSoldier = Load<GameObject>(AiFoes + "Knight.prefab") ?? Load<GameObject>(KnightFbx);
            lib.humanController = BuildController(HumanControllerPath, new (string, string)[]
            {
                ("Idle", WK + "WK_heavy_infantry_05_combat_idle.FBX"),
                ("Walk", WK + "WK_heavy_infantry_06_combat_walk.FBX"),
                ("Run", WK + "WK_heavy_infantry_04_charge.FBX"),
                ("Attack01", WK + "WK_heavy_infantry_08_attack_B.FBX"),
                ("Attack02", m + "Combat/Shield/HumanM@AttackShield01.fbx"),
                ("GetHit", m + "Combat/HumanM@CombatDamage01.fbx"),
                ("Die", m + "Combat/HumanM@Death01.fbx"),
            });

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

            lib.grassMaterial = Load<Material>("Assets/Packages/Point-Grass-Renderer/Runtime/Demo/Materials/PGDemo_Blade.mat");
            lib.ruinsKit = Load<GameObject>(KitModel);

            // Sky.
            lib.skybox = Load<Material>(SkyPath());
            lib.skyStage2 = Load<Material>("Assets/Fantasy Skybox FREE/Panoramics/FS002/FS002_Day.mat");
            lib.skyStage3 = Load<Material>("Assets/Fantasy Skybox FREE/Panoramics/FS002/FS002_Night.mat");

            // Effects.
            lib.vfx = new List<SkinLibrary.Named>();
            void V(string name, string path)
            {
                var p = Load<GameObject>(path);
                if (p) lib.vfx.Add(new SkinLibrary.Named { name = name, prefab = p });
            }
            // Gabriel Aguiar's Free Quick Effects (URP): the reworked talents, spell and passive uniques.
            V("gq_shield", Quick + "vfx_Shield_01.prefab");
            V("gq_implosion", Quick + "vfx_Implosion_01.prefab");
            V("gq_lightning", Quick + "vfx_Lightning_01.prefab");
            V("gq_heal", Quick + "vfx_Heal_01.prefab");
            V("gq_shockwave", Quick + "vfx_Shockwave_01.prefab");
            V("gq_flames", Quick + "vfx_Flames_01.prefab");
            V("gq_sparks", Quick + "vfx_Sparks_01.prefab");
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
            V("portal_blue", Hovl + "Portals/Portal blue.prefab");
            V("dust_puff", Hovl + "Smoke effects/Dust puff.prefab");
            V("dust_ground", Hovl + "Smoke effects/Dust ground.prefab");
            // Stage ambience (Polygonal Particles; FX swaps its built-in particle shaders for URP ones at spawn).
            const string Poly = "Assets/Polygonal Particles/Assets/Prefabs/";
            V("amb_fireflies", FindPrefab(Poly, "Fireflies"));
            V("amb_leaves", FindPrefab(Poly, "Leaves"));

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

        // ---------------------------------------------------------------- Blood Huntress animator

        static AnimationClip Clip(string fbx)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) Debug.LogWarning("Skyfall: missing animation " + fbx);
            return clip;
        }

        /// A controller whose state names match what the game asks for (Idle, Walk, Run, JumpAir, Attack01, GetHit),
        /// built from the Kevin Iglesias female clips. Her attack is an overhand right-hand throw (the javelin).
        static RuntimeAnimatorController BuildHuntressController()
        {
            string f = KI + "Animations/Female/";
            return BuildController(HuntressControllerPath, new (string, string)[]
            {
                ("Idle", f + "Combat/Polearm/HumanF@CombatIdlePolearm01.fbx"),
                ("Walk", f + "Movement/Walk/HumanF@Walk01_Forward.fbx"),
                ("Run", f + "Movement/Run/HumanF@Run01_Forward.fbx"),
                ("JumpAir", f + "Movement/Jump/HumanF@Fall01.fbx"),
                ("Attack01", f + "Combat/Spellcasting/MagicAttacks/Directional/HumanF@MagicAttackDirect1H01_R - Cast.fbx"),
                ("Attack02", f + "Combat/Polearm/HumanF@AttackPolearm01.fbx"),
                ("JumpUpAttack", f + "Combat/Polearm/HumanF@AttackPolearm01.fbx"),
                ("GetHit", f + "Combat/HumanF@CombatDamage01.fbx"),
                ("Die", f + "Combat/HumanF@Death01.fbx"),
            });
        }

        /// The Dragonguard: the RPG Hero pack's own sword-and-shield states, plus what that pack lacks (a fall for
        /// jumps, and an overhead two-handed strike for the leap slam), from the Kevin Iglesias male clips.
        static RuntimeAnimatorController BuildKnightController(GameObject hero)
        {
            var an = hero ? hero.GetComponentInChildren<Animator>() : null;
            var src = an ? an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
            if (src == null) return null;
            string m = KI + "Animations/Male/";
            SetLoop(m + "Movement/Jump/HumanM@Fall01.fbx");
            AssetDatabase.DeleteAsset(KnightControllerPath);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(KnightControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var cs in src.layers[0].stateMachine.states)
            {
                if (cs.state.motion == null) continue;
                var st = sm.AddState(cs.state.name);
                st.motion = cs.state.motion;
                st.speed = cs.state.speed;
                if (cs.state == src.layers[0].stateMachine.defaultState) sm.defaultState = st;
            }
            foreach (var (name, fbx) in new[] { ("JumpAir", m + "Movement/Jump/HumanM@Fall01.fbx"), ("JumpUpAttack", m + "Combat/2H/HumanM@Attack2H01.fbx") })
            {
                var clip = Clip(fbx);
                if (clip == null) continue;
                sm.AddState(name).motion = clip;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// The Ember Mage: Kevin Iglesias male idle and locomotion, a left-hand directional cast (his wand hand), a hit
        /// and a death, with the state names the game asks for.
        static RuntimeAnimatorController BuildMageController()
        {
            string m = KI + "Animations/Male/";
            return BuildController(MageControllerPath, new (string, string)[]
            {
                ("Idle", m + "Idles/HumanM@Idle01.fbx"),
                ("Walk", m + "Movement/Walk/HumanM@Walk01_Forward.fbx"),
                ("Run", m + "Movement/Run/HumanM@Run01_Forward.fbx"),
                ("JumpAir", m + "Movement/Jump/HumanM@Fall01.fbx"),
                ("Attack01", m + "Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect1H01_L - Cast.fbx"),
                ("JumpUpAttack", m + "Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect1H01_L - Cast.fbx"),
                ("GetHit", m + "Combat/HumanM@CombatDamage01.fbx"),
                ("Die", m + "Combat/HumanM@Death01.fbx"),
            });
        }

        /// A prefab for one of the game's rigged heroes (Assets/Skyfall/Art/Heroes/&lt;name&gt;/&lt;name&gt;_Rigged.fbx), imported
        /// as humanoid with its texture extracted. The game sets the controller at spawn. Null when the FBX is missing.
        static GameObject HeroArtPrefab(string name)
        {
            string dir = HeroArt + name + "/", fbx = dir + name + "_Rigged.fbx", prefab = dir + name + ".prefab";
            if (!File.Exists(fbx) || !(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) return null;
            if (imp.animationType != ModelImporterAnimationType.Human || imp.importAnimation)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = false;
                imp.SaveAndReimport();
            }
            return SavePrefab(fbx, prefab, null);
        }

        /// A prefab for a generic-rig model that carries its own clips: one controller state per clip (named after the
        /// clip, Blender's "Rig|" prefix removed), looping idle, walk, run and flight clips.
        static GameObject DragonArtPrefab(string fbx)
        {
            if (!File.Exists(fbx) || !(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) return null;
            var loops = new HashSet<string> { "Idle", "Walk", "Run", "Flame Attack", "Fly Forward", "Fly Float", "Fly Glide", "Fly Flame Attack" };
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.IndexOf('|') + 1) : c.takeName;
                c.loopTime = loops.Contains(c.name);
            }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            string ctrlPath = Path.ChangeExtension(fbx, ".controller");
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
            {
                var st = sm.AddState(clip.name);
                st.motion = clip;
                if (clip.name == "Idle") sm.defaultState = st;
            }
            return SavePrefab(fbx, Path.ChangeExtension(fbx, ".prefab"), ctrl);
        }

        static GameObject SavePrefab(string fbx, string prefabPath, RuntimeAnimatorController ctrl)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.applyRootMotion = false;
            if (ctrl) anim.runtimeAnimatorController = ctrl;
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            UnityEngine.Object.DestroyImmediate(inst);
            return saved;
        }

        /// Imports a model as humanoid (making its own avatar) and its separate animation files as humanoid clips
        /// copying that avatar, so its clips and other packs' humanoid clips both drive it.
        static void MakeHumanoid(string model, params string[] anims)
        {
            if (!(AssetImporter.GetAtPath(model) is ModelImporter imp)) return;
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) { Debug.LogWarning("Skyfall: no humanoid avatar for " + model); return; }
            foreach (var a in anims)
            {
                if (!(AssetImporter.GetAtPath(a) is ModelImporter ai)) continue;
                bool changed = false;
                if (ai.animationType != ModelImporterAnimationType.Human || ai.sourceAvatar != avatar)
                {
                    ai.animationType = ModelImporterAnimationType.Human;
                    ai.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    ai.sourceAvatar = avatar;
                    changed = true;
                }
                // Bake the root height into the pose, measured from the feet: this pack's root sits oddly, which only cancels
                // out on its own model, so on others (the Unity AI remakes) the body sank below the ground.
                var clips = ai.clipAnimations.Length > 0 ? ai.clipAnimations : ai.defaultClipAnimations;
                foreach (var c in clips)
                {
                    if (c.lockRootHeightY && c.heightFromFeet && !c.keepOriginalPositionY) continue;
                    c.lockRootHeightY = true;
                    c.heightFromFeet = true;
                    c.keepOriginalPositionY = false;
                    changed = true;
                }
                if (!changed) continue;
                ai.clipAnimations = clips;
                ai.SaveAndReimport();
            }
        }

        /// Builds (or rebuilds) an animator controller with one state per clip, named as the game asks for them;
        /// the first state is the default. Locomotion and idle clips are set to loop. Null if the first clip is missing.
        static RuntimeAnimatorController BuildController(string path, (string name, string fbx)[] states)
        {
            if (!File.Exists(states[0].fbx)) return null;
            foreach (var (name, fbx) in states)
                if (name == "Idle" || name == "Walk" || name == "Run" || name == "JumpAir") SetLoop(fbx);
            AssetDatabase.DeleteAsset(path);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var (name, fbx) in states)
            {
                var clip = Clip(fbx);
                if (clip == null) continue;
                var st = sm.AddState(name);
                st.motion = clip;
                if (sm.defaultState == null || name == "Idle") sm.defaultState = st;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// Points every material embedded in a model at one external material (and fixes prefabs that copied the
        /// embedded one).
        static void RemapMaterials(string fbx, string matPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null || !(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) return;
            bool changed = false;
            foreach (var emb in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().ToList())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), emb.name);
                if (imp.GetExternalObjectMap().TryGetValue(id, out var cur) && cur == mat) continue;
                imp.AddRemap(id, mat);
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }

        /// Makes an FBX's clips loop (the Toon RTS packs ship their combat idle and walk as one-shots).
        static void SetLoop(string fbx)
        {
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) return;
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
            {
                if (c.loopTime) continue;
                c.loopTime = true;
                changed = true;
            }
            if (!changed) return;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }

        // ---------------------------------------------------------------- UI icons

        /// Maps game ids (spells, passives, weapons, defenses, buckets, armor, talents, power-ups, heroes) to the
        /// Basic RPG Icons pack.
        static void BuildIcons(SkinLibrary lib)
        {
            lib.icons = new List<SkinLibrary.NamedIcon>();
            if (!AssetDatabase.IsValidFolder("Assets/Basic_RPG_Icons")) return;
            const string Sk = Icons + "Skills/", Wp = Icons + "Items/Weapons/", Ar = Icons + "Items/Armor/", Rs = Icons + "Items/Resources/", Ch = Icons + "Char/";
            void I(string name, string path)
            {
                var t = Load<Texture2D>(path);
                if (t) lib.icons.Add(new SkinLibrary.NamedIcon { name = name, icon = t });
            }
            string fireball = Sk + "01_Fireball.png", fire = Sk + "02_Fire.png", fireRain = Sk + "03_Fire_rain.png", nova = Sk + "04_Ice_Nova.png",
                shards = Sk + "05_Ice_shards.png", bolt = Sk + "06_Lightning_Bolt.png", strike = Sk + "07_Lightning_Strike.png", blink = Sk + "08_Blink.png",
                slash = Sk + "09_Melee_slash.png", aoe = Sk + "10_Melee_AOE.png", cone = Sk + "11_Melee_Cone.png", heal = Sk + "12_Healing_spell.png",
                heal2 = Sk + "13_Healing_spell_2.png", summon = Sk + "14_Summon.png", curse = Sk + "15_curse.png", drain = Sk + "16_life_drain.png";
            // Abilities.
            // The pack's "lightning bolt" is a golden hammer, so Chain Lightning gets its own icon: the lightning strike
            // recolored electric blue-violet (written to a gitignored folder, since it is derived from the pack's art).
            string chain = TintedIcon(strike, "chain_lightning", new Color(0.55f, 0.65f, 1f));
            I("gravity_well", summon); I("frost_bolt", shards); I("chain_lightning", chain ?? strike); I("meteor", fireRain);
            I("blood_aura", drain); I("lightning_storm", strike); I("bloodthirst", curse); I("spellblade", slash);
            I("sword", Wp + "09_Longsword.png"); I("blood_lance", TintedIcon(cone, "blood_lance", new Color(0.95f, 0.08f, 0.15f)) ?? cone); I("staff", Wp + "08_Mage_Staff.png");
            I("def_shield", Wp + "05_Shield.png"); I("def_evasion", blink); I("def_ward", fire);
            I("dragonfire", fireball); I("ride", aoe);
            // Stat buckets.
            I("bucket_health", Rs + "15_Heal_potion.png"); I("bucket_mana", Rs + "06_blue_flower.png"); I("bucket_offense", Wp + "02_Sword.png");
            I("bucket_defense", Wp + "05_Shield.png"); I("bucket_dragon", Rs + "11_Beast_Hide.png"); I("bucket_utility", Ar + "Common/05_leather_boots.png");
            // Armor slots (common art for Common, epic art for Superior and Mythic).
            var slotArt = new Dictionary<string, string> { ["helm"] = "03_plate_helm", ["chest"] = "01_plate_chest", ["gloves"] = "04_plate_gloves", ["legs"] = "06_plate_pants", ["boots"] = "05_plate_boots" };
            foreach (var kv in slotArt)
            {
                I("armor_" + kv.Key, Ar + "Common/" + kv.Value + ".png");
                I("armor_" + kv.Key + "_epic", Ar + "Epic/" + kv.Value + "_E.png");
            }
            // Power-ups and heroes.
            I("pu_speed", blink); I("pu_damage", slash); I("pu_magnet", summon);
            I("hero_knight", Ch + "Warrior_2.png"); I("hero_huntress", Ch + "Summoner_5.png"); I("hero_pyromancer", Ch + "Mage_3.png");
            // Talents.
            var talentArt = new Dictionary<string, string>
            {
                ["k_wyrmsteel"] = Wp + "09_Longsword.png", ["k_bulwark"] = Wp + "05_Shield.png", ["k_rally"] = heal2,
                ["k_whirlwind"] = aoe, ["k_bash"] = Ar + "Epic/02_plate_shoulder_E.png", ["k_reach"] = cone,
                ["k_aegis"] = Ar + "Epic/01_plate_chest_E.png", ["k_charge"] = fire, ["k_oath"] = fireball,
                ["k_thunderclap"] = bolt, ["k_retribution"] = curse, ["k_sky"] = blink,
                ["k_dragonform"] = fireRain, ["k_firewave"] = slash, ["k_vow"] = heal,
                ["h_hemorrhage"] = drain, ["h_pact"] = curse, ["h_fleet"] = Ar + "Epic/05_leather_boots_E.png",
                ["h_volley"] = cone, ["h_sanguine"] = heal2, ["h_orbs"] = Rs + "15_Heal_potion.png",
                ["h_execute"] = slash, ["h_shadowstep"] = blink, ["h_wyrmbond"] = Rs + "14_Good_Beast_Hide.png",
                ["h_lance"] = Wp + "03_Greataxe.png", ["h_thirst"] = drain, ["h_phantom"] = summon,
                ["h_bloodmoon"] = curse, ["h_tempest"] = aoe, ["h_wings"] = Ar + "Epic/02_leather_cloak_E.png",
                ["p_kindling"] = fire, ["p_emberward"] = heal2, ["p_font"] = Rs + "06_blue_flower.png",
                ["p_twin"] = fireball, ["p_firewalk"] = Ar + "Epic/05_cloth_boots_E.png", ["p_combustion"] = fireRain,
                ["p_phoenix"] = heal, ["p_blink"] = blink, ["p_dragonkin"] = Rs + "11_Beast_Hide.png",
                ["p_inferno"] = fireball, ["p_molten"] = Ar + "Epic/01_cloth_chest_E.png", ["p_weaver"] = Wp + "07_Priest_Staff.png",
                ["p_supernova"] = nova, ["p_sunorbs"] = fire, ["p_pillars"] = fireRain,
            };
            // The pack has ~40 usable pictures for ~160 ids, so the rest are recolors by theme: each hero's tree in its
            // hero color, each dragon's tree in its dragon's color, shared dragon talents in gold. Talents with the same
            // effect as a dragon card share that card's art.
            string T(string src, string name, Color c) => TintedIcon(src, name, c) ?? src;
            Color gold = new Color(1f, 0.78f, 0.25f), sky = new Color(0.45f, 0.75f, 1f), steel = new Color(0.45f, 0.65f, 1f),
                crimson = new Color(0.95f, 0.12f, 0.22f), ember = new Color(1f, 0.55f, 0.12f), azure = new Color(0.2f, 0.5f, 1f),
                scarlet = new Color(1f, 0.18f, 0.08f), blood = new Color(0.7f, 0.04f, 0.14f), violet = new Color(0.55f, 0.15f, 0.75f),
                arcane = new Color(0.6f, 0.4f, 1f);
            string beast = Rs + "11_Beast_Hide.png", goodBeast = Rs + "14_Good_Beast_Hide.png", shield = Wp + "05_Shield.png";
            // Capstone forms (the F ability by the health bar).
            talentArt["h_specter"] = T(blink, "crimson_specter", crimson);
            talentArt["p_phoenixform"] = T(fireRain, "phoenix_form", ember);
            // Dragon cards (and the tree talents that grant the same thing).
            string cardWing = T(shield, "d_wing", sky), cardRescue = T(summon, "d_rescue", gold), cardWrath = T(fire, "d_wrath", scarlet);
            var dragonArt = new Dictionary<string, string>
            {
                ["d_size"] = goodBeast, ["d_cone"] = T(cone, "d_cone", ember), ["d_haste"] = T(blink, "d_haste", gold),
                ["d_rescue"] = cardRescue, ["d_wing"] = cardWing, ["d_wrath"] = cardWrath,
                ["d_blue"] = T(fire, "d_blue", azure), ["d_red"] = T(fireRain, "d_red", scarlet), ["d_black"] = T(drain, "d_black", blood),
                // Shared dragon rows.
                ["dt_fury"] = T(fireball, "dt_fury", gold), ["dt_swift"] = T(blink, "dt_swift", sky), ["dt_vast"] = beast,
                ["dt_shelter"] = cardWing, ["dt_talons"] = cardRescue, ["dt_wrath"] = cardWrath,
                ["dt_skyrider"] = T(Ar + "Epic/02_plate_cloak_E.png", "dt_skyrider", sky), ["dt_unbound"] = T(aoe, "dt_unbound", gold),
                ["dt_ancient"] = T(goodBeast, "dt_ancient", gold),
                // Azure Wyrm.
                ["db_kindle"] = T(fire, "db_kindle", azure), ["db_maw"] = T(cone, "db_maw", azure), ["db_ash"] = T(fireRain, "db_ash", new Color(0.55f, 0.6f, 0.75f)),
                ["db_inferno"] = T(fireRain, "db_inferno", azure), ["db_kiln"] = T(fireball, "db_kiln", azure), ["db_cinder"] = T(shield, "db_cinder", azure),
                ["db_azure"] = T(nova, "db_azure", azure), ["db_rekindle"] = T(heal, "db_rekindle", azure), ["db_sunburst"] = T(aoe, "db_sunburst", azure),
                // Crimson Drake.
                ["dr_volatile"] = T(fireball, "dr_volatile", scarlet), ["dr_crater"] = T(aoe, "dr_crater", scarlet),
                ["dr_hide"] = T(Ar + "Epic/01_plate_chest_E.png", "dr_hide", scarlet), ["dr_aftershock"] = T(strike, "dr_aftershock", scarlet),
                ["dr_quake"] = T(shards, "dr_quake", scarlet), ["dr_fuse"] = T(blink, "dr_fuse", scarlet), ["dr_meteor"] = T(fireRain, "dr_meteor", new Color(1f, 0.4f, 0.05f)),
                ["dr_bulwark"] = T(shield, "dr_bulwark", scarlet), ["dr_chain"] = T(nova, "dr_chain", scarlet),
                // Night Wyrm.
                ["dk_leech"] = T(drain, "dk_leech", blood), ["dk_dread"] = T(curse, "dk_dread", violet),
                ["dk_veil"] = T(Ar + "Epic/02_leather_cloak_E.png", "dk_veil", violet), ["dk_monsoon"] = T(fireRain, "dk_monsoon", blood),
                ["dk_hunger"] = T(heal2, "dk_hunger", blood), ["dk_night"] = T(blink, "dk_night", violet), ["dk_tide"] = T(heal, "dk_tide", blood),
                ["dk_pact"] = T(shield, "dk_pact", violet), ["dk_vampire"] = T(drain, "dk_vampire", violet),
            };
            foreach (var kv in dragonArt) talentArt[kv.Key] = kv.Value;
            // Permanent hero trees.
            var heroArt = new Dictionary<string, string>
            {
                ["k_iron"] = Ar + "Common/01_plate_chest.png", ["k_temper"] = T(Wp + "02_Sword.png", "k_temper", steel), ["k_squire"] = T(goodBeast, "k_squire", steel),
                ["k_guard"] = T(shield, "k_guard", steel), ["k_cleave"] = T(slash, "k_cleave", steel), ["k_rider"] = T(cone, "k_rider", gold),
                ["k_bastion"] = Ar + "Common/02_plate_cloak.png", ["k_fervor"] = T(heal2, "k_fervor", gold), ["k_wyrmcall"] = T(aoe, "k_wyrmcall", steel),
                ["h_keen"] = T(Wp + "01_Bow.png", "h_keen", crimson), ["h_lithe"] = T(Ar + "Common/05_leather_boots.png", "h_lithe", crimson),
                ["h_bondblood"] = T(beast, "h_bondblood", crimson), ["h_quiver"] = Wp + "01_Bow.png", ["h_hunter"] = T(cone, "h_hunter", crimson),
                ["h_vital"] = T(Rs + "15_Heal_potion.png", "h_vital", crimson), ["h_predator"] = T(slash, "h_predator", crimson), ["h_skyblood"] = T(aoe, "h_skyblood", crimson),
                ["p_focus"] = T(Wp + "08_Mage_Staff.png", "p_focus", arcane), ["p_ember"] = T(Ar + "Common/01_cloth_chest.png", "p_ember", ember),
                ["p_kinship"] = T(beast, "p_kinship", ember), ["p_haste"] = T(blink, "p_haste", ember), ["p_skyfire"] = T(fireball, "p_skyfire", gold),
                ["p_ascend"] = T(heal, "p_ascend", ember), ["p_warding"] = T(shield, "p_warding", ember), ["p_flamecaller"] = T(aoe, "p_flamecaller", ember),
            };
            foreach (var kv in heroArt) talentArt[kv.Key] = kv.Value;
            // The reworked hero trees' new talents have their own painted icons (Unity AI, GPT Image with a pack icon as
            // the style reference), kept small on import.
            foreach (var id in new[] { "k_wall", "k_cleaver", "k_stance", "h_barbed", "h_leap", "h_essence", "p_quick", "p_wardpillar" })
            {
                string path = AiIcons + id + ".png";
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.maxTextureSize != 256 || ti.mipmapEnabled))
                {
                    ti.maxTextureSize = 256;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
                if (Load<Texture2D>(path)) talentArt[id] = path;
            }
            foreach (var kv in talentArt) I(kv.Key, kv.Value);
            // Power-ups no longer share a picture with an ability.
            lib.icons.RemoveAll(i => i.name.StartsWith("pu_"));
            I("pu_speed", T(blink, "pu_speed", new Color(0.25f, 0.9f, 1f)));
            I("pu_damage", T(slash, "pu_damage", new Color(1f, 0.2f, 0.18f)));
            I("pu_magnet", T(summon, "pu_magnet", new Color(0.78f, 0.45f, 1f)));
            // Dragon portraits (the dragon picker and rescue screens), rendered from each dragon's own model.
            foreach (var (id, prefab) in new[] { ("blue", lib.dragonBlue), ("red", lib.dragonRed), ("black", lib.dragonBlack) })
            {
                var def = Data.DRAGONS[id];
                // The black hide reads pale under the portrait lights; darken it to match the game.
                var tint = id == "black" ? new Color(0.4f, 0.36f, 0.38f) : def.skinTint;
                var path = DragonPortrait(prefab, "dragon_" + id, tint, def.breath);
                if (path != null) I("dragon_" + id, path);
            }
        }

        const string GeneratedIcons = "Assets/Skyfall/Generated/Icons";

        /// Makes a recolored copy of an icon: every pixel's hue moves to the tint's (keeping its brightness), so a golden
        /// spell icon becomes, say, electric blue. Returns the new asset's path, or null.
        static string TintedIcon(string srcPath, string name, Color tint)
        {
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
            if (src == null) return null;
            // The pack's textures are not readable: copy through a render texture.
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color.RGBToHSV(tint, out float th, out float ts, out _);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out _, out float s, out float v);
                var c = Color.HSVToRGB(th, Mathf.Lerp(s, ts, 0.5f), v);
                c.a = px[i].a;
                px[i] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            Directory.CreateDirectory(GeneratedIcons);
            string path = GeneratedIcons + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.SaveAndReimport();
            }
            return path;
        }

        /// Renders an icon portrait of a dragon model: three-quarter view of its head and shoulders over a dark
        /// background, lit warm from the front with a rim light in its breath color. Returns the PNG's path, or null.
        static string DragonPortrait(GameObject prefab, string name, Color skinTint, Color breath)
        {
            if (prefab == null) return null;
            const int Size = 256;
            var pru = new PreviewRenderUtility();
            var tintMats = new List<Material>();
            try
            {
                var go = pru.InstantiatePrefabInScene(prefab);
                if (skinTint != Color.white)
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        var mats = r.sharedMaterials.Select(m => m ? new Material(m) : m).ToArray();
                        foreach (var m in mats)
                        {
                            if (m == null) continue;
                            tintMats.Add(m);
                            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * skinTint);
                        }
                        r.sharedMaterials = mats;
                    }
                // Framed for the Usurper (every dragon uses it): it faces +Z in its bind pose, crouched, with the head
                // low and forward. Front three-quarter view from its right, then nudged onto the head and neck.
                var root = go.transform;
                var focus = root.TransformPoint(new Vector3(0f, 3.2f, 3.2f));
                var camDir = (Quaternion.Euler(0, -35f, 0) * root.forward + Vector3.up * 0.3f).normalized;
                var cam = pru.camera;
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.Lerp(new Color(0.06f, 0.05f, 0.08f), breath, 0.18f);
                cam.transform.position = focus + camDir * 13f * root.lossyScale.x;
                cam.transform.LookAt(focus);
                cam.transform.position += (-cam.transform.right * 1.1f - cam.transform.up * 1.7f) * root.lossyScale.x;
                pru.lights[0].intensity = 1.3f;
                pru.lights[0].color = new Color(1f, 0.95f, 0.88f);
                pru.lights[0].transform.rotation = Quaternion.LookRotation(-camDir + Vector3.down * 0.6f);
                // Rim light in the breath color from behind and to the side.
                pru.lights[1].intensity = 1.2f;
                pru.lights[1].color = breath;
                pru.lights[1].transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0, 140f, 0) * camDir + Vector3.down * 0.3f);
                pru.ambientColor = new Color(0.3f, 0.3f, 0.35f);
                pru.BeginStaticPreview(new Rect(0, 0, Size, Size));
                pru.Render(true);
                var shot = pru.EndStaticPreview();
                // Copy into a readable texture.
                var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(shot, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Directory.CreateDirectory(GeneratedIcons);
                string path = GeneratedIcons + "/" + name + ".png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not render the portrait " + name + ": " + e.Message);
                return null;
            }
            finally
            {
                pru.Cleanup();
                foreach (var m in tintMats) if (m) UnityEngine.Object.DestroyImmediate(m);
            }
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
