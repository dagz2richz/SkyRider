using UnityEngine;

namespace Skyfall
{
    /// The shove a monster's melee hit (or the hero running into one) gives the hero. Tuning lives in Data (KNOCK_*).
    public partial class Player
    {
        Vector3 knockVel;      // flat velocity of the current shove, dying out exponentially
        float knockCd;         // seconds until a shove of similar strength may start again

        /// True while a shove is moving the hero.
        public bool Knocked => knockVel.sqrMagnitude > 0.01f;

        /// Shoves the hero `distance` metres along the flat direction `away`. Ignored in the saddle, in dragon form,
        /// mid-leap or mid-charge. During the cooldown only a clearly stronger blow replaces the current shove.
        public void Knockback(Vector3 away, float distance)
        {
            if (game == null || !game.running || game.traveling) return;
            if (flying || SoaringForm || leaping || chargeT > 0f || mountT > 0.2f) return;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            float v0 = Mathf.Min(distance, Data.KNOCK_MAX) * Data.KNOCK_DECAY;
            if (!onGround) v0 *= 0.5f;   // a lighter nudge in mid-air
            if (knockCd > 0f && v0 <= knockVel.magnitude * 1.25f) return;
            knockVel = away.normalized * v0;
            knockCd = Data.KNOCK_COOLDOWN;
        }

        /// The hero ran into a monster (nx, nz point from the monster to the hero): a smaller shove, and only at a run.
        void BumpInto(Enemy e, float nx, float nz)
        {
            if (knockCd > 0f) return;
            float closing = -(velocity.x * nx + velocity.z * nz);
            if (closing < Data.KNOCK_BUMP_SPEED) return;
            Knockback(new Vector3(nx, 0f, nz), e.KnockDistance * Data.KNOCK_BUMP_MULT);
        }

        /// This frame's displacement from the current shove. It stops dead (no push) if it would carry the hero over a
        /// drop of more than KNOCK_MAX_DROP, so an island rim or a cliff is never crossed by a shove.
        Vector3 KnockStep(float delta)
        {
            knockCd = Mathf.Max(0f, knockCd - delta);
            if (knockVel.sqrMagnitude < 0.01f)
            {
                knockVel = Vector3.zero;
                return Vector3.zero;
            }
            if (flying || SoaringForm || leaping || chargeT > 0f)
            {
                knockVel = Vector3.zero;
                return Vector3.zero;
            }
            var step = knockVel * delta;
            var terrain = game.terrain;
            float here = terrain.GroundHeight(Pos.x, Pos.z, Pos.y);
            float ahead = terrain.GroundHeight(Pos.x + step.x * 2f, Pos.z + step.z * 2f, Pos.y);
            if (ahead < here - Data.KNOCK_MAX_DROP)
            {
                knockVel = Vector3.zero;
                return Vector3.zero;
            }
            knockVel *= Mathf.Exp(-Data.KNOCK_DECAY * delta);
            return step;
        }
    }
}
