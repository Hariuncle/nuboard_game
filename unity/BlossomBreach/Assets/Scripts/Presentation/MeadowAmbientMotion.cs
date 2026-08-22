using UnityEngine;

namespace BlossomBreach
{
    /// <summary>Animates a small, fixed set of meadow accents without per-frame allocations.</summary>
    [DisallowMultipleComponent]
    public sealed class MeadowAmbientMotion : MonoBehaviour
    {
        private Transform[] clouds = System.Array.Empty<Transform>();
        private Transform[] leaves = System.Array.Empty<Transform>();
        private Transform[] fireflies = System.Array.Empty<Transform>();
        private Vector3[] cloudOrigins = System.Array.Empty<Vector3>();
        private Vector3[] leafOrigins = System.Array.Empty<Vector3>();
        private Vector3[] fireflyOrigins = System.Array.Empty<Vector3>();
        private Quaternion[] leafRotations = System.Array.Empty<Quaternion>();

        public void Configure(Transform[] cloudGroups, Transform[] driftingLeaves, Transform[] glowingFireflies)
        {
            clouds = cloudGroups ?? System.Array.Empty<Transform>();
            leaves = driftingLeaves ?? System.Array.Empty<Transform>();
            fireflies = glowingFireflies ?? System.Array.Empty<Transform>();
            cloudOrigins = CapturePositions(clouds);
            leafOrigins = CapturePositions(leaves);
            fireflyOrigins = CapturePositions(fireflies);
            leafRotations = CaptureRotations(leaves);
        }

        private void Update()
        {
            var time = Time.time;
            for (var i = 0; i < clouds.Length; i++)
            {
                var item = clouds[i];
                if (item == null) continue;
                var phase = time * (0.055f + i * 0.009f) + i * 1.73f;
                item.localPosition = cloudOrigins[i] + new Vector3(Mathf.Sin(phase) * (1.8f + i * 0.3f),
                    Mathf.Sin(phase * 1.7f) * 0.16f, 0f);
            }

            for (var i = 0; i < leaves.Length; i++)
            {
                var item = leaves[i];
                if (item == null) continue;
                var phase = time * (0.42f + i * 0.017f) + i * 0.91f;
                var fall = Mathf.Repeat(time * (0.18f + (i % 3) * 0.025f) + i * 0.37f, 1f);
                item.localPosition = leafOrigins[i] + new Vector3(Mathf.Sin(phase) * 0.58f,
                    0.62f - fall * 1.24f, Mathf.Cos(phase * 0.73f) * 0.28f);
                item.localRotation = leafRotations[i] * Quaternion.Euler(phase * 21f, phase * 37f, phase * 14f);
            }

            for (var i = 0; i < fireflies.Length; i++)
            {
                var item = fireflies[i];
                if (item == null) continue;
                var phase = time * (0.72f + (i % 4) * 0.09f) + i * 1.37f;
                item.localPosition = fireflyOrigins[i] + new Vector3(Mathf.Sin(phase) * 0.34f,
                    Mathf.Sin(phase * 1.43f) * 0.28f, Mathf.Cos(phase * 0.79f) * 0.24f);
                var pulse = 0.78f + Mathf.Sin(phase * 2.1f) * 0.18f;
                item.localScale = Vector3.one * pulse;
            }
        }

        private static Vector3[] CapturePositions(Transform[] items)
        {
            var positions = new Vector3[items.Length];
            for (var i = 0; i < items.Length; i++)
                positions[i] = items[i] != null ? items[i].localPosition : Vector3.zero;
            return positions;
        }

        private static Quaternion[] CaptureRotations(Transform[] items)
        {
            var rotations = new Quaternion[items.Length];
            for (var i = 0; i < items.Length; i++)
                rotations[i] = items[i] != null ? items[i].localRotation : Quaternion.identity;
            return rotations;
        }
    }
}
