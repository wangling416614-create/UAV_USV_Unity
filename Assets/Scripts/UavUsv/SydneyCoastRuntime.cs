using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class SydneyCoastRuntime : MonoBehaviour
    {
        private const float GazeboYawDegrees = 27.215f;
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private readonly List<Mesh> runtimeMeshes = new List<Mesh>();

        public Transform collisionRoot { get; private set; }
        public bool isReady { get; private set; }

        [Tooltip("Keep the outer Sydney scenery but cut out the center channel.")]
        public bool removeCenterChannel = true;
        public bool surroundWithOuterScenery = true;

        public static SydneyCoastRuntime Create()
        {
            var root = new GameObject("SydneyCoastline");
            SydneyCoastRuntime coast = root.AddComponent<SydneyCoastRuntime>();
            coast.Build();
            return coast;
        }

        private void Build()
        {
            GameObject visualPrefab = Resources.Load<GameObject>(
                "SydneyCoast/sydney_regatta"
            );
            GameObject collisionPrefab = Resources.Load<GameObject>(
                "SydneyCoast/sydney_regatta_shore"
            );
            if (!visualPrefab || !collisionPrefab)
            {
                Debug.LogWarning("Sydney coastline resources are unavailable.");
                return;
            }

            transform.rotation = Quaternion.Euler(0f, -GazeboYawDegrees, 0f);

            GameObject visualRoot = new GameObject("Sydney Coast Visual");
            visualRoot.transform.SetParent(transform, false);
            GameObject collisionRootObject = new GameObject("Sydney Coast Collision");
            collisionRootObject.transform.SetParent(transform, false);
            collisionRoot = collisionRootObject.transform;

            float[] rotations = surroundWithOuterScenery
                ? new[] { 0f, 90f, 180f, 270f }
                : new[] { 0f };

            for (int i = 0; i < rotations.Length; i++)
            {
                float yaw = rotations[i];
                GameObject visual = Instantiate(visualPrefab, visualRoot.transform);
                visual.name = "Sydney Coast Visual " + yaw.ToString("000");
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                // Unity applies the Collada node scale but not its centimeter unit.
                visual.transform.localScale = Vector3.one * .0015f;
                if (removeCenterChannel)
                    ClipCenterChannelMeshes(visual);
                ApplyLightweightMaterials(visual);
                RemoveImportedComponents(visual);

                GameObject collision = Instantiate(collisionPrefab, collisionRoot);
                collision.name = "Sydney Coast Collision " + yaw.ToString("000");
                collision.transform.localPosition = Vector3.zero;
                // The collision DAE retains Z-up and omits its 0.001 scene scale.
                collision.transform.localRotation = Quaternion.Euler(-90f, yaw, 0f);
                collision.transform.localScale = Vector3.one * .00015f;
                if (removeCenterChannel)
                    ClipCenterChannelMeshes(collision);
            }

            if (removeCenterChannel)
                BuildShorelineBlend(visualRoot.transform);

            foreach (Renderer renderer in collisionRoot.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (MeshFilter filter in collisionRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh)
                    continue;
                MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                meshCollider.convex = false;
            }

            Physics.SyncTransforms();
            isReady = collisionRoot.GetComponentInChildren<MeshCollider>() != null;
        }

        private void BuildShorelineBlend(Transform parent)
        {
            Material shallow = SceneFactory.Material(
                "Soft Shallow Water",
                new Color(.075f, .33f, .38f, .22f),
                0f,
                .74f
            );
            Material wetSand = SceneFactory.Material(
                "Soft Wet Shore",
                new Color(.34f, .43f, .31f, .26f),
                0f,
                .18f
            );
            runtimeMaterials.Add(shallow);
            runtimeMaterials.Add(wetSand);

            GameObject root = new GameObject("Soft Shoreline Blend");
            root.transform.SetParent(parent, false);

            CreateBlendStrip(
                root.transform,
                "North Shallow Blend",
                -130f,
                132f,
                58f,
                92f,
                true,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "South Shallow Blend",
                -130f,
                132f,
                -160f,
                -126f,
                true,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "East Shallow Blend",
                92f,
                126f,
                -160f,
                92f,
                false,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "West Shallow Blend",
                -126f,
                -92f,
                -160f,
                92f,
                false,
                shallow
            );

            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore",
                -168f,
                168f,
                92f,
                118f,
                true,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore South",
                -168f,
                168f,
                -184f,
                -158f,
                true,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore East",
                126f,
                152f,
                -184f,
                118f,
                false,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore West",
                -152f,
                -126f,
                -184f,
                118f,
                false,
                wetSand
            );
        }

        private void CreateBlendStrip(
            Transform parent,
            string name,
            float minX,
            float maxX,
            float minY,
            float maxY,
            bool horizontal,
            Material material
        )
        {
            const int segments = 56;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float wave = (
                    Mathf.Sin(t * Mathf.PI * 6.3f) * 3.1f +
                    Mathf.Sin(t * Mathf.PI * 13.7f + 1.2f) * 1.4f
                );

                if (horizontal)
                {
                    float x = Mathf.Lerp(minX, maxX, t);
                    vertices[i * 2] = Coordinates.ToUnity(x, minY + wave, .035f);
                    vertices[i * 2 + 1] = Coordinates.ToUnity(x, maxY + wave * .55f, .045f);
                }
                else
                {
                    float y = Mathf.Lerp(minY, maxY, t);
                    vertices[i * 2] = Coordinates.ToUnity(minX + wave, y, .035f);
                    vertices[i * 2 + 1] = Coordinates.ToUnity(maxX + wave * .55f, y, .045f);
                }
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            int triangle = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[triangle++] = a;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = d;
            }

            Mesh mesh = new Mesh { name = name + " Mesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);

            GameObject strip = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            strip.transform.SetParent(parent, false);
            strip.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = strip.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ApplyLightweightMaterials(GameObject visual)
        {
            Texture2D treeTexture = Resources.Load<Texture2D>(
                "SydneyCoast/TreeDiffuse"
            );
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                string key = (renderer.gameObject.name + " " + renderer.name).ToLowerInvariant();
                bool isTree = key.Contains("tree");
                Color color;
                float smoothness;
                if (isTree)
                {
                    color = new Color(.92f, .96f, .9f);
                    smoothness = .05f;
                }
                else if (key.Contains("terrain"))
                {
                    color = new Color(.28f, .38f, .27f);
                    smoothness = .08f;
                }
                else if (key.Contains("window"))
                {
                    color = new Color(.035f, .18f, .25f);
                    smoothness = .82f;
                }
                else if (key.Contains("roof"))
                {
                    color = new Color(.46f, .2f, .12f);
                    smoothness = .25f;
                }
                else if (key.Contains("metal"))
                {
                    color = new Color(.43f, .48f, .5f);
                    smoothness = .62f;
                }
                else if (key.Contains("dockdark"))
                {
                    color = new Color(.11f, .13f, .14f);
                    smoothness = .28f;
                }
                else if (key.Contains("dock"))
                {
                    color = new Color(.37f, .29f, .2f);
                    smoothness = .22f;
                }
                else
                {
                    color = new Color(.62f, .57f, .46f);
                    smoothness = .18f;
                }

                Material material = SceneFactory.Material(
                    "Sydney " + renderer.gameObject.name,
                    color,
                    key.Contains("metal") ? .28f : 0f,
                    smoothness
                );
                if (isTree && treeTexture)
                    ConfigureTreeMaterial(material, treeTexture);
                runtimeMaterials.Add(material);
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private void ClipCenterChannelMeshes(GameObject root)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh)
                    continue;

                Mesh clipped = ClipMesh(filter.sharedMesh, filter.transform);
                if (!clipped)
                    continue;

                runtimeMeshes.Add(clipped);
                filter.sharedMesh = clipped;
            }
        }

        private Mesh ClipMesh(Mesh source, Transform meshTransform)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            var keptVertices = new List<Vector3>(vertices.Length);
            var keptNormals = normals != null && normals.Length == vertices.Length
                ? new List<Vector3>(normals.Length)
                : null;
            var keptUvs = uvs != null && uvs.Length == vertices.Length
                ? new List<Vector2>(uvs.Length)
                : null;
            var remap = new Dictionary<int, int>(vertices.Length);
            var trianglesBySubmesh = new List<int[]>();
            bool changed = false;

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                var keptTriangles = new List<int>(triangles.Length);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector3 center = (
                        meshTransform.TransformPoint(vertices[a]) +
                        meshTransform.TransformPoint(vertices[b]) +
                        meshTransform.TransformPoint(vertices[c])
                    ) / 3f;

                    if (IsInRemovedCenterChannel(center))
                    {
                        changed = true;
                        continue;
                    }

                    keptTriangles.Add(GetMappedIndex(a));
                    keptTriangles.Add(GetMappedIndex(b));
                    keptTriangles.Add(GetMappedIndex(c));
                }
                trianglesBySubmesh.Add(keptTriangles.ToArray());
            }

            if (!changed)
                return null;

            Mesh mesh = new Mesh
            {
                name = source.name + " OuterOnly",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.SetVertices(keptVertices);
            if (keptNormals != null && keptNormals.Count == keptVertices.Count)
                mesh.SetNormals(keptNormals);
            if (keptUvs != null && keptUvs.Count == keptVertices.Count)
                mesh.SetUVs(0, keptUvs);
            mesh.subMeshCount = trianglesBySubmesh.Count;
            for (int submesh = 0; submesh < trianglesBySubmesh.Count; submesh++)
                mesh.SetTriangles(trianglesBySubmesh[submesh], submesh);
            if (keptNormals == null)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            int GetMappedIndex(int original)
            {
                if (remap.TryGetValue(original, out int mapped))
                    return mapped;

                mapped = keptVertices.Count;
                remap.Add(original, mapped);
                keptVertices.Add(vertices[original]);
                if (keptNormals != null)
                    keptNormals.Add(normals[original]);
                if (keptUvs != null)
                    keptUvs.Add(uvs[original]);
                return mapped;
            }
        }

        private static bool IsInRemovedCenterChannel(Vector3 world)
        {
            Vector3 enu = Coordinates.ToEnu(world);
            return enu.x > -105f && enu.x < 112f &&
                   enu.y > -145f && enu.y < 78f;
        }

        private static void ConfigureTreeMaterial(
            Material material,
            Texture treeTexture
        )
        {
            material.mainTexture = treeTexture;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 1f);
            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", .18f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt(
                    "_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.One
                );
            if (material.HasProperty("_DstBlend"))
                material.SetInt(
                    "_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.Zero
                );
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 1);
            if (material.HasProperty("_Cull"))
                material.SetInt(
                    "_Cull",
                    (int)UnityEngine.Rendering.CullMode.Off
                );
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.enableInstancing = true;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        private static void RemoveImportedComponents(GameObject root)
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                Destroy(camera);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                Destroy(light);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
        }

        private void OnDestroy()
        {
            foreach (Material material in runtimeMaterials)
            {
                if (material)
                    Destroy(material);
            }
            runtimeMaterials.Clear();
            foreach (Mesh mesh in runtimeMeshes)
            {
                if (mesh)
                    Destroy(mesh);
            }
            runtimeMeshes.Clear();
        }
    }
}
