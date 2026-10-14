using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Sound effects and master volume. Every sound is synthesized in code (no files needed), but any sound can be
    /// replaced by dropping audio clips into Assets/Skyfall/Resources/Skyfall/Sounds/&lt;name&gt;/ (one is picked at random).
    /// Sound names: hit, clang, block, parry, heal, slash, bow, arrow_hit, boom, whoosh, frost, zap, hurt, death,
    /// levelup, pickup, roar, flame, click.
    public class Sfx : MonoBehaviour
    {
        const int RATE = 22050;
        static readonly Dictionary<string, int> MIN_GAP_MS = new Dictionary<string, int>
        {
            ["clang"] = 50, ["block"] = 60, ["parry"] = 60, ["heal"] = 80, ["hit"] = 40, ["slash"] = 60, ["arrow_hit"] = 45,
            ["death"] = 45, ["pickup"] = 45, ["bow"] = 60, ["zap"] = 50, ["boom"] = 60,
        };

        public static Sfx inst;
        public static readonly Vector3 NO_POS = M.INF;

        readonly Dictionary<string, AudioClip> streams = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip[]> overrides = new Dictionary<string, AudioClip[]>();
        readonly List<AudioSource> players = new List<AudioSource>();
        int nextPlayer;
        readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        public Vector3 listener;
        System.Random synthRng;

        void Awake()
        {
            inst = this;
            Build();
            for (int i = 0; i < 24; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                players.Add(src);
            }
            SetVolume(Save.volume);
        }

        void OnDestroy()
        {
            if (inst == this) inst = null;
        }

        /// Batch-mode (headless/automated) runs are always silent so tests never play through the speakers.
        static bool Silent => Application.isBatchMode;

        public static void SetVolume(float v)
        {
            AudioListener.volume = Silent ? 0f : Mathf.Clamp01(v);
        }

        /// Play a sound. Pass a world position to make it quieter with distance from the player.
        public static void Play(string sound, Vector3 pos, float volumeDb = 0f, float pitchVar = 0.1f)
        {
            if (inst == null || Silent) return;
            var clip = inst.Clip(sound);
            if (clip == null) return;
            float now = Time.unscaledTime * 1000f;
            if (MIN_GAP_MS.TryGetValue(sound, out int gap) && inst.lastPlay.TryGetValue(sound, out float last) && now - last < gap)
                return;
            inst.lastPlay[sound] = now;
            float vol = volumeDb;
            if (!M.IsInf(pos))
            {
                float d = Vector3.Distance(pos, inst.listener);
                if (d > 70f) return;
                vol -= Mathf.Clamp01(d / 45f) * 18f;
            }
            var p = inst.players[inst.nextPlayer];
            inst.nextPlayer = (inst.nextPlayer + 1) % inst.players.Count;
            p.clip = clip;
            p.volume = Mathf.Clamp01(M.DbToLinear(vol));
            p.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            p.Play();
        }

        public static void Play(string sound, float volumeDb = 0f, float pitchVar = 0.1f) => Play(sound, NO_POS, volumeDb, pitchVar);

        AudioClip Clip(string sound)
        {
            var packClips = Skins.Lib ? Skins.Lib.Sound(sound) : null;
            if (packClips != null) return packClips[Random.Range(0, packClips.Length)];
            if (!overrides.TryGetValue(sound, out var list))
            {
                list = Resources.LoadAll<AudioClip>("Skyfall/Sounds/" + sound);
                overrides[sound] = list;
            }
            if (list != null && list.Length > 0) return list[Random.Range(0, list.Length)];
            streams.TryGetValue(sound, out var c);
            return c;
        }

        // ---------------------------------------------------------------- synthesis

        float Rf(float a, float b) => a + (b - a) * (float)synthRng.NextDouble();
        float Rand01() => (float)synthRng.NextDouble();

        AudioClip Wav(string name, float[] samples)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i], -1f, 1f) * (32000f / 32768f);
            var clip = AudioClip.Create(name, samples.Length, 1, RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float[] Buf(float seconds) => new float[(int)(seconds * RATE)];

        void Build()
        {
            synthRng = new System.Random(1234);
            streams["hit"] = Wav("hit", Metal(0.32f, 980f, 0.7f, 0.35f));
            streams["clang"] = Wav("clang", Metal(0.6f, 640f, 0.9f, 0.5f));
            streams["block"] = Wav("block", Metal(0.45f, 420f, 0.8f, 0.7f));
            streams["parry"] = Wav("parry", Metal(0.7f, 1450f, 0.7f, 0.15f));
            streams["heal"] = Wav("heal", Arpeggio(new[] { 659.25f, 987.77f, 1318.5f }, 0.07f));
            streams["slash"] = Wav("slash", Whoosh(0.13f, 0.25f, 0.5f));
            streams["bow"] = Wav("bow", Pluck(0.18f, 210f));
            streams["arrow_hit"] = Wav("arrow_hit", Thump(0.07f, 260f, 120f, 0.7f, 0.6f));
            streams["boom"] = Wav("boom", Boom(0.7f));
            streams["whoosh"] = Wav("whoosh", Whoosh(0.28f, 0.08f, 0.7f));
            streams["frost"] = Wav("frost", Shimmer(0.6f));
            streams["zap"] = Wav("zap", Zap(0.28f));
            streams["hurt"] = Wav("hurt", Thump(0.2f, 95f, 50f, 0.4f, 1.0f));
            streams["death"] = Wav("death", Grunt(0.26f));
            streams["levelup"] = Wav("levelup", Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.1f));
            streams["pickup"] = Wav("pickup", Blip(0.05f, 900f, 1400f));
            streams["roar"] = Wav("roar", Roar(0.9f));
            streams["flame"] = Wav("flame", Flame(0.5f));
            streams["click"] = Wav("click", Blip(0.03f, 1400f, 1100f));
        }

        /// Inharmonic struck-metal partials with a bright click: sword on armor, shields, parries.
        float[] Metal(float dur, float f, float gain, float thud)
        {
            var b = Buf(dur);
            float[] ratios = { 1.0f, 2.76f, 5.40f, 8.93f, 1.51f, 3.97f };
            float[] decays = { 7f, 10f, 14f, 20f, 9f, 16f };
            float[] amps = { 1.0f, 0.7f, 0.45f, 0.25f, 0.5f, 0.3f };
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float s = 0f;
                for (int j = 0; j < ratios.Length; j++)
                    s += Mathf.Sin(M.TAU * f * ratios[j] * t + j * 1.3f) * amps[j] * Mathf.Exp(-t * decays[j]);
                // Slight beating between near partials makes it ring like steel.
                s += Mathf.Sin(M.TAU * f * 1.012f * t) * 0.4f * Mathf.Exp(-t * 6f);
                float click = Rf(-1f, 1f) * Mathf.Exp(-t * 250f) * 0.9f;
                phase += M.TAU * Mathf.Lerp(160f, 60f, Mathf.Min(t / 0.1f, 1f)) / RATE;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 30f) * thud;
                b[i] = (s * 0.22f + click + body) * gain;
            }
            return b;
        }

        /// Pitch-dropping sine with a noisy attack: punches, impacts, thuds.
        float[] Thump(float dur, float f0, float f1, float noiseAmt, float gain)
        {
            var b = Buf(dur);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float k = t / dur;
                float f = Mathf.Lerp(f0, f1, Mathf.Sqrt(k));
                phase += M.TAU * f / RATE;
                float env = Mathf.Exp(-k * 6f);
                lp += 0.35f * (Rf(-1f, 1f) - lp);
                float n = lp * Mathf.Exp(-t * 60f) * noiseAmt;
                b[i] = (Mathf.Sin(phase) * env + n) * gain;
            }
            return b;
        }

        /// Band of noise sweeping in brightness: swings and casts.
        float[] Whoosh(float dur, float cut0, float cut1)
        {
            var b = Buf(dur);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float k = (float)i / b.Length;
                float a = Mathf.Lerp(cut0, cut1, k);
                lp += a * (Rf(-1f, 1f) - lp);
                lp2 += a * 0.5f * (lp - lp2);
                float env = Mathf.Sin(Mathf.PI * k) * (1f - k * 0.3f);
                b[i] = (lp - lp2) * env * 1.6f;
            }
            return b;
        }

        float[] Pluck(float dur, float f)
        {
            var b = Buf(dur);
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float env = Mathf.Exp(-t * 22f);
                float s = Mathf.Sin(M.TAU * f * t) + 0.5f * Mathf.Sin(M.TAU * f * 2.01f * t) + 0.25f * Mathf.Sin(M.TAU * f * 3f * t);
                b[i] = (s * 0.45f + Rf(-1f, 1f) * Mathf.Exp(-t * 200f) * 0.4f) * env;
            }
            return b;
        }

        float[] Boom(float dur)
        {
            var b = Buf(dur);
            float lp = 0f, phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                lp += 0.08f * (Rf(-1f, 1f) - lp);
                phase += M.TAU * Mathf.Lerp(70f, 35f, t / dur) / RATE;
                float env = Mathf.Exp(-t * 5f);
                b[i] = (lp * 2.2f + Mathf.Sin(phase) * 0.7f) * env;
            }
            return b;
        }

        float[] Shimmer(float dur)
        {
            var b = Buf(dur);
            float[] freqs = { 1180f, 1560f, 1990f, 2410f, 3100f };
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float s = 0f;
                for (int j = 0; j < freqs.Length; j++)
                    s += Mathf.Sin(M.TAU * freqs[j] * t + j) * Mathf.Exp(-t * (4f + j * 1.5f));
                float hiss = Rf(-1f, 1f) * Mathf.Exp(-t * 12f) * 0.3f;
                b[i] = (s * 0.16f + hiss) * Mathf.Min(1f, t * 200f);
            }
            return b;
        }

        float[] Zap(float dur)
        {
            var b = Buf(dur);
            float hold = 0f;
            int count = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                if (count <= 0)
                {
                    hold = Rf(-1f, 1f);
                    count = synthRng.Next(2, 31);
                }
                count--;
                float crackle = Rand01() < 0.6f ? 1f : 0.2f;
                b[i] = hold * crackle * Mathf.Exp(-t * 9f) * 0.6f;
            }
            return b;
        }

        float[] Grunt(float dur)
        {
            var b = Buf(dur);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float k = t / dur;
                float f = Mathf.Lerp(130f, 65f, k) * (1f + Mathf.Sin(t * 40f) * 0.04f);
                phase += f / RATE;
                float saw = (phase % 1f) * 2f - 1f;
                lp += 0.15f * (saw - lp);
                b[i] = lp * Mathf.Sin(Mathf.PI * Mathf.Min(k * 4f, 1f)) * (1f - k) * 0.9f;
            }
            return b;
        }

        float[] Arpeggio(float[] notes, float step)
        {
            var b = Buf(step * notes.Length + 0.3f);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * step * RATE);
                float f = notes[n];
                for (int i = start; i < b.Length; i++)
                {
                    float t = (float)(i - start) / RATE;
                    b[i] += (Mathf.Sin(M.TAU * f * t) + 0.3f * Mathf.Sin(M.TAU * f * 2f * t)) * Mathf.Exp(-t * 6f) * 0.28f;
                }
            }
            return b;
        }

        float[] Blip(float dur, float f0, float f1)
        {
            var b = Buf(dur);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float k = (float)i / b.Length;
                phase += M.TAU * Mathf.Lerp(f0, f1, k) / RATE;
                b[i] = Mathf.Sin(phase) * (1f - k) * 0.35f;
            }
            return b;
        }

        float[] Roar(float dur)
        {
            var b = Buf(dur);
            float phase = 0f, lp = 0f, nlp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / RATE;
                float k = t / dur;
                phase += Mathf.Lerp(85f, 55f, k) * (1f + Mathf.Sin(t * 31f) * 0.06f) / RATE;
                float saw = (phase % 1f) * 2f - 1f;
                lp += 0.1f * (saw - lp);
                nlp += 0.2f * (Rf(-1f, 1f) - nlp);
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(k * 3f, 1f)) * (1f - k);
                b[i] = (lp * 1.2f + nlp * 0.6f) * env;
            }
            return b;
        }

        float[] Flame(float dur)
        {
            var b = Buf(dur);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float k = (float)i / b.Length;
                lp += 0.18f * (Rf(-1f, 1f) - lp);
                b[i] = lp * 1.4f * Mathf.Sin(Mathf.PI * k) * (0.8f + 0.2f * Mathf.Sin(k * 60f));
            }
            return b;
        }
    }
}
