using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// -skyfallSolidTest 1: scripted checks of the solid islands and ruins, ogre jumping, player-vs-ogre collision,
    /// ogre separation and the camera. Everything is logged with SKYFALL_TEST lines and photographed to Logs/solid_*.png.
    public static partial class SkyfallTests
    {
        static WorldTerrain.Obstacle wall;
        static bool haveWall;
        static readonly List<Enemy> spawned = new List<Enemy>();
        static bool snappedJump;

        static Enemy SpawnFixed(Game g, string kind, Vector3 p, bool stun, float fromY = float.NegativeInfinity)
        {
            var e = g.SpawnEnemy(kind, p, fromY);
            e.hp = e.maxHp = 1e8f;
            if (stun) e.stunT = 1e6f;
            spawned.Add(e);
            return e;
        }

        static void ClearEnemies(Game g)
        {
            foreach (var e in g.enemies) e.dead = true;
            spawned.Clear();
        }

        static void Tele(Player pl, Vector3 p)
        {
            pl.Pos = p;
            pl.velocity = Vector3.zero;
        }

        static void FaceDir(Player pl, Vector3 d) => pl.camYaw = Mathf.Atan2(-d.x, -d.z);

        static Vector3 WallNormal(WorldTerrain.Obstacle o) => new Vector3(-o.uz, 0, o.ux);

        static void SolidTest(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            pl.hp = pl.maxHp = 99999f;
            pl.flightStamina = 99f;
            typeof(Game).GetField("spawnAccum", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, -1e9f);
            int f = frame;
            var flagsNP = BindingFlags.NonPublic | BindingFlags.Instance;

            if (f == 1)
            {
                Debug.Log($"SKYFALL_TEST solid: obstacles={tr.obstacles.Count} (from {tr.kitObstacleCount} boxes) islands={tr.islands.Count}");
                // A wall on open, fairly level ground away from the start.
                float best = float.MaxValue;
                foreach (var o in tr.obstacles)
                {
                    float h = o.top - o.bottom;
                    if (o.hx < 2.5f || o.hz > 1.0f || h < 3.2f || h > 4.4f || !o.name.StartsWith("destroyed_wall")) continue;
                    if (Mathf.Max(Mathf.Abs(o.cx), Mathf.Abs(o.cz)) > 130f || Mathf.Abs(o.bottom - tr.Height(o.cx, o.cz)) > 1.5f) continue;
                    var n = WallNormal(o);
                    float g0 = tr.Height(o.cx, o.cz), spread = 0f;
                    for (int k = 1; k <= 4; k++)
                    {
                        spread = Mathf.Max(spread, Mathf.Abs(tr.Height(o.cx + n.x * k * 2.2f, o.cz + n.z * k * 2.2f) - g0));
                        spread = Mathf.Max(spread, Mathf.Abs(tr.Height(o.cx - n.x * k * 2.2f, o.cz - n.z * k * 2.2f) - g0));
                    }
                    // Nothing else within 9 m on the approach side or 4 m behind the wall (so the test hits THIS wall).
                    bool clear = true;
                    for (float d = 1.2f; d <= 9f && clear; d += 0.6f)
                        clear = !tr.PointInObstacle(new Vector3(o.cx + n.x * d, g0 + 1.5f, o.cz + n.z * d), 0.5f);
                    for (float d = 1.2f; d <= 4f && clear; d += 0.6f)
                        clear = !tr.PointInObstacle(new Vector3(o.cx - n.x * d, g0 + 1.5f, o.cz - n.z * d), 0.5f);
                    if (!clear) continue;
                    float score = spread * 10f + new Vector2(o.cx, o.cz).magnitude * 0.01f;
                    if (score < best) { best = score; wall = o; haveWall = true; }
                }
                if (haveWall)
                    Debug.Log($"SKYFALL_TEST solid wall: {wall.name} center=({wall.cx:0.0},{wall.cz:0.0}) half=({wall.hx:0.00},{wall.hz:0.00}) bottom={wall.bottom:0.00} top={wall.top:0.00} ground={tr.Height(wall.cx, wall.cz):0.00}");
                else Debug.LogError("SKYFALL_TEST solid: no suitable wall found");
            }

            // ------------------------------------------------ (a) islands: fly up under the belly, fly into the side
            var isl = tr.islands.Find(i => !i.big) ?? tr.islands[0];
            float R = isl.EdgeRadius(0f) * 0.97f;
            if (f == 5)
            {
                ClearEnemies(game);
                // Find how far from the middle there is 5 m of clearance between the ground and the belly.
                float D = R + 3f;
                for (float d = R + 3f; d > 1.5f; d -= 0.4f)
                {
                    float gnd = tr.Height(isl.center.x + d, isl.center.z);
                    float u = (d - pl.PhysRadius * 2f) / R;
                    if (isl.topY - WorldTerrain.RimDepth(isl, u) - gnd >= 5.5f) D = d;
                }
                var p = new Vector3(isl.center.x + D, tr.Height(isl.center.x + D, isl.center.z), isl.center.z);
                Tele(pl, p);
                typeof(Player).GetMethod("SummonDragon", flagsNP).Invoke(pl, null);
                pl.velocity.y = 10f;
                pl.camYaw = 1f; pl.camDistTarget = 14f; pl.camPitch = 0.1f;
                float uu = (D - 1.2f) / R;
                Debug.Log($"SKYFALL_TEST under-island start: pos={p} island top={isl.topY:0.0} belly above player={isl.topY - WorldTerrain.RimDepth(isl, uu) - p.y:0.0} R={R:0.0} dist={D:0.0}");
            }
            if (f > 5 && f <= 245)
            {
                GameInput.TestJumpHeld = true;
                if (f % 30 == 0 || f == 245)
                {
                    float u = Mathf.Max(0f, new Vector2(pl.Pos.x - isl.center.x, pl.Pos.z - isl.center.z).magnitude - pl.PhysRadius) / R;
                    float yU = isl.topY - WorldTerrain.RimDepth(isl, u);
                    Debug.Log($"SKYFALL_TEST under-island f={f}: y={pl.Pos.y:0.00} head={pl.Pos.y + pl.PhysHeight:0.00} bellyY={yU:0.00} vy={pl.velocity.y:0.0} flying={pl.flying} top={isl.topY:0.0}");
                }
                if (f == 240) Snap("solid_a1_under");
            }
            if (f == 246) { GameInput.TestJumpHeld = false; }
            if (f == 250)
            {
                // Mid-height, outside the rim, flying into the side of the island.
                var p = new Vector3(isl.center.x + R + 9f, isl.topY - 2.5f, isl.center.z);
                Tele(pl, p);
                if (!pl.flying) typeof(Player).GetMethod("SummonDragon", flagsNP).Invoke(pl, null);
                FaceDir(pl, new Vector3(-1, 0, 0));
                GameInput.TestMove = new Vector2(0, -1);
                pl.camDistTarget = 12f; pl.camPitch = 0.25f;
            }
            if (f > 250 && f <= 400)
            {
                pl.Pos = new Vector3(pl.Pos.x, isl.topY - 2.5f, pl.Pos.z);
                pl.velocity.y = 0f;
                pl.flightStamina = 99f;
                if (f % 30 == 0)
                {
                    float dist = new Vector2(pl.Pos.x - isl.center.x, pl.Pos.z - isl.center.z).magnitude;
                    Debug.Log($"SKYFALL_TEST into-island-side f={f}: dist from center={dist:0.00} rimR={R:0.00} (blocked at ~{R + pl.PhysRadius:0.00}) y={pl.Pos.y:0.0}");
                }
                if (f == 399) Snap("solid_a2_side");
            }
            if (f == 401) { GameInput.TestMove = null; }

            // ------------------------------------------------ (b) walk into a kit wall
            if (f == 410 && haveWall)
            {
                if (pl.flying) typeof(Player).GetMethod("ReleaseDragon", flagsNP).Invoke(pl, new object[] { "" });
                var n = WallNormal(wall);
                var p = new Vector3(wall.cx + n.x * 8f, 0, wall.cz + n.z * 8f);
                p.y = tr.Height(p.x, p.z);
                Tele(pl, p);
                FaceDir(pl, -n);
                GameInput.TestMove = new Vector2(0, -1);
                pl.camDistTarget = 9f; pl.camPitch = 0.3f;
            }
            if (f > 410 && f <= 560 && haveWall)
            {
                if (f % 15 == 0)
                {
                    var n = WallNormal(wall);
                    float v = (pl.Pos.x - wall.cx) * n.x + (pl.Pos.z - wall.cz) * n.z;
                    Debug.Log($"SKYFALL_TEST into-wall f={f}: distance from wall axis={v:0.00} (wall half thickness {wall.hz:0.00} + radius {pl.PhysRadius:0.00} = {wall.hz + pl.PhysRadius:0.00}) y={pl.Pos.y:0.00}");
                }
                if (f == 545) Snap("solid_b_wall");
            }
            if (f == 561) { GameInput.TestMove = null; }

            // ------------------------------------------------ (c) player on top of the wall, ogres below
            if (f == 570 && haveWall)
            {
                ClearEnemies(game);
                Enemy.jumpCount = 0;
                Enemy.highestJump = 0f;
                var n = WallNormal(wall);
                float g0 = tr.Height(wall.cx, wall.cz);
                Tele(pl, new Vector3(wall.cx, wall.top + 0.05f, wall.cz));
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 0.8f;
                    var p = new Vector3(wall.cx, 0, wall.cz) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (4f + (i % 3));
                    p.y = tr.Height(p.x, p.z);
                    SpawnFixed(game, i % 4 == 3 ? "brute" : "ogre", p, false);
                }
                FaceDir(pl, n);
                pl.camYaw += 0.6f;
                pl.camDistTarget = 14f; pl.camPitch = 0.22f;
                snappedJump = false;
                Debug.Log($"SKYFALL_TEST jump-test start: player on wall top y={wall.top:0.00} (ground {g0:0.00}, reach {wall.top - g0:0.00} m) ogres={spawned.Count}");
            }
            if (f > 570 && f <= 1170 && haveWall)
            {
                pl.hp = pl.maxHp;
                foreach (var e in spawned) if (e) e.hp = e.maxHp;
                bool anyAir = false;
                int up = 0;
                foreach (var e in spawned)
                {
                    if (!e) continue;
                    if (e.Airborne) anyAir = true;
                    if (e.Pos.y > wall.top - 0.6f) up++;
                }
                if (anyAir && !snappedJump && frame > 600) { snappedJump = true; Snap("solid_c1_jump"); }
                if (f % 60 == 0)
                    Debug.Log($"SKYFALL_TEST jump-test f={f}: jumps={Enemy.jumpCount} highest={Enemy.highestJump:0.00} ogresOnTop={up} playerY={pl.Pos.y:0.00} playerOnTop={pl.Pos.y > wall.top - 0.2f} playerXZoff=({pl.Pos.x - wall.cx:0.0},{pl.Pos.z - wall.cz:0.0}) hp={pl.hp:0}");
                if (f == 1100) Snap("solid_c2_after");
            }

            // ------------------------------------------------ (d) the player walks into ogres
            if (f == 1180)
            {
                ClearEnemies(game);
                Tele(pl, new Vector3(0, tr.Height(0, 0), 0));
                FaceDir(pl, new Vector3(0, 0, -1));
                pl.camDistTarget = 9f; pl.camPitch = 0.3f;
                pl.camYaw = 0f;
                SpawnFixed(game, "hulk", new Vector3(0, tr.Height(0, -7), -7), true);
                GameInput.TestMove = new Vector2(0, -1);
            }
            if (f > 1180 && f <= 1330 && f % 15 == 0 && spawned.Count > 0 && spawned[0])
            {
                var e = spawned[0];
                Debug.Log($"SKYFALL_TEST into-hulk f={f}: player z={pl.Pos.z:0.00} hulk z={e.Pos.z:0.00} gap={Vector2.Distance(new Vector2(pl.Pos.x, pl.Pos.z), new Vector2(e.Pos.x, e.Pos.z)):0.00} (radii {pl.PhysRadius + e.radius:0.00})");
                if (f == 1320) Snap("solid_d1_hulk");
            }
            if (f == 1340)
            {
                ClearEnemies(game);
                Tele(pl, new Vector3(0, tr.Height(0, 0), 0));
                SpawnFixed(game, "ogre", new Vector3(0, tr.Height(0, -6), -6), true);
            }
            if (f > 1340 && f <= 1500 && f % 15 == 0 && spawned.Count > 0 && spawned[0])
            {
                var e = spawned[0];
                Debug.Log($"SKYFALL_TEST into-ogre f={f}: player z={pl.Pos.z:0.00} ogre z={e.Pos.z:0.00} (started -6.00) speed={new Vector2(pl.velocity.x, pl.velocity.z).magnitude:0.0}");
                if (f == 1425) Snap("solid_d2_ogre");
            }
            if (f == 1510)
            {
                // Surrounded by small ogres: can the hero still get out?
                ClearEnemies(game);
                Tele(pl, new Vector3(0, tr.Height(0, 0), 0));
                for (int i = 0; i < 14; i++)
                {
                    float a = i * M.TAU / 14f;
                    var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (i % 2 == 0 ? 1.4f : 2.5f);
                    SpawnFixed(game, i % 3 == 0 ? "runt" : "ogre", new Vector3(p.x, tr.Height(p.x, p.z), p.z), true);
                }
                GameInput.TestMove = new Vector2(0, -1);
            }
            if (f > 1510 && f <= 1690 && f % 30 == 0)
                Debug.Log($"SKYFALL_TEST surrounded-by-14 f={f}: player=({pl.Pos.x:0.00},{pl.Pos.z:0.00}) moved={new Vector2(pl.Pos.x, pl.Pos.z).magnitude:0.00} m");
            if (f == 1695) { GameInput.TestMove = null; Snap("solid_d3_crowd"); }

            // ------------------------------------------------ (e) camera near a wall and under an island
            if (f == 1700 && haveWall)
            {
                ClearEnemies(game);
                var n = WallNormal(wall);
                var p = new Vector3(wall.cx + n.x * 2.0f, 0, wall.cz + n.z * 2.0f);
                p.y = tr.Height(p.x, p.z);
                Tele(pl, p);
                pl.camYaw = Mathf.Atan2(-n.x, -n.z);   // the camera sits on the far side of the wall
                pl.camDistTarget = 9f; pl.camPitch = 0.15f;
                pl.SnapCamera();
            }
            if (f >= 1701 && f <= 1760 && haveWall)
            {
                if (f % 20 == 0 || f == 1760)
                {
                    var n = WallNormal(wall);
                    var c = pl.cam.transform.position;
                    float v = (c.x - wall.cx) * n.x + (c.z - wall.cz) * n.z;
                    Debug.Log($"SKYFALL_TEST camera-wall f={f}: cam dist from hero={Vector3.Distance(c, pl.Pos + Vector3.up * 2f):0.00} cam side offset v={v:0.00} (hero at 2.00, wall half {wall.hz:0.00}) blocked={tr.CameraBlocked(c)}");
                }
                if (f == 1760) Snap("solid_e1_camwall");
            }
            if (f == 1765)
            {
                // Camera above the hero who stands under an island: it would be inside the rock.
                float d = R * 0.5f;
                var p = new Vector3(isl.center.x + d, 0, isl.center.z);
                p.y = tr.Height(p.x, p.z);
                Tele(pl, p);
                pl.camYaw = -Mathf.PI * 0.5f; pl.camPitch = 1.0f; pl.camDistTarget = 12f;
                pl.SnapCamera();
            }
            if (f >= 1766 && f <= 1830 && (f % 20 == 0 || f == 1830))
            {
                var c = pl.cam.transform.position;
                Debug.Log($"SKYFALL_TEST camera-island f={f}: cam y={c.y:0.00} hero y={pl.Pos.y:0.00} island top={isl.topY:0.00} blocked={tr.CameraBlocked(c)} dist={Vector3.Distance(c, pl.Pos):0.00}");
                if (f == 1830) Snap("solid_e2_camisland");
            }

            // ------------------------------------------------ (f) ogres piled on one spot spread out
            if (f == 1840)
            {
                ClearEnemies(game);
                Tele(pl, new Vector3(40f, tr.Height(40f, 56f), 56f));
                for (int i = 0; i < 70; i++)
                {
                    string k = i < 50 ? "ogre" : (i < 64 ? "brute" : "hulk");
                    var p = new Vector3(40f + Mathf.Cos(i) * 1.2f, 0, 40f + Mathf.Sin(i * 1.7f) * 1.2f);
                    p.y = tr.Height(p.x, p.z);
                    SpawnFixed(game, k, p, true);
                }
                Debug.Log($"SKYFALL_TEST pile start: {PileStats()}");
            }
            if (f == 1960)
            {
                Debug.Log($"SKYFALL_TEST pile after 2 s: {PileStats()}");
                pl.Pos = new Vector3(40f, tr.Height(40f, 40f) + 0.1f, 52f);
                pl.camYaw = 0f; pl.camDistTarget = 22f; pl.camPitch = 0.6f;
                pl.SnapCamera();
            }
            if (f == 1990) Snap("solid_f_pile");
            // ------------------------------------------------ (g) the straggler cull leaves island ogres alone
            if (f == 2000)
            {
                ClearEnemies(game);
                Tele(pl, new Vector3(0, tr.Height(0, 0), 0));
                var b = tr.bossIsland;
                var onIsl = SpawnFixed(game, "ogre", new Vector3(b.center.x + 4f, b.topY, b.center.z), true, b.topY);
                var far = new Vector3(-100f, tr.Height(-100f, -100f), -100f);
                SpawnFixed(game, "ogre", far, true);
                Debug.Log($"SKYFALL_TEST cull start: island ogre at {onIsl.Pos} (dist {Vector3.Distance(onIsl.Pos, pl.Pos):0} from player), ground ogre at {spawned[1].Pos}");
            }
            if (f == 2060)
            {
                var b = tr.bossIsland;
                Debug.Log($"SKYFALL_TEST cull after 1 s: island ogre at {spawned[0].Pos} (island top {b.topY:0.0}, still on island={spawned[0].OnFloatingIsland()}); ground ogre at {spawned[1].Pos} dist from player {Vector3.Distance(spawned[1].Pos, pl.Pos):0}");
            }
            if (f == 2070)
            {
                Debug.Log($"SKYFALL_TEST solid done: jumpCount={Enemy.jumpCount} highestJump={Enemy.highestJump:0.00}");
                Finish(0);
            }
        }

        /// Overlap statistics of the spawned ogres: worst and mean penetration as a fraction of the radius sum.
        static string PileStats()
        {
            int pairs = 0, over = 0;
            float worst = 0f, sum = 0f;
            for (int i = 0; i < spawned.Count; i++)
                for (int j = i + 1; j < spawned.Count; j++)
                {
                    if (!spawned[i] || !spawned[j]) continue;
                    pairs++;
                    var a = spawned[i]; var b = spawned[j];
                    float d = Vector2.Distance(new Vector2(a.Pos.x, a.Pos.z), new Vector2(b.Pos.x, b.Pos.z));
                    float rr = a.radius + b.radius;
                    float pen = (rr - d) / rr;
                    if (pen > 0.25f) over++;
                    if (pen > worst) worst = pen;
                    if (pen > 0f) sum += pen;
                }
            float maxR = 0f;
            foreach (var e in spawned) if (e) maxR = Mathf.Max(maxR, Mathf.Max(Mathf.Abs(e.Pos.x - 40f), Mathf.Abs(e.Pos.z - 40f)));
            return $"pairs={pairs} pairsOverlapping>25%={over} worstPenetration={worst * 100f:0}% sumPenetration={sum:0.0} spread radius~{maxR:0.0}";
        }
    }
}
namespace Skyfall.EditorTools
{
    public static partial class SkyfallTests
    {
        /// -skyfallKitView 1: photographs the first towers and houses built from the Ruins kit.
        static void KitView(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            pl.hp = pl.maxHp = 99999f;
            typeof(Game).GetField("spawnAccum", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(game, -1e9f);
            var spots = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < Mathf.Min(4, tr.kitTowers.Count); i++) spots.Add(tr.kitTowers[i]);
            for (int i = 0; i < Mathf.Min(3, tr.kitHouses.Count); i++) spots.Add(tr.kitHouses[i]);
            int idx = (frame - 5) / 40, sub = (frame - 5) % 40;
            if (frame == 1) Debug.Log($"SKYFALL_TEST kitview: towers={tr.kitTowers.Count} houses={tr.kitHouses.Count}");
            if (frame < 5) return;
            if (idx >= spots.Count) { Finish(0); return; }
            var c = spots[idx];
            if (sub == 0)
            {
                pl.Pos = new Vector3(c.x + 14f, tr.Height(c.x + 14f, c.z), c.z);
                pl.velocity = Vector3.zero;
                pl.camYaw = Mathf.Atan2(-(c.x - pl.Pos.x), -(c.z - pl.Pos.z));
                pl.camDistTarget = 14f; pl.camPitch = 0.3f;
                pl.SnapCamera();
            }
            if (sub == 30) Snap((idx < Mathf.Min(4, tr.kitTowers.Count) ? "kit_tower_" : "kit_house_") + idx);
        }
    }
}
