#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UavUsv.Editor
{
    public static class SydneyCoastValidator
    {
        private const string VisualPath =
            "Assets/Resources/SydneyCoast/sydney_regatta.dae";
        private const string CollisionPath =
            "Assets/Resources/SydneyCoast/sydney_regatta_shore.dae";

        [MenuItem("UAV-USV/Validate Sydney Coast")]
        public static void Validate()
        {
            ValidatePrefab(VisualPath, "visual", .0015f, Quaternion.identity);
            ValidatePrefab(
                CollisionPath,
                "collision",
                .00015f,
                Quaternion.Euler(-90f, 0f, 0f)
            );
            ValidateRuntime();
        }

        private static void ValidatePrefab(
            string path,
            string label,
            float scale,
            Quaternion localRotation)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
                throw new BuildFailedException($"Sydney {label} prefab not found: {path}");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.localRotation = localRotation;
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length == 0)
                    throw new BuildFailedException($"Sydney {label} contains no meshes.");

                Bounds bounds = renderers.Length > 0
                    ? renderers[0].bounds
                    : TransformBounds(filters[0].transform, filters[0].sharedMesh.bounds);
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                if (renderers.Length == 0)
                {
                    for (int i = 1; i < filters.Length; i++)
                        bounds.Encapsulate(
                            TransformBounds(filters[i].transform, filters[i].sharedMesh.bounds)
                        );
                }

                Debug.Log(
                    $"Sydney {label}: meshes={filters.Length}, renderers={renderers.Length}, " +
                    $"bounds center={bounds.center}, size={bounds.size}"
                );
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateRuntime()
        {
            SydneyCoastRuntime coast = SydneyCoastRuntime.Create();
            try
            {
                if (!coast.isReady || !coast.collisionRoot)
                    throw new BuildFailedException("Sydney runtime collision was not created.");

                Physics.SyncTransforms();
                Renderer[] renderers = coast.GetComponentsInChildren<Renderer>(true);
                Collider[] colliders = coast.collisionRoot.GetComponentsInChildren<Collider>(true);
                if (renderers.Length == 0 || colliders.Length == 0)
                    throw new BuildFailedException("Sydney runtime is missing renderers or colliders.");

                Renderer treeRenderer = null;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].name.ToLowerInvariant().Contains("tree"))
                    {
                        treeRenderer = renderers[i];
                        break;
                    }
                }
                if (treeRenderer && (!treeRenderer.sharedMaterial ||
                    !treeRenderer.sharedMaterial.mainTexture ||
                    !treeRenderer.sharedMaterial.IsKeywordEnabled("_ALPHATEST_ON")))
                    throw new BuildFailedException(
                        "Sydney trees are missing their cutout foliage material."
                    );

                Bounds visualBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i].enabled)
                        visualBounds.Encapsulate(renderers[i].bounds);
                }
                Bounds collisionBounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    collisionBounds.Encapsulate(colliders[i].bounds);

                if (visualBounds.size.x < 250f || visualBounds.size.x > 700f ||
                    visualBounds.size.y > 30f)
                    throw new BuildFailedException(
                        $"Sydney visual bounds are implausible: {visualBounds}"
                    );
                if (collisionBounds.size.x < 200f || collisionBounds.size.x > 700f ||
                    collisionBounds.size.y > 20f)
                    throw new BuildFailedException(
                        $"Sydney collision bounds are implausible: {collisionBounds}"
                    );

                int blocked = 0;
                int samples = 0;
                for (int y = -180; y <= 180; y += 10)
                {
                    for (int x = -180; x <= 180; x += 10)
                    {
                        samples++;
                        if (IsBlocked(colliders, new Vector2(x, y)))
                            blocked++;
                    }
                }

                bool startBlocked = IsBlocked(colliders, Vector2.zero);
                bool lighthouseBlocked = IsBlocked(colliders, new Vector2(35f, 18f));
                if (blocked == 0 || blocked >= samples)
                    throw new BuildFailedException(
                        $"Sydney occupancy sampling is invalid: {blocked}/{samples}"
                    );
                if (startBlocked || lighthouseBlocked)
                    throw new BuildFailedException(
                        $"Mission waterway is blocked: start={startBlocked}, " +
                        $"lighthouse={lighthouseBlocked}"
                    );

                Debug.Log(
                    $"Sydney runtime passed: visual={visualBounds}, " +
                    $"collision={collisionBounds}, occupied={blocked}/{samples}"
                );
            }
            finally
            {
                Object.DestroyImmediate(coast.gameObject);
            }
        }

        private static bool IsBlocked(Collider[] colliders, Vector2 enu)
        {
            Ray ray = new Ray(Coordinates.ToUnity(enu.x, enu.y, 80f), Vector3.down);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] && colliders[i].Raycast(ray, out _, 160f))
                    return true;
            }
            return false;
        }

        private static Bounds TransformBounds(Transform target, Bounds local)
        {
            Vector3 center = target.TransformPoint(local.center);
            Vector3 extents = local.extents;
            Vector3 axisX = target.TransformVector(extents.x, 0f, 0f);
            Vector3 axisY = target.TransformVector(0f, extents.y, 0f);
            Vector3 axisZ = target.TransformVector(0f, 0f, extents.z);
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
            );
            return new Bounds(center, extents * 2f);
        }
    }
}
#endif
