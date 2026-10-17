using System.Collections.Generic;
using UnityEngine;
using static Skyfall.UiKit;

namespace Skyfall
{
    /// All 2D UI: main menu, save slots, high scores, hero/dragon select, loadout, the permanent talent trees, settings,
    /// quit screen, in-run HUD, level-up cards (abilities, stats, dragon, talents, armor), pause, stats and end screen.
    public partial class Hud : MonoBehaviour
    {
        public Game game;

        enum Menu
        {
            None, Main, HeroSelect, Loadout, Settings, Quit, Farewell, Talents, Slots,
            Dev,   // DEV
        }
        Menu menu = Menu.None;
        bool hudVisible;
        bool controlsOpen, pauseOpen, statsOpen, endOpen;
        List<Game.LevelOption> levelOptions;
        int levelShown;
        string levelKind = "ability";
        bool endWon, endBest;
        int endScore;

        // Banner / toast fade state.
        string bannerTitle = "", bannerSub = "";
        float bannerT = 999f;
        string toastText = "";
        float toastT = 999f;
        float hurtA;

        string bossName = "";
        float bossFrac;

        string pickHero = "", pickDragon = "", pickSpell = "", pickPassive = "";

        // Talent trees: which tree is shown (an index into TreeKeys; changes are queued for after layout), and where Back returns to.
        int talentHero;
        /// Every permanent tree: the heroes' (hero XP), then the dragons' (bond XP).
        static readonly string[] TreeKeys = BuildTreeKeys();
        static string[] BuildTreeKeys()
        {
            var keys = new List<string>();
            foreach (var h in Data.HERO_ORDER) keys.Add(Save.HeroTree(h));
            foreach (var d in Data.DRAGON_ORDER) keys.Add(Save.DragonTree(d));
            return keys.ToArray();
        }
        // Save slots: the slot waiting on a delete confirmation (-1 for none).
        int confirmDelete = -1;
        int pendingTab = -1;
        Menu talentReturn = Menu.Main;

        // Full-screen fade (the stage portal).
        float fadeA, fadeTarget, fadeSpeed = 1f;

        // Gamepad: selection changes queued by cards (run at the start of the next Update, never inside an OnGUI pass).
        readonly Queue<System.Action> deferred = new Queue<System.Action>();

        void Awake()
        {
            UiNav.Reset();
            UiNav.DefaultKeyFor = DefaultFocusKey;
        }

        void OnDestroy()
        {
            GameInput.StopRumble();
            FreeStill();
        }

        /// The topmost screen that takes menu input, or null during play.
        string TopScreen()
        {
            if (game == null) return null;
            if (controlsOpen) return "controls";
            switch (menu)
            {
                case Menu.Main: return "main";
                case Menu.HeroSelect: return "hero";
                case Menu.Loadout: return "loadout";
                case Menu.Settings: return "settings";
                case Menu.Talents: return "talents";
                case Menu.Slots: return "slots";
                case Menu.Dev: return "dev";   // DEV
                case Menu.Quit: return "quit";
                case Menu.Farewell: return null;
            }
            if (endOpen) return "end";
            if (statsOpen) return "stats";
            if (levelOptions != null) return "level";
            if (pauseOpen) return "pause";
            return null;
        }

        /// The element the gamepad focus starts on when a screen opens.
        string DefaultFocusKey(string screen)
        {
            switch (screen)
            {
                case "main": return Save.HasRun ? "Continue Run#0" : "New Run#0";
                case "hero": return "Continue#0";
                case "loadout": return "Ride Forth#0";
                case "settings": return "Volume#0";
                case "talents": return "Back#0";
                case "slots": return "Back#0";
                case "dev": return "Back#0";   // DEV
                case "quit": return "Stay#0";
                case "controls": return "Back#0";
                case "pause": return "Resume#0";
                case "stats": return "Close#0";
                case "level": return "Choose#0";
                case "end": return "Ride Again#0";
            }
            return null;
        }

        /// Set by the automated tests: a fake screen name that takes gamepad input, and what Back / the tab buttons do on it.
        public static string TestTop;
        public static System.Action TestBack;

        /// B / East on each screen.
        void GamepadBack()
        {
            if (TestTop != null) { TestBack?.Invoke(); return; }
            switch (TopScreen())
            {
                case "controls": controlsOpen = false; break;
                case "hero": ShowMainMenu(); break;
                case "loadout": ShowHeroSelect(); break;
                case "talents": LeaveTalents(); break;
                case "slots": ShowMainMenu(); break;
                case "dev": ShowMainMenu(); break;   // DEV
                case "settings": Save.Write(); ShowMainMenu(); break;
                case "quit": StayFromQuit(); break;
                case "pause": TogglePause(); break;
                case "stats": ToggleStats(); break;
            }
        }

        /// LB / RB on the talent trees switch trees.
        void GamepadTab(int dir)
        {
            if (TopScreen() == "talents") pendingTab = (talentHero + (dir > 0 ? 1 : TreeKeys.Length - 1)) % TreeKeys.Length;
        }

        void StayFromQuit()
        {
            if (game.running)
            {
                menu = Menu.None;
                pauseOpen = true;
            }
            else if (game.ended) game.Restart(false);   // the finished run's state (ended, mounted, kills...) must not leak into a new run
            else ShowMainMenu();
        }

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            bannerT += udt;
            fadeA = Mathf.MoveTowards(fadeA, fadeTarget, udt * fadeSpeed);
            while (deferred.Count > 0) deferred.Dequeue()();
            if (pendingTab >= 0)
            {
                talentHero = pendingTab;
                pendingTab = -1;
            }
            toastT += udt;
            splashT += udt;
            hurtA = Mathf.MoveTowards(hurtA, 0f, Time.deltaTime * 1.5f);
            GameInput.TickRumble();
            if (game == null) return;
            UiNav.Tick(TestTop ?? TopScreen(), udt, GamepadBack, GamepadTab);

            if (stillOpen)
            {
                TickStill(udt);
                return;
            }
            if (GameInput.StatsPressed && game.running && !game.leveling && menu == Menu.None)
            {
                ToggleStats();
                return;
            }
            if (GameInput.PausePressed && game.running && !game.leveling && menu == Menu.None)
            {
                if (controlsOpen) controlsOpen = false;
                else if (statsOpen) ToggleStats();
                else TogglePause();
            }
            if (levelOptions != null)
            {
                int idx = GameInput.CardPressed;
                if (idx >= 0 && idx < levelOptions.Count) Choose(levelOptions[idx]);
                else if (GameInput.RerollPressed) game.Reroll(levelShown, levelKind);
            }
        }

        // ---------------------------------------------------------------- public API

        public void ShowMainMenu()
        {
            menu = Menu.Main;
            GameInput.MouseCaptured = false;
        }

        public void ShowHeroSelect()
        {
            menu = Menu.HeroSelect;
            GameInput.MouseCaptured = false;
            if (pickHero == "")
            {
                pickHero = Data.HEROES.ContainsKey(Save.last_hero) ? Save.last_hero : "knight";
                pickDragon = Data.DRAGONS.ContainsKey(Save.last_dragon) && Save.IsDragonUnlocked(Save.last_dragon) ? Save.last_dragon : "blue";
            }
            // Preview the picks on the menu backdrop.
            game.player.SetupLoadout(game.player.spellId, game.player.passiveId, pickHero, pickDragon);
        }

        /// Opens the talent trees from the main menu or the hero select screen; Back returns there.
        void ShowTalents(Menu from, string hero = null)
        {
            talentReturn = from;
            menu = Menu.Talents;
            if (hero != null) talentHero = Mathf.Max(0, System.Array.IndexOf(TreeKeys, Save.HeroTree(hero)));
            GameInput.MouseCaptured = false;
        }

        void LeaveTalents()
        {
            if (talentReturn == Menu.Dev) { menu = Menu.Dev; return; }   // DEV
            if (talentReturn == Menu.HeroSelect) ShowHeroSelect();
            else ShowMainMenu();
        }

        /// Fades the screen to black (1) or back (0) over dur seconds of real time.
        public void FadeTo(float a, float dur)
        {
            fadeTarget = Mathf.Clamp01(a);
            fadeSpeed = 1f / Mathf.Max(0.01f, dur);
        }

        void ShowLoadout()
        {
            menu = Menu.Loadout;
            GameInput.MouseCaptured = false;
            if (pickSpell == "")
            {
                pickSpell = Data.SPELLS.ContainsKey(Save.last_spell) ? Save.last_spell : "frost_bolt";
                pickPassive = Data.PASSIVES.ContainsKey(Save.last_passive) ? Save.last_passive : "lightning_storm";
            }
        }

        public void ShowHud()
        {
            menu = Menu.None;
            hudVisible = true;
            RefreshAbilities();
        }

        public void SetBoss(string n, float frac)
        {
            bossName = n;
            bossFrac = frac;
        }

        public void Banner(string title, string sub)
        {
            bannerTitle = title;
            bannerSub = sub;
            bannerT = 0f;
        }

        string splashTitle = "", splashSub = "";
        Color splashColor = Color.white;
        float splashT = 999f;

        /// A big warning in the middle of the screen (harpoon hits): pops in, holds 1.5 s, fades out.
        public void Splash(string title, string sub, Color color)
        {
            splashTitle = title;
            splashSub = sub;
            splashColor = color;
            splashT = 0f;
        }

        void DrawSplash()
        {
            float a = splashT < 0.12f ? splashT / 0.12f : (splashT < 1.6f ? 1f : 1f - (splashT - 1.6f) / 0.5f);
            if (a <= 0f || splashTitle == "") return;
            // A quick pop: starts 30% bigger and settles.
            int size = Mathf.RoundToInt(64f * (1f + 0.3f * Mathf.Clamp01(1f - splashT / 0.15f)));
            float y = H / 2f - 90f;
            DrawText(new Rect(W / 2 - 500, y, 1000, 80), splashTitle, Text(size, TextAnchor.MiddleCenter, false, true), splashColor, 10, a);
            DrawText(new Rect(W / 2 - 500, y + 78, 1000, 34), splashSub, Text(26, TextAnchor.UpperCenter), PARCHMENT, 6, a);
        }

        // ---------------------------------------------------------------- cinematic still

        bool stillOpen;
        Texture2D stillTex;
        string stillCaption = "";
        float stillT;
        float stillEnd = -1f;       // stillT when the fade-out started (-1 while it is still showing)
        bool stillHadPause, stillOwned;
        System.Action stillDone;
        const float STILL_FADE = 0.9f, STILL_HOLD = 6f;

        /// True while any menu, title, pause, stats, level-up, end or still screen is up (gameplay keys do nothing then).
        public bool MenuOpen => menu != Menu.None || controlsOpen || pauseOpen || statsOpen || endOpen || levelOptions != null || stillOpen;

        /// A full-screen still with a caption: fades in, holds, fades out (any key, click or gamepad button skips to the
        /// fade-out). Gameplay is paused meanwhile; done() runs when it is gone.
        public void ShowStill(Texture2D tex, string caption, System.Action done, bool ownsTexture = false)
        {
            FreeStill();
            stillTex = tex;
            stillOwned = ownsTexture;
            stillCaption = caption;
            stillDone = done;
            stillT = 0f;
            stillEnd = -1f;
            stillOpen = true;
            stillHadPause = game.Paused;
            game.SetPaused(true);
            Sfx.Play("roar", 0f, 0f);
        }

        /// Drops the picture; a captured one (owned by the HUD) is destroyed with it.
        void FreeStill()
        {
            if (stillOwned && stillTex) Destroy(stillTex);
            stillTex = null;
            stillOwned = false;
        }

        void TickStill(float udt)
        {
            stillT += udt;
            if (stillEnd < 0f)
            {
                // Input is ignored for the first moment so a key held during the boss fight does not skip it at once.
                if (stillT > 0.6f && GameInput.AnyPressed) stillEnd = Mathf.Min(stillT, STILL_FADE + STILL_HOLD);
                else if (stillT >= STILL_FADE + STILL_HOLD) stillEnd = stillT;
            }
            if (stillEnd >= 0f && stillT - stillEnd >= STILL_FADE)
            {
                stillOpen = false;
                FreeStill();
                if (!stillHadPause) game.SetPaused(false);
                var done = stillDone;
                stillDone = null;
                done?.Invoke();
            }
        }

        void DrawStill()
        {
            float a = Mathf.Clamp01(stillT / STILL_FADE);
            if (stillEnd >= 0f) a = Mathf.Min(a, 1f - Mathf.Clamp01((stillT - stillEnd) / STILL_FADE));
            UiKit.Rect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, a));
            if (stillTex != null)
            {
                // Fill the screen without stretching: the picture is scaled to cover and the overflow is cropped.
                float k = Mathf.Max(W / stillTex.width, H / stillTex.height);
                float w = stillTex.width * k, h = stillTex.height * k;
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(new Rect((W - w) / 2f, (H - h) / 2f, w, h), stillTex, ScaleMode.StretchToFill);
                GUI.color = prev;
            }
            // Caption on a dark band at the bottom.
            UiKit.Rect(new Rect(0, H - 150, W, 150), new Color(0f, 0f, 0f, 0.55f * a));
            DrawText(new Rect(W / 2 - 800, H - 130, 1600, 70), stillCaption, Text(34, TextAnchor.MiddleCenter, false, true), PARCHMENT, 8, a);
            DrawText(new Rect(W / 2 - 400, H - 50, 800, 30), "Press any key to continue", Text(16, TextAnchor.MiddleCenter), M.Darken(PARCHMENT, 0.3f), 3, a * 0.8f);
        }

        public void Toast(string text)
        {
            toastText = text;
            toastT = 0f;
        }

        public void HurtFlash() => hurtA = 0.28f;

        public void RefreshAbilities() { }

        public void ShowLevelUp(int level, List<Game.LevelOption> options, string kind)
        {
            levelOptions = options;
            levelShown = level;
            levelKind = kind;
            bannerT = 999f;
        }

        void Choose(Game.LevelOption o)
        {
            if (levelOptions == null) return;
            levelOptions = null;
            game.ChooseOption(o);
        }

        public void ShowEnd(bool won, int score, bool best)
        {
            hudVisible = false;
            pauseOpen = false;
            statsOpen = false;
            levelOptions = null;
            bannerT = 999f;
            endOpen = true;
            endWon = won;
            endScore = score;
            endBest = best;
            if (best) Sfx.Play("levelup", 0f, 0f);
        }

        void TogglePause()
        {
            if (pauseOpen)
            {
                pauseOpen = false;
                game.SetPaused(false);
                GameInput.MouseCaptured = true;
                return;
            }
            pauseOpen = true;
            game.SetPaused(true);
            GameInput.MouseCaptured = false;
        }

        void ToggleStats()
        {
            if (statsOpen)
            {
                statsOpen = false;
                if (!pauseOpen)
                {
                    game.SetPaused(false);
                    GameInput.MouseCaptured = true;
                }
                return;
            }
            statsOpen = true;
            game.SetPaused(true);
            GameInput.MouseCaptured = false;
        }

        void ShowQuit()
        {
            pauseOpen = false;
            menu = Menu.Quit;
            GameInput.MouseCaptured = false;
        }

        void Farewell()
        {
            menu = Menu.Farewell;
            Save.Write();
            Tw.After(1.4f, () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, true);
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            if (game == null) return;
            BeginScaled();
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) UiNav.BeginFrame(TopScreen());
            if (hudVisible) DrawHud();
            DrawFloatingTexts();
            DrawBannerAndToast();
            if (hudVisible && levelOptions == null && !pauseOpen) DrawSplash();
            if (levelOptions != null) { UiNav.Layer = "level"; DrawLevelUp(); }
            if (pauseOpen) { UiNav.Layer = "pause"; DrawPause(); }
            if (statsOpen) { UiNav.Layer = "stats"; DrawStats(); }
            if (endOpen) { UiNav.Layer = "end"; DrawEnd(); }
            switch (menu)
            {
                case Menu.Main: UiNav.Layer = "main"; DrawMainMenu(); break;
                case Menu.HeroSelect: UiNav.Layer = "hero"; DrawHeroSelect(); break;
                case Menu.Loadout: UiNav.Layer = "loadout"; DrawLoadout(); break;
                case Menu.Settings: UiNav.Layer = "settings"; DrawSettings(); break;
                case Menu.Talents: UiNav.Layer = "talents"; DrawTalents(); break;
                case Menu.Slots: UiNav.Layer = "slots"; DrawSlotsMenu(); break;
                case Menu.Dev: UiNav.Layer = "dev"; DrawDevMenu(); break;   // DEV
                case Menu.Quit: UiNav.Layer = "quit"; DrawQuit(); break;
                case Menu.Farewell: DrawFarewell(); break;
            }
            if (controlsOpen) { UiNav.Layer = "controls"; DrawControls(); }
            if (stillOpen) DrawStill();
            if (fadeA > 0.001f) UiKit.Rect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, fadeA));
            if (repaint)
            {
                DrawFocus();
                UiNav.EndFrame();
            }
        }

        static string Clock(float seconds)
        {
            int s = Mathf.Max(0, (int)seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        // ---------------------------------------------------------------- HUD

        static readonly string[] SLOT_KEYS = { "WEAPON", "DEFENSE", "PASSIVE", "SPELL", "BREATH", "E" };

        void DrawHud()
        {
            var p = game.player;
            if (hurtA > 0.001f) UiKit.Rect(new Rect(0, 0, W, H), new Color(0.7f, 0f, 0f, hurtA));

            // XP bar along the top.
            Bar(new Rect(10, 8, W - 20, 14), p.xp / p.xpNext, new Color(0.3f, 0.75f, 1f));
            DrawText(new Rect(14, 26, 200, 26), $"Lv {p.level}", Text(20), GOLD);
            DrawText(new Rect(88, 26, 300, 26), $"World {game.stage} / {Data.STAGE_COUNT}", Text(18, TextAnchor.UpperLeft, false, true), new Color(0.75f, 0.85f, 1f));

            // Timer + wave at top center.
            float left = Mathf.Max(0f, Data.RUN_LENGTH - game.timeElapsed);
            var center = Text(44, TextAnchor.UpperCenter, false, true);
            var st = game.Stage;
            if (game.traveling) DrawText(new Rect(W / 2 - 300, 26, 600, 52), "The Portal", center, new Color(0.6f, 0.8f, 1f), 8);
            else if (game.finalStarted) DrawText(new Rect(W / 2 - 300, 26, 600, 52), "Final Battle", center, new Color(1f, 0.45f, 0.3f), 8);
            else DrawText(new Rect(W / 2 - 300, 26, 600, 52), Clock(left), center, PARCHMENT, 8);
            string waveText;
            string bossShort = st.finalBoss.Split(',')[0];
            if (game.traveling) waveText = "Traveling to the next world...";
            else if (game.finalStarted) waveText = $"Defeat {st.finalBoss}";
            else if (!game.miniSpawned) waveText = $"Wave {game.wave} / {st.waves.Length} · Boss in {Mathf.CeilToInt(Data.MINI_BOSS_TIME - game.timeElapsed)}s";
            else waveText = $"Wave {game.wave} / {st.waves.Length} · {bossShort} in {Mathf.CeilToInt(left)}s";
            DrawText(new Rect(W / 2 - 300, 80, 600, 24), waveText, Text(18, TextAnchor.UpperCenter), GOLD);
            if (bossName != "")
            {
                DrawText(new Rect(W / 2 - 300, 108, 600, 28), bossName, Text(22, TextAnchor.UpperCenter, false, true), new Color(1f, 0.45f, 0.35f));
                Bar(new Rect(W / 2 - 300, 138, 600, 20), bossFrac, new Color(0.75f, 0.1f, 0.08f));
            }

            // Kills, foes and FPS in the top right (the character stats live on the Stats screen).
            string stats = $"Kills  {game.kills}\nFoes  {game.enemies.Count}\nFPS  {Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f) * Time.timeScale)}";
            DrawText(new Rect(W - 276, 28, 260, 80), stats, Text(16, TextAnchor.UpperRight), M.Darken(PARCHMENT, 0.15f), 5);

            // Stat bucket ranks on the right.
            float y = H / 2 - 180;
            foreach (var id in Data.BUCKET_ORDER)
            {
                int lv = p.B(id);
                if (lv <= 0) continue;
                var bk = Data.BUCKETS[id];
                string pips = lv <= 5 ? Repeat("◆", lv) : $"◆ x{lv}";
                DrawText(new Rect(W - 286, y, 270, 22), $"{bk.name}  {pips}", Text(16, TextAnchor.UpperRight), Color.Lerp(bk.color, PARCHMENT, 0.3f), 5);
                y += 22;
            }

            // Health, mana and dragon stamina at bottom-left.
            float bx = 20, bw = 400, by = H - 20;
            by -= 10; DrawFlightBar(p, bx, by, bw);
            by -= 5 + 16; Bar(new Rect(bx, by, bw, 16), p.mana / p.MaxMana, new Color(0.45f, 0.35f, 1f));
            by -= 5 + 20; DrawText(new Rect(bx, by, bw, 20), $"Mana {(int)p.mana} / {(int)p.MaxMana}", Text(16), new Color(0.75f, 0.7f, 1f));
            by -= 5 + 22; Bar(new Rect(bx, by, bw, 22), p.hp / p.maxHp, new Color(0.8f, 0.12f, 0.1f));
            DrawFormTile(p, bx + bw + 18, by - 4);
            // Talent barriers show as a pale band over the health bar.
            if (p.barrier > 0.5f) UiKit.Rect(new Rect(bx + 2, by + 2, (bw - 4) * Mathf.Clamp01(p.barrier / p.maxHp), 6), new Color(0.75f, 0.88f, 1f, 0.9f));
            by -= 5 + 22; DrawText(new Rect(bx, by, bw, 22), $"{Mathf.CeilToInt(p.hp)} / {(int)p.maxHp}" + (p.barrier > 0.5f ? $"   +{Mathf.CeilToInt(p.barrier)} barrier" : ""), Text(18), PARCHMENT);

            DrawPowerUps(p);
            DrawSlots();
            DrawIslandMarker();
        }

        /// The capstone form (F) as an icon tile beside the health bar, once its talent is learned: a cooldown sweep
        /// while it recharges, a glowing frame and the seconds left while it is active.
        void DrawFormTile(Player p, float x, float healthY)
        {
            string id = p.Has("k_dragonform") ? "k_dragonform" : p.Has("h_specter") ? "h_specter" : p.Has("p_phoenixform") ? "p_phoenixform" : null;
            if (id == null || Event.current.type != EventType.Repaint) return;
            string name = id == "k_dragonform" ? "Dragon Form" : id == "h_specter" ? "Crimson Specter" : "Phoenix Form";
            var color = id == "k_dragonform" ? new Color(0.45f, 0.7f, 1f) : id == "h_specter" ? new Color(0.95f, 0.2f, 0.25f) : new Color(1f, 0.6f, 0.2f);
            const float sz = 62;
            var r = new Rect(x, healthY + 26 - sz, sz, sz);
            bool active = p.DragonFormActive;
            bool ready = !active && p.dragonFormCd <= 0f;
            if (active || ready) UiKit.Rect(new Rect(r.x - 3, r.y - 3, sz + 6, sz + 6), M.WithA(color, active ? 0.55f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f) : 0.35f));
            DrawIcon(r, id, Color.Lerp(color, GOLD, 0.35f), 1f, false);
            if (!active && p.dragonFormCd > 0f)
            {
                float frac = Mathf.Clamp01(p.dragonFormCd / Player.DRAGON_FORM_CD);
                UiKit.Rect(new Rect(r.x + 3, r.y + 3 + (sz - 6) * (1f - frac), sz - 6, (sz - 6) * frac), new Color(0, 0, 0, 0.62f));
                DrawText(r, Mathf.CeilToInt(p.dragonFormCd).ToString(), Text(22, TextAnchor.MiddleCenter, false, true), PARCHMENT, 6);
            }
            if (active) DrawText(r, Mathf.CeilToInt(p.dragonFormT) + " s",Text(22, TextAnchor.MiddleCenter, false, true), Color.white, 6);
            DrawText(new Rect(r.x, r.y - 16, sz, 14), GameInput.PadConnected ? "F / LB" : "F", Text(11, TextAnchor.UpperCenter, false, true), M.WithA(GOLD, 0.9f), 3);
            DrawText(new Rect(r.x - 20, r.yMax + 4, sz + 40, 16), name, Text(12, TextAnchor.UpperCenter), ready || active ? new Color(1f, 0.85f, 0.45f) : M.Darken(PARCHMENT, 0.1f), 3);
        }

        /// The flight bar: dimmed until Q can call the dragon again (1 s after first touching solid ground).
        void DrawFlightBar(Player p, float bx, float by, float bw)
        {
            var blue = new Color(0.35f, 0.7f, 1f);
            if (!p.CanFly)
            {
                // The hatchling (world 1) can't carry you yet.
                Bar(new Rect(bx + 80, by, bw - 80, 10), 0f, blue);
                DrawText(new Rect(bx, by - 6, 80, 20), "No flight", Text(15), M.Darken(PARCHMENT, 0.35f), 5);
                return;
            }
            if (p.qLockT > 0f)
            {
                // E was just used: Q (mount / dismount) is locked. The bar refills as the lockout runs out.
                var lockCol = new Color(1f, 0.55f, 0.35f);
                Bar(new Rect(bx + 80, by, bw - 80, 10), 1f - p.qLockT / Data.Q_LOCKOUT_AFTER_E, lockCol);
                DrawText(new Rect(bx, by - 6, 80, 20), "Q locked", Text(15), lockCol, 5);
                DrawText(new Rect(bx + bw - 90, by - 6, 90, 20), $"{p.qLockT:0.0}s", Text(13, TextAnchor.MiddleRight), M.Darken(lockCol, 0.1f), 4);
                return;
            }
            bool landing = !p.flying && p.RemountWait > 0f;
            var fill =landing ? new Color(0.22f, 0.4f, 0.6f) : p.UnlimitedFlight ? new Color(1f, 0.8f, 0.35f) : blue;
            Bar(new Rect(bx + 80, by, bw - 80, 10), p.flightStamina / p.MaxFlight, fill);
            string label = landing ? (p.RemountLanded ? "Landing" : "Land!") : p.UnlimitedFlight ? "Unlimited" : "Flight (Q)";
            var labelCol = landing ? new Color(1f, 0.7f, 0.4f) : new Color(0.6f, 0.85f, 1f);
            DrawText(new Rect(bx, by - 6, 80, 20), label, Text(15), labelCol, 5);
            // Flight is unlimited in worlds 2-3 (no time readout); the bar only counts down in the (unreachable) limited case.
            if (!p.UnlimitedFlight)
                DrawText(new Rect(bx + bw - 90, by - 6, 90, 20), $"{Mathf.CeilToInt(p.flightStamina)} / {p.MaxFlight:0}s", Text(13, TextAnchor.MiddleRight), M.Darken(labelCol, 0.1f), 4);
        }

        /// Active power-ups under the level: a glyph chip, the name and the seconds left, with a thin timer bar.
        void DrawPowerUps(Player p)
        {
            float y = 56f;
            foreach (var id in Data.POWERUP_ORDER)
            {
                float left = p.PowerLeft(id);
                if (left <= 0f) continue;
                var d = Data.POWERUPS[id];
                float a = left < 3f ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
                var r = new Rect(14, y, 190, 30);
                Panel(r, INK, d.color, 8, 1, a);
                if (IconTex("pu_" + id)) DrawIcon(new Rect(r.x + 3, r.y + 3, 24, 24), "pu_" + id, d.color, a);
                else
                {
                    UiKit.Rect(new Rect(r.x + 3, r.y + 3, 24, 24), M.WithA(M.Mul(d.color, 0.55f), 0.9f * a));
                    DrawText(new Rect(r.x + 3, r.y + 3, 24, 24), d.glyph, Text(d.glyph.Length > 1 ? 15 : 22, TextAnchor.MiddleCenter, false, true), Color.Lerp(d.color, Color.white, 0.4f), 3, a);
                }
                DrawText(new Rect(r.x + 34, r.y + 2, 110, 18), d.name, Text(15, TextAnchor.UpperLeft, false, true), Color.Lerp(d.color, PARCHMENT, 0.4f), 4, a);
                DrawText(new Rect(r.x + 120, r.y + 2, 64, 18), $"{Mathf.CeilToInt(left)}s", Text(16, TextAnchor.UpperRight, false, true), PARCHMENT, 4, a);
                UiKit.Rect(new Rect(r.x + 34, r.y + 22, 150 * Mathf.Clamp01(left / Data.POWERUP_TIME), 3), M.WithA(d.color, 0.9f * a));
                y += 34f;
            }
        }

        static Texture2D arrowTex;

        static Texture2D ArrowTex
        {
            get
            {
                if (arrowTex) return arrowTex;
                const int N = 64;
                arrowTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Arrow" };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        // Triangle pointing right (+x): tip at the right edge, base on the left.
                        float u = (x + 0.5f) / N, v = (y + 0.5f) / N - 0.5f;
                        float half = (1f - u) * 0.5f;
                        float a = Mathf.Clamp01((half - Mathf.Abs(v)) * N * 0.5f) * (u > 0.06f ? 1f : 0f);
                        px[y * N + x] = new Color(1, 1, 1, a);
                    }
                arrowTex.SetPixels(px);
                arrowTex.Apply();
                return arrowTex;
            }
        }

        /// Screen position (virtual canvas units) and visibility of the island marker; used by the HUD and the tests.
        public bool IslandMarker(out Vector2 pos, out bool onScreen, out float meters, out float angleDeg)
        {
            pos = Vector2.zero;
            onScreen = false;
            meters = 0f;
            angleDeg = 0f;
            var target = game.IslandMarkerPos;
            var cam = game.player ? game.player.cam : null;
            if (M.IsInf(target) || cam == null || game.PlayerOnBossIsland()) return false;
            var flat = game.terrain.BossIslandCenter - game.player.Pos;
            flat.y = 0;
            meters = flat.magnitude;
            float s = Scale;
            var sp = cam.WorldToScreenPoint(target);
            var c = new Vector2(W / 2f, H / 2f);
            var p = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
            const float margin = 60f;
            bool inside = sp.z > 0f && p.x > margin && p.x < W - margin && p.y > margin && p.y < H - margin;
            if (inside)
            {
                pos = p;
                onScreen = true;
                return true;
            }
            // Behind the camera the projection is mirrored; flip it so the arrow points the right way.
            var dir = p - c;
            if (sp.z <= 0f) dir = -dir;
            if (dir.sqrMagnitude < 1f) dir = Vector2.down;
            dir.Normalize();
            float kx = dir.x != 0f ? (W / 2f - margin) / Mathf.Abs(dir.x) : float.MaxValue;
            float ky = dir.y != 0f ? (H / 2f - margin - 20f) / Mathf.Abs(dir.y) : float.MaxValue;
            pos = c + dir * Mathf.Min(kx, ky);
            angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            return true;
        }

        /// From 0:00 on: a diamond with the distance over the island when it is on screen, else an arrow at the screen edge.
        void DrawIslandMarker()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!IslandMarker(out var pos, out bool onScreen, out float meters, out float ang)) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            var col = new Color(1f, 0.82f, 0.4f);
            string label = $"{Mathf.RoundToInt(meters)}m";
            if (onScreen)
            {
                DrawText(new Rect(pos.x - 150, pos.y - 56, 300, 22), game.Stage.islandName, Text(16, TextAnchor.UpperCenter, false, true), col, 6, pulse);
                DrawText(new Rect(pos.x - 150, pos.y - 34, 300, 24), label, Text(20, TextAnchor.UpperCenter, false, true), PARCHMENT, 6, pulse);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(90f, pos);
                var pc = GUI.color;
                GUI.color = new Color(col.r, col.g, col.b, pulse);
                GUI.DrawTexture(new Rect(pos.x - 14, pos.y - 14, 28, 28), ArrowTex);
                GUI.color = pc;
                GUI.matrix = m;
            }
            else
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, pos);
                var pc = GUI.color;
                GUI.color = new Color(col.r, col.g, col.b, pulse);
                GUI.DrawTexture(new Rect(pos.x - 26, pos.y - 26, 52, 52), ArrowTex);
                GUI.color = pc;
                GUI.matrix = m;
                DrawText(new Rect(pos.x - 60, pos.y + 24, 120, 24), label, Text(18, TextAnchor.UpperCenter, false, true), col, 6, pulse);
            }
        }

        /// The action bar: six icon tiles (weapon, defense, passive, spell | dragon breath, signature) with level pips, a
        /// cooldown sweep and the ability's name under each.
        void DrawSlots()
        {
            if (Event.current.type != EventType.Repaint) return;
            var p = game.player;
            const float sz = 62, sep = 14, gap = 26;
            float total = 6 * sz + 5 * sep + gap;
            float x = W / 2 - total / 2;
            float y = H - 46 - sz;
            for (int i = 0; i < 6; i++)
            {
                if (i == 4) x += gap;
                var r = new Rect(x, y, sz, sz);
                SlotInfo(i, out string title, out Color color, out int lvl, out string extra, out float cdFrac);
                bool dim = i == 3 && p.mana < game.spellbook.Cost;
                DrawIcon(r, SlotIcon(i), Color.Lerp(color, GOLD, 0.35f), 1f, dim);
                if (cdFrac > 0f) UiKit.Rect(new Rect(r.x + 3, r.y + 3 + (sz - 6) * (1f - cdFrac), sz - 6, (sz - 6) * cdFrac), new Color(0, 0, 0, 0.62f));
                // Level pips along the bottom edge.
                for (int k = 0; k < lvl && k < Data.SPELL_MAX_LEVEL; k++)
                    UiKit.Rect(new Rect(r.x + 6 + k * 5f, r.yMax - 8, 4, 3), Color.Lerp(color, Color.white, 0.4f));
                string shortTitle = title.Length > 12 ? title.Substring(0, 11).TrimEnd() + "…" : title;
                DrawText(new Rect(r.x - 6, r.yMax + 4, sz + 12, 16), shortTitle, Text(12, TextAnchor.UpperCenter), M.Darken(PARCHMENT, 0.1f), 3, dim ? 0.6f : 1f);
                DrawText(new Rect(r.x - 6, r.yMax + 19, sz + 12, 14), extra, Text(11, TextAnchor.UpperCenter), M.Darken(PARCHMENT, 0.35f), 3);
                DrawText(new Rect(r.x, r.y - 16, sz, 14), SLOT_KEYS[i], Text(10, TextAnchor.UpperCenter), i < 4 ? M.WithA(GOLD, 0.8f) : new Color(0.55f, 0.8f, 1f, 0.8f), 3);
                x += sz + sep;
            }
        }

        string SlotIcon(int i)
        {
            var p = game.player;
            switch (i)
            {
                case 0: return Data.HEROES[p.heroId].weapon;
                case 1: return p.IsKnight ? "def_shield" : (p.IsPyro ? "def_ward" : "def_evasion");
                case 2: return p.passiveId;
                case 3: return p.spellId;
                case 4: return "dragonfire";
                default: return "ride";
            }
        }

        /// The icon name for a level-up option.
        static string OptionIconName(Game.LevelOption o, Player p)
        {
            switch (o.type)
            {
                case "bucket": return "bucket_" + o.id;
                case "talent": return o.id;
                case "armor": return "armor_" + o.armor.slot + (o.armor.tier > 0 ? "_epic" : "");
                default: return o.id == "weapon" ? Data.HEROES[p.heroId].weapon : o.id;
            }
        }

        void OptionIcon(Game.LevelOption o, float size) => IconBox(OptionIconName(o, game.player), size, Color.Lerp(o.color, GOLD, 0.3f));

        void TalentIcon(string id, float size, Color accent) => IconBox(id, size, accent);

        void SlotInfo(int i, out string title, out Color color, out int lvl, out string extra, out float cdFrac)
        {
            var p = game.player;
            cdFrac = 0f;
            extra = "";
            switch (i)
            {
                case 0:
                {
                    var w = p.Weapon;
                    title = w.name; color = w.color; lvl = p.weaponLevel; extra = p.IsHuntress ? (p.Has("h_pact") ? "pact" : "2% hp") : "+mana";
                    return;
                }
                case 1:
                    if (p.IsPyro) { title = Data.WARD.name; color = Data.WARD.color; extra = $"{Mathf.RoundToInt(p.BlockChance * 100)}% ward"; }
                    else if (p.IsKnight) { title = Data.SHIELD.name; color = Data.SHIELD.color; extra = $"{Mathf.RoundToInt(p.BlockChance * 100)}% block"; }
                    else { title = Data.EVASION.name; color = Data.EVASION.color; extra = $"{Mathf.RoundToInt(p.DodgeChance * 100)}% dodge"; }
                    lvl = p.shieldLevel;
                    return;
                case 2:
                {
                    var ps = Data.PASSIVES[p.passiveId];
                    title = ps.name; color = ps.color; lvl = Lv(p.passiveId);
                    return;
                }
                case 3:
                {
                    var sp = Data.SPELLS[p.spellId];
                    bool evo = Lv(p.spellId) >= Data.EVOLVE_LEVEL;
                    title = evo ? sp.evolved : sp.name; color = evo ? sp.evolvedColor : sp.color; lvl = Lv(p.spellId); extra = $"{Mathf.RoundToInt(game.spellbook.Cost)} mp";
                    cdFrac = game.spellbook.CooldownFrac;
                    return;
                }
                case 4:
                {
                    var dd = Data.DRAGONS[p.dragonId];
                    title = dd.name; color = M.Lighten(dd.body, 0.3f); lvl = Lv("dragonfire"); extra = dd.focus.ToLower();
                    cdFrac = game.pet.CommandFrac;
                    return;
                }
                default:
                {
                    var dd = Data.DRAGONS[p.dragonId];
                    title = dd.sig; color = dd.breath; lvl = Lv("ride"); extra = "ground/air";
                    cdFrac = game.pet.RideFrac;
                    if (game.pet.Grounded) { extra = "World 2"; cdFrac = 1f; }   // the hatchling has no signature yet
                    return;
                }
            }
        }

        int Lv(string id) => game.player.spells.TryGetValue(id, out int v) ? v : 1;

        void DrawFloatingTexts()
        {
            var cam = game.player ? game.player.cam : null;
            if (cam == null || Event.current.type != EventType.Repaint) return;
            float s = Scale;
            foreach (var f in FX.texts)
            {
                float k = Tw.Apply(Ease.OutSine, Mathf.Clamp01(f.t / f.riseDur));
                var wp = f.pos + new Vector3(0, f.rise * k, 0);
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0f) continue;
                float a = 1f - Mathf.Clamp01((f.t - f.fadeDelay) / f.fadeDur);
                var st = Text(f.big ? 24 : 18, TextAnchor.MiddleCenter, false, true);
                var r = new Rect(sp.x / s - 80, (Screen.height - sp.y) / s - 14, 160, 28);
                DrawText(r, f.text, st, f.color, 6, a);
            }
        }

        void DrawBannerAndToast()
        {
            // Banner: fade in 0.3 s, hold 2.6 s, fade out 0.8 s.
            float ba = bannerT < 0.3f ? bannerT / 0.3f : (bannerT < 2.9f ? 1f : 1f - (bannerT - 2.9f) / 0.8f);
            if (ba > 0f && bannerTitle != "")
            {
                DrawText(new Rect(W / 2 - 500, 180, 1000, 56), bannerTitle, Text(46, TextAnchor.UpperCenter, false, true), GOLD, 10, ba);
                DrawText(new Rect(W / 2 - 500, 238, 1000, 30), bannerSub, Text(22, TextAnchor.UpperCenter), PARCHMENT, 6, ba);
            }
            // Toast: fade in 0.2 s, hold 1.4 s, fade out 0.6 s.
            float ta = toastT < 0.2f ? toastT / 0.2f : (toastT < 1.6f ? 1f : 1f - (toastT - 1.6f) / 0.6f);
            if (ta > 0f && toastText != "")
                DrawText(new Rect(W / 2 - 400, H - 160, 800, 28), toastText, Text(20, TextAnchor.UpperCenter), PARCHMENT, 6, ta);
        }

        // ---------------------------------------------------------------- main menu

        void Title()
        {
            Label("DRAGONRIDER", 84, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
            Label("S U R V I V O R S", 20, M.WithA(PARCHMENT, 0.85f), 4, TextAnchor.UpperCenter);
            Space(8);
        }

        void DrawMainMenu()
        {
            Overlay(0.35f);
            const float colW = 280f;
            Centered(() =>
            {
                Title();
                Label("Cross three lands, slay their warlords, free the dragons they hold captive.", 17, M.Darken(PARCHMENT, 0.15f), 4, TextAnchor.UpperCenter);
                Space(26);
                CenterRow(() =>
                {
                    GUILayout.BeginVertical(GUILayout.Width(colW));
                    if (Save.HasRun)
                    {
                        var r = Save.run;
                        Button("Continue Run", 24, colW, true, () => game.Restart(false, true));
                        Space(4);
                        Label($"{Data.HEROES[r.hero].name}  ·  Lv {r.level}  ·  World {r.stage}", 13, new Color(0.75f, 0.85f, 1f), 3, TextAnchor.UpperCenter, colW);
                        Space(10);
                    }
                    Button("New Run", 22, colW, true, ShowHeroSelect);
                    Space(8);
                    Button("Talent Trees", 20, colW, true, () => ShowTalents(Menu.Main));
                    Space(8);
                    Button($"Save Slot {Save.slot + 1}", 20, colW, true, () => { confirmDelete = -1; menu = Menu.Slots; });
                    Space(8);
                    Button("Settings", 20, colW, true, () => menu = Menu.Settings);
                    Space(8);
                    Button("Controls", 20, colW, true, () => controlsOpen = true);
                    Space(8);
                    Button("Dev Menu", 18, colW, true, () => menu = Menu.Dev);   // DEV
                    Space(8);   // DEV
                    Button("Quit", 20, colW, true, ShowQuit);
                    GUILayout.EndVertical();
                    Space(36);
                    GUILayout.BeginVertical(PanelStyle(null, null, 12, 1, 18), GUILayout.Width(360));
                    Label("Hall of Legends", 22, GOLD, 5, TextAnchor.UpperLeft, 0, false, true);
                    Space(8);
                    Kv("High score", Save.best_score.ToString(), 16);
                    Kv("Highest level", Save.best_level.ToString(), 16);
                    Kv("Furthest world",$"{Mathf.Max(1, Save.best_stage)} / {Data.STAGE_COUNT}", 16);
                    Kv("Runs  ·  Victories", $"{Save.runs}  ·  {Save.wins}", 16);
                    Space(8);
                    // Dragons owned: one swatch per dragon, dim when still locked.
                    GUILayout.BeginHorizontal();
                    Label("Dragons", 16, PARCHMENT, 5);
                    GUILayout.FlexibleSpace();
                    foreach (var id in Data.DRAGON_ORDER)
                    {
                        var d = Data.DRAGONS[id];
                        var sr = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18), GUILayout.Height(18));
                        bool own = Save.IsDragonUnlocked(id);
                        Panel(new Rect(sr.x, sr.y + 2, 16, 16), own ? M.Lighten(d.body, 0.15f) : new Color(0.15f, 0.13f, 0.12f, 0.9f), own ? M.Lighten(d.breath, 0.4f) : M.WithA(GOLD, 0.25f), 8, 1);
                        Space(4);
                    }
                    GUILayout.EndHorizontal();
                    if (Save.history.Count > 0)
                    {
                        Space(10);
                        Label("Recent", 14, M.Darken(GOLD, 0.15f), 3);
                        int n = 0;
                        foreach (var h in Save.history)
                        {
                            if (n++ >= 3) break;
                            Label($"{h.score,7}   Lv {h.level}   World {Mathf.Max(1, h.stage)}{(h.won ? "Victory" : Clock(h.time))}", 13, M.Darken(PARCHMENT, 0.2f), 3);
                        }
                    }
                    GUILayout.EndVertical();
                });
                Space(28);
                Label("Everything attacks on its own   ·   Q fly   ·   E dragon signature   ·   F capstone talent   ·   Tab stats", 14, M.Darken(PARCHMENT, 0.35f), 3, TextAnchor.UpperCenter);
            });
        }

        void Kv(string k, string val, int size = 18)
        {
            GUILayout.BeginHorizontal();
            Label(k, size, PARCHMENT, 6);
            GUILayout.FlexibleSpace();
            Label(val, size, GOLD, 6);
            GUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- hero & dragon select

        void SelectCard(string title, string desc, List<string> lines, Color c, bool sel, float width, System.Action onPick = null, string icon = null)
        {
            var bg = sel ? new Color(0.16f, 0.12f, 0.06f, 0.92f) : INK;
            var border = sel ? c : M.Darken(GOLD, 0.45f);
            GUILayout.BeginVertical(PanelStyle(bg, border, 10, sel ? 2 : 1), GUILayout.Width(width));
            GUILayout.BeginHorizontal();
            if (icon != null && IconTex(icon)) IconBox(icon, 40, c, onPick == null);
            else
            {
                var sw = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(26));
                UiKit.Rect(new Rect(sw.x, sw.y + 6, 14, 14), c);
            }
            Space(8);
            Label(title, 22, Color.Lerp(c, PARCHMENT, 0.3f), 6, TextAnchor.UpperLeft, 0, false, true);
            GUILayout.FlexibleSpace();
            if (sel) Label("SELECTED", 13, Color.Lerp(c, PARCHMENT, 0.3f), 3);
            GUILayout.EndHorizontal();
            Space(5);
            Label(desc, 14, PARCHMENT, 6, TextAnchor.UpperLeft, width - 30, true);
            foreach (var ln in lines)
            {
                Space(5);
                Label(ln, 14, Color.Lerp(GOLD, PARCHMENT, 0.4f), 3, TextAnchor.UpperLeft, width - 30, true);
            }
            GUILayout.EndVertical();
            Clickable(GUILayoutUtility.GetLastRect(), "card:" + title, onPick == null ? null : () => deferred.Enqueue(onPick));
        }

        void DrawHeroSelect()
        {
            Overlay(0.4f);
            float heroW = Mathf.Min(420f, (W - 80f) / 3f - 18f);
            float dragonW = Mathf.Min(300f, (W - 80f) / Data.DRAGON_ORDER.Length - 16f);
            Centered(() =>
            {
                Label("Choose Your Hero", 44, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                CenterRow(() =>
                {
                    foreach (var id in Data.HERO_ORDER)
                    {
                        var h = Data.HEROES[id];
                        var w = Data.WEAPONS[h.weapon];
                        string def = h.defense == "shield" ? $"Shield ({Mathf.RoundToInt(100f * Data.SHIELD.baseChance)}% block)"
                            : (h.defense == "evasion" ? $"Evasion ({Mathf.RoundToInt(100f * Data.EVASION.baseChance)}% dodge)" : $"Flame Ward ({Mathf.RoundToInt(100f * Data.WARD.baseChance)}% ward)");
                        var lines = new List<string> { $"Weapon:  {w.name}", $"Defense:  {def}", $"Health:  {(int)h.hp}" };
                        switch (id)
                        {
                            case "knight": lines.Add("Capstone talent: Dragon Form"); break;
                            case "huntress": lines.Add("Javelins pierce and bleed; cost 2% current health, no mana"); break;
                            case "pyromancer": lines.Add("Staff bolts burst and ignite; ward sets attackers on fire"); break;
                        }
                        string hid = id;
                        SelectCard(h.name, h.desc, lines, h.color, id == pickHero, heroW, () => { pickHero = hid; ShowHeroSelect(); }, "hero_" + id);
                        Space(18);
                    }
                });
                Space(18);
                Label("Choose Your Dragon", 36, GOLD, 8, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                CenterRow(() =>
                {
                    foreach (var id in Data.DRAGON_ORDER)
                    {
                        var d = Data.DRAGONS[id];
                        var lines = new List<string> { $"Focus:  {d.focus}", $"Signature (E):  {d.sig}", d.sig_desc };
                        if (d.damage > 1f) lines.Insert(1, $"Damage:  {d.damage:0.00}x");
                        bool owned = Save.IsDragonUnlocked(id);
                        if (!owned)
                        {
                            // Locked: dimmed, with the hint, and not selectable.
                            var prev = GUI.color;
                            GUI.color = new Color(1f, 1f, 1f, 0.45f);
                            SelectCard(d.name, "Locked. A world's final boss may hold it captive: defeat one to set it free.", new List<string>(), Color.Lerp(d.body, Color.gray, 0.6f), false, dragonW, null, "dragon_" + id);
                            GUI.color = prev;
                        }
                        else
                        {
                            string did = id;
                            SelectCard(d.name, d.desc, lines, M.Lighten(d.body, 0.25f), id == pickDragon, dragonW, () => { pickDragon = did; ShowHeroSelect(); }, "dragon_" + id);
                        }
                        Space(16);
                    }
                });
                Space(18);
                CenterRow(() =>
                {
                    Button("Back", 22, 0, true, ShowMainMenu);
                    Space(16);
                    Button("Talents", 22, 0, true, () => ShowTalents(Menu.HeroSelect, pickHero));
                    Space(16);
                    Button("Continue", 28, 0, true, ShowLoadout);
                });
            });
        }

        // ---------------------------------------------------------------- loadout

        void DrawLoadout()
        {
            Overlay(0.45f);
            Centered(() =>
            {
                Label("Choose Your Abilities", 44, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                Label($"{Data.HEROES[pickHero].name} with the {Data.DRAGONS[pickDragon].name}.  Everything fights on its own; your spell casts whenever you have the mana.\n" +
                      "In world 1 your dragon is a hatchling: it walks beside you and breathes fire. From world 2 it is big enough to ride and its signature awakens.\n" +
                      "Q (gamepad Y) calls your dragon to carry you: flight is unlimited in worlds 2 and 3. E (RB / X) unleashes the dragon's signature, on the ground or in the air (from the air you leap down and slam; Q is locked for 5s after).\n" +
                      "After leaving your dragon, it can carry you again 1s after you first touch solid ground.\n" +
                      "Every level-up offers a card for your dragon. Talents open at levels 3 and 7; armor every 5 levels. Permanent talent trees grow between runs.",
                    16, M.Darken(PARCHMENT, 0.1f), 4, TextAnchor.UpperCenter);
                Space(18);
                ChoiceRow("Spell  ·  casts itself when you have the mana", Data.SPELL_ORDER, true);
                Space(18);
                ChoiceRow("Passive", Data.PASSIVE_ORDER, false);
                Space(18);
                CenterRow(() =>
                {
                    Button("Back", 22, 0, true, ShowHeroSelect);
                    Space(16);
                    Button("Ride Forth", 28, 0, true, () =>
                    {
                        menu = Menu.None;
                        game.StartRun(pickSpell, pickPassive, pickHero, pickDragon);
                    });
                });
            });
        }

        void ChoiceRow(string heading, string[] ids, bool spells)
        {
            const float sep = 14f;
            float cardW = Mathf.Min(330f, (W - 60f) / ids.Length - sep);
            Label(heading, 19, GOLD, 6);
            Space(4);
            GUILayout.BeginHorizontal();
            foreach (var id in ids)
            {
                string name, desc;
                Color c;
                string extra = null;
                if (spells)
                {
                    var d = Data.SPELLS[id];
                    name = d.name; desc = d.desc; c = d.color;
                    extra = $"{(int)d.cost} mana · {d.cd:0.0}s cooldown";
                }
                else
                {
                    var d = Data.PASSIVES[id];
                    name = d.name; desc = d.desc; c = d.color;
                }
                bool sel = id == (spells ? pickSpell : pickPassive);
                var bg = sel ? new Color(0.16f, 0.12f, 0.06f, 0.95f) : INK;
                var border = sel ? c : M.Darken(GOLD, 0.45f);
                GUILayout.BeginVertical(PanelStyle(bg, border, 10, sel ? 2 : 1), GUILayout.Width(cardW), GUILayout.MinHeight(150));
                GUILayout.BeginHorizontal();
                if (IconTex(id)) { IconBox(id, 36, c); Space(8); }
                Label(name, 20, Color.Lerp(c, PARCHMENT, 0.3f), 6, TextAnchor.UpperLeft, 0, false, true);
                GUILayout.FlexibleSpace();
                if (sel) Label("SELECTED", 13, Color.Lerp(c, PARCHMENT, 0.3f), 3);
                GUILayout.EndHorizontal();
                Space(6);
                Label(desc, 14, PARCHMENT, 6, TextAnchor.UpperLeft, cardW - 30, true);
                GUILayout.FlexibleSpace();
                if (extra != null) Label(extra, 14, new Color(0.7f, 0.65f, 1f), 6);
                GUILayout.EndVertical();
                string pid = id;
                Clickable(GUILayoutUtility.GetLastRect(), (spells ? "spell:" : "passive:") + name, () => deferred.Enqueue(() => { if (spells) pickSpell = pid; else pickPassive = pid; }));
                Space(sep);
            }
            GUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- talent trees

        /// The permanent trees of the active save slot: one per hero (hero XP, earned by playing that hero) and one per
        /// dragon (bond XP, earned by riding it and from its kills). Each row is bought once with XP; then one of its three
        /// talents is picked (swapping is free). Reset refunds everything (free, for testing).
        void DrawTalents()
        {
            Overlay(0.78f);
            string key = TreeKeys[Mathf.Clamp(talentHero, 0, TreeKeys.Length - 1)];
            bool isHero = key.StartsWith("hero:");
            string id = key.Substring(key.IndexOf(':') + 1);
            var rows = Data.Tree(key);
            var state = Save.Tree(key);
            Color accent = isHero ? Data.HEROES[id].color : M.Lighten(Data.DRAGONS[id].body, 0.3f);
            string title = isHero ? Data.HEROES[id].name : Data.DRAGONS[id].name;
            string xpName = isHero ? "hero XP" : "bond XP";
            float cardW = Mathf.Min(340f, (W - 200f) / 3f - 14f);
            Centered(() =>
            {
                Label("Talent Trees", 44, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(8);
                CenterRow(() =>
                {
                    for (int i = 0; i < TreeKeys.Length; i++)
                    {
                        int idx = i;
                        string k = TreeKeys[i];
                        string kid = k.Substring(k.IndexOf(':') + 1);
                        string name = k.StartsWith("hero:") ? Data.HEROES[kid].name : Data.DRAGONS[kid].name;
                        var prev = GUI.color;
                        if (i != talentHero) GUI.color = new Color(0.62f, 0.62f, 0.62f, 1f);
                        Button(name, 16, 0, true, () => pendingTab = idx);
                        GUI.color = prev;
                        if (i < TreeKeys.Length - 1) Space(i == Data.HERO_ORDER.Length - 1 ? 28 : 8);
                    }
                });
                Space(10);
                Label($"{title}   ·   {state.xp} {xpName} to spend   ·   {state.earned} earned", 20, Color.Lerp(accent, PARCHMENT, 0.4f), 6, TextAnchor.UpperCenter);
                if (!isHero && !Save.IsDragonUnlocked(id))
                    Label("You don't own this dragon yet: a world's final boss may hold it captive. Defeat one to set it free.", 14, M.Darken(PARCHMENT, 0.3f), 3, TextAnchor.UpperCenter);
                Space(10);
                for (int r = 0; r < rows.Length; r++)
                {
                    int row = r;
                    bool open = row < state.rows;
                    bool next = row == state.rows;
                    int cost = Data.RowCost(key, row);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginVertical(GUILayout.Width(150));
                    Label($"Row {row + 1}" + (row == rows.Length - 1 ? "  ·  Capstone" : ""), 16, GOLD, 4, TextAnchor.UpperLeft, 150, false, true);
                    bool canBuy = state.xp >= cost;
                    canBuy |= DevMenu.FreeTalents;   // DEV
                    if (open) Label("Open", 13, new Color(0.6f, 0.9f, 0.6f), 3, TextAnchor.UpperLeft, 150);
                    else if (next) Button($"Open · {cost} XP", 14, 150, canBuy, () => Save.BuyRow(key));
                    else Label($"{cost} XP", 13, M.Darken(PARCHMENT, 0.35f), 3, TextAnchor.UpperLeft, 150);
                    GUILayout.EndVertical();
                    foreach (var t in rows[row])
                    {
                        string tid = t.id;
                        bool picked = open && state.picks[row] == tid;
                        bool cap = row == rows.Length - 1;
                        var c = cap ? new Color(1f, 0.75f, 0.3f) : accent;
                        var bg = picked ? new Color(0.16f, 0.12f, 0.06f, 0.95f) : INK;
                        var border = picked ? c : M.WithA(Color.Lerp(c, GOLD, 0.25f), open ? 0.6f : 0.25f);
                        var prev = GUI.color;
                        if (!open) GUI.color = new Color(1f, 1f, 1f, 0.5f);
                        GUILayout.BeginVertical(PanelStyle(bg, border, 8, picked ? 3 : 1, 8), GUILayout.Width(cardW), GUILayout.MinHeight(74));
                        GUILayout.BeginHorizontal();
                        TalentIcon(tid, 28, c);
                        Space(6);
                        Label(t.name, 16, Color.Lerp(c, PARCHMENT, 0.35f), 4, TextAnchor.UpperLeft, cardW - 110, false, true);
                        GUILayout.FlexibleSpace();
                        if (picked) Label("CHOSEN", 11, Color.Lerp(c, PARCHMENT, 0.3f), 3);
                        GUILayout.EndHorizontal();
                        Label(t.desc, 12, M.Darken(PARCHMENT, 0.05f), 3, TextAnchor.UpperLeft, cardW - 20, true);
                        GUILayout.EndVertical();
                        GUI.color = prev;
                        if (open) Clickable(GUILayoutUtility.GetLastRect(), "tal:" + tid, () => deferred.Enqueue(() => Save.Pick(key, row, tid)));
                        Space(8);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    Space(5);
                }
                Space(6);
                if (isHero)
                {
                    var runRows = Data.RUN_TALENTS[id];
                    var names = new List<string>();
                    for (int r = 0; r < runRows.Length; r++)
                    {
                        var opts = new List<string>();
                        foreach (var t in runRows[r]) opts.Add(t.name);
                        names.Add($"Lv {Data.TALENT_LEVELS[r]}: {string.Join(" / ", opts)}");
                    }
                    Label("Picked during a run:   " + string.Join("     ", names), 14, M.Darken(PARCHMENT, 0.2f), 3, TextAnchor.UpperCenter);
                    Space(4);
                }
                Label("Open a row with XP, then click a talent to choose it (you can swap at any time outside a run).   Gamepad: LB / RB switch trees", 14, M.Darken(PARCHMENT, 0.3f), 3, TextAnchor.UpperCenter);
                Space(8);
                CenterRow(() =>
                {
                    Button("Back", 22, 0, true, LeaveTalents);
                    Space(16);
                    Button("Reset Tree (free)", 18, 0, state.rows > 0, () => Save.Respec(key));
                });
            });
        }

        // ---------------------------------------------------------------- save slots

        void DrawSlotsMenu()
        {
            Overlay(0.72f);
            float cardW = Mathf.Min(380f, (W - 120f) / Save.SLOT_COUNT - 20f);
            Centered(() =>
            {
                Label("Save Slots", 48, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(6);
                Label("Each slot keeps its own dragons, records, talent trees and run in progress.", 16, M.Darken(PARCHMENT, 0.15f), 4, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    for (int i = 0; i < Save.SLOT_COUNT; i++)
                    {
                        int si = i;
                        var info = Save.SlotInfo(i);
                        bool active = i == Save.slot;
                        GUILayout.BeginVertical(PanelStyle(active ? new Color(0.16f, 0.12f, 0.06f, 0.95f) : INK, active ? GOLD : M.Darken(GOLD, 0.45f), 10, active ? 3 : 1, 14),
                            GUILayout.Width(cardW), GUILayout.MinHeight(250));
                        Label($"Slot {i + 1}" + (active ? "   ·   IN USE" : ""), 24, GOLD, 6, TextAnchor.UpperLeft, 0, false, true);
                        Space(8);
                        if (info == null) Label("Empty", 18, M.Darken(PARCHMENT, 0.3f), 4);
                        else
                        {
                            Kv("Started", info.created ?? "-", 15);
                            Kv("Runs  ·  Victories", $"{info.runs}  ·  {info.wins}", 15);
                            Kv("High score", info.best_score.ToString(), 15);
                            Kv("Dragons", $"{(info.unlocked_dragons?.Count ?? 1)} / {Data.DRAGON_ORDER.Length}", 15);
                            int spent = 0;
                            foreach (var tr in info.trees ?? new List<Save.TreeState>()) spent += tr.rows;
                            Kv("Talent rows open", spent.ToString(), 15);
                            if (info.run != null && info.run.active) Label("A run is in progress", 13, new Color(0.75f, 0.85f, 1f), 3);
                        }
                        GUILayout.FlexibleSpace();
                        if (!active) Button(info == null ? $"Start Slot {i + 1}" : $"Play Slot {i + 1}", 18, 0, true, () => deferred.Enqueue(() => { Save.UseSlot(si); confirmDelete = -1; RefreshPreview(); }));
                        if (info != null)
                        {
                            Space(6);
                            if (confirmDelete == i) Button($"Really delete Slot {i + 1}?", 16, 0, true, () => deferred.Enqueue(() => { Save.DeleteSlot(si); confirmDelete = -1; RefreshPreview(); }));
                            else Button($"Delete Slot {i + 1}", 16, 0, true, () => confirmDelete = si);
                        }
                        GUILayout.EndVertical();
                        if (i < Save.SLOT_COUNT - 1) Space(20);
                    }
                });
                Space(18);
                CenterRow(() => Button("Back", 22, 0, true, ShowMainMenu));
            });
        }

        /// After switching or wiping a slot: the menu backdrop shows that slot's last hero and dragon.
        void RefreshPreview()
        {
            pickHero = pickDragon = pickSpell = pickPassive = "";
            game.player.SetupLoadout(Save.last_spell, Save.last_passive, Save.last_hero, Save.last_dragon);
        }

        // ---------------------------------------------------------------- settings

        void AdjustVolume(int d)
        {
            Save.volume = Mathf.Clamp01(Mathf.Round((Save.volume + d * 0.05f) * 20f) / 20f);
            Sfx.SetVolume(Save.volume);
            Save.Write();
        }

        void AdjustSpeed(int d) => Save.game_speed = Mathf.Clamp(Mathf.Round((Save.game_speed + d * 0.25f) / 0.25f) * 0.25f, 0.5f, 2f);

        void AdjustSens(int d) => Save.mouse_sens = Mathf.Clamp(Mathf.Round((Save.mouse_sens + d * 0.05f) / 0.05f) * 0.05f, 0.2f, 3f);

        void VolumeRow()
        {
            GUILayout.BeginHorizontal();
            Label("Volume", 20, PARCHMENT, 6, TextAnchor.UpperLeft, 190);
            float v = GUILayout.HorizontalSlider(Save.volume, 0f, 1f, GUILayout.Width(260));
            Label($"{(int)(v * 100)}%", 20, GOLD, 6, TextAnchor.UpperLeft, 70);
            GUILayout.EndHorizontal();
            SliderRow("Volume", AdjustVolume);
            if (!Mathf.Approximately(v, Save.volume))
            {
                Save.volume = v;
                Sfx.SetVolume(v);
            }
            if (Event.current.type == EventType.MouseUp) Save.Write();
        }

        void SpeedRow()
        {
            GUILayout.BeginHorizontal();
            Label("Game speed", 20, PARCHMENT, 6, TextAnchor.UpperLeft, 190);
            float v = GUILayout.HorizontalSlider(Save.game_speed, 0.5f, 2f, GUILayout.Width(260));
            v = Mathf.Round(v / 0.25f) * 0.25f;
            Label($"{v:0.00}x", 20, GOLD, 6, TextAnchor.UpperLeft, 70);
            GUILayout.EndHorizontal();
            SliderRow("Game speed", AdjustSpeed);
            if (!Mathf.Approximately(v, Save.game_speed)) Save.game_speed = v;
        }

        void SensRow()
        {
            GUILayout.BeginHorizontal();
            Label("Look sensitivity", 20, PARCHMENT, 6, TextAnchor.UpperLeft, 190);
            float v = GUILayout.HorizontalSlider(Save.mouse_sens, 0.2f, 3f, GUILayout.Width(260));
            v = Mathf.Round(v / 0.05f) * 0.05f;
            Label($"{v:0.00}x", 20, GOLD, 6, TextAnchor.UpperLeft, 70);
            GUILayout.EndHorizontal();
            SliderRow("Look sensitivity", AdjustSens);
            if (!Mathf.Approximately(v, Save.mouse_sens)) Save.mouse_sens = v;
        }

        void DrawSettings()
        {
            Overlay(0.55f);
            Centered(() =>
            {
                Label("Settings", 54, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                GUILayout.BeginVertical(PanelStyle());
                VolumeRow();
                Space(12);
                SensRow();
                Space(12);
                Label("Master volume for all sounds. Music is coming later.", 14, M.Darken(PARCHMENT, 0.3f), 3);
                GUILayout.EndVertical();
                Space(18);
                CenterRow(() =>
                {
                    Button("Back", 22, 0, true, () =>
                    {
                        Save.Write();
                        ShowMainMenu();
                    });
                });
            });
        }

        static readonly string[,] CONTROLS =
        {
            { "W A S D", "Move (relative to the camera)" },
            { "Mouse", "Look around; orbits all the way, stopping at the ground" },
            { "Scroll wheel", "Zoom in and out" },
            { "Space", "Jump; press again in the air to double jump; hold to climb while flying" },
            { "Q", "Call your dragon and take flight (press again to drop off). Flight is unlimited in worlds 2 and 3; the hatchling of world 1 is too small to ride. After you leave it, Q works again 1s after you touch the ground" },
            { "E", "Dragon signature (from world 2). Riding, you leap down and slam; Q is locked for 5s after E" },
            { "F", "Capstone form once you learn its talent (Dragon Form / Crimson Specter / Phoenix Form)" },
            { "Shift / C", "Dive while flying" },
            { "Tab", "Stats screen" },
            { "Esc / P", "Pause" },
            { "1 / 2 / 3", "Pick a level-up card; R rerolls the cards" },
            { "(automatic)", "Your attacks, spell, passive, defense and dragon breath all fire on their own" },
        };

        static readonly string[,] PAD_CONTROLS =
        {
            { "Left stick", "Move" },
            { "Right stick", "Look around (R-stick click resets the zoom)" },
            { "A / Cross", "Jump, double jump; hold to climb while flying" },
            { "Y / Triangle", "Call your dragon and take flight (press again to drop off); rerolls the level-up cards" },
            { "RB / X", "Dragon signature (from world 2)" },
            { "LB", "Capstone form once you learn its talent (Dragon Form / Crimson Specter / Phoenix Form)" },
            { "LT / B", "Dive while flying" },
            { "D-pad up / down", "Zoom in and out" },
            { "Start / Select", "Pause / Stats screen" },
            { "Menus", "D-pad / stick + A choose · B back · LB / RB talent tree tabs · left / right sliders" },
        };

        void DrawControls()
        {
            Overlay(0.8f);
            Centered(() =>
            {
                Label("Controls", 44, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(8);
                GUILayout.BeginVertical(PanelStyle(null, null, 6, 2, 10));
                Label("Keyboard and mouse", 20, M.Darken(GOLD, 0.1f), 5);
                for (int i = 0; i < CONTROLS.GetLength(0); i++) ControlRow(CONTROLS[i, 0], CONTROLS[i, 1]);
                Space(6);
                Label("Gamepad", 20, M.Darken(GOLD, 0.1f), 5);
                for (int i = 0; i < PAD_CONTROLS.GetLength(0); i++) ControlRow(PAD_CONTROLS[i, 0], PAD_CONTROLS[i, 1]);
                GUILayout.EndVertical();
                Space(10);
                CenterRow(() => Button("Back", 22, 0, true, () => controlsOpen = false));
            });
        }

        static void ControlRow(string key, string text)
        {
            GUILayout.BeginHorizontal();
            Label(key, 18, GOLD, 5, TextAnchor.UpperLeft, 170);
            Space(24);
            Label(text, 17, PARCHMENT, 4, TextAnchor.UpperLeft, 700);
            GUILayout.EndHorizontal();
            Space(3);
        }

        // ---------------------------------------------------------------- quit

        void DrawQuit()
        {
            Overlay(0.75f);
            Centered(() =>
            {
                Label("Leave the kingdom?", 54, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                Label($"Your dragon will wait for your return.\nBest score so far: {Save.best_score}", 20, PARCHMENT, 6, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    Button("Stay", 24, 0, true, StayFromQuit);
                    Space(20);
                    Button("Quit Game", 24, 0, true, Farewell);
                });
            });
        }

        void DrawFarewell()
        {
            Overlay(1f);
            Centered(() => Label("Farewell, Dragon Rider.", 54, GOLD, 10, TextAnchor.UpperCenter, 0, false, true));
        }

        // ---------------------------------------------------------------- level up

        void DrawLevelUp()
        {
            Overlay(0.6f);
            var options = levelOptions;
            Centered(() =>
            {
                Label($"Level {levelShown}", 56, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                string sub;
                switch (levelKind)
                {
                    case "talent": sub = "A talent row opens: choose one. It lasts the whole run."; break;
                    case "armor": sub = "Choose an armor piece: it fills its slot, replacing what you wear there."; break;
                    case "ability": sub = "Choose an ability to upgrade, or feed your dragon."; break;
                    default: sub = "Choose a stat bucket (you gain all of its stats), or feed your dragon."; break;
                }
                Label(sub + $"   (Every level: +{Mathf.RoundToInt(game.player.DamagePerLevel * 100f)}% damage · +1% health · +0.5% parry)", 20, PARCHMENT, 6, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        var o = options[i];
                        var c = o.color;
                        GUILayout.BeginVertical(PanelStyle(INK, Color.Lerp(c, GOLD, 0.3f), 8, 3), GUILayout.Width(330), GUILayout.MinHeight(300));
                        GUILayout.BeginHorizontal();
                        OptionIcon(o, 44);
                        Space(10);
                        GUILayout.BeginVertical();
                        Label($"{o.tag}   [{i + 1}]", 14, c, 5);
                        Label(o.name, 22, Color.Lerp(c, PARCHMENT, 0.35f), 6, TextAnchor.UpperLeft, 240, true, true);
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        Space(8);
                        string lvlText = o.type == "bucket" ? $"Rank {o.level}" : o.type == "talent" ? $"Row {o.level} / {Data.TALENT_LEVELS.Length}"
                            : o.type == "armor" ? $"{Data.ARMOR_TIER_STATS[o.armor.tier]} stats · tier {o.armor.tier + 1} / {Data.ARMOR_TIER_NAMES.Length}"
                            : o.type == "dragon" ? $"Rank {o.level} / {Data.FindDragonCard(o.id).max}"
                            : $"Level {o.level} / {(Data.SPELLS.ContainsKey(o.id) ? Data.SPELL_MAX_LEVEL : Data.MAX_LEVEL)}";
                        Label(lvlText, 16, GOLD, 5);
                        Space(8);
                        Label(o.desc, 16, PARCHMENT, 5, TextAnchor.UpperLeft, 300, true);
                        if (o.type == "armor")
                        {
                            var worn = game.player.ArmorIn(o.armor.slot);
                            Space(6);
                            Label(worn == null ? "Empty slot" : $"Replaces: {Game.ArmorName(worn)}", 13, M.Darken(PARCHMENT, 0.3f), 3, TextAnchor.UpperLeft, 300, true);
                        }
                        GUILayout.FlexibleSpace();
                        bool pick = Button("Choose", 22, 0, true, () => { if (levelOptions == options) Choose(o); });
                        GUILayout.EndVertical();
                        if (pick) GUIUtility.ExitGUI();
                        if (i < options.Count - 1) Space(20);
                    }
                });
                // Rerolls: a fresh set of the same kind of cards, five per world.
                Space(18);
                bool canReroll = game.CanReroll(levelKind);
                string rerollText = levelKind == "talent" ? "Talent rows can't be rerolled" : $"Reroll  [R / Y]   ·   {game.rerolls} left in this world";
                CenterRow(() =>
                {
                    bool rr = Button(rerollText, 18, 0, canReroll, () => { if (levelOptions == options) game.Reroll(levelShown, levelKind); });
                    if (rr) GUIUtility.ExitGUI();
                });
            });
        }

        // ---------------------------------------------------------------- pause / end

        void DrawPause()
        {
            Overlay(0.6f);
            const float bw = 260f;
            Centered(() =>
            {
                Label("Paused", 56, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Label($"World {game.stage}  ·  {game.Stage.name}", 16, M.Darken(PARCHMENT, 0.2f), 3, TextAnchor.UpperCenter);
                Space(20);
                CenterRow(() => Button("Resume", 24, bw, true, TogglePause));
                Space(16);
                CenterRow(() =>
                {
                    GUILayout.BeginVertical(PanelStyle(null, null, 12, 1, 16));
                    VolumeRow();
                    Space(6);
                    SpeedRow();
                    Space(6);
                    SensRow();
                    GUILayout.EndVertical();
                });
                Space(16);
                CenterRow(() => Button("Stats", 20, bw, true, ToggleStats));
                Space(8);
                CenterRow(() => Button("Controls", 20, bw, true, () => controlsOpen = true));
                Space(8);
                CenterRow(() => Button("Save & Main Menu", 20, bw, true, game.SaveAndQuit));
                Space(8);
                CenterRow(() => Button("Quit", 20, bw, true, () => { game.WriteRunSave(); ShowQuit(); }));
                Space(10);
                Label("Your run is saved: Continue resumes it at the start of this world.", 13, M.Darken(PARCHMENT, 0.35f), 3, TextAnchor.UpperCenter);
            });
        }

        void DrawEnd()
        {
            Overlay(0.7f);
            var p = game.player;
            Centered(() =>
            {
                Label(endWon ? "Victory" : "You Have Fallen", 72, endWon ? GOLD : new Color(0.9f, 0.25f, 0.2f), 12, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                Label(endWon ? "All three warlords are slain. Dawn returns to the kingdom." : $"Your dragon cries out as the clans close in on {game.Stage.name}.", 22, PARCHMENT, 6, TextAnchor.UpperCenter);
                foreach (var hid in game.rescued)
                {
                    if (!Data.DRAGONS.ContainsKey(hid)) continue;
                    var hd = Data.DRAGONS[hid];
                    Space(8);
                    Label($"Rescued from the warlord's chains: the {hd.name} is yours!", 24, M.Lighten(hd.body, 0.35f), 8, TextAnchor.UpperCenter, 0, false, true);
                }
                Space(18);
                Label($"World {game.stage} / {Data.STAGE_COUNT}   ·   {Clock(game.totalTime)}   ·   Level {p.level}   ·   {game.kills} foes slain", 24, PARCHMENT, 6, TextAnchor.UpperCenter);
                Space(18);
                Label($"Score  {endScore}", 48, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(10);
                Label($"+{game.heroXpGained} hero XP for the {Data.HEROES[p.heroId].name}   ·   +{game.bondXpGained} bond XP with the {Data.DRAGONS[p.dragonId].name}",
                    20, new Color(0.75f, 0.88f, 1f), 6, TextAnchor.UpperCenter);
                Label("Spend it on their talent trees from the main menu.", 15, M.Darken(PARCHMENT, 0.25f), 4, TextAnchor.UpperCenter);
                Space(18);
                if (endBest) Label("New High Score!", 26, new Color(1f, 0.85f, 0.3f), 8, TextAnchor.UpperCenter, 0, false, true);
                else Label($"Best  {Save.best_score}", 20, M.Darken(PARCHMENT, 0.2f), 8, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    Button("Ride Again", 22, 220, true, () => game.Restart(true));
                    Space(16);
                    Button("Main Menu", 20, 220, true, () => game.Restart(false));
                    Space(16);
                    Button("Quit", 20, 220, true, () =>
                    {
                        endOpen = false;
                        ShowQuit();
                    });
                });
            });
        }

        // ---------------------------------------------------------------- stats screen

        void DrawStats()
        {
            Overlay(0.75f);
            var p = game.player;
            Centered(() =>
            {
                Label("Character", 52, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                CenterRow(() =>
                {
                    StatColumn("Battlemage", () =>
                    {
                        Kv("Level", p.level.ToString(), 16);
                        Kv("Health", $"{Mathf.CeilToInt(p.hp)} / {(int)p.maxHp}", 16);
                        Kv("Mana", $"{(int)p.mana} / {(int)p.MaxMana}", 16);
                        Kv("Mana regen",$"{p.ManaRegen:0.0} / s", 16);
                        Kv("Damage bonus", $"+{Mathf.RoundToInt((p.DamageMult - 1f) * 100)}%", 16);
                        Kv("Spell damage", $"+{Mathf.RoundToInt((p.SpellDamageMult - 1f) * 100)}%", 16);
                        Kv("Critical chance", $"{Mathf.RoundToInt(p.CritChance * 100)}%", 16);
                        Kv("Attack speed", $"+{Mathf.RoundToInt((1f / p.AttackSpeedMult - 1f) * 100)}%", 16);
                        Kv("Dragon damage", $"+{Mathf.RoundToInt((p.DragonDamageMult - 1f) * 100)}%", 16);
                        Kv("Healing received", $"{Mathf.RoundToInt(p.HealMult * 100)}%", 16);
                        if (p.IsHuntress) Kv("Dodge chance", $"{Mathf.RoundToInt(p.DodgeChance * 100)}%", 16);
                        else Kv(p.IsPyro ? "Ward chance" : "Block chance", $"{Mathf.RoundToInt(p.BlockChance * 100)}%", 16);
                        Kv("Parry chance", $"{p.ParryChance * 100f:0.0}%", 16);
                        Kv("Armor", $"-{(int)p.Armor} per hit", 16);
                        Kv("Health regen", $"{p.Regen:0.0} / s", 16);
                        Kv("Lifesteal", $"{p.Lifesteal * 100f:0.0}%", 16);
                        Kv("Move speed", $"{Mathf.RoundToInt(p.MoveMult * 100)}%", 16);
                        Kv("Dragon cooldowns", $"{Mathf.RoundToInt(p.DragonCdMult * 100)}%", 16);
                        Kv("Dragon flight", !p.CanFly ? "None (hatchling)" : p.UnlimitedFlight ? "Unlimited" : $"{p.MaxFlight:0.0}s", 16);
                        Kv("Power-up drop chance", $"{p.PowerUpDropChance * 100f:0.0}%", 16);
                    });
                    Space(18);
                    StatColumn("Abilities", () =>
                    {
                        Kv(Data.HEROES[p.heroId].name, "", 16);
                        Kv(p.Weapon.name, $"Lv {p.weaponLevel}", 16);
                        var def = p.IsKnight ? Data.SHIELD : (p.IsPyro ? Data.WARD : Data.EVASION);
                        Kv(def.name, $"Lv {p.shieldLevel}", 16);
                        Kv(Data.SPELLS[p.spellId].name, $"Lv {Lv(p.spellId)}", 16);
                        Kv(Data.PASSIVES[p.passiveId].name, $"Lv {Lv(p.passiveId)}", 16);
                        Kv(Data.DRAGONS[p.dragonId].name + " breath", $"Lv {Lv("dragonfire")}", 16);
                        Kv(Data.DRAGONS[p.dragonId].sig + " (E)", $"Lv {Lv("ride")}", 16);
                        Space(6);
                        Label("Stat buckets", 20, GOLD, 5);
                        foreach (var id in Data.BUCKET_ORDER) Kv(Data.BUCKETS[id].name, $"Rank {p.B(id)}", 16);
                        Space(6);
                        Label("Armor", 20, GOLD, 5);
                        foreach (var id in Data.ARMOR_SLOTS)
                        {
                            var a = p.ArmorIn(id);
                            Kv(Data.ARMOR_SLOT_NAMES[id], a == null ? "-" : Data.ARMOR_TIER_NAMES[a.tier], 16);
                        }
                        Space(6);
                        Label("Talents", 20, GOLD, 5);
                        for (int r = 0; r < Data.TALENT_LEVELS.Length; r++)
                        {
                            var tid = p.TalentInRow(r);
                            Kv($"Row {r + 1}", tid == null ? $"Lv {Data.TALENT_LEVELS[r]}" : Data.FindTalent(tid).name, 16);
                        }
                        Space(6);
                        Label("Power-ups", 20, GOLD, 5);
                        foreach (var id in Data.POWERUP_ORDER)
                            Kv(Data.POWERUPS[id].name, p.PowerActive(id) ? $"{Mathf.CeilToInt(p.PowerLeft(id))}s" : "-", 16);
                    });
                    Space(18);
                    StatColumn("This Run", () =>
                    {
                        Kv("Time", Clock(game.totalTime), 16);
                        Kv("World", $"{game.stage} / {Data.STAGE_COUNT}", 16);
                        Kv("Wave", game.finalStarted ? "Final Battle" : $"{game.wave} / {game.Stage.waves.Length}", 16);
                        Kv("Foes slain", game.kills.ToString(), 16);
                        Kv("Damage dealt", ((int)game.damageDealt).ToString(), 16);
                        Kv("Biggest hit", ((int)game.biggestHit).ToString(), 16);
                        Kv("Bosses slain", game.bossesSlain.ToString(), 16);
                        Kv("Score so far", Save.ScoreFor(game.totalTime, game.kills, p.level, game.stage, false).ToString(), 16);
                        Kv("Best score", Save.best_score.ToString(), 16);
                    });
                });
                Space(18);
                CenterRow(() => Button("Close", 22, 0, true, ToggleStats));
                Space(6);
                Label("Tab or Esc to close  (gamepad: Select, Start or B)", 16, M.Darken(PARCHMENT, 0.3f), 4, TextAnchor.UpperCenter);
            });
        }

        void StatColumn(string heading, System.Action body)
        {
            GUILayout.BeginVertical(PanelStyle(), GUILayout.Width(340));
            Label(heading, 24, GOLD, 6);
            Space(3);
            body();
            GUILayout.EndVertical();
        }
    }
}
