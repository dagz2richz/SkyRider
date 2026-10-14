using System.Collections.Generic;
using UnityEngine;
using static Skyfall.UiKit;

namespace Skyfall
{
    /// All 2D UI: main menu, high scores, hero/dragon select, loadout, settings, quit screen, in-run HUD,
    /// level-up cards, pause, stats screen and end screen.
    public class Hud : MonoBehaviour
    {
        public Game game;

        enum Menu { None, Main, HeroSelect, Loadout, Settings, Quit, Farewell, Shop }
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

        // Shop state: which tab, where Back returns to, the last purchase message, and a purchase queued for after layout.
        bool shopArmorTab;
        int pendingTab = -1;
        Menu shopReturn = Menu.Main;
        string shopMsg = "";
        float shopMsgT = 999f;
        string pendingBuy = "";
        Vector2 scroll;

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            bannerT += udt;
            shopMsgT += udt;
            ApplyPendingBuy();
            toastT += udt;
            hurtA = Mathf.MoveTowards(hurtA, 0f, Time.deltaTime * 1.5f);
            if (game == null) return;

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

        /// Opens the shop from the main menu or the loadout screen; Back returns there.
        void ShowShop(Menu from)
        {
            shopReturn = from;
            menu = Menu.Shop;
            shopMsg = "";
            GameInput.MouseCaptured = false;
        }

        void LeaveShop()
        {
            if (shopReturn == Menu.Loadout) ShowLoadout();
            else ShowMainMenu();
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
            if (hudVisible) DrawHud();
            DrawFloatingTexts();
            DrawBannerAndToast();
            if (levelOptions != null) DrawLevelUp();
            if (pauseOpen) DrawPause();
            if (statsOpen) DrawStats();
            if (endOpen) DrawEnd();
            switch (menu)
            {
                case Menu.Main: DrawMainMenu(); break;
                case Menu.HeroSelect: DrawHeroSelect(); break;
                case Menu.Loadout: DrawLoadout(); break;
                case Menu.Settings: DrawSettings(); break;
                case Menu.Shop: DrawShop(); break;
                case Menu.Quit: DrawQuit(); break;
                case Menu.Farewell: DrawFarewell(); break;
            }
            if (controlsOpen) DrawControls();
        }

        static string Clock(float seconds)
        {
            int s = Mathf.Max(0, (int)seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        // ---------------------------------------------------------------- HUD

        static readonly string[] SLOT_KEYS = { "AUTO ATTACK", "DEFENSE", "PASSIVE", "SPELL · AUTO", "DRAGON · AUTO", "E · SIGNATURE" };

        void DrawHud()
        {
            var p = game.player;
            if (hurtA > 0.001f) UiKit.Rect(new Rect(0, 0, W, H), new Color(0.7f, 0f, 0f, hurtA));

            // XP bar along the top.
            Bar(new Rect(10, 8, W - 20, 14), p.xp / p.xpNext, new Color(0.3f, 0.75f, 1f));
            DrawText(new Rect(14, 26, 200, 26), $"Lv {p.level}", Text(20), GOLD);
            CoinIcon(new Rect(88, 28, 22, 22));
            DrawText(new Rect(116, 26, 200, 26), game.runGold.ToString(), Text(20, TextAnchor.UpperLeft, false, true), new Color(1f, 0.85f, 0.35f));

            // Timer + wave at top center.
            float left = Mathf.Max(0f, Data.RUN_LENGTH - game.timeElapsed);
            var center = Text(44, TextAnchor.UpperCenter, false, true);
            if (game.finalStarted) DrawText(new Rect(W / 2 - 300, 26, 600, 52), "FINAL BATTLE", center, new Color(1f, 0.45f, 0.3f), 8);
            else DrawText(new Rect(W / 2 - 300, 26, 600, 52), Clock(left), center, PARCHMENT, 8);
            string waveText;
            if (game.finalStarted) waveText = "Defeat the Mountain King";
            else if (game.bossesSpawned == 0) waveText = $"Wave {game.wave} / {Data.WAVE_NAMES.Length} · Boss in {Mathf.CeilToInt(Data.MINI_BOSS_TIME - game.timeElapsed)} s";
            else waveText = $"Wave {game.wave} / {Data.WAVE_NAMES.Length} · Mountain King in {Mathf.CeilToInt(left)} s";
            DrawText(new Rect(W / 2 - 300, 80, 600, 24), waveText, Text(18, TextAnchor.UpperCenter), GOLD);
            if (bossName != "")
            {
                DrawText(new Rect(W / 2 - 300, 108, 600, 28), bossName, Text(22, TextAnchor.UpperCenter, false, true), new Color(1f, 0.45f, 0.35f));
                Bar(new Rect(W / 2 - 300, 138, 600, 20), bossFrac, new Color(0.75f, 0.1f, 0.08f));
            }

            // Stats in the top right.
            string dodge = p.DodgeChance > 0f ? $"\nDodge  {Mathf.RoundToInt(p.DodgeChance * 100)}%" : "";
            string stats = $"Kills  {game.kills}\nFoes  {game.enemies.Count}\nFPS  {Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f) * Time.timeScale)}\n\n" +
                           $"Damage  +{Mathf.RoundToInt((p.DamageMult - 1f) * 100)}%\nHealth  {(int)p.maxHp}\nBlock  {Mathf.RoundToInt(p.BlockChance * 100)}%\n" +
                           $"Parry  {p.ParryChance * 100f:0.0}%{dodge}";
            DrawText(new Rect(W - 276, 28, 260, 260), stats, Text(16, TextAnchor.UpperRight), M.Darken(PARCHMENT, 0.15f), 5);

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
            by -= 10; Bar(new Rect(bx + 80, by, bw - 80, 10), p.flightStamina / p.MaxFlight, new Color(0.35f, 0.7f, 1f));
            DrawText(new Rect(bx, by - 6, 80, 20), "Flight (Q)", Text(15), new Color(0.6f, 0.85f, 1f), 5);
            by -= 5 + 16; Bar(new Rect(bx, by, bw, 16), p.mana / p.MaxMana, new Color(0.45f, 0.35f, 1f));
            by -= 5 + 20; DrawText(new Rect(bx, by, bw, 20), $"Mana {(int)p.mana} / {(int)p.MaxMana}", Text(16), new Color(0.75f, 0.7f, 1f));
            by -= 5 + 22; Bar(new Rect(bx, by, bw, 22), p.hp / p.maxHp, new Color(0.8f, 0.12f, 0.1f));
            by -= 5 + 22; DrawText(new Rect(bx, by, bw, 22), $"{Mathf.CeilToInt(p.hp)} / {(int)p.maxHp}", Text(18), PARCHMENT);

            DrawSlots();
            DrawIslandMarker();
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
            string label = $"{Mathf.RoundToInt(meters)} m";
            if (onScreen)
            {
                DrawText(new Rect(pos.x - 150, pos.y - 56, 300, 22), "Mountain King's Island", Text(16, TextAnchor.UpperCenter, false, true), col, 6, pulse);
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

        void DrawSlots()
        {
            var p = game.player;
            const float sw = 124, sh = 92, sep = 10, gap = 16;
            float total = 6 * sw + 5 * sep + gap;
            float x = W / 2 - total / 2;
            float y = H - 18 - sh;
            for (int i = 0; i < 6; i++)
            {
                if (i == 4) x += gap;
                var r = new Rect(x, y, sw, sh);
                SlotInfo(i, out string title, out Color color, out int lvl, out string extra, out float cdFrac);
                float alpha = i == 3 && p.mana < game.spellbook.Cost ? 0.6f : 1f;
                Panel(r, INK, M.Darken(GOLD, 0.3f), 6, 2, alpha);
                DrawText(new Rect(r.x + 10, r.y + 8, sw - 16, 18), SLOT_KEYS[i], Text(14), i < 4 ? GOLD : new Color(0.55f, 0.8f, 1f), 4, alpha);
                DrawText(new Rect(r.x + 10, r.y + 26, sw - 18, 40), title, Text(15, TextAnchor.UpperLeft, true), Color.Lerp(color, PARCHMENT, 0.35f), 4, alpha);
                DrawText(new Rect(r.x + 10, r.y + sh - 24, sw - 16, 18), $"{Repeat("◆", lvl)}  {extra}", Text(13), M.Darken(PARCHMENT, 0.2f), 4, alpha);
                if (cdFrac > 0f) UiKit.Rect(new Rect(r.x, r.y + sh * (1f - cdFrac), sw, sh * cdFrac), new Color(0, 0, 0, 0.6f));
                x += sw + sep;
            }
        }

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
                    title = w.name; color = w.color; lvl = p.swordLevel; extra = p.IsHuntress ? "2% hp" : "+mana";
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
                    title = sp.name; color = sp.color; lvl = Lv(p.spellId); extra = $"{Mathf.RoundToInt(game.spellbook.Cost)} mp";
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
            Label("SKYFALL", 96, GOLD, 12, TextAnchor.UpperCenter, 0, false, true);
            Label("~  Dragon Riders  ~", 30, PARCHMENT, 8, TextAnchor.UpperCenter);
        }

        void DrawMainMenu()
        {
            Overlay(0.3f);
            Centered(() =>
            {
                Title();
                Space(18);
                CenterRow(() =>
                {
                    GUILayout.BeginVertical(PanelStyle(), GUILayout.Width(300));
                    Label("The ogre clans pour out of the hills.\nYou are the last battlemage of the Dragonguard.\nSurvive eight minutes of the long night, then face the Mountain King.",
                        16, M.Darken(PARCHMENT, 0.1f), 4, TextAnchor.UpperLeft, 270, true);
                    Space(14);
                    if (Button("Play", 28)) ShowHeroSelect();
                    Space(14);
                    if (Button("Shop", 22)) ShowShop(Menu.Main);
                    Space(14);
                    if (Button("Settings", 22)) menu = Menu.Settings;
                    Space(14);
                    if (Button("Controls", 22)) controlsOpen = true;
                    Space(14);
                    if (Button("Quit", 22)) ShowQuit();
                    GUILayout.EndVertical();
                    Space(28);
                    GUILayout.BeginVertical(PanelStyle(), GUILayout.Width(400));
                    Label("Hall of Legends", 26, GOLD, 6);
                    Space(6);
                    Kv("High score", Save.best_score.ToString());
                    Kv("Longest survival", Clock(Save.best_time));
                    Kv("Most kills", Save.best_kills.ToString());
                    Kv("Highest level", Save.best_level.ToString());
                    Kv("Runs / Victories", $"{Save.runs} / {Save.wins}");
                    Kv("Gold", Save.gold.ToString());
                    Space(6);
                    Label("Recent runs", 18, M.Darken(GOLD, 0.1f), 4);
                    if (Save.history.Count == 0) Label("No runs yet. Ride forth!", 15, M.Darken(PARCHMENT, 0.3f), 4);
                    foreach (var h in Save.history)
                        Label($"{h.date}   {h.score,6}   {Clock(h.time)}   Lv {h.level}   {(h.won ? "WON" : "")}", 14, M.Darken(PARCHMENT, 0.15f), 3);
                    GUILayout.EndVertical();
                });
                Space(18);
                Label("Everything attacks on its own.   WASD move · Mouse look · SPACE jump / double jump · Q take flight · E riding ability · Scroll zoom · TAB stats · ESC pause",
                    15, M.Darken(PARCHMENT, 0.2f), 5, TextAnchor.UpperCenter);
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

        bool SelectCard(string title, string desc, List<string> lines, Color c, bool sel, float width)
        {
            var bg = sel ? new Color(0.16f, 0.12f, 0.06f, 0.95f) : INK;
            var border = sel ? c : M.Darken(GOLD, 0.45f);
            GUILayout.BeginVertical(PanelStyle(bg, border, 8, sel ? 4 : 2), GUILayout.Width(width));
            GUILayout.BeginHorizontal();
            var sw = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(26));
            UiKit.Rect(new Rect(sw.x, sw.y + 6, 14, 14), c);
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
            return Clickable(GUILayoutUtility.GetLastRect());
        }

        void DrawHeroSelect()
        {
            Overlay(0.4f);
            float heroW = Mathf.Min(420f, (W - 80f) / 3f - 18f);
            float dragonW = Mathf.Min(380f, (W - 80f) / 3f - 16f);
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
                        int legs = Save.ArmorTier("legs");
                        string def = h.defense == "shield" ? $"Shield ({Mathf.RoundToInt(100f * (0.12f + 0.05f * (Mathf.Min(Save.WeaponLevel(h.weapon) + legs, 8) - 1)))}% block)"
                            : (h.defense == "evasion" ? $"Evasion ({Mathf.RoundToInt(100f * (0.15f + 0.05f * legs))}% dodge)" : $"Flame Ward ({Mathf.RoundToInt(100f * (0.10f + 0.05f * legs))}% ward)");
                        var lines = new List<string> { $"Weapon:  {w.shopName}  Lv {Save.WeaponLevel(h.weapon)}", $"Defense:  {def}", $"Health:  {(int)(h.hp * (1f + 0.06f * Save.ArmorTier("helm")))}" };
                        switch (id)
                        {
                            case "knight": lines.Add("Sword deals 1.25x damage"); break;
                            case "huntress": lines.Add("Javelins pierce, cause bleeding, deal 1.25x damage, cost 2% current health, no mana"); break;
                            case "pyromancer": lines.Add("Fast fire-tipped arrows, long range, ignite foes; ward sets attackers on fire"); break;
                        }
                        if (SelectCard(h.name, h.desc, lines, h.color, id == pickHero, heroW))
                        {
                            pickHero = id;
                            ShowHeroSelect();
                        }
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
                            SelectCard(d.name, "LOCKED — defeat the Mountain King to find its egg", new List<string>(), Color.Lerp(d.body, Color.gray, 0.6f), false, dragonW);
                            GUI.color = prev;
                        }
                        else if (SelectCard(d.name, d.desc, lines, M.Lighten(d.body, 0.25f), id == pickDragon, dragonW))
                        {
                            pickDragon = id;
                            ShowHeroSelect();
                        }
                        Space(16);
                    }
                });
                Space(18);
                CenterRow(() =>
                {
                    if (Button("Back", 22)) ShowMainMenu();
                    Space(16);
                    if (Button("Continue", 28)) ShowLoadout();
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
                      "Q takes flight on your dragon. E unleashes your dragon's signature, on the ground or in the air (from the air you leap down and slam).",
                    16, M.Darken(PARCHMENT, 0.1f), 4, TextAnchor.UpperCenter);
                Space(18);
                ChoiceRow("Spell  ·  casts itself when you have the mana", Data.SPELL_ORDER, ref pickSpell, true);
                Space(18);
                ChoiceRow("Passive", Data.PASSIVE_ORDER, ref pickPassive, false);
                Space(18);
                CenterRow(() =>
                {
                    if (Button("Back", 22)) ShowHeroSelect();
                    Space(16);
                    if (Button("Shop", 22)) ShowShop(Menu.Loadout);
                    Space(16);
                    if (Button("Ride Forth", 28))
                    {
                        menu = Menu.None;
                        game.StartRun(pickSpell, pickPassive, pickHero, pickDragon);
                    }
                });
            });
        }

        void ChoiceRow(string heading, string[] ids, ref string selected, bool spells)
        {
            const float sep = 14f;
            float cardW = Mathf.Min(330f, (W - 60f) / ids.Length - sep);
            Label(heading, 19, GOLD, 6);
            Space(4);
            string picked = selected;
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
                bool sel = id == selected;
                var bg = sel ? new Color(0.16f, 0.12f, 0.06f, 0.95f) : INK;
                var border = sel ? c : M.Darken(GOLD, 0.45f);
                GUILayout.BeginVertical(PanelStyle(bg, border, 8, sel ? 4 : 2), GUILayout.Width(cardW), GUILayout.MinHeight(150));
                GUILayout.BeginHorizontal();
                Label(name, 20, Color.Lerp(c, PARCHMENT, 0.3f), 6, TextAnchor.UpperLeft, 0, false, true);
                GUILayout.FlexibleSpace();
                if (sel) Label("SELECTED", 13, Color.Lerp(c, PARCHMENT, 0.3f), 3);
                GUILayout.EndHorizontal();
                Space(6);
                Label(desc, 14, PARCHMENT, 6, TextAnchor.UpperLeft, cardW - 30, true);
                GUILayout.FlexibleSpace();
                if (extra != null) Label(extra, 14, new Color(0.7f, 0.65f, 1f), 6);
                GUILayout.EndVertical();
                if (Clickable(GUILayoutUtility.GetLastRect())) picked = id;
                Space(sep);
            }
            GUILayout.EndHorizontal();
            selected = picked;
        }

        // ---------------------------------------------------------------- shop

        static readonly Color GOLD_TEXT = new Color(1f, 0.85f, 0.35f);

        /// Weapons and armor bought with the gold earned in runs. Purchases are queued (pendingBuy) and applied in Update,
        /// so the controls drawn never change in the middle of a layout pass.
        void DrawShop()
        {
            Overlay(0.7f);
            bool armor = shopArmorTab;   // fixed for this whole pass; a tab click only queues a change
            float cardW = armor ? Mathf.Min(300f, (W - 80f) / 5f - 14f) : Mathf.Min(420f, (W - 80f) / 3f - 18f);
            Centered(() =>
            {
                Label("Shop", 54, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(6);
                CenterRow(() =>
                {
                    var r = GUILayoutUtility.GetRect(30, 30, GUILayout.Width(30), GUILayout.Height(30));
                    CoinIcon(new Rect(r.x, r.y + 2, 26, 26));
                    Space(8);
                    Label($"{Save.gold} gold", 30, GOLD_TEXT, 8, TextAnchor.UpperLeft, 0, false, true);
                });
                if (shopMsgT < 3f && shopMsg != "") Label(shopMsg, 18, new Color(0.6f, 1f, 0.6f), 5, TextAnchor.UpperCenter, 0, false, false, Mathf.Clamp01(3f - shopMsgT));
                else Label(" ", 18, PARCHMENT, 0, TextAnchor.UpperCenter);
                Space(8);
                CenterRow(() =>
                {
                    if (ShopTab("Weapons", !armor)) pendingTab = 0;
                    Space(14);
                    if (ShopTab("Armor", armor)) pendingTab = 1;
                });
                Space(16);
                CenterRow(() =>
                {
                    if (!armor)
                        foreach (var id in Data.WEAPON_ORDER) { WeaponCard(id, cardW); Space(18); }
                    else
                        foreach (var id in Data.ARMOR_ORDER) { ArmorCard(id, cardW); Space(14); }
                });
                Space(14);
                Label(armor ? "Armor is shared by every hero. Each slot has five tiers."
                                   : "Each hero is locked to one weapon. Weapon levels also strengthen the Knight's shield block.",
                    15, M.Darken(PARCHMENT, 0.2f), 4, TextAnchor.UpperCenter);
                Space(10);
                CenterRow(() => { if (Button("Back", 24)) LeaveShop(); });
            });
        }

        bool ShopTab(string text, bool selected)
        {
            var prev = GUI.color;
            if (!selected) GUI.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            bool clicked = Button(text, 24, 180);
            GUI.color = prev;
            return clicked;
        }

        void ApplyPendingBuy()
        {
            if (pendingTab >= 0)
            {
                shopArmorTab = pendingTab == 1;
                pendingTab = -1;
            }
            if (pendingBuy == "") return;
            var parts = pendingBuy.Split(':');
            pendingBuy = "";
            bool ok;
            string what;
            if (parts[0] == "w")
            {
                ok = Save.BuyWeapon(parts[1]);
                what = $"{Data.WEAPONS[parts[1]].shopName} upgraded to Lv {Save.WeaponLevel(parts[1])}";
            }
            else
            {
                ok = Save.BuyArmor(parts[1]);
                what = $"{Data.ARMOR[parts[1]].name} is now tier {Save.ArmorTier(parts[1])}";
            }
            if (!ok) return;
            Sfx.Play("buy", -2f, 0f);
            shopMsg = what;
            shopMsgT = 0f;
        }

        GUIStyle ShopCard(Color accent, float width) =>
            PanelStyle(INK, Color.Lerp(accent, GOLD, 0.35f), 8, 3);

        void WeaponCard(string id, float width)
        {
            var w = Data.WEAPONS[id];
            var hero = Data.HEROES[w.hero];
            int lvl = Save.WeaponLevel(id);
            int cost = Save.WeaponUpgradeCost(id);
            var c = w.color;
            GUILayout.BeginVertical(ShopCard(c, width), GUILayout.Width(width), GUILayout.MinHeight(420));
            Label(w.shopName, 28, Color.Lerp(c, PARCHMENT, 0.3f), 6, TextAnchor.UpperLeft, width - 30, false, true);
            Label($"Used by {hero.name}", 15, Color.Lerp(hero.color, PARCHMENT, 0.4f), 4);
            Space(8);
            GUILayout.BeginHorizontal();
            Pips(lvl, Data.MAX_LEVEL, c);
            Space(10);
            Label($"Level {lvl} / {Data.MAX_LEVEL}", 17, GOLD, 5);
            GUILayout.EndHorizontal();
            Space(10);
            Label("Now", 17, GOLD, 5);
            foreach (var ln in Data.WeaponStats(id, lvl)) Label(ln, 15, PARCHMENT, 4, TextAnchor.UpperLeft, width - 30, true);
            Space(10);
            if (cost >= 0)
            {
                Label($"Level {lvl + 1}", 17, new Color(0.6f, 1f, 0.6f), 5);
                foreach (var ln in Data.WeaponStats(id, lvl + 1)) Label(ln, 15, new Color(0.75f, 1f, 0.75f), 4, TextAnchor.UpperLeft, width - 30, true);
            }
            GUILayout.FlexibleSpace();
            if (cost >= 0)
            {
                if (Button($"Upgrade  ·  {cost} gold", 20, 0, Save.gold >= cost)) pendingBuy = "w:" + id;
            }
            else Label("MAX LEVEL", 22, GOLD, 6, TextAnchor.UpperCenter, 0, false, true);
            GUILayout.EndVertical();
        }

        void ArmorCard(string slot, float width)
        {
            var a = Data.ARMOR[slot];
            int tier = Save.ArmorTier(slot);
            int cost = Save.ArmorUpgradeCost(slot);
            var c = new Color(0.55f, 0.75f, 1f);
            GUILayout.BeginVertical(ShopCard(c, width), GUILayout.Width(width), GUILayout.MinHeight(420));
            Label(a.name, 28, Color.Lerp(c, PARCHMENT, 0.3f), 6, TextAnchor.UpperLeft, width - 30, false, true);
            Label(a.desc, 14, M.Darken(PARCHMENT, 0.1f), 4, TextAnchor.UpperLeft, width - 30, true);
            Space(8);
            Pips(tier, Data.MAX_ARMOR_TIER, c);
            Space(4);
            Label(tier == 0 ? "Not owned" : $"Tier {tier} / {Data.MAX_ARMOR_TIER}", 17, GOLD, 5);
            Space(10);
            Label("Now", 17, GOLD, 5);
            Label(tier == 0 ? "No bonus" : Data.ArmorBonus(slot, tier), 15, PARCHMENT, 4, TextAnchor.UpperLeft, width - 30, true);
            Space(10);
            if (cost >= 0)
            {
                Label($"Tier {tier + 1}", 17, new Color(0.6f, 1f, 0.6f), 5);
                Label(Data.ArmorBonus(slot, tier + 1), 15, new Color(0.75f, 1f, 0.75f), 4, TextAnchor.UpperLeft, width - 30, true);
            }
            GUILayout.FlexibleSpace();
            if (cost >= 0)
            {
                if (Button($"{(tier == 0 ? "Buy" : "Upgrade")}  ·  {cost}", 20, 0, Save.gold >= cost)) pendingBuy = "a:" + slot;
            }
            else Label("MAX TIER", 22, GOLD, 6, TextAnchor.UpperCenter, 0, false, true);
            GUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- settings

        void VolumeRow()
        {
            GUILayout.BeginHorizontal();
            Label("Volume", 20, PARCHMENT, 6, TextAnchor.UpperLeft, 190);
            float v = GUILayout.HorizontalSlider(Save.volume, 0f, 1f, GUILayout.Width(260));
            Label($"{(int)(v * 100)}%", 20, GOLD, 6, TextAnchor.UpperLeft, 70);
            GUILayout.EndHorizontal();
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
            if (!Mathf.Approximately(v, Save.game_speed)) Save.game_speed = v;
        }

        void SensRow()
        {
            GUILayout.BeginHorizontal();
            Label("Mouse sensitivity", 20, PARCHMENT, 6, TextAnchor.UpperLeft, 190);
            float v = GUILayout.HorizontalSlider(Save.mouse_sens, 0.2f, 3f, GUILayout.Width(260));
            v = Mathf.Round(v / 0.05f) * 0.05f;
            Label($"{v:0.00}x", 20, GOLD, 6, TextAnchor.UpperLeft, 70);
            GUILayout.EndHorizontal();
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
                    if (Button("Back", 22))
                    {
                        Save.Write();
                        ShowMainMenu();
                    }
                });
            });
        }

        static readonly string[,] CONTROLS =
        {
            { "W A S D", "Move (relative to the camera)" },
            { "Mouse", "Look around; orbits all the way, stopping at the ground" },
            { "Scroll wheel", "Zoom in and out" },
            { "Space", "Jump; press again in the air to double jump; hold to climb while flying" },
            { "Q", "Call your dragon and take flight (press again to drop off)" },
            { "E", "Dragon signature ability (from the air you also leap down and slam)" },
            { "Shift / Ctrl", "Dive while flying" },
            { "Tab", "Stats screen" },
            { "Esc / P", "Pause" },
            { "1 / 2 / 3", "Pick a level-up card" },
            { "(automatic)", "Your attacks, spell, passive, defense and dragon breath all fire on their own" },
        };

        void DrawControls()
        {
            Overlay(0.8f);
            Centered(() =>
            {
                Label("Controls", 54, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                GUILayout.BeginVertical(PanelStyle());
                for (int i = 0; i < CONTROLS.GetLength(0); i++)
                {
                    GUILayout.BeginHorizontal();
                    Label(CONTROLS[i, 0], 20, GOLD, 5, TextAnchor.UpperLeft, 160);
                    Space(30);
                    Label(CONTROLS[i, 1], 18, PARCHMENT, 4, TextAnchor.UpperLeft, 700);
                    GUILayout.EndHorizontal();
                    Space(8);
                }
                GUILayout.EndVertical();
                Space(18);
                CenterRow(() => { if (Button("Back", 22)) controlsOpen = false; });
            });
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
                    if (Button("Stay", 24))
                    {
                        if (game.running)
                        {
                            menu = Menu.None;
                            pauseOpen = true;
                        }
                        else ShowMainMenu();
                    }
                    Space(20);
                    if (Button("Quit Game", 24)) Farewell();
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
                Label($"LEVEL {levelShown}", 56, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                Label((levelKind == "ability" ? "Choose an ability to upgrade." : "Choose a stat bucket: you gain all three of its stats.") +
                      "   (Every level: +5% damage · +2% health · +0.5% parry)", 20, PARCHMENT, 6, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        var o = options[i];
                        var c = o.color;
                        GUILayout.BeginVertical(PanelStyle(INK, Color.Lerp(c, GOLD, 0.3f), 8, 3), GUILayout.Width(310), GUILayout.MinHeight(270));
                        Label($"{o.tag}   [{i + 1}]", 15, c, 6);
                        Space(10);
                        Label(o.name, 26, Color.Lerp(c, PARCHMENT, 0.35f), 6, TextAnchor.UpperLeft, 280, true, true);
                        Space(10);
                        Label(o.type == "bucket" ? $"Rank {o.level}" : $"Level {o.level} / {Data.MAX_LEVEL}", 17, GOLD, 6);
                        Space(10);
                        Label(o.desc, 17, PARCHMENT, 6, TextAnchor.UpperLeft, 280, true);
                        GUILayout.FlexibleSpace();
                        bool pick = Button("Choose", 22);
                        GUILayout.EndVertical();
                        if (pick && levelOptions == options)
                        {
                            Choose(o);
                            GUIUtility.ExitGUI();
                        }
                        if (i < options.Count - 1) Space(20);
                    }
                });
            });
        }

        // ---------------------------------------------------------------- pause / end

        void DrawPause()
        {
            Overlay(0.6f);
            Centered(() =>
            {
                Label("PAUSED", 60, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                CenterRow(() => { if (Button("Resume", 26)) TogglePause(); });
                Space(18);
                GUILayout.BeginVertical(PanelStyle());
                VolumeRow();
                Space(8);
                SpeedRow();
                Space(8);
                SensRow();
                GUILayout.EndVertical();
                Space(18);
                CenterRow(() => { if (Button("Stats", 22)) ToggleStats(); });
                Space(18);
                CenterRow(() => { if (Button("Controls", 22)) controlsOpen = true; });
                Space(18);
                CenterRow(() => { if (Button("Main Menu", 22)) { Save.Write(); game.Restart(false); } });
                Space(18);
                CenterRow(() => { if (Button("Quit", 22)) { Save.Write(); ShowQuit(); } });
            });
        }

        void DrawEnd()
        {
            Overlay(0.7f);
            var p = game.player;
            Centered(() =>
            {
                Label(endWon ? "VICTORY" : "YOU HAVE FALLEN", 72, endWon ? GOLD : new Color(0.9f, 0.25f, 0.2f), 12, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                Label(endWon ? "The Mountain King is slain. Dawn returns to the kingdom." : "Your dragon cries out as the clans close in.", 22, PARCHMENT, 6, TextAnchor.UpperCenter);
                if (endWon && game.hatchedDragon != "" && Data.DRAGONS.ContainsKey(game.hatchedDragon))
                {
                    var hd = Data.DRAGONS[game.hatchedDragon];
                    Space(8);
                    Label($"The egg hatches: the {hd.name} is yours!", 26, M.Lighten(hd.body, 0.35f), 8, TextAnchor.UpperCenter, 0, false, true);
                }
                Space(18);
                Label($"Survived {Clock(game.timeElapsed)}   ·   Level {p.level}   ·   {game.kills} ogres slain", 24, PARCHMENT, 6, TextAnchor.UpperCenter);
                Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var gr = GUILayoutUtility.GetRect(30, 30, GUILayout.Width(30), GUILayout.Height(30));
                CoinIcon(new Rect(gr.x, gr.y + 2, 26, 26));
                Space(8);
                Label($"Gold earned  +{game.runGold}   (total {Save.gold})", 26, new Color(1f, 0.85f, 0.35f), 6, TextAnchor.UpperLeft, 0, false, true);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                Space(18);
                Label($"Score  {endScore}", 48, GOLD, 10, TextAnchor.UpperCenter, 0, false, true);
                Space(18);
                if (endBest) Label("NEW HIGH SCORE!", 26, new Color(1f, 0.85f, 0.3f), 8, TextAnchor.UpperCenter, 0, false, true);
                else Label($"Best  {Save.best_score}", 20, M.Darken(PARCHMENT, 0.2f), 8, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    if (Button("Ride Again", 26)) game.Restart(true);
                    Space(16);
                    if (Button("Main Menu", 22)) game.Restart(false);
                    Space(16);
                    if (Button("Quit", 22))
                    {
                        endOpen = false;
                        ShowQuit();
                    }
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
                        Kv("Mana regen", $"{p.ManaRegen:0.0} / s", 16);
                        Kv("Damage bonus", $"+{Mathf.RoundToInt((p.DamageMult - 1f) * 100)}%", 16);
                        Kv("Spell damage", $"+{Mathf.RoundToInt((p.SpellDamageMult - 1f) * 100)}%", 16);
                        Kv("Critical chance", $"{Mathf.RoundToInt(p.CritChance * 100)}%", 16);
                        Kv("Attack speed", $"+{Mathf.RoundToInt((1f / p.AttackSpeedMult - 1f) * 100)}%", 16);
                        Kv("Dragon damage", $"+{Mathf.RoundToInt((p.DragonDamageMult - 1f) * 100)}%", 16);
                        Kv("Healing received", $"{Mathf.RoundToInt(p.HealMult * 100)}%", 16);
                        Kv("Block chance", $"{Mathf.RoundToInt(p.BlockChance * 100)}%", 16);
                        Kv("Parry chance", $"{p.ParryChance * 100f:0.0}%", 16);
                        Kv("Dodge chance", $"{Mathf.RoundToInt(p.DodgeChance * 100)}%", 16);
                        Kv("Armor", $"-{(int)p.Armor} per hit", 16);
                        Kv("Health regen", $"{p.Regen:0.0} / s", 16);
                        Kv("Lifesteal", $"{p.Lifesteal * 100f:0.0}%", 16);
                        Kv("Move speed", $"{Mathf.RoundToInt(p.MoveMult * 100)}%", 16);
                        Kv("Dragon cooldowns", $"{Mathf.RoundToInt(p.DragonCdMult * 100)}%", 16);
                        Kv("Dragon flight", $"{p.MaxFlight:0.0} s", 16);
                    });
                    Space(18);
                    StatColumn("Abilities", () =>
                    {
                        Kv(Data.HEROES[p.heroId].name, "", 16);
                        Kv(p.Weapon.shopName, $"Lv {p.swordLevel}", 16);
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
                        foreach (var id in Data.ARMOR_ORDER) Kv(Data.ARMOR[id].name, p.Tier(id) == 0 ? "none" : $"Tier {p.Tier(id)}", 16);
                    });
                    Space(18);
                    StatColumn("This Run", () =>
                    {
                        Kv("Time", Clock(game.timeElapsed), 16);
                        Kv("Wave", game.finalStarted ? "Final battle" : $"{game.wave} / {Data.WAVE_NAMES.Length}", 16);
                        Kv("Ogres slain", game.kills.ToString(), 16);
                        Kv("Gold", game.runGold.ToString(), 16);
                        Kv("Damage dealt", ((int)game.damageDealt).ToString(), 16);
                        Kv("Biggest hit", ((int)game.biggestHit).ToString(), 16);
                        Kv("Bosses slain", game.bossesSlain.ToString(), 16);
                        Kv("Score so far", Save.ScoreFor(game.timeElapsed, game.kills, p.level, false).ToString(), 16);
                        Kv("Best score", Save.best_score.ToString(), 16);
                    });
                });
                Space(18);
                Label("TAB or ESC to close", 16, M.Darken(PARCHMENT, 0.3f), 4, TextAnchor.UpperCenter);
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
