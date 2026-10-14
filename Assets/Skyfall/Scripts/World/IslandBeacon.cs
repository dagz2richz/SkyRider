using UnityEngine;

namespace Skyfall
{
    /// A tall pulsing pillar of light over the boss island, so the player can see where the final battle is.
    public class IslandBeacon : MonoBehaviour
    {
        const float HEIGHT = 420f;
        Material coreMat, haloMat, ringMat;
        float intensity = 0.3f;
        float target = 0.3f;
        float phase;

        public static IslandBeacon Create(Vector3 center, float islandRadius)
        {
            var b = new GameObject("IslandBeacon").AddComponent<IslandBeacon>();
            var t = b.transform;
            t.position = center;
            var gold = new Color(1f, 0.78f, 0.35f, 1f);
            b.coreMat = FX.FadeMat(gold);
            b.haloMat = FX.FadeMat(new Color(1f, 0.55f, 0.2f, 1f));
            b.ringMat = FX.FadeMat(gold);
            var cyl = MeshGen.Cylinder(0.5f, 0.5f, 20);
            FX.NoShadow(FX.Part(t, cyl, b.coreMat, new Vector3(0, HEIGHT / 2f, 0), new Vector3(1.6f, HEIGHT, 1.6f)));
            FX.NoShadow(FX.Part(t, cyl, b.haloMat, new Vector3(0, HEIGHT / 2f, 0), new Vector3(7f, HEIGHT, 7f)));
            FX.NoShadow(FX.Part(t, cyl, b.haloMat, new Vector3(0, HEIGHT / 2f, 0), new Vector3(14f, HEIGHT, 14f)));
            FX.NoShadow(FX.Part(t, MeshGen.Ring(), b.ringMat, new Vector3(0, 0.6f, 0), new Vector3(islandRadius * 0.9f, 0.25f, islandRadius * 0.9f)));
            FX.Particles(t, new FX.ParticleSpec
            {
                amount = 40, lifetime = 3f, size = 0.7f, scaleMin = 0.5f, scaleMax = 1.3f, speedMin = 6f, speedMax = 12f,
                spread = 6f, sphereRadius = 0f, startColor = new Color(1f, 0.85f, 0.5f, 0.9f), endColor = new Color(1f, 0.6f, 0.2f, 0f),
                boost = 2f, worldSpace = true,
            }).transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(t, false);
            light.transform.localPosition = new Vector3(0, 12f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.intensity = 3f;
            light.range = islandRadius * 1.6f;
            light.shadows = LightShadows.None;
            b.Apply(1f);
            return b;
        }

        public void SetIntensity(float k) => target = k;

        /// Fades the beacon away (the Mountain King is dead).
        public void Extinguish() => target = 0f;

        void Update()
        {
            intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime * 0.5f);
            phase += Time.deltaTime;
            Apply(0.85f + 0.15f * Mathf.Sin(phase * 2.2f));
            if (intensity <= 0.001f && target <= 0f) gameObject.SetActive(false);
        }

        void Apply(float pulse)
        {
            float k = intensity * pulse;
            FX.SetAlpha(coreMat, 0.85f * k);
            FX.SetAlpha(haloMat, 0.10f * k);
            FX.SetAlpha(ringMat, 0.3f * k);
        }

        void OnDestroy()
        {
            if (coreMat) Destroy(coreMat);
            if (haloMat) Destroy(haloMat);
            if (ringMat) Destroy(ringMat);
        }
    }
}
