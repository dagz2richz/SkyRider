using UnityEngine;

namespace Skyfall
{
    /// Stage ambience around the player: rising embers in the Burning Fields, fireflies and drifting leaves in the
    /// Verdant Marches, low smoke and fog in the Sunken Crypts. The emitters ride along with the player and simulate in
    /// world space, so the air is full wherever they go without filling the whole map.
    public class WorldAmbient : MonoBehaviour
    {
        Transform rig;

        public void Init(int stage)
        {
            rig = FX.Node(transform, "Ambient");
            switch (stage)
            {
                case 2:
                    FX.VfxLoop("amb_fireflies", rig, new Vector3(0, 1.5f, 0), 2f);
                    FX.VfxLoop("amb_leaves", rig, new Vector3(0, 6f, 0), 2f);
                    break;
                case 3:
                    Fog(new Color(0.26f, 0.29f, 0.36f, 0.3f));
                    break;
                default:
                    Embers();
                    break;
            }
            // The rig moves with the player; particles already in the air must stay where they are.
            foreach (var ps in rig.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                if (!ps.isPlaying) ps.Play();
            }
            Follow();
        }

        /// Hot flecks drifting up off the scorched ground.
        void Embers()
        {
            var tex = FX.Tex("fx_ember");
            if (tex == null) return;
            var node = FX.Node(rig, "Embers", new Vector3(0, 0.3f, 0));
            node.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            FX.Particles(node, new FX.ParticleSpec
            {
                amount = 45, lifetime = 4.5f, size = 0.2f, scaleMin = 0.5f, scaleMax = 1.3f, speedMin = 0.3f, speedMax = 1.1f,
                boxExtents = new Vector3(18f, 18f, 0.5f), gravity = new Vector3(0.35f, 0.9f, 0.2f), tex = tex, boost = 2.6f, shrink = true,
                startColor = new Color(1f, 0.7f, 0.3f), midColor = new Color(1f, 0.45f, 0.1f), midAt = 0.5f, endColor = new Color(1f, 0.25f, 0.05f, 0f),
            });
        }

        /// Big slow smoke sprites hugging the ground.
        void Fog(Color color)
        {
            var tex = FX.Tex("fx_smoke_puff");
            if (tex == null) return;
            var node = FX.Node(rig, "Fog", new Vector3(0, 0.6f, 0));
            FX.Particles(node, new FX.ParticleSpec
            {
                amount = 22, lifetime = 9f, size = 7f, scaleMin = 0.7f, scaleMax = 1.4f, speedMin = 0.15f, speedMax = 0.45f,
                boxExtents = new Vector3(22f, 0.4f, 22f), additive = false, tex = tex, grow = true, spin = true,
                startColor = M.WithA(color, 0f), midColor = color, midAt = 0.5f, endColor = M.WithA(color, 0f),
            });
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            var g = Game.I;
            if (rig == null || g == null || g.player == null) return;
            rig.position = g.player.Pos;
        }
    }
}
