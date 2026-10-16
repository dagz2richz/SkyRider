using System.Collections;
using UnityEngine;

namespace Skyfall
{
    /// Your dragon companion (Azure Wyrm, Crimson Drake, Night Wyrm, Venom Wyrm or Tempest Drake). Flies beside you, breathes on its own,
    /// dive-bombs crowds by itself, carries you when you press Q, and unleashes its signature ability on E
    /// (on the ground or in the air; from the air your hero also leaps down and slams the ground).
    public class DragonPet : MonoBehaviour
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

        float DmgMult => player.DamageMult * DataDef.damage * player.DragonDamageMult;

        /// On foot the dragon dive-bombs crowds by itself on this cooldown.
        float CommandCooldown => (12f - 1f * (Lvl("dragonfire") - 1)) * player.DragonCdMult;

        public float CommandFrac => Mathf.Clamp01(commandCd / Mathf.Max(CommandCooldown, 0.01f));

        float RideCooldown => Data.SIGNATURE_CD * (1f - 0.08f * (Lvl("ride") - 1)) * player.DragonCdMult;

        public float RideFrac => Mathf.Clamp01(rideCd / Mathf.Max(RideCooldown, 0.01f));

        /// Applies this dragon's flavor to a hit: blue burns, red bursts, black weakens.
        void Afflict(Enemy e, float strength)
        {
            switch (dragonId)
            {
                case "blue":
                    e.Ignite(4f, (4f + 2f * Lvl("dragonfire")) * strength);
                    break;
                case "red":
                    if (M.RandF() < 0.15f * strength)
                        FX.Flash(e.Pos + new Vector3(0, e.size, 0), 1.2f, new Color(fire.r, fire.g, fire.b, 0.7f), 0.2f);
                    break;
                case "black":
                    e.Weaken(2.5f, 0.4f);
                    break;
                case "green":
                    e.Poison(5f, (5f + 2.5f * Lvl("dragonfire")) * strength * DmgMult);
                    break;
                case "gold":
                    if (M.RandF() < 0.2f * strength) e.Shock(1f);
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
            if (running && state == "pet" && commandCd <= 0f) AutoDive();
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
                    Follow(delta, game.terrain);
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
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * PET_SCALE, Mathf.Clamp01(delta * 6f));
            var look = breathActive > 0f ? breathDir : new Vector3(vel.x, 0, vel.z);
            if (look.magnitude > 0.5f) faceDir = Vector3.Lerp(faceDir, look.normalized, Mathf.Clamp01(delta * 5f));
            if (new Vector2(faceDir.x, faceDir.z).magnitude > 0.1f) yaw = Mathf.Atan2(-faceDir.x, -faceDir.z);
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            model.Animate(delta, new Vector2(vel.x, vel.z).magnitude, true, vel.y);
            AnimateSkin(new Vector2(vel.x, vel.z).magnitude, !game.running);
        }

        // ---------------------------------------------------------------- passive: breath

        void Breath(float delta)
        {
            int L = Lvl("dragonfire");
            var mouth = MouthPos;
            float reach = (11f + L * 1.2f) * player.AreaMult * player.DragonReachMult;
            var hitCol = Color.Lerp(fire, Color.white, 0.4f);
            if (breathActive > 0f)
            {
                breathActive -= delta;
                var target = game.NearestEnemy(mouth, reach + 6f, 30f);
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
                    float dmg = (6f + 4f * L) * DmgMult;
                    float cosA = Mathf.Cos((20f + L) * Mathf.Deg2Rad);
                    var list = game.enemies;
                    Enemy lastHit = null;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e.dead) continue;
                        var v = e.Pos + new Vector3(0, 0.8f, 0) - mouth;
                        float dist = v.magnitude;
                        if (dist > reach + e.radius || dist < 0.01f) continue;
                        if (Vector3.Dot(v.normalized, breathDir) < cosA && dist > 2f) continue;
                        game.DamageEnemy(e, dmg, breathDir, dragonId == "red" ? 1.5f : 0.5f, true, hitCol);
                        Afflict(e, 1f);
                        lastHit = e;
                    }
                    // Tempest Drake: the lightning breath arcs on to one more ogre beyond the cone.
                    if (dragonId == "gold" && lastHit != null && !lastHit.dead)
                    {
                        var arc = game.NearestEnemy(lastHit.Pos, 9f, 6f, lastHit);
                        if (arc)
                        {
                            FX.Bolt(lastHit.Pos + new Vector3(0, lastHit.size, 0), arc.Pos + new Vector3(0, arc.size, 0), Color.Lerp(fire, Color.white, 0.3f), 0.15f, 0.15f);
                            game.DamageEnemy(arc, dmg * 1.5f, Vector3.zero, 0f, true, hitCol);
                            Afflict(arc, 1f);
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
                var target = game.NearestEnemy(mouth, reach + 3f, 30f);
                if (target == null)
                {
                    breathTimer = 0.3f;
                    return;
                }
                breathTimer = (3f - 0.2f * L) * player.DragonCdMult;
                breathActive = 1f + L * 0.12f;
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
            FX.Telegraph(at, (5f + 0.5f * L) * player.AreaMult, 0.75f, new Color(fire.r, fire.g, fire.b, 0.4f));
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
            float rad = (5f + 0.5f * L) * player.AreaMult * (dragonId == "red" ? 1.2f : 1f);
            float dmg = (70f + 30f * L) * DmgMult;
            var at = diveTo;
            if (!FX.Vfx(dragonId == "black" ? "dark_explosion" : dragonId == "red" ? "red_explosion" : "explosion", at, rad / 4f, 2.5f, null, 0.04f, dragonId == "black" ? (Color?)null : fire))
                FX.Flash(at + new Vector3(0, 1, 0), rad, new Color(fire.r, fire.g, fire.b, 0.85f), 0.45f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.3f, new Color(fire.r, fire.g, fire.b, 0.9f), 0.45f, DataDef.glowBreath);
            if (dragonId == "gold")
                for (int i = 0; i < 3; i++)
                {
                    var p = at + new Vector3(M.RandRange(-rad, rad) * 0.6f, 0, M.RandRange(-rad, rad) * 0.6f);
                    FX.Bolt(p + new Vector3(0, 18f, 0), p, Color.Lerp(fire, Color.white, 0.4f), 0.35f, 0.25f);
                }
            FX.Burst(at + new Vector3(0, 0.5f, 0), new Color(0.55f, 0.45f, 0.35f), 30, 13f, 0.6f);
            FX.Burst(at + new Vector3(0, 1f, 0), fire, 24, 10f, 0.5f);
            game.Shake(0.7f);
            Sfx.Play("boom", at, 4f, 0.05f);
            foreach (var e in game.EnemiesInRadius(at, rad, 5f))
            {
                var outDir = M.Flat(e.Pos - at);
                game.DamageEnemy(e, dmg, outDir.normalized, 12f, true, Color.Lerp(fire, Color.white, 0.4f));
                Afflict(e, 1.5f);
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 0.8f);
            }
        }

        // ---------------------------------------------------------------- signature (E)

        /// Usable on the ground or in the air. From the air your hero also leaps off and slams the target.
        public bool RideCommand()
        {
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
                    for (int i = 0; i < 7 + L; i++)
                    {
                        var tgt = at + new Vector3(M.RandRange(-8f, 8f), 0.5f, M.RandRange(-8f, 8f));
                        var from = mouth + new Vector3(M.RandRange(-0.5f, 0.5f), 0, M.RandRange(-0.5f, 0.5f));
                        Fireball.Spawn(game, null, from, (tgt - mouth).normalized * 36f, (38f + 14f * L) * DmgMult, (3f + 0.3f * L) * player.AreaMult, 0.35f, false, fire);
                    }
                    Sfx.Play("flame", 2f, 0f);
                    Sfx.Play("whoosh", 0f, 0f);
                    break;
                case "red":
                {
                    // Cataclysm: one massive blast, then a ring of aftershocks.
                    float rad = (7f + 0.5f * L) * player.AreaMult;
                    float dmg = (110f + 40f * L) * DmgMult;
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
                    BloodRain.Spawn(this, ground, (8f + 1f * L) * player.AreaMult, 4f + 0.5f * L, (10f + 4f * L) * DmgMult);
                    Sfx.Play("roar", 0f, 0.05f);
                    break;
                }
                case "green":
                {
                    // Plague Cloud: a drifting miasma that poisons and slows.
                    var ground = at;
                    ground.y = game.terrain.Height(at.x, at.z);
                    PlagueCloud.Spawn(this, ground, (8f + 0.8f * L) * player.AreaMult, 6f + 0.5f * L, (9f + 4f * L) * DmgMult);
                    FX.Bolt(mouth, ground + Vector3.up, Color.Lerp(fire, Color.white, 0.2f), 0.4f, 0.4f, 3);
                    Sfx.Play("roar", 0f, 0.05f);
                    Sfx.Play("whoosh", 0f, 0f);
                    break;
                }
                case "gold":
                {
                    // Eye of the Storm: a tornado that hunts the crowd, pulling ogres in under falling lightning.
                    var ground = at;
                    ground.y = game.terrain.Height(at.x, at.z);
                    StormTornado.Spawn(this, ground, (6f + 0.5f * L) * player.AreaMult, 5f + 0.4f * L, (22f + 9f * L) * DmgMult);
                    FX.Bolt(mouth, ground + Vector3.up, Color.Lerp(fire, Color.white, 0.4f), 0.4f, 0.3f);
                    Sfx.Play("zap", 2f, 0f);
                    Sfx.Play("roar", 0f, 0.05f);
                    break;
                }
            }
            model.jawOpen = 1f;
            if (skin) skin.Action(1.2f, SK_ROAR);
            rideCd = RideCooldown;
            return true;
        }

        IEnumerator CataclysmRoutine(Vector3 at, float rad, float dmg)
        {
            yield return Tw.Wait(0.6f);
            if (this == null) yield break;
            Blast(at, rad, dmg, 1f);
            for (int i = 0; i < 5; i++)
            {
                float a = M.TAU * i / 5f + M.RandF() * 0.5f;
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
            foreach (var e in game.EnemiesInRadius(at, rad, 6f))
            {
                var outDir = M.Flat(e.Pos - at);
                game.DamageEnemy(e, dmg, outDir.normalized, 14f * big, true, Color.Lerp(fire, Color.white, 0.4f));
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 1f * big);
            }
        }
    }

    /// The Night Wyrm's Blood Rain: a pool of blood that damages and weakens ogres inside it.
    public class BloodRain : MonoBehaviour
    {
        DragonPet pet;
        float radius = 8f, life = 4f, tickDamage = 10f, tick;

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
                foreach (var e in g.EnemiesInRadius(transform.position, radius, 8f))
                {
                    g.DamageEnemy(e, tickDamage, Vector3.zero, 0f, false);
                    e.Weaken(1f, 0.4f);
                }
            }
            if (life <= 0f) Destroy(gameObject);
        }
    }

    /// The Venom Wyrm's Plague Cloud: a churning green miasma that drifts toward the thickest crowd, poisoning and
    /// slowing every ogre inside it.
    public class PlagueCloud : MonoBehaviour
    {
        DragonPet pet;
        float radius = 8f, life = 6f, tickDamage = 10f, tick;
        Transform puffs;

        public static void Spawn(DragonPet pet, Vector3 ground, float radius, float life, float tickDamage)
        {
            var c = new GameObject("PlagueCloud").AddComponent<PlagueCloud>();
            c.transform.SetParent(pet.game.spellbook.transform, false);
            c.transform.position = ground;
            c.pet = pet;
            c.radius = radius;
            c.life = life;
            c.tickDamage = tickDamage;
            var green = new Color(0.4f, 0.95f, 0.2f);
            FX.NoShadow(FX.Part(c.transform, MeshGen.Disc(), FX.FadeMat(new Color(0.25f, 0.6f, 0.1f, 0.35f), false), new Vector3(0, 0.08f, 0), new Vector3(radius, 1, radius)));
            FX.NoShadow(FX.Part(c.transform, MeshGen.Ring(), FX.Glow(new Color(0.45f, 1f, 0.2f, 0.6f), 1.4f), new Vector3(0, 0.12f, 0), new Vector3(radius, 0.3f, radius)));
            c.puffs = FX.Node(c.transform, "Puffs");
            var puffMat = FX.FadeMat(new Color(0.35f, 0.75f, 0.15f, 0.22f), false);
            for (int i = 0; i < 9; i++)
            {
                float a = M.TAU * i / 9f;
                float r = radius * (i % 3 == 0 ? 0.25f : 0.6f);
                FX.NoShadow(FX.Part(c.puffs, MeshGen.Sphere(10), puffMat, new Vector3(Mathf.Cos(a) * r, 1.2f + (i % 2) * 0.8f, Mathf.Sin(a) * r), Vector3.one * radius * 0.55f));
            }
            var mist = FX.Node(c.transform, "Mist", new Vector3(0, 0.3f, 0), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.Particles(mist, new FX.ParticleSpec
            {
                amount = 90, lifetime = 1.6f, size = 1.6f, scaleMin = 0.8f, scaleMax = 2f, speedMin = 0.4f, speedMax = 1.4f, spread = 40f, additive = false,
                boxExtents = new Vector3(radius * 0.7f, radius * 0.7f, 0.2f), startColor = new Color(0.45f, 0.85f, 0.2f, 0.55f), endColor = new Color(0.2f, 0.4f, 0.05f, 0f),
            });
            FX.Burst(ground + Vector3.up, green, 30, 8f, 0.5f);
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = pet.game;
            life -= delta;
            tick -= delta;
            puffs.Rotate(0f, delta * 25f, 0f, Space.Self);
            // Drift toward the thickest crowd nearby.
            var dense = g.DensestPoint(transform.position, radius * 2f);
            if (!M.IsInf(dense))
            {
                var p = Vector3.MoveTowards(transform.position, dense, delta * 3f);
                p.y = g.terrain.Height(p.x, p.z);
                transform.position = p;
            }
            if (tick <= 0f)
            {
                tick = 0.35f;
                foreach (var e in g.EnemiesInRadius(transform.position, radius, 8f))
                {
                    g.DamageEnemy(e, tickDamage, Vector3.zero, 0f, false);
                    e.Poison(3f, tickDamage * 0.8f);
                    e.Slow(0.6f, 0.6f);
                }
            }
            if (life < 0.8f) transform.localScale = Vector3.one * Mathf.Max(0.01f, life / 0.8f);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    /// The Tempest Drake's Eye of the Storm: a golden tornado that wanders through the crowd, pulling ogres toward its
    /// core and calling lightning down on them.
    public class StormTornado : MonoBehaviour
    {
        DragonPet pet;
        float radius = 6f, life = 5f, boltDamage = 20f, tick, boltT;
        Transform funnel;

        public static void Spawn(DragonPet pet, Vector3 ground, float radius, float life, float boltDamage)
        {
            var t = new GameObject("StormTornado").AddComponent<StormTornado>();
            t.transform.SetParent(pet.game.spellbook.transform, false);
            t.transform.position = ground;
            t.pet = pet;
            t.radius = radius;
            t.life = life;
            t.boltDamage = boltDamage;
            t.funnel = FX.Node(t.transform, "Funnel");
            var mat = FX.FadeMat(new Color(0.95f, 0.85f, 0.55f, 0.25f), false);
            var glow = FX.Glow(new Color(1f, 0.85f, 0.35f, 0.5f), 1.6f);
            for (int i = 0; i < 7; i++)
            {
                float y = 0.6f + i * 1.5f;
                float r = Mathf.Lerp(0.8f, radius * 0.9f, i / 6f);
                FX.NoShadow(FX.Part(t.funnel, MeshGen.Torus(0.7f, 1f, 24, 5), i % 2 == 0 ? glow : mat, new Vector3(0, y, 0), new Vector3(r, 0.6f, r), new Vector3(0.12f * (i % 2 == 0 ? 1 : -1), 0, 0)));
            }
            var dust = FX.Node(t.transform, "Dust", new Vector3(0, 0.3f, 0), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.Particles(dust, new FX.ParticleSpec
            {
                amount = 80, lifetime = 1f, size = 0.8f, speedMin = 4f, speedMax = 9f, spread = 25f, sphereRadius = radius * 0.6f, radialSpeed = -3f,
                startColor = new Color(0.9f, 0.8f, 0.55f, 0.6f), endColor = new Color(0.6f, 0.5f, 0.3f, 0f), additive = false,
            });
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(t.transform, false);
            light.transform.localPosition = new Vector3(0, 5f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.85f, 0.4f);
            light.intensity = 2.2f;
            light.range = radius * 3f;
            light.shadows = LightShadows.None;
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = pet.game;
            life -= delta;
            tick -= delta;
            boltT -= delta;
            funnel.Rotate(0f, delta * 420f, 0f, Space.Self);
            var dense = g.DensestPoint(transform.position, radius * 2.5f);
            if (!M.IsInf(dense))
            {
                var p = Vector3.MoveTowards(transform.position, dense, delta * 6f);
                p.y = g.terrain.Height(p.x, p.z);
                transform.position = p;
            }
            if (tick <= 0f)
            {
                tick = 0.2f;
                foreach (var e in g.EnemiesInRadius(transform.position, radius * 1.4f, 8f))
                {
                    var to = M.Flat(transform.position - e.Pos);
                    if (!e.isBoss) e.knock += to.normalized * 5f;
                    g.DamageEnemy(e, boltDamage * 0.15f, Vector3.zero, 0f, false);
                }
            }
            if (boltT <= 0f)
            {
                boltT = 0.25f;
                var e = g.RandomEnemyNear(transform.position, radius * 1.4f);
                if (e)
                {
                    var hitAt = e.Pos + new Vector3(0, e.size, 0);
                    FX.Bolt(hitAt + new Vector3(M.RandRange(-2f, 2f), 20f, M.RandRange(-2f, 2f)), hitAt, new Color(1f, 0.92f, 0.6f), 0.3f, 0.2f);
                    FX.Flash(hitAt, 1.6f, new Color(1f, 0.9f, 0.5f, 0.7f), 0.15f);
                    g.DamageEnemy(e, boltDamage, Vector3.zero, 0f, true, new Color(1f, 0.92f, 0.6f));
                    if (M.RandF() < 0.35f) e.Shock(1f);
                    Sfx.Play("zap", e.Pos, -8f, 0.2f);
                }
            }
            if (life < 0.6f) transform.localScale = Vector3.one * Mathf.Max(0.01f, life / 0.6f);
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
