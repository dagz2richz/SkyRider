using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Small math helpers that mirror the Godot functions the game was written with.
    public static class M
    {
        public const float TAU = Mathf.PI * 2f;
        public const float PI = Mathf.PI;

        /// Marker for "no point" (Godot used Vector3.INF).
        public static readonly Vector3 INF = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        public static bool IsInf(Vector3 v) => float.IsInfinity(v.x);

        public static float RandF() => UnityEngine.Random.value;
        public static float RandRange(float a, float b) => UnityEngine.Random.Range(a, b);
        /// Inclusive on both ends, like Godot's randi_range.
        public static int RandInt(int a, int b) => UnityEngine.Random.Range(a, b + 1);

        public static float MoveToward(float from, float to, float delta) => Mathf.MoveTowards(from, to, delta);

        public static float Smoothstep(float from, float to, float x)
        {
            if (Mathf.Approximately(from, to)) return x < from ? 0f : 1f;
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        /// Interpolates an angle in radians along the shortest path.
        public static float LerpAngle(float from, float to, float t)
        {
            float diff = Mathf.Repeat(to - from + PI, TAU) - PI;
            return from + diff * t;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static Vector3 SafeNormal(Vector3 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero;

        public static Quaternion Euler(Vector3 radians) => Quaternion.Euler(radians * Mathf.Rad2Deg);

        /// Yaw (radians) that points a model's local -Z along dir (the Godot facing convention).
        public static float YawTo(Vector3 dir) => Mathf.Atan2(-dir.x, -dir.z);

        /// Rotation whose local Y axis points along dir (for cylinders/capsules spanning two points).
        public static Quaternion AlongY(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-10f) return Quaternion.identity;
            return Quaternion.FromToRotation(Vector3.up, dir.normalized);
        }

        /// Rotation that points local -Z along dir.
        public static Quaternion FaceNegZ(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-10f) return Quaternion.identity;
            var up = Mathf.Abs(dir.normalized.y) < 0.99f ? Vector3.up : Vector3.right;
            return Quaternion.LookRotation(-dir, up);
        }

        public static Color Lighten(Color c, float amt) => new Color(c.r + (1f - c.r) * amt, c.g + (1f - c.g) * amt, c.b + (1f - c.b) * amt, c.a);
        public static Color Darken(Color c, float amt) => new Color(c.r * (1f - amt), c.g * (1f - amt), c.b * (1f - amt), c.a);
        public static Color WithA(Color c, float a) { c.a = a; return c; }
        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);

        public static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// Seeded random numbers for world generation.
    public class Rng
    {
        System.Random r;
        public Rng(int seed) { r = new System.Random(seed); }
        public float Randf() => (float)r.NextDouble();
        public float Range(float a, float b) => a + (b - a) * (float)r.NextDouble();
        /// Inclusive on both ends.
        public int RangeInt(int a, int b) => r.Next(a, b + 1);
        public int Next() => r.Next();
    }

    /// A transform whose local rotation is kept as Euler radians (so it can be read back, lerped and tweened).
    public class Joint
    {
        public readonly Transform t;
        Vector3 rot;
        Coroutine active;

        public Joint(Transform t) { this.t = t; rot = Vector3.zero; }

        public Vector3 Rot
        {
            get => rot;
            set { rot = value; if (t) t.localRotation = M.Euler(value); }
        }

        public float RotX { get => rot.x; set { var r = rot; r.x = value; Rot = r; } }
        public float RotY { get => rot.y; set { var r = rot; r.y = value; Rot = r; } }
        public float RotZ { get => rot.z; set { var r = rot; r.z = value; Rot = r; } }

        /// Plays a tween on this joint, replacing whatever tween it was playing.
        public void Play(IEnumerator routine)
        {
            if (active != null) Tw.Stop(active);
            active = Tw.Run(routine);
        }

        /// Tweens the rotation through a list of (target, duration) keys.
        public void Keys(params (Vector3 to, float dur)[] keys) => Play(KeysRoutine(keys));

        IEnumerator KeysRoutine((Vector3 to, float dur)[] keys)
        {
            foreach (var k in keys)
                yield return Tw.To(() => Rot, v => Rot = v, k.to, k.dur, t);
        }

        public void KeysX(params (float to, float dur)[] keys) => Play(KeysXRoutine(keys));

        IEnumerator KeysXRoutine((float to, float dur)[] keys)
        {
            foreach (var k in keys)
                yield return Tw.To(() => RotX, v => RotX = v, k.to, k.dur, t);
        }
    }

    public enum Ease { Linear, OutCubic, OutQuart, OutSine }

    /// A tiny coroutine-based tween system (stand-in for Godot's create_tween).
    public static class Tw
    {
        static TweenRunner runner;

        static TweenRunner Runner
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("Tweens");
                    runner = go.AddComponent<TweenRunner>();
                }
                return runner;
            }
        }

        public static Coroutine Run(IEnumerator routine) => Runner.StartCoroutine(routine);

        public static void Stop(Coroutine c)
        {
            if (c != null && runner != null) runner.StopCoroutine(c);
        }

        public static float Apply(Ease e, float t)
        {
            switch (e)
            {
                case Ease.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case Ease.OutQuart: return 1f - Mathf.Pow(1f - t, 4f);
                case Ease.OutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                default: return t;
            }
        }

        static float Dt(bool unscaled) => unscaled ? Time.unscaledDeltaTime : Time.deltaTime;

        /// Waits for a duration (scaled time unless unscaled).
        public static IEnumerator Wait(float dur, bool unscaled = false)
        {
            float t = 0f;
            while (t < dur) { t += Dt(unscaled); yield return null; }
        }

        /// Calls f(k) with k going 0 to 1 over dur. Stops early if alive is destroyed.
        public static IEnumerator Lerp(float dur, Action<float> f, UnityEngine.Object alive = null, Ease ease = Ease.Linear, bool unscaled = false)
        {
            bool track = alive != null;
            float t = 0f;
            while (t < dur)
            {
                if (track && alive == null) yield break;
                f(Apply(ease, dur > 0f ? t / dur : 1f));
                t += Dt(unscaled);
                yield return null;
            }
            if (track && alive == null) yield break;
            f(1f);
        }

        public static IEnumerator To(Func<float> get, Action<float> set, float to, float dur, UnityEngine.Object alive = null, Ease ease = Ease.Linear, bool unscaled = false)
        {
            if (alive == null && !ReferenceEquals(alive, null)) yield break;
            float from = get();
            yield return Lerp(dur, k => set(Mathf.LerpUnclamped(from, to, k)), alive, ease, unscaled);
        }

        public static IEnumerator To(Func<Vector3> get, Action<Vector3> set, Vector3 to, float dur, UnityEngine.Object alive = null, Ease ease = Ease.Linear, bool unscaled = false)
        {
            if (alive == null && !ReferenceEquals(alive, null)) yield break;
            Vector3 from = get();
            yield return Lerp(dur, k => set(Vector3.LerpUnclamped(from, to, k)), alive, ease, unscaled);
        }

        /// Runs a list of steps one after another.
        public static IEnumerator Seq(params IEnumerator[] steps)
        {
            foreach (var s in steps) yield return s;
        }

        public static IEnumerator Call(Action a)
        {
            a();
            yield break;
        }

        /// Runs a callback after a delay.
        public static Coroutine After(float delay, Action a, bool unscaled = false) => Run(Seq(Wait(delay, unscaled), Call(a)));
    }

    public class TweenRunner : MonoBehaviour { }
}
