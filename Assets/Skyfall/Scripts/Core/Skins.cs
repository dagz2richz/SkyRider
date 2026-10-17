using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyfall
{
    /// Access to the skin library and helpers for spawning Asset Store models into the game.
    public static class Skins
    {
        static SkinLibrary lib;
        static bool loaded;

        /// The skin library, or null when the asset packs are not set up (the game then uses procedural models).
        public static SkinLibrary Lib
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    lib = Resources.Load<SkinLibrary>("Skyfall/SkinLibrary");
                }
                return lib;
            }
        }

        public static T Pick<T>(T[] arr) where T : Object
        {
            if (arr == null || arr.Length == 0) return null;
            return arr[Random.Range(0, arr.Length)];
        }

        public static T Pick<T>(T[] arr, Rng rng) where T : Object
        {
            if (arr == null || arr.Length == 0) return null;
            return arr[rng.Next() % arr.Length];
        }

        static readonly Dictionary<GameObject, float> headHeights = new Dictionary<GameObject, float>();

        /// Spawns a character prefab under parent, turned to face -Z (the game's forward) and scaled so its head sits
        /// at headHeight. Returns the rig driving its animations.
        public static CharacterRig SpawnCharacter(GameObject prefab, Transform parent, float headHeight, RuntimeAnimatorController controller = null)
        {
            var holder = new GameObject(prefab.name + " (skin)").transform;
            holder.SetParent(parent, false);
            var go = Object.Instantiate(prefab, holder, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one;
            var rig = holder.gameObject.AddComponent<CharacterRig>();
            rig.Init(go, controller);
            float h = HeadHeight(prefab, rig);
            if (h > 0.01f) holder.localScale = Vector3.one * (headHeight / h);
            return rig;
        }

        static float HeadHeight(GameObject prefab, CharacterRig rig)
        {
            var id = prefab;
            if (headHeights.TryGetValue(id, out float h)) return h;
            var head = rig.Bone(HumanBodyBones.Head);
            if (head != null) h = head.position.y - rig.transform.position.y;
            else
            {
                var b = rig.Bounds();
                h = b.size.y * 0.85f;
            }
            headHeights[id] = h;
            return h;
        }

        /// Spawns a prop prefab (scenery) with a random turn.
        public static Transform SpawnProp(GameObject prefab, Transform parent, Vector3 pos, float yawDeg, float scale)
        {
            var go = Object.Instantiate(prefab, parent, false);
            var t = go.transform;
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, yawDeg, 0f) * t.localRotation;
            t.localScale = t.localScale * scale;
            return t;
        }

        /// Finds a child transform by exact name, then by partial name.
        public static Transform Find(Transform root, params string[] names)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var n in names)
                foreach (var t in all)
                    if (t.name == n) return t;
            foreach (var n in names)
                foreach (var t in all)
                    if (t.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }
    }

    /// Drives an Asset Store character's Animator by state name: a looping locomotion state plus one-shot actions
    /// (attacks, hits) that return to locomotion. Also tints the model for hit flashes and status effects.
    public class CharacterRig : MonoBehaviour
    {
        public Animator anim;
        public GameObject model;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        string current = "";
        float actionUntil;
        readonly Dictionary<string, bool> has = new Dictionary<string, bool>();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public void Init(GameObject go, RuntimeAnimatorController controller)
        {
            model = go;
            anim = go.GetComponentInChildren<Animator>();
            if (anim)
            {
                if (controller) anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
        }

        public bool Has(string state)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            if (!has.TryGetValue(state, out bool h))
            {
                h = anim.HasState(0, Animator.StringToHash(state));
                has[state] = h;
            }
            return h;
        }

        /// The game's state names, and the names a model with its own animator (the dragons) uses for the same thing.
        static string Alias(string s)
        {
            switch (s)
            {
                case "GetHit": return "Get Hit";
                case "Attack01": return "Basic Attack";
                case "Attack02": return "Tail Attack";
                case "Victory": return "Scream";
                default: return null;
            }
        }

        /// Plays the first state of the list that exists (or its alias). Returns the state played or null.
        string PlayFirst(string[] states, float fade, bool restart)
        {
            foreach (var name in states)
            {
                string s = name;
                if (!Has(s))
                {
                    s = Alias(name);
                    if (s == null || !Has(s)) continue;
                }
                if (s == current && !restart) return s;
                anim.CrossFadeInFixedTime(s, fade, 0);
                current = s;
                return s;
            }
            return null;
        }

        /// Sets the looping locomotion state (ignored while an action plays).
        public void Loop(params string[] states)
        {
            if (Time.time < actionUntil) return;
            PlayFirst(states, 0.2f, false);
        }

        /// Plays a one-shot action for a while, then locomotion resumes.
        public bool Action(float duration, params string[] states)
        {
            if (PlayFirst(states, 0.08f, true) == null) return false;
            actionUntil = Time.time + duration;
            return true;
        }

        /// Snaps the first state of the list that exists to one frame of its clip and evaluates the pose right now
        /// (for a still picture). Locomotion resumes with the next Loop call.
        public bool Pose(float normalizedTime, params string[] states)
        {
            string s = PlayFirst(states, 0f, true);
            if (s == null) return false;
            anim.Play(s, 0, normalizedTime);
            anim.Update(0f);
            anim.Update(0f);
            return true;
        }

        public bool InAction => Time.time < actionUntil;

        public float Speed
        {
            set { if (anim) anim.speed = value; }
        }

        public Transform Bone(HumanBodyBones b)
        {
            if (anim == null || !anim.isHuman) return null;
            return anim.GetBoneTransform(b);
        }

        public Bounds Bounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        Color lastEmission = new Color(-1, 0, 0, 0);

        /// Glow color for hit flashes and status effects (black = none). Linear color expected.
        public void SetEmission(Color linear)
        {
            if (linear == lastEmission) return;
            lastEmission = linear;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (linear.maxColorComponent <= 0.001f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, linear);
                r.SetPropertyBlock(mpb);
            }
        }

        public void SetShadows(bool on)
        {
            foreach (var r in renderers)
                if (r && !(r is ParticleSystemRenderer)) r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }
}
