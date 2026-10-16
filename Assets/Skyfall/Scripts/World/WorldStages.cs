using UnityEngine;

namespace Skyfall
{
    /// Scenery for the later stages: forest villages of the Slavic World pack among BrokenVector trees (stage 2), and
    /// crypt ruins of the Elementary Dungeon pack lit by torches (stage 3). Without the packs the procedural props stand in.
    public partial class WorldTerrain
    {
        /// Trees for this stage's islands and forests.
        GameObject[] StageTrees => stage == 2 && Lib && Lib.forestTrees != null && Lib.forestTrees.Length > 0 ? Lib.forestTrees : (Lib ? Lib.deadTrees : null);

        // ---------------------------------------------------------------- stage 2

        readonly System.Collections.Generic.List<Vector3> villageCenters = new System.Collections.Generic.List<Vector3>();

        /// Keeps trees and bushes out of the villages.
        bool NearVillage(Vector3 p, float r = 24f)
        {
            foreach (var c in villageCenters)
                if ((new Vector2(p.x - c.x, p.z - c.z)).sqrMagnitude < r * r) return true;
            return false;
        }

        /// Moves a placed building so its base sits on the lowest ground under its footprint (minus a small sink), whatever
        /// its pivot is; so nothing floats on a slope or sinks into a hill.
        void SeatOnGround(Transform t, float sink)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float g = float.MaxValue;
            foreach (var c in new[] { new Vector2(b.min.x, b.min.z), new Vector2(b.min.x, b.max.z), new Vector2(b.max.x, b.min.z), new Vector2(b.max.x, b.max.z), new Vector2(b.center.x, b.center.z) })
                g = Mathf.Min(g, Height(c.x, c.y));
            t.position += new Vector3(0f, g - sink - b.min.y, 0f);
        }

        void ScatterForest()
        {
            // Villages: a ring of houses around a little square, fences and village clutter.
            for (int v = 0; v < 8; v++)
            {
                var center = Spot(34f, 0.1f);
                if (!M.IsInf(center)) villageCenters.Add(center);
                if (M.IsInf(center)) continue;
                int houses = rng.RangeInt(3, 5);
                for (int h = 0; h < houses; h++)
                {
                    float a = M.TAU * h / houses + rng.Range(-0.3f, 0.3f);
                    var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rng.Range(10f, 15f);
                    if (!GroundOk(p, 0.12f)) continue;
                    p.y = Height(p.x, p.z) - 0.2f;
                    var t = Lib ? PlacePackT(Lib.villageHouses, p, rng.Range(9f, 13f)) : null;
                    if (t != null)
                    {
                        // Face the square.
                        t.rotation = Quaternion.LookRotation(M.Flat(center - p).normalized + new Vector3(0.001f, 0, 0)) * Quaternion.Euler(0, rng.Range(-15f, 15f), 0);
                        SeatOnGround(t, 0.15f);
                        AddPropObstacle(t);
                    }
                    else Place(Props.RuinedHouse(rng), p - new Vector3(0, 0.1f, 0));
                }
                int props = rng.RangeInt(6, 10);
                for (int i = 0; i < props; i++)
                {
                    var p = Spot(16f, 0.2f, center, 18f);
                    if (M.IsInf(p)) continue;
                    if (!(Lib && PlacePack(Lib.villageProps, p - new Vector3(0, 0.05f, 0), rng.Range(1.2f, 3.2f))))
                        Place(Props.BrokenCart(rng), p, true);
                }
                var wp = Spot(16f, 0.15f, center, 6f);
                if (!M.IsInf(wp)) Place(Props.Well(rng), wp - new Vector3(0, 0.2f, 0));
                var cp = Spot(16f, 0.2f, center, 8f);
                if (!M.IsInf(cp)) Place(Props.Campfire(rng), cp, true);
            }
            // Forests of living trees.
            for (int f = 0; f < 22; f++)
            {
                var center = Spot(24f, 0.3f);
                if (M.IsInf(center)) continue;
                int n = rng.RangeInt(10, 20);
                for (int t = 0; t < n; t++)
                {
                    var p = Spot(12f, 0.35f, center, 22f);
                    if (!M.IsInf(p) && NearVillage(p)) continue;
                    if (M.IsInf(p)) continue;
                    if (!(Lib && PlacePack(StageTrees, p - new Vector3(0, 0.2f, 0), rng.Range(7f, 13f)))) Place(Props.DeadTree(rng), p - new Vector3(0, 0.2f, 0));
                }
            }
            // Lone trees, bushes, grass and rocks everywhere.
            for (int i = 0; i < 90; i++)
            {
                var p = Spot(14f, 0.3f);
                if (!M.IsInf(p) && Lib && !NearVillage(p)) PlacePack(StageTrees, p - new Vector3(0, 0.2f, 0), rng.Range(6f, 11f));
            }
            for (int i = 0; i < 220; i++)
            {
                var p = Spot(8f, 0.3f);
                if (M.IsInf(p) || Lib == null || NearVillage(p, 16f)) continue;
                PlacePack(Lib.forestBushes != null && Lib.forestBushes.Length > 0 ? Lib.forestBushes : Lib.shrubs, p - new Vector3(0, 0.1f, 0), rng.Range(1.2f, 2.6f));
            }
            for (int i = 0; i < 380; i++)
            {
                var p = Spot(6f, 0.22f);
                if (M.IsInf(p) || p.y > 26f || Lib == null) continue;
                PlacePack(Lib.grass, p - new Vector3(0, 0.05f, 0), rng.Range(0.9f, 1.8f), true);
            }
            for (int i = 0; i < 140; i++)
            {
                var p = Spot(8f, 0.6f);
                if (M.IsInf(p)) continue;
                if (!(Lib && PlacePack(Lib.forestRocks != null && Lib.forestRocks.Length > 0 ? Lib.forestRocks : Lib.rocks, p - new Vector3(0, 0.25f, 0), rng.Range(0.8f, 3.6f), true)))
                    PlaceRock(p);
            }
            // A few old standing stones and watchtowers from the first land still dot the hills.
            for (int i = 0; i < 3; i++)
            {
                var c = Spot(40f, 0.12f);
                if (!M.IsInf(c)) PlaceStoneCircle(c);
            }
            for (int i = 0; i < 5; i++)
            {
                var p = Spot(44f, 0.18f);
                if (M.IsInf(p)) continue;
                if (KitAvailable) BuildKitTower(p);
                else Place(Props.Watchtower(rng), p - new Vector3(0, 0.5f, 0));
            }
        }

        // ---------------------------------------------------------------- stage 3

        void ScatterCrypts()
        {
            bool pack = Lib && Lib.cryptPieces != null && Lib.cryptPieces.Length > 0;
            // Crypt halls: rings of chiseled walls and columns around a torch-lit floor, with clutter inside.
            for (int v = 0; v < 12; v++)
            {
                var center = Spot(30f, 0.12f);
                if (M.IsInf(center)) continue;
                if (KitAvailable && rng.Randf() < 0.4f) BuildKitVillage(center);
                int walls = rng.RangeInt(6, 9);
                float r0 = rng.Range(9f, 13f);
                float a0 = rng.Randf() * M.TAU;
                for (int i = 0; i < walls; i++)
                {
                    if (rng.Randf() < 0.2f) continue;   // broken gaps
                    float a = a0 + M.TAU * i / walls;
                    var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r0;
                    if (!GroundOk(p, 0.18f)) continue;
                    p.y = Height(p.x, p.z) - 0.3f;
                    if (pack)
                    {
                        var t = PlacePackT(Lib.cryptPieces, p, rng.Range(4f, 6.5f));
                        if (t != null)
                        {
                            t.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                            SeatOnGround(t, 0.25f);
                            AddPropObstacle(t, 0.9f);
                        }
                    }
                    else if (KitAvailable) KitPiece(i % 2 == 0 ? "pillar_broke_01" : "destroyed_wall_02", p, -a * Mathf.Rad2Deg + 90f, rng.Range(0.7f, 1f), 0.3f);
                    else Place(Props.RuinedHouse(rng), p);
                }
                int props = rng.RangeInt(5, 9);
                for (int i = 0; i < props; i++)
                {
                    var p = Spot(12f, 0.2f, center, r0 * 0.7f);
                    if (M.IsInf(p)) continue;
                    if (!(Lib && PlacePack(Lib.cryptProps, p - new Vector3(0, 0.05f, 0), rng.Range(0.9f, 2.4f))))
                        Place(Props.DeadSoldier(rng), p, true);
                }
                // Torches flanking the hall.
                for (int i = 0; i < 2; i++)
                {
                    float a = a0 + M.TAU * (i + 0.5f) / 2f;
                    var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r0 - 2f);
                    if (!GroundOk(p, 0.25f)) continue;
                    p.y = Height(p.x, p.z);
                    PlaceTorch(p);
                }
            }
            // Graveyards: rows of dead soldiers and blades, bone fields.
            for (int b = 0; b < 10; b++)
            {
                var center = Spot(18f, 0.2f);
                if (M.IsInf(center)) continue;
                for (int s = 0; s < rng.RangeInt(10, 16); s++)
                {
                    var p = Spot(8f, 0.3f, center, 14f);
                    if (!M.IsInf(p)) Place(Props.DeadSoldier(rng), p, true);
                }
                for (int s = 0; s < rng.RangeInt(4, 8); s++)
                {
                    var p = Spot(8f, 0.3f, center, 16f);
                    if (!M.IsInf(p)) Place(Props.BladeInGround(rng), p);
                }
                var cp = Spot(8f, 0.2f, center, 10f);
                if (!M.IsInf(cp)) PlaceTorch(cp);
            }
            // Dead forests and rocks.
            for (int f = 0; f < 10; f++)
            {
                var center = Spot(20f, 0.3f);
                if (M.IsInf(center)) continue;
                for (int t = 0; t < rng.RangeInt(6, 12); t++)
                {
                    var p = Spot(10f, 0.35f, center, 18f);
                    if (!M.IsInf(p)) PlaceTree(p);
                }
            }
            for (int i = 0; i < 160; i++)
            {
                var p = Spot(8f, 0.6f);
                if (!M.IsInf(p)) PlaceRock(p);
            }
            for (int i = 0; i < 5; i++)
            {
                var c = Spot(35f, 0.12f);
                if (!M.IsInf(c)) PlaceStoneCircle(c);
            }
        }

        /// A torch on a pole with a warm light (the dungeon pack's torch when available).
        void PlaceTorch(Vector3 p)
        {
            var holder = new GameObject("Torch").transform;
            holder.SetParent(packRoot, false);
            holder.position = p;
            if (Lib && Lib.cryptTorch)
            {
                var t = Skins.SpawnProp(Lib.cryptTorch, holder, Vector3.zero, rng.Randf() * 360f, 1f);
                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    float size = Mathf.Max(b.size.y, 0.01f);
                    t.localScale *= 2.6f / size;
                }
                // The pack's torch lights cast shadows; one shadowless light per torch is plenty.
                foreach (var tl in t.GetComponentsInChildren<Light>(true)) Destroy(tl);
            }
            else
            {
                FX.Part(holder, MeshGen.Cylinder(0.5f, 0.5f, 6), FX.Lit(Props.BARK), new Vector3(0, 1.2f, 0), new Vector3(0.12f, 2.4f, 0.12f));
                FX.NoShadow(FX.Part(holder, MeshGen.Sphere(8), FX.Glow(new Color(1f, 0.55f, 0.15f), 3f), new Vector3(0, 2.5f, 0), new Vector3(0.3f, 0.45f, 0.3f)));
            }
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(holder, false);
            l.transform.localPosition = new Vector3(0, 2.8f, 0);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.25f);
            l.intensity = 2.2f;
            l.range = 9f;
            l.shadows = LightShadows.None;
            foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.gameObject.layer = PROP_LAYER;
        }
    }
}
