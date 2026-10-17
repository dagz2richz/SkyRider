using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Skyfall
{
    /// Run manager: world setup, three 8-minute stages joined by portals, spawning, bosses, dragon rescues, XP, level-ups
    /// (abilities, stat buckets, talents, armor), the run save and the sky.
    /// Put this on one GameObject in an otherwise empty scene; it builds everything else from code.
    public class Game : MonoBehaviour
    {
        const int MAX_ENEMIES = 320;
        const int MAX_GEMS = 450;

        public static Game I;

        public WorldTerrain terrain;
        public Player player;
        public SpellBook spellbook;
        public DragonPet pet;
        public Hud hud;
        Light sun;
        Material skyMat;
        bool packSky;
        Color skyTint;
        float skyExposure = 1f;
        Volume volume;
        Transform fxRoot;

        public readonly List<Enemy> enemies = new List<Enemy>();
        class Gem
        {
            public Transform node;
            public float value;
            public bool pulled;
            public bool orb;
        }
        readonly List<Gem> gems = new List<Gem>();
        public readonly List<Enemy> bosses = new List<Enemy>();
        public bool running;
        public bool ended;
        /// True while the portal carries the hero to the next stage (no spawning, no damage).
        public bool traveling;
        /// Seconds into the current stage.
        public float timeElapsed;
        /// Seconds across the whole run (for the score).
        public float totalTime;
        float spawnAccum;
        public int wave = 1;
        /// Current stage (1..Data.STAGE_COUNT).
        public int stage = 1;
        public int bossesSpawned;
        public int bossesSlain;
        /// True once the timer hit 0:00: the field is cleared and the stage's final boss waits (on the floating island in worlds 2 and 3).
        public bool finalStarted;
        public bool miniSpawned;
        Enemy miniBoss;
        IslandBeacon beacon;
        /// Dragons rescued from captivity this run (for the end screen).
        public readonly List<string> rescued = new List<string>();
        public int kills;
        /// Meta XP sources: kills by the hero and by the dragon, seconds ridden, dragon signatures and rescues.
        public int heroKills, dragonKills, signatures, rescues;
        public float rideTime;
        /// Set by the dragon while it deals damage, so its kills count toward bond XP.
        public bool dragonSource;
        /// Hero and bond XP awarded when the run ended (for the end screen).
        public int heroXpGained, bondXpGained;
        public float damageDealt;
        public float biggestHit;
        float shakeAmt;
        public bool leveling;
        public bool freezePreview;
        int worldSeed;
        readonly Dictionary<int, Mesh> gemMeshes = new Dictionary<int, Mesh>();
        readonly Dictionary<int, Material> gemMats = new Dictionary<int, Material>();

        /// "Ride Again" goes straight to the hero select screen; "Continue" resumes the saved run.
        public static bool skipMenu, continueRun;

        Enemy finalBoss;
        bool finalDefeated;
        bool paused;

        public Data.StageDef Stage => Data.STAGES[Mathf.Clamp(stage - 1, 0, Data.STAGES.Length - 1)];

        /// Dragon rescued most recently this run, or empty.
        public string rescuedDragon => rescued.Count > 0 ? rescued[rescued.Count - 1] : "";

        /// True while the game is paused (pause menu, stats screen, level-up choice).
        public bool Paused => paused;

        public void SetPaused(bool p)
        {
            paused = p;
            if (p) GameInput.StopRumble();
            Time.timeScale = p ? 0f : (running ? Save.game_speed : 1f);
        }

        void Awake()
        {
            I = this;
            Save.LoadAll();
            Time.timeScale = 1f;
            gameObject.AddComponent<Sfx>();
            SetupEnvironment();
            fxRoot = new GameObject("FX").transform;
            fxRoot.SetParent(transform, false);
            FX.root = fxRoot;
            FX.liveBursts = 0;
            FX.texts.Clear();
            worldSeed = Random.Range(0, int.MaxValue / 2);
            stage = continueRun && Save.HasRun ? Mathf.Clamp(Save.run.stage, 1, Data.STAGE_COUNT) : 1;
            if (stage != 1) SetSky();
            BuildTerrain();
            hud = gameObject.AddComponent<Hud>();
            hud.game = this;
            // Preview dragon on the title screen.
            player = new GameObject("Player").AddComponent<Player>();
            player.transform.SetParent(transform, false);
            player.Pos = StartPos;
            player.Init(this);
            player.camDist = 6f;
            player.camDistTarget = 6f;
            player.camYaw = 0.6f;
            player.camPitch = 0.12f;
            spellbook = new GameObject("SpellBook").AddComponent<SpellBook>();
            spellbook.transform.SetParent(transform, false);
            spellbook.Init(this, player);
            pet = new GameObject("Dragon").AddComponent<DragonPet>();
            pet.transform.SetParent(transform, false);
            pet.Init(this, player);
            pet.Pos = player.Pos + new Vector3(3, 3, -1);
            player.pet = pet;
            player.SetupLoadout(
                Data.SPELLS.ContainsKey(Save.last_spell) ? Save.last_spell : "frost_bolt",
                Data.PASSIVES.ContainsKey(Save.last_passive) ? Save.last_passive : "lightning_storm",
                Data.HEROES.ContainsKey(Save.last_hero) ? Save.last_hero : "knight",
                Data.DRAGONS.ContainsKey(Save.last_dragon) ? Save.last_dragon : "blue");
            player.SnapCamera();
            MakeGemMeshes();
            Atmosphere(0f);
            if (continueRun)
            {
                continueRun = false;
                if (Save.HasRun) { ContinueRun(); return; }
            }
            if (skipMenu)
            {
                skipMenu = false;
                hud.ShowHeroSelect();
            }
            else hud.ShowMainMenu();
        }

        void BuildTerrain()
        {
            terrain = new GameObject("Terrain").AddComponent<WorldTerrain>();
            terrain.transform.SetParent(transform, false);
            terrain.Generate(worldSeed + stage * 7919, stage);
        }

        Vector3 StartPos => new Vector3(0, terrain.Height(0, 0), 0);

        void OnDestroy()
        {
            if (I == this) I = null;
            GameInput.StopRumble();
            Time.timeScale = 1f;
            foreach (var m in gemMats.Values) if (m) Destroy(m);
            if (skyMat) Destroy(skyMat);
            if (volume && volume.profile) Destroy(volume.profile);
        }

        void SetupEnvironment()
        {
            SetSky();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0045f;

            // Sun.
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = 110f;

            // Post-processing: bloom on glowing things, filmic tonemapping, a touch more saturation and contrast.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.55f);
            ca.saturation.Override(10f);
            ca.contrast.Override(5f);
            var vg = profile.Add<Vignette>(true);
            vg.intensity.Override(0.18f);
            volume = new GameObject("PostProcess").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = profile;
        }

        /// Sky: the stage's Fantasy Skybox when the pack is set up, else the built-in gradient sky.
        void SetSky()
        {
            if (skyMat) Destroy(skyMat);
            var src = Skins.Lib != null ? Skins.Lib.SkyFor(stage) : null;
            packSky = src != null;
            skyMat = packSky ? new Material(src) : new Material(Shader.Find("Skyfall/Sky"));
            if (packSky)
            {
                skyTint = skyMat.HasProperty("_Tint") ? skyMat.GetColor("_Tint") : Color.gray;
                skyExposure = skyMat.HasProperty("_Exposure") ? skyMat.GetFloat("_Exposure") : 1f;
            }
            RenderSettings.skybox = skyMat;
        }

        void MakeGemMeshes()
        {
            var tiers = new Dictionary<int, Color>
            {
                [1] = new Color(0.3f, 1f, 0.5f), [3] = new Color(0.3f, 0.6f, 1f), [10] = new Color(0.8f, 0.35f, 1f), [50] = new Color(1f, 0.8f, 0.25f),
            };
            foreach (var kv in tiers)
            {
                var mat = FX.NewLit(kv.Value, 0.2f, 0f, 2f);
                gemMats[kv.Key] = mat;
                gemMeshes[kv.Key] = MeshGen.Sphere(4);
            }
        }

        // ---------------------------------------------------------------- run flow

        public void StartRun(string spell, string passive, string hero = "knight", string dragon = "blue")
        {
            player.SetupLoadout(spell, passive, hero, dragon);
            Save.last_spell = spell;
            Save.last_passive = passive;
            Save.last_hero = hero;
            Save.last_dragon = dragon;
            if (stage != 1) BuildStage(1);
            BeginPlay();
            player.OnStageStart();
            WriteRunSave();
            hud.Banner($"Stage 1 — {Stage.name}", Stage.intro);
            Tw.After(3.8f, DragonGrowthBanner);
        }

        /// Resumes the saved run at the start of the stage it was on, with the hero exactly as saved.
        public void ContinueRun()
        {
            var s = Save.run;
            player.SetupLoadout(Data.SPELLS.ContainsKey(s.spell ?? "") ? s.spell : "frost_bolt", Data.PASSIVES.ContainsKey(s.passive ?? "") ? s.passive : "lightning_storm", s.hero,
                Save.IsDragonUnlocked(s.dragon) && Data.DRAGONS.ContainsKey(s.dragon ?? "") ? s.dragon : "blue");
            player.ApplyRunState(s);
            kills = s.kills;
            rerolls = s.rerolls;
            heroKills = s.heroKills;
            dragonKills = s.dragonKills;
            signatures = s.signatures;
            rescues = s.rescues;
            rideTime = s.rideTime;
            bossesSlain = s.bossesSlain;
            totalTime = s.totalTime;
            damageDealt = s.damageDealt;
            biggestHit = s.biggestHit;
            rescued.Clear();
            rescued.AddRange(s.hatched);
            int st = Mathf.Clamp(s.stage, 1, Data.STAGE_COUNT);
            if (st != stage) BuildStage(st);
            else player.Teleport(StartPos);
            BeginPlay();
            hud.Banner($"Stage {stage} — {Stage.name}", "Your journey continues.");
        }

        void BeginPlay()
        {
            running = true;
            Time.timeScale = Save.game_speed;
            player.camDistTarget = 9f;
            player.camPitch = 0.32f;
            GameInput.MouseCaptured = true;
            hud.ShowHud();
        }

        /// Writes the run in progress to the save file (stage start, and when leaving mid-run).
        public void WriteRunSave()
        {
            if (!running || ended) return;
            var s = Save.run ?? new Save.RunState();
            s.active = true;
            s.hero = player.heroId;
            s.dragon = player.dragonId;
            s.spell = player.spellId;
            s.passive = player.passiveId;
            s.stage = stage;
            s.kills = kills;
            s.rerolls = rerolls;
            s.heroKills = heroKills;
            s.dragonKills = dragonKills;
            s.signatures = signatures;
            s.rescues = rescues;
            s.rideTime = rideTime;
            s.bossesSlain = bossesSlain;
            s.totalTime = totalTime;
            s.damageDealt = damageDealt;
            s.biggestHit = biggestHit;
            s.hatched = new List<string>(rescued);
            s.date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            player.WriteRunState(s);
            Save.run = s;
            Save.Write();
        }

        void Update()
        {
            float delta = Time.deltaTime;
            FX.TickTexts(Time.unscaledDeltaTime * (paused ? 0f : Mathf.Max(Time.timeScale, 0.0001f)));
            if (paused) return;
            ApplyShake(delta);
            if (player && Sfx.inst) Sfx.inst.listener = player.Center;
            if (!running)
            {
                if (pet && !freezePreview)
                {
                    pet.Tick(delta, false);
                    player.camYaw += delta * 0.08f;
                }
                return;
            }
            if (traveling)
            {
                pet.Tick(delta, false);
                return;
            }
            timeElapsed += delta;
            totalTime += delta;
            if (player.flying) rideTime += delta;
            Waves();
            Spawn(delta);
            TickEnemies(delta);
            spellbook.Tick(delta);
            pet.Tick(delta, true);
            TickGems(delta);
            TickBeacon();
            Atmosphere(Mathf.Clamp01(timeElapsed / Data.RUN_LENGTH));
            if (player.pendingLevels > 0 && !leveling && !ended) LevelUp();
        }

        void Waves()
        {
            var names = Stage.waves;
            int newWave = Mathf.Min((int)(timeElapsed / Data.WAVE_LENGTH) + 1, names.Length);
            // The mini boss arrives at 4:00; at 0:00 the field is cleared for the final battle.
            if (!miniSpawned && timeElapsed >= Data.MINI_BOSS_TIME)
            {
                miniSpawned = true;
                SpawnBoss(Data.MINI_BOSS);
            }
            if (!finalStarted && timeElapsed >= Data.RUN_LENGTH) BeginFinalBattle();
            if (newWave != wave && !finalStarted)
            {
                wave = newWave;
                hud.Banner($"Wave {wave} — {names[wave - 1]}", "");
            }
        }

        void Spawn(float delta)
        {
            if (finalStarted) return;
            WyvernDirector.Tick(this, delta);   // WYVERN: world 2+ flying harasser spawns (Wyvern.cs)
            float t = timeElapsed;
            float rate = (1f + t * 0.045f) * Data.SPAWN_RATE_MULT * (1f + 0.15f * (stage - 1));
            int cap = Mathf.Min(MAX_ENEMIES, (int)((60 + t * 0.55f + 20 * (stage - 1)) * Data.SPAWN_CAP_MULT));
            spawnAccum += rate * delta;
            while (spawnAccum >= 1f)
            {
                spawnAccum -= 1f;
                if (enemies.Count >= cap) continue;
                var weights = Data.WAVE_WEIGHTS[Mathf.Clamp(wave - 1 + (stage - 1), 0, Data.WAVE_WEIGHTS.Length - 1)];
                int total = 0;
                foreach (var w in weights) total += w.weight;
                int roll = Random.Range(0, total);
                string kind = "runt";
                foreach (var w in weights)
                {
                    roll -= w.weight;
                    if (roll < 0)
                    {
                        kind = w.kind;
                        break;
                    }
                }
                // Harpooners only hunt riders: where the dragon can't fly yet half of them come as archers, the rest as runts.
                if (kind == "harpooner" && stage < Data.FLIGHT_STAGE) kind = M.RandF() < Data.HARPOONER_AS_ARCHER ? "archer" : "runt";
                float a = M.RandF() * M.TAU;
                float d = M.RandRange(34f, 52f);
                var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                SpawnEnemy(kind, p);
            }
        }

        /// Health and damage multipliers for ogres right now: they grow through the stage, and each stage starts tougher.
        float EnemyHpMult => (1f + timeElapsed / 60f * 0.38f) * Data.ENEMY_STAT_MULT * Data.ENEMY_HP_MULT * Stage.enemyMult;
        float EnemyDmgMult => (1f + timeElapsed / 600f) * Data.ENEMY_STAT_MULT * Mathf.Sqrt(Stage.enemyMult);

        /// Spawns an ogre on the terrain, or (with fromY, e.g. an island's top) on the surface at or below that height.
        public Enemy SpawnEnemy(string kind, Vector3 p, float fromY = float.NegativeInfinity)
        {
            float lim = WorldTerrain.PLAY_LIMIT;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            var e = Enemy.Make(this, kind, EnemyHpMult, EnemyDmgMult);
            e.transform.SetParent(transform, false);
            e.Pos = new Vector3(p.x, terrain.SurfaceHeight(p.x, p.z, fromY), p.z);
            enemies.Add(e);
            return e;
        }

        /// The mini boss: arrives near the player. Killing him does not end the stage.
        void SpawnBoss(int idx)
        {
            float a = M.RandF() * M.TAU;
            var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 40f;
            var e = Enemy.MakeBoss(this, idx, Data.BOSS_STAT_MULT * Stage.enemyMult, Data.BOSS_STAT_MULT * Mathf.Sqrt(Stage.enemyMult), Stage.miniBoss, Stage.bossTint);
            e.transform.SetParent(transform, false);
            float lim = WorldTerrain.PLAY_LIMIT;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            e.Pos = new Vector3(p.x, terrain.Height(p.x, p.z), p.z);
            enemies.Add(e);
            bosses.Add(e);
            bossesSpawned++;
            miniBoss = e;
            Sfx.Play("roar", 2f, 0f);
            hud.Banner("BOSS — " + e.displayName, $"It approaches from the {Compass(p - player.Pos)}.");
            Shake(0.5f);
            FX.Vfx("portal_red", e.Pos + new Vector3(0, e.size, 0), e.size / 2f, 2.5f, null, 0f);
        }

        /// 0:00: every ogre is swept away (no kills, no XP), spawning stops and the stage's final boss appears on the big
        /// island. A mini boss still alive is despawned too, so the final fight is one-on-one.
        public void BeginFinalBattle()
        {
            if (finalStarted) return;
            finalStarted = true;
            int puffs = 0;
            foreach (var e in enemies)
            {
                if (e.dead) continue;
                e.dead = true;
                if (puffs++ < 30)
                {
                    FX.Burst(e.Pos + new Vector3(0, e.size, 0), FX.EnemyRed, 8, 4f * e.size, 0.3f * e.size);
                    FX.Vfx("dust_puff", e.Pos + new Vector3(0, 0.3f, 0), 0.35f * Mathf.Max(0.7f, e.size), 1.4f, null, 0.05f, FX.EnemyRed);
                }
                e.gameObject.SetActive(false);
            }
            bosses.Clear();
            miniBoss = null;
            Sfx.Play("death", player.Pos, -2f, 0f);

            var c = terrain.BossIslandCenter;
            var isl = terrain.bossIsland;
            var b = Enemy.MakeBoss(this, Data.FINAL_BOSS, Data.FinalBossHpMult(Stage), Data.BOSS_STAT_MULT * Mathf.Sqrt(Stage.enemyMult), Stage.finalBoss, Stage.bossTint,
                Stage.finalBossModel, Stage.finalBossAbility);
            b.transform.SetParent(transform, false);
            if (isl != null && !M.IsInf(c))
            {
                b.Pos = c;
                b.BindToIsland(isl);
            }
            else
            {
                // No island in this world: fall back to a normal arena fight near the player.
                var p = player.Pos + new Vector3(30f, 0, 0);
                b.Pos = new Vector3(p.x, terrain.Height(p.x, p.z), p.z);
            }
            enemies.Add(b);
            bosses.Add(b);
            bossesSpawned++;
            finalBoss = b;
            Sfx.Play("roar", 3f, 0f);
            Shake(0.9f);
            hud.Banner("FINAL BATTLE", isl != null ? $"{Stage.finalBoss} awaits on the floating island" : $"{Stage.finalBoss} comes for you");
            FX.Vfx("portal_red", b.Pos + new Vector3(0, b.size, 0), b.size / 2f, 2.5f, null, 0f);
            FX.Flash(b.Pos + new Vector3(0, b.size, 0), 14f, new Color(1f, 0.5f, 0.2f, 0.7f), 0.8f);
        }

        /// The beacon pillar rises from the boss island in the last 30 s (faint) and burns fully from 0:00.
        void TickBeacon()
        {
            if (terrain.bossIsland == null) return;
            float left = Data.RUN_LENGTH - timeElapsed;
            if (left > 30f) return;
            if (beacon == null)
            {
                beacon = IslandBeacon.Create(terrain.BossIslandCenter, terrain.BossIslandRadius);
                beacon.transform.SetParent(transform, true);
            }
            if (!finalDefeated) beacon.SetIntensity(finalStarted ? 1f : Mathf.Lerp(0.15f, 0.45f, 1f - left / 30f));
        }

        /// World position the HUD marker/arrow points at (just above the island), or M.INF outside the final battle.
        public Vector3 IslandMarkerPos => finalStarted && !finalDefeated && !M.IsInf(terrain.BossIslandCenter)
            ? terrain.BossIslandCenter + new Vector3(0, 8f, 0) : M.INF;

        /// Whether the player stands on (or over) the big island.
        public bool PlayerOnBossIsland()
        {
            var isl = terrain.bossIsland;
            return isl != null && isl.Contains(player.Pos.x, player.Pos.z) && player.Pos.y > isl.topY - 2f;
        }

        static string Compass(Vector3 v)
        {
            float a = Mathf.Atan2(v.x, -v.z);
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            int i = Mathf.RoundToInt(a / (M.TAU / 8f));
            return names[((i % 8) + 8) % 8];
        }

        // Spatial hash for ogre separation: linked lists of enemy indices per 5 m cell, positions cached in flat arrays.
        readonly Dictionary<int, int> sepHead = new Dictionary<int, int>();
        int[] sepNext = new int[512];
        float[] sepX = new float[512], sepZ = new float[512], sepR = new float[512], sepDx = new float[512], sepDz = new float[512];
        bool[] sepSkip = new bool[512];
        const float SEP_CELL = 5f;

        static int SepKey(int cx, int cz) => (cx + 512) * 1024 + (cz + 512);

        /// Keeps ogres from overlapping: every pair closer than the sum of their radii is pushed apart, the smaller one more.
        /// Bosses push others away but are never pushed themselves; ogres in mid-jump are left alone.
        void SeparateEnemies(float delta)
        {
            int n = enemies.Count;
            if (sepNext.Length < n)
            {
                int cap = n + 256;
                sepNext = new int[cap]; sepX = new float[cap]; sepZ = new float[cap]; sepR = new float[cap];
                sepDx = new float[cap]; sepDz = new float[cap]; sepSkip = new bool[cap];
            }
            sepHead.Clear();
            for (int i = 0; i < n; i++)
            {
                var e = enemies[i];
                var p = e.transform.position;
                sepX[i] = p.x; sepZ[i] = p.z; sepR[i] = e.radius;
                sepDx[i] = 0f; sepDz[i] = 0f;
                sepSkip[i] = e.dead || e.Airborne;
                if (sepSkip[i]) continue;
                int key = SepKey(Mathf.FloorToInt(p.x / SEP_CELL), Mathf.FloorToInt(p.z / SEP_CELL));
                sepNext[i] = sepHead.TryGetValue(key, out int h) ? h : -1;
                sepHead[key] = i;
            }
            float stiff = Mathf.Min(1f, delta * 14f) * 0.8f;
            for (int i = 0; i < n; i++)
            {
                if (sepSkip[i]) continue;
                float ax = sepX[i], az = sepZ[i], ar = sepR[i];
                bool aBoss = enemies[i].isBoss;
                int cx = Mathf.FloorToInt(ax / SEP_CELL), cz = Mathf.FloorToInt(az / SEP_CELL);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!sepHead.TryGetValue(SepKey(cx + dx, cz + dz), out int j)) continue;
                        for (; j >= 0; j = sepNext[j])
                        {
                            if (j <= i) continue;
                            float vx = ax - sepX[j], vz = az - sepZ[j];
                            float rr = ar + sepR[j];
                            float d2 = vx * vx + vz * vz;
                            if (d2 >= rr * rr) continue;
                            bool bBoss = enemies[j].isBoss;
                            if (aBoss && bBoss) continue;
                            float d = Mathf.Sqrt(d2);
                            if (d < 0.01f) { float ang = M.RandF() * M.TAU; vx = Mathf.Cos(ang); vz = Mathf.Sin(ang); d = 1f; }
                            float o = (rr - d) * stiff;
                            float fa;   // share of the correction the first one takes; the second takes the rest
                            if (aBoss) fa = 0f;
                            else if (bBoss) fa = 1f;
                            else
                            {
                                float ma = ar * ar, mb = sepR[j] * sepR[j];
                                fa = mb / (ma + mb);
                            }
                            float nx = vx / d * o, nz = vz / d * o;
                            sepDx[i] += nx * fa; sepDz[i] += nz * fa;
                            sepDx[j] -= nx * (1f - fa); sepDz[j] -= nz * (1f - fa);
                        }
                    }
            }
            for (int i = 0; i < n; i++)
                if (sepDx[i] != 0f || sepDz[i] != 0f) enemies[i].Shove(new Vector3(sepDx[i], 0f, sepDz[i]));
        }

        void TickEnemies(float delta)
        {
            SeparateEnemies(delta);
            for (int i = 0; i < enemies.Count; i++)
                if (!enemies[i].dead) enemies[i].Tick(delta, player);
            // Sweep the dead; also cull stragglers who fell far behind.
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (e.dead)
                {
                    enemies.RemoveAt(i);
                    Destroy(e.gameObject);
                }
                else if (!e.isBoss && !e.IsWyvern && Vector3.Distance(e.Pos, player.Pos) > 90f && !e.OnFloatingIsland())
                {
                    float a = M.RandF() * M.TAU;
                    var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 45f;
                    float lim = WorldTerrain.PLAY_LIMIT;
                    p.x = Mathf.Clamp(p.x, -lim, lim);
                    p.z = Mathf.Clamp(p.z, -lim, lim);
                    e.Pos = new Vector3(p.x, terrain.Height(p.x, p.z), p.z);
                }
            }
            if (bosses.Count > 0)
            {
                var b = bosses[0];
                hud.SetBoss(b.displayName, b.hp / b.maxHp);
            }
            else hud.SetBoss("", 0f);
        }

        // ---------------------------------------------------------------- queries

        /// WYVERN: the hero can't hurt a wyvern on foot, so the hero's targeting (the default) skips it; the dragon's own
        /// queries pass asDragon so it can find wyverns. See Wyvern.cs.
        public bool HeroCanHitWyverns => player != null && (player.flying || player.SoaringForm);
        public bool CanTarget(Enemy e, bool asDragon = false) => !e.IsWyvern || asDragon || HeroCanHitWyverns;

        public Enemy NearestEnemy(Vector3 pos, float maxDist, float maxDy = 6f, Enemy exclude = null, List<Enemy> excludeList = null, bool asDragon = false)
        {
            Enemy best = null;
            float bd = maxDist * maxDist;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.dead || e == exclude || !CanTarget(e, asDragon)) continue;
                var v = e.Pos - pos;
                if (Mathf.Abs(v.y + 1f) > maxDy + e.radius) continue;
                v.y = 0f;
                float d = v.sqrMagnitude;
                if (d < bd && (excludeList == null || !excludeList.Contains(e)))
                {
                    bd = d;
                    best = e;
                }
            }
            return best;
        }

        public List<Enemy> EnemiesInRadius(Vector3 pos, float r, float maxDy = 4f, bool asDragon = false)
        {
            var result = new List<Enemy>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.dead || !CanTarget(e, asDragon)) continue;
                var v = e.Pos - pos;
                if (Mathf.Abs(v.y + 1f) > maxDy + e.radius) continue;
                v.y = 0f;
                float rr = r + e.radius;
                if (v.sqrMagnitude < rr * rr) result.Add(e);
            }
            return result;
        }

        /// The spot with the most ogres around it (sampled), within range of pos. M.INF if none.
        public Vector3 DensestPoint(Vector3 pos, float range, Vector3? avoid = null, float avoidR = 0f)
        {
            var best = M.INF;
            int bestN = -1;
            for (int i = 0; i < 8; i++)
            {
                var e = RandomEnemyNear(pos, range);
                if (e == null || e.IsWyvern) continue;   // area spells and dives aim at the ground crowd, never at a flyer
                if (avoid.HasValue && Vector3.Distance(e.Pos, avoid.Value) < avoidR) continue;
                int n = EnemiesInRadius(e.Pos, 5f, 6f).Count;
                if (n > bestN)
                {
                    bestN = n;
                    best = e.Pos;
                }
            }
            return best;
        }

        readonly List<Enemy> cands = new List<Enemy>();

        public Enemy RandomEnemyNear(Vector3 pos, float r, bool asDragon = false)
        {
            cands.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.dead && CanTarget(e, asDragon) && (e.Pos - pos).sqrMagnitude < r * r)
                {
                    cands.Add(e);
                    if (cands.Count > 40) break;
                }
            }
            if (cands.Count == 0) return null;
            return cands[Random.Range(0, cands.Count)];
        }

        // ---------------------------------------------------------------- damage

        public void DamageEnemy(Enemy e, float amount, Vector3 knockDir = default, float knockForce = 0f, bool show = true, Color? color = null)
        {
            if (e == null || e.dead) return;
            // WYVERN: only the dragon (or a mounted hero) can hurt it; everything else passes through.
            if (e.IsWyvern && !dragonSource && !e.dotTick && !HeroCanHitWyverns)
            {
                if (show) e.ShowImmune();
                return;
            }
            bool crit = show && M.RandF() < player.CritChance;
            if (crit) amount *= 2f;
            e.hp -= amount;
            damageDealt += amount;
            biggestHit = Mathf.Max(biggestHit, amount);
            if (knockForce > 0f)
            {
                float f = knockForce * (e.isBoss ? 0.15f : 1f);
                e.knock += knockDir * f;
            }
            if (show)
            {
                e.HitPop(amount, knockForce > 2f);
                FX.Number(e.Pos + new Vector3(0, 2.2f * e.size, 0), amount, crit ? new Color(1f, 0.85f, 0.2f) : (color ?? Color.white), crit);
            }
            if (e.hp <= 0f) Kill(e);
        }

        void Kill(Enemy e)
        {
            e.dead = true;
            kills++;
            if (dragonSource) { dragonKills++; pet.OnDragonKill(e); }
            else heroKills++;
            var pos = e.Pos;
            FX.Burst(pos + new Vector3(0, e.size, 0), FX.EnemyRed, 10, 5f * e.size, 0.3f * e.size);
            Sfx.Play("death", pos, -4f + e.size * 2f, 0.15f);
            if (e.isBoss)
            {
                bosses.Remove(e);
                bossesSlain++;
                Shake(1f);
                FX.Flash(pos + new Vector3(0, 3, 0), 10f, new Color(FX.EnemyRed.r, 0.25f, 0.12f, 0.8f), 0.8f);
                // Bosses drop no experience (the final boss may have held a dragon captive).
                // Bosses always drop two random power-ups.
                for (int i = 0; i < Data.BOSS_POWERUPS; i++)
                {
                    var pp = pos + new Vector3(M.RandRange(-3f, 3f), 0, M.RandRange(-3f, 3f));
                    pp.y = terrain.SurfaceHeight(pp.x, pp.z, pos.y);
                    DropPowerUp(Data.POWERUP_ORDER[Random.Range(0, Data.POWERUP_ORDER.Length)], pp);
                    bossPowerUps++;
                }
                player.Heal(player.maxHp * 0.4f);
                hud.Banner($"{e.displayName} has fallen!", "");
                Sfx.Play("roar", 0f, 0f);
                if (e == finalBoss) FinalBossDown(pos);
            }
            else
            {
                // Half as many gems, each worth twice as much.
                if (e.IsWyvern) pos = e.GroundPos;   // WYVERN: it dies in the air; its loot lands on the ground below
                if (M.RandF() < Data.GEM_DROP_CHANCE) DropGem(pos, e.xpValue * Data.GEM_VALUE_MULT);
                // Random health orbs; bigger ogres drop them more often.
                float orbChance = Data.ORB_CHANCE * (0.6f + e.size * 0.6f);
                if (e.lanceMarked) orbChance += Data.WEAPONS["blood_lance"].orb_bonus;
                if (player.Has("h_orbs")) orbChance *= 2f;
                if (M.RandF() < orbChance) DropOrb(pos);
                if (M.RandF() < player.PowerUpDropChance) DropPowerUp(Data.POWERUP_ORDER[Random.Range(0, Data.POWERUP_ORDER.Length)], pos);
            }
            player.OnKill(e);
            // Pack models fall with their death animation (up to a cap); the rest vanish in a dust puff.
            if (!e.LeaveCorpse())
                FX.Vfx("dust_puff", pos + new Vector3(0, 0.3f, 0), 0.35f * Mathf.Max(0.7f, e.size), 1.6f, null, 0.25f, FX.EnemyRed);
            e.gameObject.SetActive(false);
        }

        public void Shake(float amount)
        {
            shakeAmt = Mathf.Max(shakeAmt, amount);
            if (amount >= 0.25f && !paused && running) GameInput.Rumble(amount, 0.15f);   // hurt, boss slams, hero slam
        }

        void ApplyShake(float delta)
        {
            if (player == null) return;
            shakeAmt = Mathf.MoveTowards(shakeAmt, 0f, delta * 2f);
            if (shakeAmt > 0f)
                player.SetShake(new Vector2(M.RandRange(-1f, 1f), M.RandRange(-1f, 1f)) * shakeAmt * 0.4f);
            else player.SetShake(Vector2.zero);
        }

        // ---------------------------------------------------------------- pickups

        void DropGem(Vector3 pos, float value)
        {
            if (gems.Count >= MAX_GEMS)
            {
                // Merge into a random existing gem to keep counts sane.
                var g = gems[Random.Range(0, gems.Count)];
                if (!g.orb) g.value += value;
                return;
            }
            int tier = 1;
            foreach (int t in new[] { 50, 10, 3 })
                if (value >= t)
                {
                    tier = t;
                    break;
                }
            var r = FX.Part(transform, gemMeshes[tier], gemMats[tier], Vector3.zero, new Vector3(0.6f, 0.75f, 0.6f) * (tier >= 10 ? 1.8f : 1.15f));
            FX.NoShadow(r);
            r.gameObject.name = "Gem";
            r.transform.position = pos + new Vector3(0, 0.6f, 0);
            gems.Add(new Gem { node = r.transform, value = value });
        }

        /// Total power-ups dropped this run, and how many of them came from bosses (the tests check these).
        public int powerUpDrops, bossPowerUps;

        /// A glowing power-up pickup (kind: speed, damage or magnet) that bobs, spins and magnets to the player like a gem.
        public PowerUpPickup DropPowerUp(string id, Vector3 pos)
        {
            powerUpDrops++;
            var p = PowerUpPickup.Spawn(this, id, pos + new Vector3(0, 0.2f, 0));
            p.transform.SetParent(transform, true);
            return p;
        }

        /// The Lodestone power-up: every gem and orb on the field starts flying to the hero.
        public void VacuumPickups()
        {
            foreach (var g in gems) g.pulled = true;
            foreach (var p in FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude)) p.Pull();
        }

        void DropOrb(Vector3 pos)
        {
            var n = new GameObject("HealthOrb").transform;
            n.SetParent(transform, false);
            FX.NoShadow(FX.Part(n, MeshGen.Sphere(14), FX.Glow(new Color(1f, 0.18f, 0.2f), 2.2f), Vector3.zero, Vector3.one * 0.55f));
            FX.NoShadow(FX.Part(n, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.7f, 0.7f), 2.5f), Vector3.zero, Vector3.one * 0.25f));
            FX.NoShadow(FX.Part(n, MeshGen.Ring(), FX.Glow(new Color(1f, 0.3f, 0.3f, 0.6f), 1.5f), Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f), new Vector3(M.PI * 0.5f, 0, 0)));
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(n, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.3f, 0.3f);
            light.intensity = 1.2f;
            light.range = 4f;
            light.shadows = LightShadows.None;
            n.position = pos + new Vector3(0, 0.9f, 0);
            gems.Add(new Gem { node = n, value = Data.ORB_HEAL, orb = true });
        }

        void TickGems(float delta)
        {
            var c = player.Center;
            float mag = player.MagnetRadius;
            float spin = Time.time * 3f * Mathf.Rad2Deg;
            for (int i = gems.Count - 1; i >= 0; i--)
            {
                var g = gems[i];
                var node = g.node;
                var to = c - node.position;
                float d = to.magnitude;
                if (!g.pulled && d < mag) g.pulled = true;
                if (g.pulled) node.position += to.normalized * Mathf.Min(d, delta * (18f + 30f / Mathf.Max(d, 0.5f) + d * 0.8f));
                else node.rotation = Quaternion.Euler(0, spin, 0);
                if (d < 1.6f)
                {
                    if (g.orb)
                    {
                        player.Heal(player.maxHp * g.value);
                        if (player.Has("h_orbs")) player.AddMana(20f);
                        FX.Flash(c, 1.5f, new Color(1f, 0.3f, 0.3f, 0.5f), 0.3f);
                        Sfx.Play("heal", -2f, 0.05f);
                        FX.Vfx("healing", player.Pos, 1f, 1.5f, null, 0.3f);
                    }
                    else
                    {
                        player.AddXp(g.value);
                        Sfx.Play("pickup", -12f, 0.15f);
                    }
                    Destroy(node.gameObject);
                    gems.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------------- level ups

        string nextLevelKind = "ability";

        public class LevelOption
        {
            public string type, id, name, desc, tag;
            public int level;
            public Color color;
            public Save.ArmorPiece armor;
        }

        /// Level-ups: in-run talent rows open at Data.TALENT_LEVELS, armor is offered every Data.ARMOR_EVERY levels, and
        /// the rest alternate between ability upgrades and stat buckets (with a random chance to swap). Those always show
        /// two cards plus a third for your dragon.
        void LevelUp()
        {
            leveling = true;
            player.pendingLevels--;
            int lvl = player.level - player.pendingLevels;
            string kind;
            List<LevelOption> options;
            int row = System.Array.IndexOf(Data.TALENT_LEVELS, lvl);
            if (row >= 0 && player.TalentInRow(row) == null && Data.RUN_TALENTS.ContainsKey(player.heroId))
            {
                kind = "talent";
                options = TalentOptions(row);
            }
            else
            {
                kind = lvl % Data.ARMOR_EVERY == 0 ? "armor" : nextLevelKind;
                if (kind != "armor")
                {
                    if (M.RandF() < Data.LEVEL_SWAP_CHANCE) kind = kind == "ability" ? "stats" : "ability";
                    nextLevelKind = nextLevelKind == "ability" ? "stats" : "ability";
                }
                options = RollOptions(ref kind, lvl);
            }
            SetPaused(true);
            GameInput.MouseCaptured = false;
            Sfx.Play("levelup", -3f, 0f);
            FX.Vfx("buff", player.Pos, 1f, 2f, null, 0f);
            hud.ShowLevelUp(lvl, options, kind);
        }

        /// Fresh cards for a level-up of this kind: armor pieces, or two ability / stat cards plus one for your dragon.
        List<LevelOption> RollOptions(ref string kind, int lvl)
        {
            if (kind == "armor") return ArmorOptions(lvl);
            var options = kind == "ability" ? AbilityOptions() : new List<LevelOption>();
            if (options.Count == 0)
            {
                kind = "stats";
                options = BucketOptions();
            }
            // The third card is always your dragon's (unless every dragon upgrade is maxed).
            var dragon = DragonCard();
            if (dragon != null)
            {
                if (options.Count >= 3) options.RemoveAt(options.Count - 1);
                options.Add(dragon);
            }
            return options;
        }

        /// Rerolls left this world: Data.REROLLS_PER_WORLD at every world's start.
        public int rerolls = Data.REROLLS_PER_WORLD;

        /// Whether the level-up on screen can be rerolled (talent rows always offer the same three).
        public bool CanReroll(string kind) => rerolls > 0 && kind != "talent";

        /// Swaps the cards on screen for new ones of the same kind. Returns false when none are left.
        public bool Reroll(int lvl, string kind)
        {
            if (!CanReroll(kind)) return false;
            rerolls--;
            var options = RollOptions(ref kind, lvl);
            Sfx.Play("click", -2f, 0f);
            hud.ShowLevelUp(lvl, options, kind);
            return true;
        }

        static List<LevelOption> Pick3(List<LevelOption> pool)
        {
            M.Shuffle(pool);
            return pool.GetRange(0, Mathf.Min(3, pool.Count));
        }

        /// Ability upgrades: weapon, passive and spell (the dragon's upgrades come on its own card).
        List<LevelOption> AbilityOptions()
        {
            var pool = new List<LevelOption>();
            var p = player;
            var w = p.Weapon;
            int wl = p.weaponLevel;
            if (wl < Data.MAX_LEVEL)
                pool.Add(new LevelOption { type = "spell", id = "weapon", name = w.name, level = wl + 1, desc = w.up + "\n" + string.Join("\n", Data.WeaponStats(Data.HEROES[p.heroId].weapon, wl + 1)),
                    tag = "WEAPON", color = w.color });
            int pl = p.spells.TryGetValue(p.passiveId, out int v1) ? v1 : 1;
            if (pl < Data.MAX_LEVEL)
            {
                var ps = Data.PASSIVES[p.passiveId];
                pool.Add(new LevelOption { type = "spell", id = p.passiveId, name = ps.name, level = pl + 1, desc = ps.up, tag = "PASSIVE", color = ps.color });
            }
            int sl = p.spells.TryGetValue(p.spellId, out int v2) ? v2 : 1;
            if (sl < Data.SPELL_MAX_LEVEL)
            {
                var sp = Data.SPELLS[p.spellId];
                bool evolve = sl + 1 == Data.EVOLVE_LEVEL;
                bool unique = sl + 1 == Data.SPELL_FINAL_LEVEL;
                pool.Add(new LevelOption { type = "spell", id = p.spellId, name = sl + 1 >= Data.EVOLVE_LEVEL ? sp.evolved : sp.name, level = sl + 1, desc = sp.ups[sl],
                    tag = evolve ? "SPELL · EVOLUTION" : unique ? "SPELL · UNIQUE" : "SPELL", color = sl + 1 >= Data.EVOLVE_LEVEL ? sp.evolvedColor : sp.color });
            }
            // The spell's evolution is too important to be shuffled away (it stays among the first two cards).
            var evo = pool.Find(o => o.tag == "SPELL · EVOLUTION" || o.tag == "SPELL · UNIQUE");
            var picked = Pick3(pool);
            if (evo != null && picked.IndexOf(evo) != 0 && picked.IndexOf(evo) != 1)
            {
                picked.Remove(evo);
                picked.Insert(0, evo);
            }
            return picked;
        }

        /// Feed your dragon: one random upgrade for the dragon you ride (breath, signature, size, cone, haste, the
        /// guardian instincts that look after you, and its own flavor), or null when they are all maxed.
        LevelOption DragonCard()
        {
            var p = player;
            var dd = Data.DRAGONS[p.dragonId];
            var pool = new List<LevelOption>();
            foreach (var c in Data.DRAGON_CARDS)
            {
                if (c.only != "" && c.only != p.dragonId) continue;
                if (c.id == "ride" && pet.Grounded) continue;   // the hatchling has no signature yet
                int lv = p.spells.TryGetValue(c.id, out int v) ? v : c.start;
                if (lv >= c.max) continue;
                string name = c.id == "dragonfire" ? dd.name + " Breath" : c.id == "ride" ? dd.sig : c.name;
                string tag = c.guardian ? "DRAGON · GUARDIAN" : c.id == "ride" ? "DRAGON · SIGNATURE" : "DRAGON";
                pool.Add(new LevelOption { type = "dragon", id = c.id, name = name, level = lv + 1, desc = c.desc, tag = tag,
                    color = c.guardian ? M.Lighten(dd.breath, 0.35f) : c.id == "ride" ? dd.breath : M.Lighten(dd.body, 0.3f) });
            }
            return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
        }

        /// Stat buckets: pick one, gain all of its stats.
        List<LevelOption> BucketOptions()
        {
            var pool = new List<LevelOption>();
            foreach (var id in Data.BUCKET_ORDER)
            {
                var bk = Data.BUCKETS[id];
                var lines = new List<string>();
                foreach (var s in bk.stats) lines.Add(s.Text());
                pool.Add(new LevelOption { type = "bucket", id = id, name = bk.name, level = player.B(id) + 1,
                    desc = string.Join("\n", lines), tag = "STATS", color = bk.color });
            }
            return Pick3(pool);
        }

        /// The three talents of a row.
        List<LevelOption> TalentOptions(int row)
        {
            var list = new List<LevelOption>();
            var hero = Data.HEROES[player.heroId];
            foreach (var t in Data.RUN_TALENTS[player.heroId][row])
                list.Add(new LevelOption { type = "talent", id = t.id, name = t.name, desc = t.desc, level = row + 1, tag = $"TALENT · ROW {row + 1}",
                    color = row == Data.TALENT_LEVELS.Length - 1 ? new Color(1f, 0.75f, 0.3f) : hero.color });
            return list;
        }

        /// Three pieces for this level's slot (helm at 5, breastplate at 10, ... then around again), all of one rolled tier.
        List<LevelOption> ArmorOptions(int lvl)
        {
            var list = new List<LevelOption>();
            string slot = Data.ARMOR_SLOTS[(lvl / Data.ARMOR_EVERY - 1) % Data.ARMOR_SLOTS.Length];
            int tier = RollArmorTier();
            for (int i = 0; i < 3; i++)
            {
                var piece = RollArmor(slot, tier);
                list.Add(new LevelOption { type = "armor", id = piece.slot, armor = piece, name = ArmorName(piece), level = piece.tier + 1,
                    desc = ArmorText(piece), tag = $"ARMOR · {Data.ARMOR_TIER_NAMES[piece.tier].ToUpper()}", color = Data.ARMOR_TIER_COLORS[piece.tier] });
            }
            return list;
        }

        public static int RollArmorTier()
        {
            float r = M.RandF();
            return r < Data.ARMOR_TIER_ODDS[2] ? 2 : (r < Data.ARMOR_TIER_ODDS[2] + Data.ARMOR_TIER_ODDS[1] ? 1 : 0);
        }

        public static Save.ArmorPiece RollArmor(string slot) => RollArmor(slot, RollArmorTier());

        public static Save.ArmorPiece RollArmor(string slot, int tier)
        {
            var keys = new List<string>();
            foreach (var b in Data.BUCKET_ORDER)
                for (int i = 0; i < Data.BUCKETS[b].stats.Length; i++) keys.Add(Data.StatKey(b, i));
            M.Shuffle(keys);
            var piece = new Save.ArmorPiece { slot = slot, tier = tier };
            for (int i = 0; i < Data.ARMOR_TIER_STATS[tier]; i++) piece.stats.Add(keys[i]);
            return piece;
        }

        public static string ArmorName(Save.ArmorPiece a)
        {
            // Named after the bucket most of its stats come from.
            var count = new Dictionary<string, int>();
            string best = "";
            foreach (var k in a.stats)
            {
                string b = k.Split(':')[0];
                count[b] = (count.TryGetValue(b, out int n) ? n : 0) + 1;
                if (best == "" || count[b] > count[best]) best = b;
            }
            string slotName = Data.ARMOR_SLOT_NAMES.TryGetValue(a.slot, out var sn) ? sn : a.slot;
            return $"{Data.ARMOR_TIER_NAMES[a.tier]} {slotName} {(Data.ARMOR_SUFFIX.TryGetValue(best, out var suf) ? suf : "")}".Trim();
        }

        public static string ArmorText(Save.ArmorPiece a)
        {
            float m = Data.ARMOR_TIER_MULT[a.tier];
            var lines = new List<string>();
            foreach (var k in a.stats)
            {
                var parts = k.Split(':');
                if (!Data.BUCKETS.TryGetValue(parts[0], out var b)) continue;
                int idx = int.Parse(parts[1]);
                if (idx < b.stats.Length) lines.Add(b.stats[idx].Text(m));
            }
            return string.Join("\n", lines);
        }

        public void ChooseOption(LevelOption o)
        {
            switch (o.type)
            {
                case "spell": player.spells[o.id] = o.level; break;
                case "dragon": player.spells[o.id] = o.level; break;
                case "bucket": player.AddBucket(o.id); break;
                case "talent": player.LearnTalent(o.id); break;
                case "armor": player.EquipArmor(o.armor); break;
            }
            if (o.type == "spell" && o.id == player.spellId && o.level == Data.EVOLVE_LEVEL)
                hud.Banner($"{Data.SPELLS[o.id].name} evolves!", Data.SPELLS[o.id].evolved);
            Sfx.Play("click", -4f, 0f);
            SetPaused(false);
            GameInput.MouseCaptured = true;
            leveling = false;
            hud.RefreshAbilities();
        }

        // ---------------------------------------------------------------- endings and stages

        /// The run is over: hero XP to the hero you played, bond XP to the dragon you rode (both kept in the save slot).
        void AwardMetaXp(bool won)
        {
            heroXpGained = Data.HeroXpFor(heroKills, totalTime, stage, won);
            bondXpGained = Data.BondXpFor(dragonKills, rideTime, signatures, rescues, stage, won);
            Save.GrantXp(Save.HeroTree(player.heroId), heroXpGained);
            Save.GrantXp(Save.DragonTree(player.dragonId), bondXpGained);
        }

        public void OnPlayerDied()
        {
            if (ended) return;
            ended = true;
            running = false;
            Time.timeScale = 1f;
            GameInput.MouseCaptured = false;
            AwardMetaXp(false);
            bool best = Save.RecordRun(totalTime, kills, player.level, stage, false);
            hud.ShowEnd(false, Save.last_score, best);
        }

        /// The stage's final boss has fallen. Order: the dragon devours the warlord and grows (a still picture, worlds 1 and 2),
        /// then the warlord's captive dragon is freed when the hero walks up to it (if it held one), then the portal opens.
        /// The first rescue ever is guaranteed; after that it is a 50% chance, while dragons remain locked.
        void FinalBossDown(Vector3 pos)
        {
            finalDefeated = true;
            if (beacon) beacon.Extinguish();
            var locked = new List<string>();
            foreach (var id in Data.DRAGON_ORDER)
                if (!Save.IsDragonUnlocked(id)) locked.Add(id);
            bool captive = locked.Count > 0 && (Save.rescues_found == 0 || M.RandF() < Data.RESCUE_CHANCE);
            string pick = captive ? locked[Random.Range(0, locked.Count)] : "";
            var at = pos;
            at.y = terrain.SurfaceHeight(pos.x, pos.z, pos.y);
            // A beat to watch the warlord fall, then the feast (worlds 1-2) or straight on to the rescue / portal.
            Tw.After(1.8f, () =>
            {
                if (this == null || ended) return;
                if (stage < Data.STAGE_COUNT) ShowFeast(() => AfterFinalBoss(pick, at, locked.Count > 0));
                else AfterFinalBoss(pick, at, locked.Count > 0);
            });
        }

        /// Full-screen still of the hero's dragon eating the fallen warlord: this is how it grows into its next age.
        /// The picture is a screenshot of the real world (CaptureFeast); if that fails the caption shows as a banner instead.
        void ShowFeast(System.Action done)
        {
            string warlord = Stage.finalBoss.Split(',')[0];
            string age = Data.DRAGON_AGE[Mathf.Clamp(stage, 0, Data.DRAGON_AGE.Length - 1)];
            string caption = string.Format(Data.FEAST_CAPTION[Mathf.Clamp(stage - 1, 0, Data.FEAST_CAPTION.Length - 1)], warlord, age);
            Texture2D tex = null;
            try { tex = CaptureFeast(); }
            catch (System.Exception ex) { Debug.LogWarning("Feast capture failed: " + ex.Message); }
            if (tex == null)
            {
                hud.Banner("Your dragon feeds", caption);
                Tw.After(4f, () => { if (this != null && !ended) done(); });
                return;
            }
            hud.ShowStill(tex, caption, () => { if (this != null && !ended) done(); }, true);   // the HUD frees the picture
        }

        /// Stages the shot in the live world and photographs it, all inside one frame: the dragon (its current skin and
        /// size) is set down beside the warlord's corpse with its head to it, frozen on a bite frame; the other foes are
        /// hidden; a temporary camera frames both from a low three-quarter angle into a screen-sized RenderTexture, which
        /// is read into a Texture2D. Everything is put back afterwards. Returns null if the shot cannot be taken.
        Texture2D CaptureFeast()
        {
            if (finalBoss == null || finalBoss.corpse == null || pet == null || player == null || player.cam == null) return null;
            Bounds b = default;
            bool any = false;
            foreach (var r in finalBoss.corpse.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) return null;
            float radius = Mathf.Max(Mathf.Max(b.extents.x, b.extents.z), 1f);
            var corpseAt = new Vector3(b.center.x, b.min.y, b.center.z);
            // The dragon stands on the hero's side of the corpse.
            var towards = player.Pos - corpseAt;
            towards.y = 0f;
            towards = towards.sqrMagnitude < 0.01f ? Vector3.back : towards.normalized;

            // Nothing else may appear in the picture: hide every other foe (and the hero if he is seated on the dragon).
            var hidden = new List<Renderer>();
            foreach (var e in enemies)
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                foreach (var r in e.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
            }
            if (pet.Mounted)
                foreach (var r in player.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }

            System.Action putBack = null;
            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                putBack = pet.StageFeast(corpseAt, radius, towards, terrain);
                if (putBack == null) return null;
                // Framing: the subject runs along the dragon-corpse axis (tail to the corpse's far edge); the camera sits
                // beside it, a little toward the corpse's end so it sees the dragon's face, and low (14 degrees up).
                float len = pet.BodyLength;
                float span = len + 1.3f * radius;
                float height = Mathf.Max(len * 0.4f, radius * 1.2f);
                var mid = corpseAt + towards * ((len - 0.7f * radius) * 0.5f) + Vector3.up * (height * 0.4f);
                var axis = -towards;
                var side = Vector3.Cross(Vector3.up, axis).normalized;
                var flat = (side * 0.9f + axis * 0.35f).normalized;
                const float ELEV = 14f * Mathf.Deg2Rad;
                int w = Mathf.Clamp(Screen.width, 16, 2560), h = Mathf.Clamp(Screen.height, 16, 1440);
                float vFov = 40f;
                float vHalf = vFov * 0.5f * Mathf.Deg2Rad;
                float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * w / h);
                // Back off until the length fits the width and the height fits the height (10% margin).
                float dist = Mathf.Max(span * 0.55f / Mathf.Tan(hHalf), height * 0.6f / Mathf.Tan(vHalf));
                var camPos = mid + (flat * Mathf.Cos(ELEV) + Vector3.up * Mathf.Sin(ELEV)) * dist;
                camPos.y = Mathf.Max(camPos.y, terrain.SurfaceHeight(camPos.x, camPos.z, camPos.y + 5f) + 1.2f);

                camGo = new GameObject("FeastCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(player.cam);
                cam.enabled = false;
                cam.fieldOfView = vFov;
                cam.nearClipPlane = 0.3f;
                cam.layerCullDistances = player.cam.layerCullDistances;
                camGo.transform.position = camPos;
                camGo.transform.rotation = Quaternion.LookRotation(mid - camPos, Vector3.up);
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prev;
                return tex;
            }
            catch
            {
                if (tex) Destroy(tex);
                throw;
            }
            finally
            {
                if (camGo) Destroy(camGo);
                if (rt) { rt.Release(); Destroy(rt); }
                foreach (var r in hidden) if (r) r.enabled = true;
                putBack?.Invoke();
            }
        }

        /// After the feast: free the captive dragon if there is one, otherwise the way onward opens.
        void AfterFinalBoss(string captiveId, Vector3 at, bool anyLocked)
        {
            if (captiveId != "")
            {
                DragonCaptive.Spawn(this, captiveId, at);
                hud.Banner($"{Stage.finalBoss} held a dragon captive!", $"Walk up to the {Data.DRAGONS[captiveId].name} to break its chains");
                return;
            }
            hud.Banner($"{Stage.finalBoss} has fallen!", anyLocked ? "No captive this time..." : "The way onward opens");
            Tw.After(4f, StageCleared);
        }

        /// The captive dragon broke free and joined you: it is unlocked and saved, then the stage ends.
        public void FreeDragon(string dragonId)
        {
            if (ended) return;
            if (Save.UnlockDragon(dragonId)) Save.rescues_found++;
            Save.Write();
            rescued.Add(dragonId);
            Sfx.Play("levelup", -2f, 0f);
            FX.Vfx("buff", player.Pos, 1.2f, 2f, null, 0f);
            hud.Banner($"The {Data.DRAGONS[dragonId].name} is free!", "Freed from the warlord's chains: it is yours to ride");
            Tw.After(3f, StageCleared);
        }

        /// After a stage's final boss: on to the next stage through a portal, or victory after the last.
        void StageCleared()
        {
            if (ended || traveling || !running) return;
            if (stage >= Data.STAGE_COUNT)
            {
                Victory();
                return;
            }
            traveling = true;
            var next = Data.STAGES[stage];
            hud.Banner("A portal opens", $"Onward to {next.name}");
            FX.Vfx("portal_blue", player.Pos + new Vector3(0, 1.5f, 0), 2.2f, 3f, null, 0f);
            FX.Vfx("buff", player.Pos, 1.5f, 2.5f, null, 0f);
            Sfx.Play("whoosh", 2f, 0f);
            Tw.After(1.6f, () =>
            {
                hud.FadeTo(1f, 0.6f);
                Tw.After(0.7f, () =>
                {
                    if (this == null || ended) return;
                    BuildStage(stage + 1);
                    rerolls = Data.REROLLS_PER_WORLD;
                    player.OnStageStart();
                    WriteRunSave();
                    hud.FadeTo(0f, 0.8f);
                    FX.Vfx("portal_blue", player.Pos + new Vector3(0, 1.5f, 0), 2.2f, 2.5f, null, 0f);
                    hud.Banner($"Stage {stage} — {Stage.name}", Stage.intro);
                    traveling = false;
                    Tw.After(3.8f, DragonGrowthBanner);
                });
            });
        }

        /// The dragon grows each world: say what it can do now.
        void DragonGrowthBanner()
        {
            if (this == null || ended || !running) return;
            string age = Data.DRAGON_AGE[Mathf.Clamp(stage - 1, 0, Data.DRAGON_AGE.Length - 1)];
            if (stage < Data.FLIGHT_STAGE) hud.Banner($"Your dragon is a {age}", "It walks at your side and breathes fire; its wings come with the next world");
            else
            {
                // It grew by devouring the last world's warlord (the still after his defeat).
                string fed = Data.STAGES[Mathf.Clamp(stage - 2, 0, Data.STAGES.Length - 1)].finalBoss.Split(',')[0];
                if (stage < Data.GUARDIAN_WINGS_STAGE) hud.Banner($"Your dragon has grown: {age}", $"Fed by {fed}'s fall, it can carry you now (Q)");
                else hud.Banner($"Your dragon has grown: {age}", $"Fed by {fed}'s fall: Guardian Wings at 30% health, and unlimited flight (Q)");
            }
        }

        /// Replaces the world with a new stage's land (30% bigger maps, more islands, the stage's scenery) and puts the hero
        /// at its center. Everything from the last stage is cleared away.
        void BuildStage(int n)
        {
            stage = Mathf.Clamp(n, 1, Data.STAGE_COUNT);
            foreach (var e in enemies) if (e) Destroy(e.gameObject);
            enemies.Clear();
            bosses.Clear();
            foreach (var g in gems) if (g.node) Destroy(g.node.gameObject);
            gems.Clear();
            foreach (var p in FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Include)) Destroy(p.gameObject);
            foreach (var cap in FindObjectsByType<DragonCaptive>(FindObjectsInactive.Include)) Destroy(cap.gameObject);
            if (beacon) Destroy(beacon.gameObject);
            beacon = null;
            if (FX.root)
                for (int i = FX.root.childCount - 1; i >= 0; i--) Destroy(FX.root.GetChild(i).gameObject);
            // Spell objects (wells, meteors, clouds...) live under the spellbook; its own aura rings have no scripts.
            for (int i = spellbook.transform.childCount - 1; i >= 0; i--)
            {
                var c = spellbook.transform.GetChild(i);
                if (c.GetComponent<MonoBehaviour>() != null) Destroy(c.gameObject);
            }
            if (terrain) Destroy(terrain.gameObject);
            BuildTerrain();
            timeElapsed = 0f;
            spawnAccum = 0f;
            wave = 1;
            miniSpawned = false;
            finalStarted = false;
            finalDefeated = false;
            finalBoss = null;
            miniBoss = null;
            player.Teleport(StartPos);
            pet.Pos = player.Pos + new Vector3(3, 3, -1);
            SetSky();
            Atmosphere(0f);
        }

        void Victory()
        {
            if (ended) return;
            ended = true;
            running = false;
            Time.timeScale = 1f;
            GameInput.MouseCaptured = false;
            AwardMetaXp(true);
            bool best = Save.RecordRun(totalTime, kills, player.level, stage, true);
            hud.ShowEnd(true, Save.last_score, best);
        }

        /// Pause menu: keeps the run in the save file and returns to the title screen (Continue resumes it).
        public void SaveAndQuit()
        {
            WriteRunSave();
            Restart(false);
        }

        public void Restart(bool toHeroSelect, bool resume = false)
        {
            skipMenu = toHeroSelect;
            continueRun = resume;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---------------------------------------------------------------- sky

        /// The sky slowly burns toward dusk as the stage goes on (k = 0..1), in the stage's colors.
        void Atmosphere(float k)
        {
            var st = Stage;
            var top = Color.Lerp(st.skyTop, new Color(0.22f, 0.1f, 0.12f), k * 0.8f);
            var horizon = Color.Lerp(st.skyHorizon, new Color(0.85f, 0.35f, 0.15f), k * 0.8f);
            var groundHorizon = new Color(0.5f, 0.45f, 0.4f);
            var groundBottom = new Color(0.12f, 0.1f, 0.09f);
            // Warmer haze under the sunset skybox so the distance blends into it.
            var baseFog = packSky && stage == 1 ? new Color(0.8f, 0.58f, 0.44f) : st.fog;
            var fog = Color.Lerp(baseFog, new Color(0.55f, 0.25f, 0.15f), k * 0.8f);
            var sunCol = Color.Lerp(st.sunColor, new Color(1f, 0.55f, 0.3f), k * 0.8f);
            float pitch = Mathf.Lerp(st.sunPitch, -14f, k);
            // Godot rotated the light by (pitch, -40) with the light shining down its -Z; Unity lights shine down +Z.
            sun.transform.rotation = Quaternion.Euler(-pitch, -40f + 180f, 0f);
            sun.color = sunCol;
            sun.intensity = stage == 3 ? 1.15f : 1.25f;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = st.fogDensity;
            RenderSettings.ambientSkyColor = M.Mul(top, 0.9f);
            RenderSettings.ambientEquatorColor = M.Mul(horizon, 0.75f);
            RenderSettings.ambientGroundColor = M.Mul(groundHorizon, 0.45f);
            if (packSky)
            {
                // Burn the stage's pack sky toward a red dusk as the stage goes on.
                if (skyMat.HasProperty("_Tint")) skyMat.SetColor("_Tint", Color.Lerp(skyTint, new Color(0.62f, 0.34f, 0.28f, skyTint.a), k * (stage == 1 ? 1f : 0.6f)));
                if (skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", Mathf.Lerp(skyExposure, skyExposure * 0.7f, k));
            }
            else
            {
                FX.SetLin(skyMat, "_TopColor", top);
                FX.SetLin(skyMat, "_HorizonColor", horizon);
                FX.SetLin(skyMat, "_GroundHorizonColor", groundHorizon);
                FX.SetLin(skyMat, "_GroundBottomColor", groundBottom);
                FX.SetLin(skyMat, "_SunColor", sunCol);
                skyMat.SetVector("_SunDir", -sun.transform.forward);
            }
            UpdateAmbientProbe(M.Mul(top, 0.9f), M.Mul(horizon, 0.75f), M.Mul(groundHorizon, 0.45f));
        }

        static void UpdateAmbientProbe(Color sky, Color equator, Color ground)
        {
            var sh = new SphericalHarmonicsL2();
            var s = sky.linear;
            var e = equator.linear;
            var g = ground.linear;
            sh.AddAmbientLight(e * 0.75f);
            sh.AddDirectionalLight(Vector3.up, (s - e) * 0.9f + new Color(0, 0, 0, 0), 0.5f);
            sh.AddDirectionalLight(Vector3.down, g * 0.6f, 0.4f);
            RenderSettings.ambientProbe = sh;
        }
    }
}
