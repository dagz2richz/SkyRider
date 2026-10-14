using UnityEngine;

namespace Skyfall
{
    /// Seeded 2D gradient noise with fractal (FBM) layering, standing in for Godot's FastNoiseLite.
    /// Output is roughly in [-1, 1].
    public class Noise
    {
        readonly int[] perm = new int[512];
        public float frequency = 0.01f;
        public int octaves = 5;
        public float lacunarity = 2f;
        public float gain = 0.5f;

        public Noise(int seed, float frequency = 0.01f, int octaves = 5)
        {
            this.frequency = frequency;
            this.octaves = octaves;
            var r = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = r.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        /// Single octave of gradient noise, roughly [-1, 1].
        public float Raw(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255;
            yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[xi] + yi], ab = perm[perm[xi] + yi + 1];
            int ba = perm[perm[xi + 1] + yi], bb = perm[perm[xi + 1] + yi + 1];
            float x1 = Mathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float x2 = Mathf.Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Mathf.Lerp(x1, x2, v) * 0.85f;
        }

        /// Fractal noise at world coordinates (frequency applied), normalized to roughly [-1, 1].
        public float Get(float x, float y)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            float fx = x * frequency, fy = y * frequency;
            for (int i = 0; i < octaves; i++)
            {
                sum += Raw(fx + i * 31.7f, fy + i * 17.3f) * amp;
                norm += amp;
                amp *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }
            return Mathf.Clamp(sum / norm * 1.35f, -1f, 1f);
        }
    }
}
