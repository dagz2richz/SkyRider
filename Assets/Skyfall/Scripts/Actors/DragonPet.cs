using System.Collections;
using UnityEngine;

namespace Skyfall
{
    /// Your dragon partner (Azure Wyrm, Crimson Drake or Night Wyrm). Flies beside you, breathes on its own,
    /// dive-bombs crowds by itself, carries you when you press Q, and unleashes its signature ability on E
    /// (on the ground or in the air; from the air your hero also leaps down and slams the ground). It also looks after
    /// you: its wing stops shots meant for you, it scorches what hurts you, and it snatches you out of danger.
    /// Upgrades come from the dragon card on level-ups (player.spells) and the dragon's permanent tree (player.Mod).
    public partial class DragonPet : MonoBehaviour
    {
        const float PET_SCALE = 0.45f;
        const float MOUNT_SCALE = 0.6f;

        public Game game;
        public Player player;
        public DragonModel model;
        public string dragonId = "";
        Color fire = new Color(0.25f, 0.6f, 1f);
        string state = "pet";       // pet, mount, dive
        Vector3 vel;
        float orbit;
        Vector3 faceDir = Vector3.forward;
        float yaw;

        // Breath (passive)
        float breathTimer = 2f;
        float breathActive;
        float breathTick;
        Vector3 breathDir = Vector3.forward;
        ParticleSystem breathFx;
        Transform breathNode;
        Light breathLight;

        // Auto dive (on foot) and signature (E)
        float commandCd = 4f;
        float rideCd;
        float diveT;
        Vector3 diveFrom, diveTo;

        public Vector3 Pos
        {
            get => transform.position;
            set => transform.position = value;
        }

        public void Init(Game g, Player p)
        {
            game = g;
            player = p;
            transform.localScale = Vector3.one * PET_SCALE;
            breathNode = new GameObject("Breath").transform;
            breathNode.SetParent(g.transform, false);
            breathFx = FX.Particles(breathNode, new FX.ParticleSpec
            {
                amount = 140, lifetime = 0.5f, size = 0.8f, scaleMin = 0.8f, scaleMax = 2.4f, speedMin = 20f, speedMax = 28f, spread = 13f,
                emitting = false,
            });
            breathLight = new GameObject("BreathLight").AddComponent<Light>();
            breathLight.transform.SetParent(g.transform, false);
            breathLight.type = LightType.Point;
            breathLight.range = 12f;
            breathLight.intensity = 0f;
            breathLight.shadows = LightShadows.None;
            if (dragonId == "") SetDragon("blue");
        }

        /// Swap to a different dragon (rebuilds the model and recolors its breath).
        public void SetDragon(string id)
        {
            if (id == dragonId && model) return;
            dragonId = id;
            var d = Data.DRAGONS[id];
            if (model) DestroyImmediate(model.gameObject);
            var go = new GameObject("DragonModel");
            go.transform.SetParent(transform, false);
            model = go.AddComponent<DragonModel>().Setup(d);
            fire = d.breath;
            ApplySkin(id);
            if (breathFx)
            {
                var start = Color.Lerp(Color.white, fire, 0.4f);
                FX.SetParticleColors(breathFx, start, new Color(fire.r * 0.4f, fire.g * 0.4f, fire.b * 0.4f, 0f), fire, 0.2f);
                breathFx.GetComponent<ParticleSystemRenderer>().material = FX.ParticleMat(d.glowBreath, 1.5f);
            }
            if (breathLight) breathLight.color = fire;
        }

        // ---------------------------------------------------------------- Asset Store dragon model

        CharacterRig skin;
        Transform skinMouth, skinChest;
        float skinBack;


        /// The procedural dragon is 12 units long at scale 1; pack dragons are scaled to match.
        const float DRAGON_LENGTH = 12f;

        readonly System.Collections.Generic.List<Material> skinMats = new System.Collections.Generic.List<Material>();

        void OnDestroy()
        {
            foreach (var m in skinMats) if (m) Destroy(m);
        }

        /// Instances the skin's materials once and multiplies their base color by a tint (the pack's own assets stay untouched).
        void TintSkin(Transform root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    skinMats.Add(m);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * tint);
                }
                r.materials = mats;
            }
        }

        void ApplySkin(string id)
        {
            foreach (var m in skinMats) if (m) Destroy(m);
            skinMats.Clear();
            if (skin) DestroyImmediate(skin.gameObject);
            skin = null;
            skinMouth = skinChest = null;
            // Every dragon is the same model (the Usurper) in its own colors.
            var def = Data.DRAGONS[id];
            var prefab = Skins.Lib ? Skins.Lib.Dragon(def.skin) : null;
            if (prefab == null) return;
            // Measure in a neutral pose so the rotation and current scale don't skew the bounds.
            var savedRot = transform.rotation;
            var savedScale = transform.localScale;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            var holder = FX.Node(transform, "DragonSkin");
            var go = Instantiate(prefab, holder, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            skin = holder.gameObject.AddComponent<CharacterRig>();
            skin.Init(go, null);
            var b = skin.Bounds();
            float len = Mathf.Max(b.size.z, 0.01f);
            holder.localScale = Vector3.one * (DRAGON_LENGTH / len);
            skinMouth = Skins.Find(go.transform, "UpperMouth", "UpperMouth01", "JawTip", "Head");
            if (def.skinTint != Color.white) TintSkin(go.transform, def.skinTint);
            skinChest = Skins.Find(go.transform, "Chest");
            if (skinChest) skinBack = (skinChest.position.y - transform.position.y) * 0.4f;
            transform.rotation = savedRot;
            transform.localScale = savedScale;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            skin.Loop("Idle", "Idle01");
        }

        /// The mouth the breath comes from.
        public Vector3 MouthPos
        {
            get
            {
                if (skinMouth) return skinMouth.position + transform.forward * -0.6f * transform.lossyScale.z;
                return model.mouth.position;
            }
        }

        /// Where the rider sits on a pack dragon (null for the procedural dragon, which uses its own seat).
        public Vector3? SeatWorld
        {
            get
            {
                if (skin == null || skinChest == null) return null;
                return skinChest.position + Vector3.up * skinBack * transform.lossyScale.y;
            }
        }

        static readonly string[] SK_IDLE = { "Idle", "Idle01" };
        static readonly string[] SK_HOVER = { "Fly Float", "Fly Forward" };
        static readonly string[] SK_FLY = { "Fly Forward", "Fly Glide", "Fly Float" };
        static readonly string[] SK_GLIDE = { "Fly Glide", "Fly Forward" };
        static readonly string[] SK_BREATH = { "Fly Fireball Shoot", "Fly Flame Attack", "Fly Forward" };
        static readonly string[] SK_ROAR = { "Fly Fireball Shoot", "Fly Flame Attack", "Scream" };
        static readonly string[] SK_WALK = { "Walk", "walk", "Idle" };
        static readonly string[] SK_RUN = { "Run", "run", "Walk" };
        static readonly string[] SK_GROUND_BREATH = { "Flame Attack", "Fireball Shoot", "Basic Attack", "Idle" };
        static readonly string[] SK_DEFEND = { "Defend", "Scream", "Idle01" };
        static readonly string[] SK_SNAP = { "Flame Attack", "Basic Attack", "Claw Attack" };

        /// The hatchling (world 1) can't fly yet: it walks beside you and only breathes fire (no dives, no signature).
        public bool Grounded => game != null && game.stage < Data.FLIGHT_STAGE;

        void AnimateGroundSkin(float speed)
        {
            if (skin == null) return;
            if (breathActive > 0f) skin.Loop(SK_GROUND_BREATH);
            else if (speed > 7f) skin.Loop(SK_RUN);
            else if (speed > 0.8f) skin.Loop(SK_WALK);
            else skin.Loop(SK_IDLE);
        }

        void AnimateSkin(float speed, bool perched)
        {
            if (skin == null) return;
            if (perched)
            {
                skin.Loop(SK_IDLE);
                return;
            }
            if (state == "dive") skin.Loop(SK_GLIDE);
            else if (breathActive > 0f) skin.Loop(SK_BREATH);
            else if (speed > 6f) skin.Loop(SK_FLY);
            else skin.Loop(SK_HOVER);
        }

        Data.DragonDef DataDef => Data.DRAGONS[dragonId];

        int Lvl(string id) => player.spells.TryGetValue(id, out int v) ? v : 1;
        /// Rank of a dragon card that starts at 0 (size, cone, haste, guardian instincts, the dragon's own).
        int Card(string id) => player.spells.TryGetValue(id, out int v) ? v : 0;

        /// 0 = hatchling (world 1), 1 = young (world 2), 2 = adult (world 3).
        int Age => Mathf.Clamp(game.stage - 1, 0, Data.DRAGON_AGE.Length - 1);

        float DmgMult => player.DamageMult * DataDef.damage * player.DragonDamageMult * Data.DRAGON_AGE_DAMAGE[Age];

        /// On foot the dragon dive-bombs crowds by itself on this cooldown.
        float CommandCooldown => 12f * Mathf.Pow(0.92f, Lvl("dragonfire") - 1) * player.DragonCdMult;

        public float CommandFrac => Mathf.Clamp01(commandCd / Mathf.Max(CommandCooldown, 0.01f));

        float RideCooldown => Data.SIGNATURE_CD * Mathf.Pow(0.94f, Lvl("ride") - 1) * player.DragonCdMult * (1f - Mathf.Min(player.Pct("sigcd%"), 0.5f));

        float SigDmg => 1f + player.Pct("sigdmg%");
        float SigArea => player.AreaMult * (1f + player.Pct("sigarea%")) * (1f + 0.1f * Card("d_red"));
        /// Dive radius: the dragon's size, Shatterscale (red) and tree picks.
        float DiveArea => player.AreaMult * (1f + 0.67f * (player.DragonSizeMult - 1f)) * (1f + player.Pct("diverad%")) * (1f + 0.1f * Card("d_red"));
        float PetScale => PET_SCALE * Mathf.Min(player.DragonSizeMult, 1.8f) * Data.DRAGON_AGE_SCALE[Age];

        /// Every hit the dragon lands goes through here, so its kills count toward bond XP and Blood Thirst /
        /// Sanguine Breath can heal you from them.
        public void Hit(Enemy e, float dmg, Vector3 dir = default, float knock = 0f, bool show = true, Color? col = null)
        {
            if (e == null || e.dead) return;
            game.dragonSource = true;
            game.DamageEnemy(e, dmg, dir, knock, show, col);
            game.dragonSource = false;
            float leech = 0.02f * Card("d_black") + player.Pct("leech%");
            if (leech > 0f) player.hp = Mathf.Min(player.maxHp, player.hp + dmg * leech * player.HealMult);
        }

        /// An ogre your dragon killed: Hunger heals you, Chain Reaction blows it up.
        public void OnDragonKill(Enemy e)
        {
            float heal = player.Pct("killheal%");
            if (heal > 0f) player.hp = Mathf.Min(player.maxHp, player.hp + player.maxHp * heal * player.HealMult);
            if (player.Has("dr_chain"))
            {
                var at = e.Pos;
                float dmg = e.maxHp * 0.3f;
                Tw.After(0.05f, () =>
                {
                    if (this == null) return;
                    FX.Flash(at + Vector3.up, 2.5f, new Color(fire.r, fire.g, fire.b, 0.7f), 0.2f);
                    foreach (var o in game.EnemiesInRadius(at, 3.5f, 3f, asDragon: true)) Hit(o, dmg, M.Flat(o.Pos - at).normalized, 6f, false);
                });
            }
        }

        public float RideFrac => Mathf.Clamp01(rideCd / Mathf.Max(RideCooldown, 0.01f));

        /// Applies this dragon's flavor to a hit: blue burns, red bursts, black weakens.
        void Afflict(Enemy e, float strength)
        {
            switch (dragonId)
            {
                case "blue":
                    e.Ignite(4f, (4f + 2f * Lvl("dragonfire")) * strength * (1f + 0.3f * Card("d_blue") + player.Pct("burn%")));
                    break;
                case "red":
                    if (M.RandF() < 0.15f * strength)
                        FX.Flash(e.Pos + new Vector3(0, e.size, 0), 1.2f, new Color(fire.r, fire.g, fire.b, 0.7f), 0.2f);
                    break;
                case "black":
                    e.Weaken(2.5f, Mathf.Min(0.4f + player.Mod("weaken"), 0.8f));
                    break;
            }
            if (player.Has("h_wyrmbond")) e.Bleed(3f, (4f + 2f * Lvl("dragonfire")) * strength);
        }

        // ---------------------------------------------------------------- mount

        public void Mount()
        {
            state = "mount";
            model.fold = 0f;
            model.jawOpen = 0.8f;
            FX.Shockwave(player.Pos + new Vector3(0, 0.3f, 0), 6f, new Color(fire.r, fire.g, fire.b, 0.7f), 0.4f, DataDef.glowBreath);
        }

        public void Dismount()
        {
            if (state == "mount")
            {
                state = "pet";
                vel = new Vector3(0, 8, 0);
            }
        }

        // ---------------------------------------------------------------- update

        public void Tick(float delta, bool running)
        {
            commandCd = Mathf.Max(0f, commandCd - delta);
            rideCd = Mathf.Max(0f, rideCd - delta);
            if (running && state == "pet" && commandCd <= 0f && !Grounded) AutoDive();
            switch (state)
            {
                case "mount":
                    Pos = Vector3.Lerp(Pos, player.Pos, Mathf.Clamp01(delta * 16f));
                    yaw = M.LerpAngle(yaw, player.Yaw, Mathf.Clamp01(delta * 12f));
                    transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * MOUNT_SCALE, Mathf.Clamp01(delta * 8f));
                    model.Animate(delta, new Vector2(player.velocity.x, player.velocity.z).magnitude, true, player.velocity.y);
                    AnimateSkin(new Vector2(player.velocity.x, player.velocity.z).magnitude, false);
                    break;
                case "dive":
                    Dive(delta);
                    break;
                default:
                    if (Grounded && game.running) WalkBeside(delta, game.terrain);
                    else Follow(delta, game.terrain);
                    break;
            }
            if (running) Breath(delta);
        }

        void Follow(float delta, WorldTerrain terrain)
        {
            // Hover off to the side and slightly ahead of you (as seen from the camera), so it never blocks the view.
            orbit += delta * 0.5f;
            var camFwd = player.CamFwd;
            var camRight = player.CamRight;
            var side = camRight * (4f + Mathf.Sin(orbit) * 1f) * (Mathf.Sin(orbit * 0.23f) > -0.4f ? 1f : -1f) + camFwd * (2.5f + Mathf.Cos(orbit * 1.3f) * 1.5f);
            var want = player.Pos + side;
            float gh = terrain.Height(want.x, want.z);
            want.y = Mathf.Max(gh, player.Pos.y) + 3.5f + Mathf.Sin(orbit * 2.3f) * 0.6f;
            if (!game.running)
            {
                // Menu preview: perched off to the right of the screen.
                want = player.Pos + camRight * 7.5f + camFwd * 3f;
                want.y = terrain.Height(want.x, want.z) + 1f + Mathf.Sin(Time.time * 1.5f) * 0.3f;
            }
            var to = want - Pos;
            vel = Vector3.Lerp(vel, to * 3f, Mathf.Clamp01(delta * 3f));
            if (vel.magnitude > 30f) vel = vel.normalized * 30f;
            Pos += vel * delta;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * PetScale, Mathf.Clamp01(delta * 6f));
            var look = breathActive > 0f ? breathDir : new Vector3(vel.x, 0, vel.z);
            if (look.magnitude > 0.5f) faceDir = Vector3.Lerp(faceDir, look.normalized, Mathf.Clamp01(delta * 5f));
            if (new Vector2(faceDir.x, faceDir.z).magnitude > 0.1f) yaw = Mathf.Atan2(-faceDir.x, -faceDir.z);
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            model.Animate(delta, new Vector2(vel.x, vel.z).magnitude, true, vel.y);
            AnimateSkin(new Vector2(vel.x, vel.z).magnitude, !game.running);
        }

        /// The hatchling trots on the ground just beside you (to the camera's right), turning to breathe on whatever is
        /// close. If it falls far behind (a teleport), it catches up at once.
        void WalkBeside(float delta, WorldTerrain terrain)
        {
            orbit += delta * 0.5f;
            var want = player.Pos + player.CamRight * (2.6f + Mathf.Sin(orbit) * 0.4f) - player.CamFwd * 0.6f;
            float gh = terrain.GroundHeight(want.x, want.z, player.Pos.y + 1.5f);
            var flatTo = new Vector3(want.x - Pos.x, 0f, want.z - Pos.z);
            if (flatTo.magnitude > 25f)
            {
                Pos = new Vector3(want.x, gh, want.z);
                vel = Vector3.zero;
            }
            else
            {
                vel = Vector3.Lerp(vel, flatTo * 4f, Mathf.Clamp01(delta * 6f));
                if (vel.magnitude > 34f) vel = vel.normalized * 34f;   // fast enough to keep up with dashes
                var p = Pos + vel * delta;
                p.y = Mathf.Lerp(Pos.y, terrain.GroundHeight(p.x, p.z, player.Pos.y + 1.5f), Mathf.Clamp01(delta * 15f));
                Pos = p;
            }
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * PetScale, Mathf.Clamp01(delta * 6f));
            float speed = new Vector2(vel.x, vel.z).magnitude;
            var look = breathActive > 0f ? breathDir : (speed > 0.8f ? vel : player.CamFwd);
            look.y = 0f;
            if (look.magnitude > 0.1f) faceDir = Vector3.Lerp(faceDir, look.normalized, Mathf.Clamp01(delta * 6f));
            if (new Vector2(faceDir.x, faceDir.z).magnitude > 0.1f) yaw = Mathf.Atan2(-faceDir.x, -faceDir.z);
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            model.fold = 1f;
            model.Animate(delta, speed, false, 0f);
            AnimateGroundSkin(speed);
        }

        // ---------------------------------------------------------------- passive: breath

        void Breath(float delta)
        {
            int L = Lvl("dragonfire");
            var mouth = MouthPos;
            float reach = (11f + Mathf.Min(L, 5) * 1.2f + Mathf.Max(0, L - 5) * 0.5f) * player.AreaMult * player.DragonReachMult;
            var hitCol = Color.Lerp(fire, Color.white, 0.4f);
            if (breathActive > 0f)
            {
                breathActive -= delta;
                var target = PickBreathTarget(mouth, reach + 6f);
                if (target)
                {
                    var want = (target.Pos + new Vector3(0, 0.8f, 0) - mouth).normalized;
                    breathDir = Vector3.Lerp(breathDir, want, Mathf.Clamp01(delta * 6f)).normalized;
                }
                breathNode.SetPositionAndRotation(mouth, Quaternion.LookRotation(breathDir));
                breathLight.transform.position = mouth + breathDir * 3f;
                breathLight.intensity = 2.5f;
                model.jawOpen = 1f;
                breathTick -= delta;
                if (breathTick <= 0f)
                {
                    breathTick = 0.18f;
                    float dmg = (6f + 4f * L) * DmgMult * (player.flying && player.Has("k_sky") ? 1.25f : 1f);
                    float cosA = Mathf.Cos(Mathf.Min(20f + L + 6f * Card("d_cone") + player.Mod("cone"), 70f) * Mathf.Deg2Rad);
                    bool scorchGround = player.Has("db_azure") && M.RandF() < 0.35f;
                    var list = game.enemies;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e.dead) continue;
                        var v = e.Pos + new Vector3(0, 0.8f, 0) - mouth;
                        float dist = v.magnitude;
                        if (dist > reach + e.radius || dist < 0.01f) continue;
                        if (Vector3.Dot(v.normalized, breathDir) < cosA && dist > 2f) continue;
                        Hit(e, dmg, breathDir, dragonId == "red" ? 1.5f : 0.5f, true, hitCol);
                        Afflict(e, 1f);
                        // Volatile: breath hits sometimes burst. Azure Inferno: the breath sets the ground alight.
                        if (player.Has("dr_volatile") && M.RandF() < 0.2f) Blast(e.Pos, 2.5f, dmg * 2f, 0.3f);
                        if (scorchGround)
                        {
                            scorchGround = false;
                            FirePatch.Spawn(game, e.Pos, 2.4f, 3f, dmg * 1.5f, fire);
                        }
                    }
                }
                if (breathActive <= 0f)
                {
                    breathFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    breathLight.intensity = 0f;
                }
                return;
            }
            if (state == "dive") return;
            breathTimer -= delta;
            if (breathTimer <= 0f)
            {
                var target = PickBreathTarget(mouth, reach + 3f);
                if (target == null)
                {
                    breathTimer = 0.3f;
                    return;
                }
                breathTimer = 3f * Mathf.Pow(0.93f, L - 1) * player.DragonCdMult;
                breathActive = (1f + Mathf.Min(L, 5) * 0.12f + Mathf.Max(0, L - 5) * 0.05f) * (1f + player.Pct("breathtime%"));
                breathTick = 0f;
                breathDir = (target.Pos + new Vector3(0, 0.8f, 0) - mouth).normalized;
                breathNode.SetPositionAndRotation(mouth, Quaternion.LookRotation(breathDir));
                breathFx.Play();
                Sfx.Play("flame", mouth, -6f, 0.1f);
            }
        }

        // ---------------------------------------------------------------- auto dive (on foot)

        void AutoDive()
        {
            if (WyvernHoldsDive()) { commandCd = Data.WYVERN_HARRY_DIVE_DELAY; return; }   // WYVERN: harried, the dive waits a moment
            var at = game.DensestPoint(player.Pos, 22f);
            if (M.IsInf(at))
            {
                commandCd = 0.5f;
                return;
            }
            StartDive(at);
            commandCd = CommandCooldown;
        }

        void StartDive(Vector3 at)
        {
            int L = Lvl("dragonfire");
            state = "dive";
            Sfx.Play("roar", Pos, 0f, 0.1f);
            diveT = 0f;
            diveFrom = Pos;
            diveTo = at;
            FX.Telegraph(at, (5f + 0.5f * L) * DiveArea, 0.75f, new Color(fire.r, fire.g, fire.b, 0.4f));
            model.jawOpen = 1f;
        }

        void Dive(float delta)
        {
            diveT += delta;
            var apex = diveTo + new Vector3(0, 14f, 0) + (diveFrom - diveTo).normalized * 6f;
            Vector3 dir;
            if (diveT < 0.4f)
            {
                float k = diveT / 0.4f;
                var p = Vector3.Lerp(diveFrom, apex, k * k * (3f - 2f * k));
                dir = p - Pos;
                Pos = p;
            }
            else if (diveT < 0.75f)
            {
                float k = (diveT - 0.4f) / 0.35f;
                var p = Vector3.Lerp(apex, diveTo + new Vector3(0, 0.5f, 0), k * k);
                dir = p - Pos;
                Pos = p;
            }
            else
            {
                DiveImpact();
                state = "pet";
                vel = new Vector3(0, 14, 0);
                return;
            }
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * MOUNT_SCALE, Mathf.Clamp01(delta * 6f));
            if (new Vector2(dir.x, dir.z).magnitude > 0.01f)
            {
                yaw = Mathf.Atan2(-dir.x, -dir.z);
                transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            }
            model.Animate(delta, 20f, true, dir.y / Mathf.Max(delta, 0.001f));
            AnimateSkin(20f, false);
        }

        void DiveImpact()
        {
            int L = Lvl("dragonfire");
            float rad = (5f + 0.5f * L) * DiveArea * (dragonId == "red" ? 1.2f : 1f);
            float dmg = (70f + 30f * L) * DmgMult * (1f + player.Pct("divedmg%"));
            float stun = 0.8f + player.Mod("divestun");
            if (player.Has("db_ash")) FirePatch.Spawn(game, diveTo, rad * 0.8f, 4f, dmg * 0.15f, fire);
            var at = diveTo;
            if (!FX.Vfx(dragonId == "black" ? "dark_explosion" : dragonId == "red" ? "red_explosion" : "explosion", at, rad / 4f, 2.5f, null, 0.04f, dragonId == "black" ? (Color?)null : fire))
                FX.Flash(at + new Vector3(0, 1, 0), rad, new Color(fire.r, fire.g, fire.b, 0.85f), 0.45f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.3f, new Color(fire.r, fire.g, fire.b, 0.9f), 0.45f, DataDef.glowBreath);
            FX.Burst(at + new Vector3(0, 0.5f, 0), new Color(0.55f, 0.45f, 0.35f), 30, 13f, 0.6f);
            FX.Burst(at + new Vector3(0, 1f, 0), fire, 24, 10f, 0.5f);
            game.Shake(0.7f);
            Sfx.Play("boom", at, 4f, 0.05f);
            foreach (var e in game.EnemiesInRadius(at, rad, 5f, asDragon: true))
            {
                var outDir = M.Flat(e.Pos - at);
                Hit(e, dmg, outDir.normalized, 12f, true, Color.Lerp(fire, Color.white, 0.4f));
                Afflict(e, 1.5f);
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, stun);
            }
        }

        // ---------------------------------------------------------------- signature (E)

        /// Usable on the ground or in the air. From the air your hero also leaps off and slams the target.
        public bool RideCommand()
        {
            if (Grounded)
            {
                game.hud.Toast($"Your {Data.DRAGON_AGE[0].ToLower()} can only breathe fire for now: its signature awakens in the next world");
                return false;
            }
            if (rideCd > 0f || state == "dive") return false;
            var at = game.DensestPoint(player.Pos, 34f);
            if (M.IsInf(at)) at = player.AimPoint();
            int L = Lvl("ride");
            if (state == "mount") player.LeapSlam(at);
            var mouth = MouthPos;
            switch (dragonId)
            {
                case "blue":
                    // Inferno Wave: a torrent of fireballs that sets the crowd ablaze.
                    for (int i = 0; i < 7 + L + Mathf.RoundToInt(player.Mod("fireballs")); i++)
                    {
                        var tgt = at + new Vector3(M.RandRange(-8f, 8f), 0.5f, M.RandRange(-8f, 8f)) * SigArea;
                        var from = mouth + new Vector3(M.RandRange(-0.5f, 0.5f), 0, M.RandRange(-0.5f, 0.5f));
                        Fireball.Spawn(game, null, from, (tgt - mouth).normalized * 36f, (38f + 14f * L) * DmgMult * SigDmg, (3f + 0.3f * L) * SigArea, 0.35f, false, fire);
                    }
                    Sfx.Play("flame", 2f, 0f);
                    Sfx.Play("whoosh", 0f, 0f);
                    break;
                case "red":
                {
                    // Cataclysm: one massive blast, then a ring of aftershocks.
                    float rad = (7f + 0.5f * L) * SigArea;
                    float dmg = (110f + 40f * L) * DmgMult * SigDmg;
                    FX.Telegraph(at, rad, 0.6f, new Color(fire.r, fire.g, fire.b, 0.45f));
                    FX.Bolt(mouth, at + new Vector3(0, 1, 0), Color.Lerp(fire, Color.white, 0.1f), 0.5f, 0.6f, 1);
                    Tw.Run(CataclysmRoutine(at, rad, dmg));
                    Sfx.Play("roar", 2f, 0f);
                    break;
                }
                case "black":
                {
                    // Blood Rain: steady damage and -40% ogre attack over the crowd.
                    var ground = at;
                    ground.y = game.terrain.Height(at.x, at.z);
                    BloodRain.Spawn(this, ground, (8f + 1f * L) * SigArea * (1f + player.Pct("rainarea%")), 4f + 0.5f * L + player.Mod("raintime"), (10f + 4f * L) * DmgMult * SigDmg);
                    Sfx.Play("roar", 0f, 0.05f);
                    break;
                }
            }
            model.jawOpen = 1f;
            if (skin) skin.Action(1.2f, SK_ROAR);
            rideCd = RideCooldown;
            game.signatures++;
            return true;
        }

        IEnumerator CataclysmRoutine(Vector3 at, float rad, float dmg)
        {
            yield return Tw.Wait(0.6f);
            if (this == null) yield break;
            Blast(at, rad, dmg, 1f);
            int shocks = 5 + Mathf.RoundToInt(player.Mod("aftershocks"));
            for (int i = 0; i < shocks; i++)
            {
                float a = M.TAU * i / shocks + M.RandF() * 0.5f;
                var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (rad + 2.5f);
                p.y = game.terrain.Height(p.x, p.z);
                yield return Tw.Wait(0.12f);
                if (this == null) yield break;
                Blast(p, rad * 0.55f, dmg * 0.4f, 0.5f);
            }
        }

        void Blast(Vector3 at, float rad, float dmg, float big)
        {
            if (!FX.Vfx(big >= 1f ? "red_explosion" : "explosion", at, rad / 4f, 2.5f, null, 0f, fire))
                FX.Flash(at + new Vector3(0, 1f, 0), rad, new Color(fire.r, fire.g, fire.b, 0.9f), 0.4f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.2f, new Color(fire.r, fire.g, fire.b, 0.9f), 0.4f, DataDef.glowBreath);
            if (big >= 1f) FX.Flash(at + new Vector3(0, 1.2f, 0), rad * 0.7f, new Color(fire.r, fire.g, fire.b, 0.55f), 0.45f);
            FX.Burst(at + new Vector3(0, 0.6f, 0), Color.Lerp(new Color(0.5f, 0.4f, 0.3f), fire, 0.6f), (int)(30 * big), 14f * big, 0.6f * big);
            Sfx.Play("boom", at, 4f * big, 0.05f);
            game.Shake(0.9f * big);
            foreach (var e in game.EnemiesInRadius(at, rad, 6f, asDragon: true))
            {
                var outDir = M.Flat(e.Pos - at);
                Hit(e, dmg, outDir.normalized, 14f * big, true, Color.Lerp(fire, Color.white, 0.4f));
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 1f * big);
            }
        }

        // ---------------------------------------------------------------- guardian: it looks after you

        /// Sheltering Wing: a chance to catch a potion, boulder or harpoon meant for you. Returns true if it did.
        public bool TryWingGuard(Vector3 at)
        {
            int r = Card("d_wing") + Mathf.RoundToInt(player.Mod("wing"));
            if (r <= 0 || M.RandF() > Mathf.Min(0.12f * r, 0.75f)) return false;
            FX.Text(at + Vector3.up * 1.5f, "Sheltered", Color.Lerp(fire, Color.white, 0.4f));
            FX.Shockwave(at, 2.2f, new Color(fire.r, fire.g, fire.b, 0.8f), 0.25f, DataDef.glowBreath);
            FX.Burst(at, Color.Lerp(fire, Color.white, 0.3f), 10, 6f, 0.2f);
            Sfx.Play("block", -2f, 0.1f);
            // On foot the hatchling throws a wing up (its Defend pose); in the air it roars.
            if (skin && state != "mount") skin.Action(0.6f, Grounded ? SK_DEFEND : SK_ROAR);
            return true;
        }

        /// Wrathful Guardian: a gout of breath on whatever just hurt you.
        public void Scorch(Enemy e)
        {
            if (e == null || e.dead) return;
            var mouth = MouthPos;
            var to = e.Pos + new Vector3(0, e.size, 0);
            FX.Bolt(mouth, to, Color.Lerp(fire, Color.white, 0.25f), 0.35f, 0.2f, 2);
            FX.Burst(to, fire, 12, 6f, 0.3f);
            Sfx.Play("flame", e.Pos, -4f, 0.1f);
            if (skin && state != "mount" && !skin.InAction) skin.Action(0.5f, Grounded ? SK_SNAP : SK_BREATH);
            Hit(e, (40f + 10f * Lvl("dragonfire")) * DmgMult, M.Flat(e.Pos - player.Pos).normalized, 8f, true, Color.Lerp(fire, Color.white, 0.4f));
            Afflict(e, 1f);
        }

        /// Living Bulwark: the dragon crashes down around you and blasts everything away.
        public void Eruption(Vector3 at)
        {
            float rad = 9f * DiveArea;
            FX.Telegraph(at, rad, 0.2f, new Color(fire.r, fire.g, fire.b, 0.4f));
            Blast(at, rad, (150f + 40f * Lvl("dragonfire")) * DmgMult, 1f);
            game.hud.Toast("Your dragon shields you!");
        }

        /// Guardian Wings (adult dragon): it lands beside you, spreads its wings around you (no damage gets through for a
        /// few seconds) and heals you.
        public void GuardianWings(Vector3 at)
        {
            var col = Color.Lerp(fire, Color.white, 0.35f);
            if (state != "mount") Pos = at + new Vector3(0, 2.5f, 0);
            if (skin) skin.Action(1.2f, SK_ROAR);
            model.jawOpen = 1f;
            FX.Flash(at + Vector3.up, 6f, M.WithA(col, 0.7f), 0.5f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), 8f, M.WithA(col, 0.9f), 0.5f, DataDef.glowBreath);
            Sfx.Play("roar", 2f, 0f);
            // The shield: a glowing dome of wings around the hero while it lasts.
            var dome = FX.Node(player.transform, "GuardianWings", new Vector3(0, 1.2f, 0));
            FX.NoShadow(FX.Part(dome, MeshGen.Sphere(16), FX.Glow(M.WithA(col, 0.22f), 1.4f), Vector3.zero, Vector3.one * 4.2f));
            FX.NoShadow(FX.Part(dome, MeshGen.Ring(), FX.Glow(M.WithA(col, 0.8f), 2f), new Vector3(0, -1.1f, 0), new Vector3(2.4f, 0.3f, 2.4f)));
            Tw.After(Data.GUARDIAN_WINGS_SHIELD, () => { if (dome) Destroy(dome.gameObject); });
            foreach (var e in game.EnemiesInRadius(at, 5f, 4f, asDragon: true))
                if (!e.isBoss) e.knock += M.Flat(e.Pos - at).normalized * 14f;
            game.hud.Banner("Guardian Wings", "Your dragon shields you and heals your wounds");
        }

        /// A guardian revive: the dragon sweeps down over you, scattering everything nearby.
        public void GuardianSwoop(Vector3 at)
        {
            Blast(at, 8f * DiveArea, (100f + 30f * Lvl("dragonfire")) * DmgMult, 0.8f);
            if (state != "mount") Pos = at + new Vector3(0, 5f, 0);
        }
    }

    /// The Night Wyrm's Blood Rain: a pool of blood that damages and weakens ogres inside it.
    public class BloodRain : MonoBehaviour
    {
        static readonly System.Collections.Generic.List<BloodRain> live = new System.Collections.Generic.List<BloodRain>();

        DragonPet pet;
        float radius = 8f, life = 4f, tickDamage = 10f, tick;

        /// True when a point stands inside any Blood Rain (Crimson Tide heals you there).
        public static bool Covers(Vector3 p)
        {
            foreach (var r in live)
                if (r && M.Flat(r.transform.position - p).magnitude < r.radius) return true;
            return false;
        }

        void OnEnable() => live.Add(this);
        void OnDisable() => live.Remove(this);

        public static void Spawn(DragonPet pet, Vector3 ground, float radius, float life, float tickDamage)
        {
            var r = new GameObject("BloodRain").AddComponent<BloodRain>();
            r.transform.SetParent(pet.game.spellbook.transform, false);
            r.transform.position = ground;
            r.pet = pet;
            r.radius = radius;
            r.life = life;
            r.tickDamage = tickDamage;
            var pool = FX.Part(r.transform, MeshGen.Disc(), FX.Lit(new Color(0.35f, 0f, 0.03f, 0.75f), 0.15f, 0f, 0.3f), new Vector3(0, 0.06f, 0), new Vector3(radius, 1, radius));
            FX.NoShadow(pool);
            var ring = FX.Part(r.transform, MeshGen.Ring(), FX.Glow(new Color(0.8f, 0.05f, 0.1f, 0.7f), 1.5f), new Vector3(0, 0.1f, 0), new Vector3(radius, 0.3f, radius));
            FX.NoShadow(ring);
            var rainNode = FX.Node(r.transform, "Rain", new Vector3(0, 14f, 0), new Vector3(M.PI * 0.5f, 0, 0));
            FX.Particles(rainNode, new FX.ParticleSpec
            {
                amount = 260, lifetime = 0.9f, size = 0.12f, speedMin = 16f, speedMax = 20f, gravity = new Vector3(0, -10, 0),
                boxExtents = new Vector3(radius * 0.8f, radius * 0.8f, 0.5f), stretch = true, additive = false,
                startColor = new Color(0.7f, 0.02f, 0.05f, 0.9f), endColor = new Color(0.4f, 0f, 0.02f, 0.6f),
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = pet.game;
            life -= delta;
            tick -= delta;
            if (tick <= 0f)
            {
                tick = 0.3f;
                foreach (var e in g.EnemiesInRadius(transform.position, radius, 8f, asDragon: true))
                {
                    pet.Hit(e, tickDamage, Vector3.zero, 0f, false);
                    e.Weaken(1f, Mathf.Min(0.4f + pet.player.Mod("weaken"), 0.8f));
                }
            }
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
