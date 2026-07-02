using UnityEngine;

namespace UavUsv
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class OceanSurface : MonoBehaviour
    {
        public int resolution = 180;
        public float size = 180f;
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
                vertices[i] = new Vector3(((float)x / resolution - .5f) * size, 0, ((float)z / resolution - .5f) * size);
                uv[i] = new Vector2((float)x / resolution, (float)z / resolution);
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
        }

        private void OnDestroy()
        {
            if (oceanMaterial) Destroy(oceanMaterial);
        }
    }
}
