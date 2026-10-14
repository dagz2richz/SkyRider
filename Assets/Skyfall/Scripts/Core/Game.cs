using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Skyfall
{
    /// Run manager: world setup, 8-minute wave timer, spawning, bosses, XP, level-ups, sky.
    /// Put this on one GameObject in an otherwise empty scene; it builds everything else from code.
    public class Game : MonoBehaviour
    {
        const int MAX_ENEMIES = 320;
        const int MAX_GEMS = 450;
        const int MAX_COINS = 180;

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
        class Coin
        {
            public Transform node;
            public int value;
            public bool pulled;
        }
        readonly List<Coin> coins = new List<Coin>();
        Mesh coinMesh;
        Material coinMat, coinRimMat;
        /// Gold picked up this run; banked into Save.gold when the run ends.
        public int runGold;
        public readonly List<Enemy> bosses = new List<Enemy>();
        public bool running;
        public bool ended;
        public float timeElapsed;
        float spawnAccum;
        public int wave = 1;
        public int bossesSpawned;
        public int bossesSlain;
        /// True once the timer hit 0:00: the field is cleared and the Mountain King waits on the floating island.
        public bool finalStarted;
        bool miniSpawned;
        Enemy miniBoss;
        IslandBeacon beacon;
        /// Dragon that hatched from the final boss's egg this run (for the end screen), or empty.
        public string hatchedDragon = "";
        public int kills;
        public float damageDealt;
        public float biggestHit;
        float shakeAmt;
        public bool leveling;
        public bool freezePreview;
        readonly Dictionary<int, Mesh> gemMeshes = new Dictionary<int, Mesh>();
        readonly Dictionary<int, Material> gemMats = new Dictionary<int, Material>();

        /// "Ride Again" goes straight to the hero select screen.
        public static bool skipMenu;

        Enemy finalBoss;
        bool finalDefeated;
        bool paused;

        /// True while the game is paused (pause menu, stats screen, level-up choice).
        public bool Paused => paused;

        public void SetPaused(bool p)
        {
            paused = p;
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
            terrain = new GameObject("Terrain").AddComponent<WorldTerrain>();
            terrain.transform.SetParent(transform, false);
            terrain.Generate(Random.Range(0, int.MaxValue));
            hud = gameObject.AddComponent<Hud>();
            hud.game = this;
            // Preview dragon on the title screen.
            player = new GameObject("Player").AddComponent<Player>();
            player.transform.SetParent(transform, false);
            player.Pos = new Vector3(0, terrain.Height(0, 0), 0);
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
            if (skipMenu)
            {
                skipMenu = false;
                hud.ShowHeroSelect();
            }
            else hud.ShowMainMenu();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            Time.timeScale = 1f;
            foreach (var m in gemMats.Values) if (m) Destroy(m);
            if (coinMat) Destroy(coinMat);
            if (coinRimMat) Destroy(coinRimMat);
            if (skyMat) Destroy(skyMat);
            if (volume && volume.profile) Destroy(volume.profile);
        }

        void SetupEnvironment()
        {
            // Sky: the Fantasy Skybox pack when set up, else the built-in gradient sky.
            packSky = Skins.Lib != null && Skins.Lib.skybox != null;
            skyMat = packSky ? new Material(Skins.Lib.skybox) : new Material(Shader.Find("Skyfall/Sky"));
            if (packSky)
            {
                skyTint = skyMat.HasProperty("_Tint") ? skyMat.GetColor("_Tint") : Color.gray;
                skyExposure = skyMat.HasProperty("_Exposure") ? skyMat.GetFloat("_Exposure") : 1f;
            }
            RenderSettings.skybox = skyMat;
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

            Atmosphere(0f);
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
            coinMesh = MeshGen.Cylinder(0.5f, 0.5f, 14);
            coinMat = FX.NewLit(new Color(1f, 0.88f, 0.3f), 0.3f, 0f, 0.9f);
            coinRimMat = FX.NewLit(new Color(0.9f, 0.62f, 0.1f), 0.4f, 0f, 0.5f);
        }

        // ---------------------------------------------------------------- run flow

        public void StartRun(string spell, string passive, string hero = "knight", string dragon = "blue")
        {
            player.SetupLoadout(spell, passive, hero, dragon);
            Save.last_spell = spell;
            Save.last_passive = passive;
            Save.last_hero = hero;
            Save.last_dragon = dragon;
            Save.Write();
            running = true;
            Time.timeScale = Save.game_speed;
            player.camDistTarget = 9f;
            player.camPitch = 0.32f;
            GameInput.MouseCaptured = true;
            hud.ShowHud();
            hud.Banner($"Wave 1 — {Data.WAVE_NAMES[0]}", "Survive. A champion comes at 4:00; at 0:00 the Mountain King awaits.");
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
            timeElapsed += delta;
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
            int newWave = Mathf.Min((int)(timeElapsed / Data.WAVE_LENGTH) + 1, Data.WAVE_NAMES.Length);
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
                hud.Banner($"Wave {wave} — {Data.WAVE_NAMES[wave - 1]}", "");
            }
        }

        void Spawn(float delta)
        {
            if (finalStarted) return;
            float t = timeElapsed;
            float rate = (1f + t * 0.045f) * Data.SPAWN_RATE_MULT;
            int cap = Mathf.Min(MAX_ENEMIES, 60 + (int)(t * 0.55f));
            spawnAccum += rate * delta;
            while (spawnAccum >= 1f)
            {
                spawnAccum -= 1f;
                if (enemies.Count >= cap) continue;
                var weights = Data.WAVE_WEIGHTS[wave - 1];
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
                float a = M.RandF() * M.TAU;
                float d = M.RandRange(34f, 52f);
                var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                SpawnEnemy(kind, p);
            }
        }

        /// Spawns an ogre on the terrain, or (with fromY, e.g. an island's top) on the surface at or below that height.
        public Enemy SpawnEnemy(string kind, Vector3 p, float fromY = float.NegativeInfinity)
        {
            float lim = WorldTerrain.PLAY_LIMIT;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            float t = timeElapsed;
            var e = Enemy.Make(this, kind, (1f + t / 60f * 0.38f) * Data.ENEMY_STAT_MULT, (1f + t / 600f) * Data.ENEMY_STAT_MULT);
            e.transform.SetParent(transform, false);
            e.Pos = new Vector3(p.x, terrain.SurfaceHeight(p.x, p.z, fromY), p.z);
            enemies.Add(e);
            return e;
        }

        /// The mini boss: arrives near the player, like the old champions. Killing him does not end the run.
        void SpawnBoss(int idx)
        {
            float a = M.RandF() * M.TAU;
            var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 40f;
            var e = Enemy.MakeBoss(this, idx, Data.BOSS_STAT_MULT, Data.BOSS_STAT_MULT);
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

        /// 0:00: every ogre is swept away (no kills, no XP), spawning stops and the Mountain King appears on the big
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
                    FX.Burst(e.Pos + new Vector3(0, e.size, 0), new Color(0.6f, 0.6f, 0.65f), 8, 4f * e.size, 0.3f * e.size);
                    FX.Vfx("dust_puff", e.Pos + new Vector3(0, 0.3f, 0), 0.35f * Mathf.Max(0.7f, e.size), 1.4f, null, 0.05f);
                }
                e.gameObject.SetActive(false);
            }
            bosses.Clear();
            miniBoss = null;
            Sfx.Play("death", player.Pos, -2f, 0f);

            var c = terrain.BossIslandCenter;
            var isl = terrain.bossIsland;
            var b = Enemy.MakeBoss(this, Data.FINAL_BOSS, Data.BOSS_STAT_MULT, Data.BOSS_STAT_MULT);
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
            hud.Banner("FINAL BATTLE", "The Mountain King awaits on the floating island");
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
            beacon.SetIntensity(finalStarted ? 1f : Mathf.Lerp(0.15f, 0.45f, 1f - left / 30f));
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
                else if (!e.isBoss && Vector3.Distance(e.Pos, player.Pos) > 90f && !e.OnFloatingIsland())
                {
                    float a = M.RandF() * M.TAU;
                    var p = player.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 45f;
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

        public Enemy NearestEnemy(Vector3 pos, float maxDist, float maxDy = 6f, Enemy exclude = null, List<Enemy> excludeList = null)
        {
            Enemy best = null;
            float bd = maxDist * maxDist;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.dead || e == exclude) continue;
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

        public List<Enemy> EnemiesInRadius(Vector3 pos, float r, float maxDy = 4f)
        {
            var result = new List<Enemy>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.dead) continue;
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
                if (e == null) continue;
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

        public Enemy RandomEnemyNear(Vector3 pos, float r)
        {
            cands.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.dead && (e.Pos - pos).sqrMagnitude < r * r)
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
                e.HitPop();
                FX.Number(e.Pos + new Vector3(0, 2.2f * e.size, 0), amount, crit ? new Color(1f, 0.85f, 0.2f) : (color ?? Color.white), crit);
            }
            if (e.hp <= 0f) Kill(e);
        }

        void Kill(Enemy e)
        {
            e.dead = true;
            kills++;
            var pos = e.Pos;
            FX.Burst(pos + new Vector3(0, e.size, 0), new Color(0.45f, 0.5f, 0.3f), 10, 5f * e.size, 0.3f * e.size);
            Sfx.Play("death", pos, -4f + e.size * 2f, 0.15f);
            if (e.isBoss)
            {
                bosses.Remove(e);
                bossesSlain++;
                Shake(1f);
                FX.Flash(pos + new Vector3(0, 3, 0), 10f, new Color(1f, 0.8f, 0.4f, 0.8f), 0.8f);
                for (int i = 0; i < 10; i++)
                {
                    var gp = pos + new Vector3(M.RandRange(-4f, 4f), 0, M.RandRange(-4f, 4f));
                    gp.y = terrain.SurfaceHeight(gp.x, gp.z, pos.y);
                    DropGem(gp, e.xpValue / 10f * 3f);
                }
                if (e != finalBoss) DropGoldPile(pos, Data.MINI_BOSS_GOLD, 10, e.size);
                player.Heal(player.maxHp * 0.4f);
                hud.Banner($"{e.displayName} has fallen!", "");
                Sfx.Play("roar", 0f, 0f);
                if (e == finalBoss) FinalBossDown(pos);
            }
            else
            {
                DropGem(pos, e.xpValue);
                if (Data.OGRES.TryGetValue(e.kind, out var od)) DropCoin(pos + new Vector3(M.RandRange(-0.9f, 0.9f), 0, M.RandRange(-0.9f, 0.9f)), od.gold);
                // Random health orbs; bigger ogres drop them more often.
                float orbChance = Data.ORB_CHANCE * (0.6f + e.size * 0.6f);
                if (e.lanceMarked) orbChance += Data.WEAPONS["blood_lance"].orb_bonus;
                if (M.RandF() < orbChance) DropOrb(pos);
            }
            FX.Vfx("dust_puff", pos + new Vector3(0, 0.3f, 0), 0.35f * Mathf.Max(0.7f, e.size), 1.6f, null, 0.25f);
            e.gameObject.SetActive(false);
        }

        public void Shake(float amount) => shakeAmt = Mathf.Max(shakeAmt, amount);

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
                gems[Random.Range(0, gems.Count)].value += value;
                return;
            }
            int tier = 1;
            foreach (int t in new[] { 50, 10, 3 })
                if (value >= t)
                {
                    tier = t;
                    break;
                }
            var r = FX.Part(transform, gemMeshes[tier], gemMats[tier], Vector3.zero, new Vector3(0.6f, 0.75f, 0.6f) * (tier >= 10 ? 1.8f : 1f));
            FX.NoShadow(r);
            r.gameObject.name = "Gem";
            r.transform.position = pos + new Vector3(0, 0.6f, 0);
            gems.Add(new Gem { node = r.transform, value = value });
        }

        /// A small spinning gold coin that magnets to the player. Nearby loose coins merge into one so counts stay sane.
        void DropCoin(Vector3 pos, int value, bool pulled = false)
        {
            if (value <= 0 && !pulled) return;
            if (value > 0)
            {
                for (int i = coins.Count - 1; i >= 0 && i >= coins.Count - 40; i--)
                {
                    var c = coins[i];
                    if (c.pulled || c.value >= 8 || (c.node.position - pos).sqrMagnitude > 1.4f) continue;
                    c.value += value;
                    SizeCoin(c);
                    return;
                }
                if (coins.Count >= MAX_COINS)
                {
                    var c = coins[Random.Range(0, coins.Count)];
                    c.value += value;
                    SizeCoin(c);
                    return;
                }
            }
            var n = new GameObject("Coin").transform;
            n.SetParent(transform, false);
            FX.NoShadow(FX.Part(n, coinMesh, coinRimMat, Vector3.zero, new Vector3(1f, 0.14f, 1f), new Vector3(M.PI * 0.5f, 0, 0)));
            FX.NoShadow(FX.Part(n, coinMesh, coinMat, Vector3.zero, new Vector3(0.72f, 0.2f, 0.72f), new Vector3(M.PI * 0.5f, 0, 0)));
            n.position = pos + new Vector3(0, 0.9f, 0);
            var coin = new Coin { node = n, value = value, pulled = pulled };
            SizeCoin(coin);
            coins.Add(coin);
        }

        static void SizeCoin(Coin c)
        {
            float s = c.value >= 50 ? 1.3f : (c.value >= 8 ? 0.95f : (c.value >= 4 ? 0.75f : 0.6f));
            c.node.localScale = Vector3.one * s;
        }

        /// A big pile of gold: count coins worth value in total, scattered around pos on the surface.
        void DropGoldPile(Vector3 pos, int value, int count, float spread, bool pulled = false)
        {
            for (int i = 0; i < count; i++)
            {
                var gp = pos + new Vector3(M.RandRange(-spread, spread), 0, M.RandRange(-spread, spread));
                gp.y = terrain.SurfaceHeight(gp.x, gp.z, pos.y);
                DropCoin(gp + new Vector3(0, 0.4f, 0), value / count + (i < value % count ? 1 : 0), pulled);
            }
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
                if (g.pulled) node.position += to.normalized * Mathf.Min(d, delta * (18f + 30f / Mathf.Max(d, 0.5f)));
                else node.rotation = Quaternion.Euler(0, spin, 0);
                if (d < 1.6f)
                {
                    if (g.orb)
                    {
                        player.Heal(player.maxHp * g.value);
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
            TickCoins(delta, c, mag, spin);
        }

        void TickCoins(float delta, Vector3 c, float mag, float spin)
        {
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                var g = coins[i];
                var node = g.node;
                var to = c - node.position;
                float d = to.magnitude;
                if (!g.pulled && d < mag) g.pulled = true;
                if (g.pulled) node.position += to.normalized * Mathf.Min(d, delta * (18f + 30f / Mathf.Max(d, 0.5f)));
                node.rotation = Quaternion.Euler(0, spin + i * 40f, 0);
                if (d < 1.6f)
                {
                    runGold += g.value;
                    if (g.value > 0) Sfx.Play("coin", g.value >= 8 ? -6f : -11f, 0.12f);
                    Destroy(node.gameObject);
                    coins.RemoveAt(i);
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
        }

        /// Level-ups alternate between ability upgrades and stat buckets, with a random chance to swap.
        void LevelUp()
        {
            leveling = true;
            player.pendingLevels--;
            string kind = nextLevelKind;
            if (M.RandF() < Data.LEVEL_SWAP_CHANCE) kind = kind == "ability" ? "stats" : "ability";
            nextLevelKind = nextLevelKind == "ability" ? "stats" : "ability";
            var options = kind == "ability" ? AbilityOptions() : new List<LevelOption>();
            if (options.Count == 0)
            {
                kind = "stats";
                options = BucketOptions();
            }
            SetPaused(true);
            GameInput.MouseCaptured = false;
            Sfx.Play("levelup", -3f, 0f);
            FX.Vfx("buff", player.Pos, 1f, 2f, null, 0f);
            hud.ShowLevelUp(player.level, options, kind);
        }

        static List<LevelOption> Pick3(List<LevelOption> pool)
        {
            M.Shuffle(pool);
            return pool.GetRange(0, Mathf.Min(3, pool.Count));
        }

        /// Ability upgrades: passive, spell, dragon breath and dragon signature (weapons are upgraded in the shop).
        List<LevelOption> AbilityOptions()
        {
            var pool = new List<LevelOption>();
            var p = player;
            int pl = p.spells.TryGetValue(p.passiveId, out int v1) ? v1 : 1;
            if (pl < Data.MAX_LEVEL)
            {
                var ps = Data.PASSIVES[p.passiveId];
                pool.Add(new LevelOption { type = "spell", id = p.passiveId, name = ps.name, level = pl + 1, desc = ps.up, tag = "PASSIVE", color = ps.color });
            }
            int sl = p.spells.TryGetValue(p.spellId, out int v2) ? v2 : 1;
            if (sl < Data.MAX_LEVEL)
            {
                var sp = Data.SPELLS[p.spellId];
                pool.Add(new LevelOption { type = "spell", id = p.spellId, name = sp.name, level = sl + 1, desc = sp.ups[sl], tag = "SPELL", color = sp.color });
            }
            var dd = Data.DRAGONS[p.dragonId];
            int bl = p.spells.TryGetValue("dragonfire", out int v3) ? v3 : 1;
            if (bl < Data.MAX_LEVEL)
                pool.Add(new LevelOption { type = "spell", id = "dragonfire", name = dd.name + " Breath", level = bl + 1, desc = Data.DRAGONFIRE_UP,
                    tag = "DRAGON · AUTO", color = M.Lighten(dd.body, 0.3f) });
            int gl = p.spells.TryGetValue("ride", out int v4) ? v4 : 1;
            if (gl < Data.MAX_LEVEL)
                pool.Add(new LevelOption { type = "spell", id = "ride", name = dd.sig, level = gl + 1, desc = Data.SIGNATURE_UP,
                    tag = "DRAGON · SIGNATURE", color = dd.breath });
            return Pick3(pool);
        }

        /// Stat buckets: pick one, gain all three of its stats.
        List<LevelOption> BucketOptions()
        {
            var pool = new List<LevelOption>();
            foreach (var id in Data.BUCKET_ORDER)
            {
                var bk = Data.BUCKETS[id];
                pool.Add(new LevelOption { type = "bucket", id = id, name = bk.name, level = player.B(id) + 1,
                    desc = string.Join("\n", bk.stats), tag = "STATS", color = bk.color });
            }
            return Pick3(pool);
        }

        public void ChooseOption(LevelOption o)
        {
            switch (o.type)
            {
                case "spell": player.spells[o.id] = o.level; break;
                case "bucket": player.AddBucket(o.id); break;
            }
            Sfx.Play("click", -4f, 0f);
            SetPaused(false);
            GameInput.MouseCaptured = true;
            leveling = false;
            hud.RefreshAbilities();
        }

        // ---------------------------------------------------------------- endings

        public void OnPlayerDied()
        {
            if (ended) return;
            ended = true;
            running = false;
            Time.timeScale = 1f;
            GameInput.MouseCaptured = false;
            Save.gold += runGold;
            bool best = Save.RecordRun(timeElapsed, kills, player.level, false);
            hud.ShowEnd(false, Save.last_score, best);
        }

        /// The Mountain King has fallen: he drops one egg for a dragon the player does not own yet.
        void FinalBossDown(Vector3 pos)
        {
            finalDefeated = true;
            if (beacon) beacon.Extinguish();
            var locked = new List<string>();
            foreach (var id in Data.DRAGON_ORDER)
                if (!Save.IsDragonUnlocked(id)) locked.Add(id);
            if (locked.Count == 0)
            {
                GrantBossReward();
                Tw.After(1.5f, Victory);
                return;
            }
            string pick = locked[Random.Range(0, locked.Count)];
            DragonEgg.Spawn(this, pick, pos + new Vector3(0, 0.2f, 0));
            hud.Banner($"{Data.BOSSES[Data.FINAL_BOSS].name} has fallen!", "A dragon egg glows where he stood");
        }

        /// The egg was picked up: the dragon is unlocked and saved, then the run ends in victory.
        public void CollectEgg(string dragonId)
        {
            if (ended) return;
            Save.UnlockDragon(dragonId);
            hatchedDragon = dragonId;
            Sfx.Play("levelup", -2f, 0f);
            FX.Vfx("buff", player.Pos, 1.2f, 2f, null, 0f);
            hud.Banner($"You found a {Data.DRAGONS[dragonId].name} egg!", "It hatches in your care");
            Tw.After(1.5f, Victory);
        }

        /// Reward for beating the Mountain King when every dragon is already owned (no egg drops).
        public void GrantBossReward()
        {
            runGold += Data.FINAL_BOSS_GOLD;
            hud.Banner("The Mountain King's hoard!", $"+{Data.FINAL_BOSS_GOLD} gold");
            Sfx.Play("buy", -4f, 0f);
            DropGoldPile(finalBoss ? finalBoss.Pos : player.Pos, 0, 24, 5f, true);
        }

        void Victory()
        {
            if (ended) return;
            ended = true;
            running = false;
            Time.timeScale = 1f;
            GameInput.MouseCaptured = false;
            Save.gold += runGold;
            bool best = Save.RecordRun(timeElapsed, kills, player.level, true);
            hud.ShowEnd(true, Save.last_score, best);
        }

        public void Restart(bool toHeroSelect)
        {
            skipMenu = toHeroSelect;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---------------------------------------------------------------- sky

        /// The sky slowly burns toward an ashen dusk as the run goes on (k = 0..1).
        void Atmosphere(float k)
        {
            var top = Color.Lerp(new Color(0.28f, 0.38f, 0.52f), new Color(0.22f, 0.1f, 0.12f), k);
            var horizon = Color.Lerp(new Color(0.72f, 0.64f, 0.55f), new Color(0.85f, 0.35f, 0.15f), k);
            var groundHorizon = new Color(0.5f, 0.45f, 0.4f);
            var groundBottom = new Color(0.12f, 0.1f, 0.09f);
            // Warmer haze under the sunset skybox so the distance blends into it.
            var fog = Color.Lerp(packSky ? new Color(0.8f, 0.58f, 0.44f) : new Color(0.62f, 0.58f, 0.52f), new Color(0.55f, 0.25f, 0.15f), k);
            var sunCol = Color.Lerp(new Color(1f, 0.9f, 0.78f), new Color(1f, 0.55f, 0.3f), k);
            float pitch = Mathf.Lerp(-38f, -14f, k);
            // Godot rotated the light by (pitch, -40) with the light shining down its -Z; Unity lights shine down +Z.
            sun.transform.rotation = Quaternion.Euler(-pitch, -40f + 180f, 0f);
            sun.color = sunCol;
            RenderSettings.fogColor = fog;
            RenderSettings.ambientSkyColor = M.Mul(top, 0.9f);
            RenderSettings.ambientEquatorColor = M.Mul(horizon, 0.75f);
            RenderSettings.ambientGroundColor = M.Mul(groundHorizon, 0.45f);
            if (packSky)
            {
                // Burn the pack sky toward a red dusk.
                if (skyMat.HasProperty("_Tint")) skyMat.SetColor("_Tint", Color.Lerp(skyTint, new Color(0.62f, 0.34f, 0.28f, skyTint.a), k));
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
