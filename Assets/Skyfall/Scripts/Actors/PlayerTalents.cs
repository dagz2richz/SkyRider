using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Talents: the in-run rows (Data.RUN_TALENTS, one pick per row as the hero levels up) plus the picks from the save
    /// slot's permanent hero and dragon trees (Data.HERO_TREES, Data.DRAGON_TREES), all in one set. Stat talents are
    /// summed into Mod(); the rest are read where the stats are computed (Player.cs) or act here (timers, the capstone
    /// forms on F: the Knight's Dragon Form, the Huntress's Crimson Specter and the Ember Mage's Phoenix Form).
    public partial class Player
    {
        public readonly HashSet<string> talents = new HashSet<string>();
        readonly Dictionary<string, float> mods = new Dictionary<string, float>();

        public bool Has(string id) => talents.Contains(id);

        /// Sum of a stat mod over every talent you have (see Data.HERO_TREES for the keys).
        public float Mod(string key) => mods.TryGetValue(key, out float v) ? v : 0f;
        /// A percentage mod as a fraction (12 -> 0.12).
        public float Pct(string key) => Mod(key) * 0.01f;

        /// Adds the save slot's permanent tree picks for this hero and dragon.
        void LearnPermanentTalents()
        {
            foreach (var id in Save.Picks(Save.HeroTree(heroId))) talents.Add(id);
            foreach (var id in Save.Picks(Save.DragonTree(dragonId))) talents.Add(id);
        }

        /// The in-run talent picked in each row (null when the row is not chosen yet).
        public string TalentInRow(int row)
        {
            if (!Data.RUN_TALENTS.TryGetValue(heroId, out var rows) || row < 0 || row >= rows.Length) return null;
            foreach (var t in rows[row]) if (talents.Contains(t.id)) return t.id;
            return null;
        }

        /// Number of talent rows already chosen.
        public int TalentRowsChosen
        {
            get
            {
                int n = 0;
                for (int r = 0; r < Data.TALENT_LEVELS.Length; r++) if (TalentInRow(r) != null) n++;
                return n;
            }
        }

        public void LearnTalent(string id)
        {
            if (Data.FindTalent(id) == null) return;
            talents.Add(id);
            OnTalentsChanged();
            RecalcMaxHp();
            mana = Mathf.Min(mana, MaxMana);
            var at = Center + Vector3.up;
            FX.Flash(at, 3f, new Color(1f, 0.85f, 0.4f, 0.6f), 0.4f);
            FX.Burst(at, new Color(1f, 0.85f, 0.4f), 24, 9f, 0.3f);
        }

        // ---------------------------------------------------------------- state

        float rallyT, aegisCd, chargeCd = 3f, chargeT, sanguineCd, shadowCd, moonT, moonCd = 30f, walkFireT, blinkCd, novaT, pillarT, orbTick;
        public float dragonFormT, dragonFormCd;
        /// Which capstone form is active: "dragon", "specter" or "phoenix".
        string formKind = "";
        float rescueCd, bulwarkCd, tideTick, phoenixRainT;
        bool guardianReviveUsed;
        Vector3 chargeDir;
        readonly HashSet<Enemy> chargeHit = new HashSet<Enemy>();
        bool vowUsed;
        Transform wings, orbs;
        Light moonLight;

        public bool BloodMoon => moonT > 0f;
        public bool DragonFormActive => dragonFormT > 0f;

        void ResetTalentState()
        {
            rallyT = 0f; aegisCd = 0f; chargeCd = 3f; chargeT = 0f; sanguineCd = 0f; shadowCd = 0f; moonT = 0f; moonCd = 30f;
            walkFireT = 0f; blinkCd = 0f; novaT = 35f; pillarT = 12f; vowUsed = false; invulnT = 0f; swingCount = 0; throwCount = 0;
            rescueCd = 0f; bulwarkCd = 0f; guardianReviveUsed = false;
            EndDragonForm();
            dragonFormCd = 0f;
            OnTalentsChanged();
        }

        /// A new stage: once-per-stage talents (Undying Vow, Phoenix Rebirth) are ready again.
        public void OnStageStart()
        {
            vowUsed = false;
            guardianReviveUsed = false;
            barrier = 0f;
            hp = maxHp;
            mana = MaxMana;
        }

        /// Builds or removes talent visuals (Wings of Blood, Sunfire Orbs).
        void OnTalentsChanged()
        {
            mods.Clear();
            foreach (var id in talents)
            {
                var d = Data.FindTalent(id);
                if (d == null) continue;
                foreach (var kv in d.mods) mods[kv.Key] = Mod(kv.Key) + kv.Value;
            }
            if (Has("h_wings") && wings == null) BuildWings();
            if (!Has("h_wings") && wings != null) { Destroy(wings.gameObject); wings = null; }
            if (Has("p_sunorbs") && orbs == null) BuildOrbs();
            if (!Has("p_sunorbs") && orbs != null) { Destroy(orbs.gameObject); orbs = null; }
        }

        void BuildWings()
        {
            wings = FX.Node(transform, "WingsOfBlood", new Vector3(0, 1.45f, 0.22f));
            var mat = FX.Glow(new Color(0.85f, 0.05f, 0.12f, 0.85f), 1.6f);
            foreach (int s in new[] { -1, 1 })
            {
                var w = FX.Node(wings, s < 0 ? "L" : "R", Vector3.zero, new Vector3(0, s * 0.35f, s * 0.25f));
                for (int i = 0; i < 3; i++)
                {
                    float len = 1.3f - i * 0.25f;
                    FX.NoShadow(FX.Part(w, MeshGen.Cylinder(0f, 0.5f, 4), mat, new Vector3(s * (0.3f + len * 0.45f), 0.25f - i * 0.22f, 0.05f),
                        new Vector3(0.28f, len, 0.03f), new Vector3(0, 0, -s * (1.1f + i * 0.25f))));
                }
            }
        }

        void FlapWings()
        {
            if (wings == null) return;
            var w = wings;
            w.localScale = new Vector3(1.4f, 1.2f, 1f);
            Tw.Run(Tw.To(() => w.localScale, v => w.localScale = v, Vector3.one, 0.3f, w));
        }

        void BuildOrbs()
        {
            orbs = new GameObject("SunfireOrbs").transform;
            orbs.SetParent(transform.parent, false);
            for (int i = 0; i < 3; i++)
            {
                var o = FX.Node(orbs, "Orb" + i);
                FX.NoShadow(FX.Part(o, MeshGen.Sphere(12), FX.Glow(new Color(1f, 0.6f, 0.15f), 3.2f), Vector3.zero, Vector3.one * 0.55f));
                FX.NoShadow(FX.Part(o, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.9f, 0.6f), 3.5f), Vector3.zero, Vector3.one * 0.28f));
                FX.Particles(o, new FX.ParticleSpec
                {
                    amount = 20, lifetime = 0.4f, size = 0.35f, speedMin = 0.2f, speedMax = 0.8f, spread = 180f, worldSpace = true,
                    startColor = new Color(1f, 0.75f, 0.3f, 1f), endColor = new Color(0.8f, 0.15f, 0f, 0f),
                });
            }
        }

        void OnDestroy()
        {
            if (orbs) Destroy(orbs.gameObject);
        }

        // ---------------------------------------------------------------- per frame

        void TickTalents(float delta)
        {
            invulnT = Mathf.Max(0f, invulnT - delta);
            aegisCd = Mathf.Max(0f, aegisCd - delta);
            sanguineCd = Mathf.Max(0f, sanguineCd - delta);
            dragonFormCd = Mathf.Max(0f, dragonFormCd - delta);
            rescueCd = Mathf.Max(0f, rescueCd - delta);
            bulwarkCd = Mathf.Max(0f, bulwarkCd - delta);
            TickGuardian(delta);
            if (talents.Count == 0 && !DragonFormActive) return;

            if (Has("k_rally"))
            {
                rallyT -= delta;
                if (rallyT <= 0f)
                {
                    rallyT = 10f;
                    barrier = Mathf.Max(barrier, maxHp * 0.15f);
                    FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 3f, new Color(0.6f, 0.8f, 1f, 0.7f), 0.35f);
                }
            }
            if (Has("k_charge")) TickCharge(delta);
            if (Has("h_shadowstep")) TickShadowstep(delta);
            if (Has("h_bloodmoon")) TickBloodMoon(delta);
            if (Has("p_firewalk")) TickFireWalk(delta);
            if (Has("p_blink")) TickBlink(delta);
            if (Has("p_supernova"))
            {
                novaT -= delta;
                if (novaT <= 0f && game.NearestEnemy(Pos, 18f, 10f) != null) { novaT = 35f; Supernova(); }
            }
            if (Has("p_pillars"))
            {
                pillarT -= delta;
                if (pillarT <= 0f && game.NearestEnemy(Pos, 12f, 8f) != null) { pillarT = 12f; PillarsOfFlame(); }
            }
            if (orbs) TickOrbs(delta);
            if (DragonFormActive) TickDragonForm(delta);
        }

        // ---- Knight

        void TickCharge(float delta)
        {
            if (chargeT > 0f)
            {
                chargeT -= delta;
                foreach (var e in game.EnemiesInRadius(Pos, 2.4f, 3.5f))
                {
                    if (chargeHit.Contains(e)) continue;
                    chargeHit.Add(e);
                    game.DamageEnemy(e, WeaponBaseDamage * WeaponDamageMult * 1.3f, M.Flat(e.Pos - Pos).normalized + chargeDir, 14f, true, new Color(1f, 0.6f, 0.25f));
                    e.Ignite(4f, WeaponBaseDamage * 0.15f * BurnMult);
                }
                if (M.RandF() < 0.6f) FX.Burst(Pos + Vector3.up * 0.6f, new Color(1f, 0.5f, 0.15f), 5, 4f, 0.3f);
                if (chargeT <= 0f)
                {
                    FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 5f, new Color(1f, 0.55f, 0.2f, 0.9f), 0.35f);
                    Sfx.Play("boom", -4f, 0.1f);
                }
                return;
            }
            chargeCd -= delta;
            if (chargeCd > 0f || flying || leaping || DragonFormActive) return;
            var at = game.DensestPoint(Pos, 18f);
            if (M.IsInf(at)) { chargeCd = 0.5f; return; }
            var d = M.Flat(at - Pos);
            if (d.magnitude < 3f) { chargeCd = 0.5f; return; }
            chargeCd = 8f;
            chargeDir = d.normalized;
            chargeT = Mathf.Clamp(d.magnitude / 42f + 0.08f, 0.15f, 0.45f);
            chargeHit.Clear();
            yaw = Mathf.Atan2(-chargeDir.x, -chargeDir.z);
            Sfx.Play("whoosh", 0f, 0.1f);
            if (rig) rig.Action(0.4f, SWING);
        }

        /// Thunderclap: landing after a double jump slams the ground.
        void OnLanded()
        {
            if (!Has("k_thunderclap") || airJumps == 0 || flying || DragonFormActive) return;
            airJumps = 0;
            float rad = 6f;
            float dmg = WeaponBaseDamage * WeaponDamageMult * 2.5f;
            FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), rad, new Color(0.7f, 0.85f, 1f, 0.9f), 0.4f);
            if (!FX.Vfx("ground_explosion", Pos, rad / 6f, 2f, null, 0.1f, new Color(0.6f, 0.8f, 1f)))
                FX.Flash(Pos + Vector3.up * 0.6f, rad * 0.7f, new Color(0.7f, 0.85f, 1f, 0.7f), 0.3f);
            Sfx.Play("boom", 0f, 0.05f);
            game.Shake(0.5f);
            foreach (var e in game.EnemiesInRadius(Pos, rad, 4f))
            {
                game.DamageEnemy(e, dmg, M.Flat(e.Pos - Pos).normalized, 10f, true, new Color(0.75f, 0.88f, 1f));
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 0.8f);
            }
        }

        void OnBlock()
        {
            if (Has("k_bulwark")) Heal(maxHp * 0.02f);
            if (Has("k_bash"))
            {
                FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 5f, new Color(0.5f, 0.7f, 1f, 0.9f), 0.35f);
                foreach (var e in game.EnemiesInRadius(Pos, 5f, 3f))
                {
                    game.DamageEnemy(e, WeaponBaseDamage * WeaponDamageMult * 0.6f, M.Flat(e.Pos - Pos).normalized, 12f, true, new Color(0.6f, 0.8f, 1f));
                    if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 1f);
                }
            }
        }

        void Retribution(float amount)
        {
            float dmg = amount * 0.3f * 3f * DamageMult;
            var list = game.EnemiesInRadius(Pos, 6f, 4f);
            if (list.Count == 0) return;
            FX.Shockwave(Pos + new Vector3(0, 0.5f, 0), 6f, new Color(1f, 0.85f, 0.4f, 0.8f), 0.3f);
            foreach (var e in list) game.DamageEnemy(e, dmg, Vector3.zero, 0f, true, new Color(1f, 0.85f, 0.4f));
        }

        /// Talent damage reductions (Sky Sovereign / Phantom Hunt while flying, Dragon Form).
        float DamageTakenMult
        {
            get
            {
                float m = 1f;
                if (flying && Has("k_sky")) m *= 0.7f;
                if (flying && Has("h_phantom")) m *= 0.75f;
                if (flying) m *= 1f - Mathf.Min(Pct("mountdr%"), 0.6f);
                if (DragonFormActive) m *= formKind == "specter" ? 0.4f : 0.5f;
                return m * (1f - Mathf.Min(Pct("dmgtaken%"), 0.5f));
            }
        }

        /// After taking damage: Aegis of Scales, Living Bulwark, Guardian's Talons.
        void AfterHurt()
        {
            if (Has("dr_bulwark") && bulwarkCd <= 0f && hp < maxHp * 0.3f)
            {
                bulwarkCd = 45f;
                pet.Eruption(Pos);
            }
            TryRescue();
            if (Has("k_aegis") && aegisCd <= 0f && hp < maxHp * 0.3f)
            {
                aegisCd = 60f;
                invulnT = 3f;
                Heal(maxHp * 0.2f);
                FX.Text(Center + Vector3.up * 2f, "Aegis!", new Color(0.7f, 0.85f, 1f));
                FX.Shockwave(Pos + new Vector3(0, 0.5f, 0), 5f, new Color(0.6f, 0.8f, 1f, 0.9f), 0.5f);
                if (!FX.Vfx("buff", Pos, 1.2f, 3f, null, 0f)) FX.Flash(Center, 2.5f, new Color(0.6f, 0.8f, 1f, 0.6f), 0.5f);
            }
        }

        /// Undying Vow / Ember Rebirth: once per stage, survive a killing blow. Failing that, the dragon tree's
        /// Rekindle / Pact of Wings: your dragon saves you at 40% health, also once per stage.
        bool CheatDeath()
        {
            bool vow = Has("k_vow"), phoenix = Has("p_phoenix");
            if ((!vow && !phoenix) || vowUsed) return GuardianRevive();
            vowUsed = true;
            hp = maxHp * (phoenix ? 1f : 0.5f);
            invulnT = 2f;
            var col = phoenix ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 0.9f, 0.55f);
            FX.Text(Center + Vector3.up * 2f, phoenix ? "Ember Rebirth!" : "Undying Vow!", col);
            FX.Flash(Center, 8f, M.WithA(col, 0.8f), 0.6f);
            FX.Shockwave(Pos + new Vector3(0, 0.4f, 0), 10f, M.WithA(col, 0.9f), 0.6f);
            if (!FX.Vfx("red_explosion", Pos, 2f, 2.5f, null, 0f, col)) FX.Burst(Center, col, 40, 14f, 0.5f);
            Sfx.Play("roar", 0f, 0f);
            game.Shake(0.8f);
            foreach (var e in game.EnemiesInRadius(Pos, 10f, 6f))
            {
                game.DamageEnemy(e, (200f + 30f * level) * DamageMult, M.Flat(e.Pos - Pos).normalized, 16f, true, col);
                e.Ignite(5f, 20f * BurnMult);
            }
            game.hud.Banner(phoenix ? "Ember Rebirth" : "Undying Vow", "Death is refused, once.");
            return true;
        }

        bool GuardianRevive()
        {
            if ((!Has("db_rekindle") && !Has("dk_pact")) || guardianReviveUsed) return false;
            guardianReviveUsed = true;
            hp = maxHp * 0.4f;
            invulnT = 2.5f;
            var col = Data.DRAGONS[dragonId].breath;
            FX.Text(Center + Vector3.up * 2f, Has("db_rekindle") ? "Rekindled!" : "Pact of Wings!", col);
            FX.Flash(Center, 7f, M.WithA(col, 0.7f), 0.6f);
            FX.Shockwave(Pos + new Vector3(0, 0.4f, 0), 9f, M.WithA(col, 0.9f), 0.6f, Data.DRAGONS[dragonId].glowBreath);
            Sfx.Play("roar", 2f, 0f);
            pet.GuardianSwoop(Pos);
            game.hud.Banner("Your dragon will not let you fall", "Once per stage");
            return true;
        }

        // ---- the dragon looks after you (dragon cards and dragon tree)

        int RescueRank => SpellLv("d_rescue", 0) + Mathf.RoundToInt(Mod("rescue"));

        /// Guardian's Talons: low on health and on foot, your dragon swoops in and carries you off.
        void TryRescue()
        {
            int r = RescueRank;
            if (r <= 0 || rescueCd > 0f || flying || leaping || DragonFormActive || hp > maxHp * 0.35f || hp <= 0f) return;
            rescueCd = Mathf.Max(30f, 100f - 10f * r);
            SummonDragon();
            remountWait = 0f;
            velocity.y = 14f;
            onGround = false;
            invulnT = Mathf.Max(invulnT, 1.5f);
            flightStamina = Mathf.Max(flightStamina, MaxFlight * 0.6f);
            game.rescues++;
            FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 7f, M.WithA(Data.DRAGONS[dragonId].breath, 0.8f), 0.4f);
            Sfx.Play("roar", 0f, 0.05f);
            game.hud.Toast("Your dragon snatches you out of danger!");
        }

        /// Crimson Tide: standing in Blood Rain heals you.
        void TickGuardian(float delta)
        {
            if (!Has("dk_tide") || !BloodRain.Covers(Pos)) return;
            tideTick -= delta;
            if (tideTick > 0f) return;
            tideTick = 0.5f;
            Heal(maxHp * 0.01f);
        }

        /// Wrathful Guardian: a chance that your dragon scorches whatever just hurt you.
        void DragonWrath(Enemy attacker)
        {
            int r = SpellLv("d_wrath", 0) + Mathf.RoundToInt(Mod("wrath"));
            if (r <= 0 || attacker == null || attacker.dead || M.RandF() > 0.2f * r) return;
            pet.Scorch(attacker);
        }

        // ---- Huntress

        void OnDodge()
        {
            if (Has("h_sanguine") && sanguineCd <= 0f)
            {
                sanguineCd = 6f;
                barrier = Mathf.Max(barrier, maxHp * 0.1f);
                FX.Shockwave(Pos + new Vector3(0, 0.4f, 0), 2.5f, new Color(0.9f, 0.1f, 0.15f, 0.8f), 0.3f);
            }
        }

        void TickShadowstep(float delta)
        {
            shadowCd -= delta;
            if (shadowCd > 0f || flying || DragonFormActive) return;
            var near = game.NearestEnemy(Pos, 2.6f, 3f);
            if (near == null) return;
            shadowCd = 7f;
            var away = M.Flat(Pos - near.Pos);
            if (away.sqrMagnitude < 0.01f) away = -CamFwd;
            away = away.normalized;
            var from = Pos;
            var to = from + away * 8f;
            float lim = WorldTerrain.PLAY_LIMIT;
            to.x = Mathf.Clamp(to.x, -lim, lim);
            to.z = Mathf.Clamp(to.z, -lim, lim);
            to.y = game.terrain.GroundHeight(to.x, to.z, from.y + 1f);
            if (Mathf.Abs(to.y - from.y) > 6f || game.terrain.PointInObstacle(to + Vector3.up, 0.4f)) { shadowCd = 1f; return; }
            FX.Burst(from + Vector3.up, new Color(0.6f, 0.02f, 0.06f), 20, 6f, 0.35f);
            Pos = to;
            velocity = Vector3.zero;
            FX.Burst(to + Vector3.up, new Color(0.6f, 0.02f, 0.06f), 14, 5f, 0.3f);
            Sfx.Play("whoosh", -2f, 0.1f);
            // The decoy: a crimson silhouette that bursts a moment later.
            var decoy = new GameObject("BloodDecoy").transform;
            decoy.SetParent(FX.root, false);
            decoy.position = from;
            var m = FX.Glow(new Color(0.8f, 0.05f, 0.1f, 0.8f), 1.8f);
            FX.NoShadow(FX.Part(decoy, MeshGen.Capsule(), m, new Vector3(0, 1f, 0), new Vector3(0.5f, 0.9f, 0.5f)));
            FX.NoShadow(FX.Part(decoy, MeshGen.Sphere(10), m, new Vector3(0, 1.95f, 0), Vector3.one * 0.32f));
            float dmg = WeaponBaseDamage * WeaponDamageMult * 3f;
            var g = game;
            Tw.After(0.7f, () =>
            {
                if (decoy == null || g == null) return;
                var p = decoy.position;
                FX.Flash(p + Vector3.up, 5f, new Color(0.9f, 0.05f, 0.1f, 0.8f), 0.35f);
                FX.Shockwave(p + new Vector3(0, 0.3f, 0), 5.5f, new Color(0.85f, 0.05f, 0.1f, 0.9f), 0.4f, false);
                FX.Burst(p + Vector3.up, new Color(0.7f, 0f, 0.05f), 30, 10f, 0.4f);
                Sfx.Play("boom", p, -2f, 0.1f);
                foreach (var e in g.EnemiesInRadius(p, 5.5f, 4f))
                {
                    g.DamageEnemy(e, dmg, M.Flat(e.Pos - p).normalized, 10f, true, new Color(1f, 0.3f, 0.35f));
                    e.Bleed(3f, dmg * 0.1f);
                }
                Destroy(decoy.gameObject);
            });
        }

        void TickBloodMoon(float delta)
        {
            if (moonT > 0f)
            {
                moonT -= delta;
                orbTick -= delta;
                if (orbTick <= 0f)
                {
                    orbTick = 0.5f;
                    foreach (var e in game.EnemiesInRadius(Pos, 12f, 6f)) e.Bleed(1.2f, 6f + 1.5f * level);
                }
                if (moonLight) moonLight.intensity = Mathf.Lerp(0f, 2.5f, Mathf.Clamp01(moonT));
                if (moonT <= 0f && moonLight) { Destroy(moonLight.gameObject); moonLight = null; }
                return;
            }
            moonCd -= delta;
            if (moonCd > 0f || game.NearestEnemy(Pos, 20f, 10f) == null) return;
            moonCd = 30f;
            moonT = 8f;
            game.hud.Banner("Blood Moon", "Triple javelins · lifesteal · everything bleeds");
            Sfx.Play("roar", -4f, 0f);
            FX.Flash(Center, 10f, new Color(0.9f, 0.05f, 0.1f, 0.5f), 0.8f);
            moonLight = new GameObject("BloodMoonLight").AddComponent<Light>();
            moonLight.transform.SetParent(transform, false);
            moonLight.transform.localPosition = new Vector3(0, 8f, 0);
            moonLight.type = LightType.Point;
            moonLight.color = new Color(1f, 0.1f, 0.12f);
            moonLight.range = 26f;
            moonLight.intensity = 2.5f;
            moonLight.shadows = LightShadows.None;
        }

        // ---- Ember Mage

        void TickFireWalk(float delta)
        {
            walkFireT -= delta;
            if (walkFireT > 0f || !onGround || flying) return;
            if (new Vector2(velocity.x, velocity.z).magnitude < 2f) return;
            walkFireT = 0.3f;
            FirePatch.Spawn(game, Pos, 1.7f, 3f, (8f + 2f * level) * BurnMult * DamageMult);
        }

        void TickBlink(float delta)
        {
            blinkCd -= delta;
            if (blinkCd > 0f || flying || DragonFormActive) return;
            if (game.EnemiesInRadius(Pos, 4f, 3f).Count < 5) return;
            blinkCd = 6f;
            var from = Pos;
            Vector3 best = from;
            int bestN = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float a = M.TAU * i / 8f;
                var p = from + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 10f;
                float lim = WorldTerrain.PLAY_LIMIT;
                if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.z) > lim) continue;
                p.y = game.terrain.GroundHeight(p.x, p.z, from.y + 1f);
                if (Mathf.Abs(p.y - from.y) > 6f || game.terrain.PointInObstacle(p + Vector3.up, 0.4f)) continue;
                int n = game.EnemiesInRadius(p, 5f, 4f).Count;
                if (n < bestN) { bestN = n; best = p; }
            }
            if (best == from) { blinkCd = 1f; return; }
            Explosion(from, 5f, (60f + 12f * level) * DamageMult, new Color(1f, 0.5f, 0.12f));
            Pos = best;
            velocity = Vector3.zero;
            FX.Flash(best + Vector3.up, 2.5f, new Color(1f, 0.6f, 0.2f, 0.7f), 0.25f);
            Sfx.Play("whoosh", 0f, 0.1f);
        }

        void Explosion(Vector3 at, float rad, float dmg, Color col)
        {
            if (!FX.Vfx("explosion", at, rad / 3f, 2f, null, 0.02f, col)) FX.Flash(at + Vector3.up, rad, M.WithA(col, 0.85f), 0.35f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.1f, M.WithA(col, 0.85f), 0.35f);
            Sfx.Play("boom", at, -2f, 0.05f);
            foreach (var e in game.EnemiesInRadius(at, rad, 5f))
            {
                game.DamageEnemy(e, dmg, M.Flat(e.Pos - at).normalized, 8f, true, Color.Lerp(col, Color.white, 0.4f));
                e.Ignite(4f, dmg * 0.1f * BurnMult);
            }
        }

        void Supernova()
        {
            var col = new Color(1f, 0.75f, 0.3f);
            float rad = 18f;
            game.hud.Toast("Supernova!");
            FX.Flash(Center, rad, new Color(1f, 0.8f, 0.4f, 0.8f), 0.7f);
            FX.Shockwave(Pos + new Vector3(0, 0.4f, 0), rad, new Color(1f, 0.7f, 0.3f, 0.95f), 0.7f);
            FX.Shockwave(Pos + new Vector3(0, 2f, 0), rad * 0.7f, new Color(1f, 0.95f, 0.7f, 0.8f), 0.5f);
            if (!FX.Vfx("red_explosion", Pos, 3.5f, 3f, null, 0f, col)) FX.Burst(Center, col, 60, 20f, 0.6f);
            Sfx.Play("boom", 6f, 0f);
            game.Shake(1f);
            foreach (var e in game.EnemiesInRadius(Pos, rad, 10f))
            {
                game.DamageEnemy(e, (320f + 40f * level) * SpellDamageMult, M.Flat(e.Pos - Pos).normalized, 18f, true, col);
                e.Ignite(5f, 25f * BurnMult);
            }
        }

        void PillarsOfFlame()
        {
            const int n = 8;
            float dmg = (55f + 10f * level) * SpellDamageMult;
            var center = Pos;
            for (int i = 0; i < n; i++)
            {
                float a = M.TAU * i / n;
                var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 7f;
                p.y = game.terrain.SurfaceHeight(p.x, p.z, center.y + 1f);
                var at = p;
                Tw.After(i * 0.05f, () => FirePillar.Spawn(game, at, 2.6f, dmg));
            }
            Sfx.Play("flame", 0f, 0.05f);
        }

        void TickOrbs(float delta)
        {
            orbs.position = Center;
            float t = Time.time * 2.4f;
            for (int i = 0; i < orbs.childCount; i++)
            {
                float a = t + M.TAU * i / orbs.childCount;
                orbs.GetChild(i).localPosition = new Vector3(Mathf.Cos(a) * 3.2f, Mathf.Sin(t * 1.3f + i) * 0.4f, Mathf.Sin(a) * 3.2f);
            }
            orbTick -= delta;
            if (orbTick > 0f) return;
            orbTick = 0.2f;
            float dmg = (12f + 3f * level) * SpellDamageMult;
            for (int i = 0; i < orbs.childCount; i++)
            {
                var op = orbs.GetChild(i).position;
                foreach (var e in game.EnemiesInRadius(op - Vector3.up, 1.3f, 2.5f))
                {
                    game.DamageEnemy(e, dmg, Vector3.zero, 0f, true, new Color(1f, 0.7f, 0.3f));
                    e.Ignite(3f, dmg * 0.25f * BurnMult);
                }
            }
        }

        // ---- shared

        /// A kill was made: Thirst heals, Combustion detonates burning ogres.
        public void OnKill(Enemy e)
        {
            if (Has("h_thirst")) hp = Mathf.Min(maxHp, hp + maxHp * 0.015f * HealMult);
            if (Has("p_combustion") && e.Burning)
            {
                var at = e.Pos;
                FX.Flash(at + Vector3.up, 3f, new Color(1f, 0.5f, 0.1f, 0.8f), 0.25f);
                FX.Burst(at + Vector3.up, new Color(1f, 0.55f, 0.15f), 14, 8f, 0.3f);
                foreach (var o in game.EnemiesInRadius(at, 4f, 3f))
                {
                    if (o == e) continue;
                    game.DamageEnemy(o, (25f + 5f * level) * DamageMult, M.Flat(o.Pos - at).normalized, 5f, false);
                    o.Ignite(4f, (8f + 2f * level) * BurnMult);
                }
            }
        }

        // ---------------------------------------------------------------- capstone forms (F)

        // Knight: Dragon Form (fly, breathe fire). Huntress: Crimson Specter (a blood wraith on foot). Ember Mage: Phoenix
        // Form (fly, rain fire). All three share the timer, cooldown and DragonFormActive.
        DragonModel formModel;
        ParticleSystem formBreath;
        float formBreathTick;
        Vector3 formBreathDir = Vector3.forward;
        Transform specterAura;

        public const float DRAGON_FORM_TIME = 12f, SPECTER_TIME = 10f, DRAGON_FORM_CD = 45f;

        /// The capstone form this hero has picked ("" for none).
        string CapstoneForm => Has("k_dragonform") ? "dragon" : Has("h_specter") ? "specter" : Has("p_phoenixform") ? "phoenix" : "";

        /// A flying form (Dragon Form, Phoenix Form): the hero soars instead of walking.
        public bool SoaringForm => DragonFormActive && formKind != "specter";
        public bool SpecterActive => DragonFormActive && formKind == "specter";

        static string FormName(string kind) => kind == "specter" ? "Crimson Specter" : kind == "phoenix" ? "Phoenix Form" : "Dragon Form";

        void TryDragonForm()
        {
            string kind = CapstoneForm;
            if (kind == "" || DragonFormActive) return;
            if (dragonFormCd > 0f)
            {
                game.hud.Toast($"{FormName(kind)} ready in {Mathf.CeilToInt(dragonFormCd)} s");
                return;
            }
            if (leaping) return;
            formKind = kind;
            dragonFormT = kind == "specter" ? SPECTER_TIME : DRAGON_FORM_TIME;
            dragonFormCd = DRAGON_FORM_CD;
            if (kind == "specter")
            {
                StartSpecter();
                return;
            }
            ReleaseDragon();
            remountWait = 0f;
            bool phoenix = kind == "phoenix";
            var def = phoenix
                ? new Data.DragonDef
                {
                    body = new Color(1f, 0.45f, 0.1f), edge = new Color(0.55f, 0.08f, 0.02f), belly = new Color(1f, 0.85f, 0.35f),
                    membrane = new Color(1f, 0.6f, 0.15f), horn = new Color(1f, 0.9f, 0.5f), eye = new Color(1f, 1f, 0.7f),
                }
                : new Data.DragonDef
                {
                    body = new Color(0.25f, 0.45f, 0.85f), edge = new Color(0.05f, 0.08f, 0.25f), belly = new Color(0.95f, 0.82f, 0.45f),
                    membrane = new Color(0.12f, 0.2f, 0.55f), horn = new Color(0.95f, 0.8f, 0.35f), eye = new Color(1f, 0.9f, 0.4f),
                };
            var go = new GameObject(phoenix ? "PhoenixForm" : "DragonForm");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (phoenix ? 0.45f : 0.55f);
            formModel = go.AddComponent<DragonModel>().Setup(def);
            formModel.fold = 0f;
            if (phoenix)
            {
                // A body of living flame: every part glows, alternating the phoenix's body and wing colors.
                var flame = new[] { FX.Glow(def.body, 1.6f), FX.Glow(def.membrane, 1.8f), FX.Glow(def.belly, 1.4f) };
                int i = 0;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = flame[i++ % flame.Length];
            }
            formBreath = FX.Particles(transform, new FX.ParticleSpec
            {
                amount = 160, lifetime = 0.5f, size = 0.9f, scaleMin = 0.8f, scaleMax = 2.4f, speedMin = 22f, speedMax = 30f, spread = 13f, emitting = false,
                startColor = new Color(1f, 0.9f, 0.6f, 1f), midColor = new Color(1f, 0.55f, 0.15f, 1f), endColor = new Color(0.5f, 0.1f, 0f, 0f), boost = 1.5f,
            });
            SetHeroVisible(false);
            if (staffGem) staffGem.gameObject.SetActive(false);
            velocity.y = 8f;
            onGround = false;
            FX.Flash(Center, 6f, new Color(1f, 0.8f, 0.4f, 0.8f), 0.5f);
            FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 8f, new Color(1f, 0.75f, 0.3f, 0.9f), 0.5f);
            Sfx.Play("roar", 2f, 0f);
            if (phoenix) game.hud.Banner("PHOENIX FORM", "Rise and rain fire on them all");
            else game.hud.Banner("DRAGON FORM", "Fly freely and burn them all");
        }

        void StartSpecter()
        {
            var col = new Color(0.85f, 0.05f, 0.12f);
            specterAura = FX.Node(transform, "CrimsonSpecter", new Vector3(0, 1f, 0));
            FX.Particles(specterAura, new FX.ParticleSpec
            {
                amount = 60, lifetime = 0.6f, size = 0.5f, speedMin = 0.5f, speedMax = 2f, spread = 180f, worldSpace = true,
                boxExtents = new Vector3(0.5f, 0.9f, 0.5f), gravity = new Vector3(0, 1.5f, 0),
                startColor = new Color(1f, 0.15f, 0.2f, 0.9f), endColor = new Color(0.3f, 0f, 0.02f, 0f),
            });
            var l = specterAura.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = col;
            l.range = 8f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;
            FX.Flash(Center, 5f, M.WithA(col, 0.7f), 0.5f);
            FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 7f, M.WithA(col, 0.9f), 0.5f, false);
            Sfx.Play("roar", 0f, 0.1f);
            game.hud.Banner("CRIMSON SPECTER", "Through them, and drink deep");
        }

        void TickDragonForm(float delta)
        {
            dragonFormT -= delta;
            if (dragonFormT <= 0f)
            {
                EndDragonForm();
                return;
            }
            if (formKind == "specter") return;
            if (formKind == "phoenix") PhoenixRain(delta);
            formBreathTick -= delta;
            var mouth = formModel ? formModel.mouth.position : Center;
            var target = game.NearestEnemy(Pos, 18f, 26f);
            if (target)
            {
                var want = (target.Pos + new Vector3(0, 0.8f, 0) - mouth).normalized;
                formBreathDir = Vector3.Lerp(formBreathDir, want, Mathf.Clamp01(delta * 8f)).normalized;
                if (formBreath && !formBreath.isEmitting) formBreath.Play();
                if (formModel) formModel.jawOpen = 1f;
            }
            else if (formBreath && formBreath.isEmitting) formBreath.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (formBreath) formBreath.transform.SetPositionAndRotation(mouth, Quaternion.LookRotation(formBreathDir));
            if (target && formBreathTick <= 0f)
            {
                formBreathTick = 0.15f;
                float dmg = (22f + 6f * level) * DamageMult;
                float cosA = Mathf.Cos(24f * Mathf.Deg2Rad);
                foreach (var e in game.EnemiesInRadius(mouth, 18f, 30f))
                {
                    var v = e.Pos + new Vector3(0, 0.8f, 0) - mouth;
                    if (v.magnitude > 2f && Vector3.Dot(v.normalized, formBreathDir) < cosA) continue;
                    game.DamageEnemy(e, dmg, formBreathDir, 1f, true, new Color(1f, 0.75f, 0.35f));
                    e.Ignite(4f, dmg * 0.3f * BurnMult);
                }
            }
        }

        /// Phoenix Form: burning feathers rain on the ground below, leaving fire behind.
        void PhoenixRain(float delta)
        {
            phoenixRainT -= delta;
            if (phoenixRainT > 0f) return;
            phoenixRainT = 0.35f;
            var at = Pos + new Vector3(M.RandRange(-6f, 6f), 0f, M.RandRange(-6f, 6f));
            at.y = game.terrain.SurfaceHeight(at.x, at.z, Pos.y);
            FX.Bolt(Center, at, new Color(1f, 0.6f, 0.2f), 0.25f, 0.2f, 3);
            Explosion(at, 3.5f, (40f + 8f * level) * SpellDamageMult, new Color(1f, 0.55f, 0.15f));
            FirePatch.Spawn(game, at, 2.2f, 3f, (10f + 2f * level) * BurnMult * DamageMult);
        }

        void AnimateDragonForm(float delta, float hspeed)
        {
            if (formModel == null) return;
            formModel.transform.localRotation = Quaternion.identity;
            formModel.transform.localPosition = new Vector3(0, 0.6f, 0);
            formModel.Animate(delta, hspeed, true, velocity.y);
        }

        void EndDragonForm()
        {
            if (formModel == null && specterAura == null && dragonFormT <= 0f) return;
            dragonFormT = 0f;
            if (formModel) Destroy(formModel.gameObject);
            formModel = null;
            if (formBreath) Destroy(formBreath.gameObject);
            formBreath = null;
            if (specterAura) Destroy(specterAura.gameObject);
            specterAura = null;
            if (knight) SetHeroVisible(true);
            if (staffGem) staffGem.gameObject.SetActive(true);
            if (game != null && game.running) FX.Flash(Center, 3f, new Color(1f, 0.8f, 0.4f, 0.6f), 0.3f);
        }
    }

    // ==================================================================== talent effects

    /// A patch of fire on the ground that burns ogres standing in it (Fire Walk, Starfall).
    public class FirePatch : MonoBehaviour
    {
        Game game;
        float radius, life, dps, tick;
        Color color;

        public static FirePatch Spawn(Game g, Vector3 at, float radius, float life, float dps, Color? color = null)
        {
            var p = new GameObject("FirePatch").AddComponent<FirePatch>();
            p.transform.SetParent(FX.root, false);
            p.transform.position = at + new Vector3(0, 0.05f, 0);
            p.game = g;
            p.radius = radius;
            p.life = life;
            p.dps = dps;
            p.color = color ?? new Color(1f, 0.45f, 0.1f);
            FX.NoShadow(FX.Part(p.transform, MeshGen.Disc(), FX.Glow(M.WithA(p.color, 0.45f), 1.4f), Vector3.zero, new Vector3(radius, 1f, radius)));
            FX.Particles(p.transform, new FX.ParticleSpec
            {
                amount = 18, lifetime = 0.6f, size = 0.55f, speedMin = 1f, speedMax = 2.5f, spread = 15f, worldSpace = true,
                boxExtents = new Vector3(radius * 0.6f, 0.05f, radius * 0.6f), gravity = new Vector3(0, 2f, 0),
                startColor = Color.Lerp(p.color, Color.white, 0.4f), endColor = M.WithA(M.Mul(p.color, 0.4f), 0f),
            }).transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            return p;
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            life -= delta;
            tick -= delta;
            if (tick <= 0f)
            {
                tick = 0.4f;
                foreach (var e in game.EnemiesInRadius(transform.position, radius, 2.5f))
                {
                    game.DamageEnemy(e, dps * 0.4f, Vector3.zero, 0f, false);
                    e.Ignite(2f, dps * 0.25f);
                }
            }
            if (life < 0.5f) transform.localScale = Vector3.one * Mathf.Max(0.01f, life / 0.5f);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    /// A column of fire erupting from the ground (Pillars of Flame).
    public class FirePillar : MonoBehaviour
    {
        public static void Spawn(Game g, Vector3 at, float radius, float dmg)
        {
            var col = new Color(1f, 0.5f, 0.12f);
            FX.Telegraph(at, radius, 0.25f, new Color(1f, 0.5f, 0.1f, 0.4f));
            Tw.After(0.25f, () =>
            {
                if (g == null) return;
                var p = new GameObject("FirePillar").transform;
                p.SetParent(FX.root, false);
                p.position = at;
                var m = FX.Glow(new Color(1f, 0.6f, 0.2f, 0.85f), 2.6f);
                var r = FX.Part(p, MeshGen.Cylinder(0.3f, 0.5f, 12), m, new Vector3(0, 3f, 0), new Vector3(radius * 0.9f, 6f, radius * 0.9f));
                FX.NoShadow(r);
                FX.Particles(p, new FX.ParticleSpec
                {
                    amount = 40, lifetime = 0.7f, size = 0.9f, speedMin = 6f, speedMax = 11f, spread = 12f, worldSpace = true,
                    startColor = new Color(1f, 0.85f, 0.5f, 1f), endColor = new Color(0.6f, 0.1f, 0f, 0f), oneShot = true,
                }).transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                FX.Shockwave(at + new Vector3(0, 0.2f, 0), radius * 1.2f, M.WithA(col, 0.85f), 0.3f);
                foreach (var e in g.EnemiesInRadius(at, radius, 6f))
                {
                    g.DamageEnemy(e, dmg, Vector3.up, 4f, true, new Color(1f, 0.7f, 0.3f));
                    e.Ignite(4f, dmg * 0.15f * g.player.BurnMult);
                }
                Tw.Run(Tw.Seq(Tw.Lerp(0.5f, k => { if (r) r.transform.localScale = new Vector3(radius * 0.9f * (1f - k), 6f, radius * 0.9f * (1f - k)); }, p),
                    Tw.Call(() => { if (p) Destroy(p.gameObject); })));
            });
        }
    }

    /// A rolling wave of dragonfire launched by the Knight's sword (Dragonfire Blade).
    public class FireWave : MonoBehaviour
    {
        Player player;
        Vector3 dir;
        float damage, life = 0.55f;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static void Spawn(Player p, Vector3 at, Vector3 dir, float damage)
        {
            var w = new GameObject("FireWave").AddComponent<FireWave>();
            w.transform.SetParent(FX.root, false);
            w.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-dir));
            w.player = p;
            w.dir = dir;
            w.damage = damage;
            FX.NoShadow(FX.Part(w.transform, MeshGen.Box(), FX.Glow(new Color(1f, 0.55f, 0.15f, 0.8f), 2.4f), new Vector3(0, 0.5f, 0), new Vector3(3.2f, 1f, 0.35f)));
            FX.Particles(w.transform, new FX.ParticleSpec
            {
                amount = 40, lifetime = 0.35f, size = 0.7f, speedMin = 1f, speedMax = 3f, spread = 40f, worldSpace = true,
                boxExtents = new Vector3(1.6f, 0.3f, 0.1f), gravity = new Vector3(0, 3f, 0),
                startColor = new Color(1f, 0.8f, 0.4f, 1f), endColor = new Color(0.7f, 0.1f, 0f, 0f),
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = player.game;
            life -= delta;
            var p = transform.position + dir * 26f * delta;
            p.y = g.terrain.SurfaceHeight(p.x, p.z, p.y + 1f);
            transform.position = p;
            foreach (var e in g.EnemiesInRadius(p, 1.8f, 3f))
            {
                if (hit.Contains(e)) continue;
                hit.Add(e);
                g.DamageEnemy(e, damage, dir, 4f, true, new Color(1f, 0.65f, 0.3f));
                e.Ignite(3f, damage * 0.12f * player.BurnMult);
            }
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
