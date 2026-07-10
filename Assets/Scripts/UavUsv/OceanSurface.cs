using UnityEngine;

namespace UavUsv
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class OceanSurface : MonoBehaviour
    {
        public int resolution = 180;
        public float size = 180f;
        [Range(0f, .45f)] public float edgeIrregularity = .16f;
        [Range(0f, 20f)] public float windSpeed = 7f;
        [Range(0f, 360f)] public float windDirectionDegrees = 28f;
        [Range(0f, 1.5f)] public float waveAmplitude = .26f;
        private Mesh mesh;
        private Material oceanMaterial;

        private void Awake()
        {
            mesh = new Mesh { name = "Procedural Gerstner Ocean", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            int n = resolution + 1;
            var vertices = new Vector3[n * n];
            var uv = new Vector2[n * n];
            var triangles = new int[resolution * resolution * 6];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float u = (float)x / resolution;
                float v = (float)z / resolution;
                Vector2 p = new Vector2(u - .5f, v - .5f) * size;
                p = WarpEdge(p);
                vertices[i] = new Vector3(p.x, 0, p.y);
                uv[i] = new Vector2(u, v);
            }
            int t = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * n + x;
                triangles[t++] = i; triangles[t++] = i + n; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + n; triangles[t++] = i + n + 1;
            }
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var shader = Resources.Load<Shader>("WindOcean") ?? Shader.Find("UavUsv/WindOcean");
            if (shader)
            {
                oceanMaterial = new Material(shader) { name = "Runtime Wind Ocean" };
                GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;
                ApplyWind();
            }
        }

        private void Update()
        {
            ApplyWind();
        }

        private void ApplyWind()
        {
            if (!oceanMaterial) return;
            float radians = windDirectionDegrees * Mathf.Deg2Rad;
            oceanMaterial.SetVector("_WindDirection", new Vector4(Mathf.Cos(radians), 0, Mathf.Sin(radians), 0));
            oceanMaterial.SetFloat("_WindSpeed", windSpeed);
            oceanMaterial.SetFloat("_WaveAmplitude", waveAmplitude);
            oceanMaterial.SetColor("_DeepColor", new Color(.035f, .17f, .22f, 1f));
            oceanMaterial.SetColor("_ShallowColor", new Color(.075f, .31f, .37f, 1f));
            oceanMaterial.SetColor("_FoamColor", new Color(.72f, .86f, .88f, 1f));
        }

        private void OnDestroy()
        {
            if (oceanMaterial) Destroy(oceanMaterial);
        }

        private Vector2 WarpEdge(Vector2 point)
        {
            if (edgeIrregularity <= 0f)
                return point;

            float half = size * .5f;
            float edgeBand = size * .22f;
            float distanceToEdge = Mathf.Min(
                Mathf.Min(point.x + half, half - point.x),
                Mathf.Min(point.y + half, half - point.y)
            );
            float edgeWeight = 1f - Mathf.Clamp01(distanceToEdge / edgeBand);
            if (edgeWeight <= 0f)
                return point;

            Vector2 fromCenter = point.sqrMagnitude > .001f
                ? point.normalized
                : Vector2.up;
            float angle = Mathf.Atan2(fromCenter.y, fromCenter.x);
            float wave =
                Mathf.Sin(angle * 5.0f + .35f) * .46f +
                Mathf.Sin(angle * 9.0f + 1.8f) * .31f +
                Mathf.Sin(angle * 15.0f - .7f) * .18f;
            float offset = wave * size * edgeIrregularity * .42f * edgeWeight;
            return point + fromCenter * offset;
        }
    }
}
