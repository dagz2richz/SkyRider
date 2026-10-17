using UnityEngine;

namespace Skyfall
{
    /// The dragon's side of the wyvern encounter (see Wyvern.cs): what the AI reads to decide between bombing and
    /// harrying, and how a harrying wyvern pulls the dragon's attention.
    public partial class DragonPet
    {
        Enemy harryTarget;
        float harryUntil;
        float nextDiveHold;

        /// The dragon is breathing at something right now.
        public bool IsBreathing => breathActive > 0f;
        /// The dragon is in an automatic dive-bomb.
        public bool IsDiving => state == "dive";
        /// The hero is riding the dragon.
        public bool IsMounted => state == "mount";
        /// Seconds until the passive breath can start again (0 while it is breathing or ready).
        public float BreathReadyIn => breathActive > 0f ? 0f : Mathf.Max(0f, breathTimer);

        /// A wyvern swoops at the dragon: for a while its breath prefers that wyvern over everything else in reach.
        public void Harry(Enemy wyvern, float seconds)
        {
            if (wyvern == null || wyvern.dead) return;
            harryTarget = wyvern;
            harryUntil = Time.time + seconds;
            if (skin && state != "mount" && !skin.InAction) skin.Action(0.5f, SK_ROAR);
        }

        bool Harried => harryTarget != null && !harryTarget.dead && Time.time < harryUntil;

        /// The breath's target: the wyvern harrying the dragon when it is within reach, otherwise whatever is nearest
        /// (wyverns included: the dragon can hurt them).
        Enemy PickBreathTarget(Vector3 mouth, float range)
        {
            if (Harried && Vector3.Distance(harryTarget.Pos, mouth) <= range) return harryTarget;
            return game.NearestEnemy(mouth, range, 30f, null, null, true);
        }

        /// A harried dragon puts off its next automatic dive for a moment, but at most once per Data.WYVERN_HARRY_DIVE_LOCK
        /// seconds, so wyverns can delay dives yet never shut them off.
        bool WyvernHoldsDive()
        {
            if (!Harried || Time.time < nextDiveHold) return false;
            nextDiveHold = Time.time + Data.WYVERN_HARRY_DIVE_LOCK;
            return true;
        }
    }
}
