using UnityEngine;
using UnityEngine.Rendering;

namespace UavUsv
{
    /// <summary>
    /// Renders the same Catalina terrain used by the ROS/Gazebo
    /// heterogeneous_332 world. The mesh is visual-only in Unity; Gazebo
    /// remains authoritative for shoreline and mountain collision.
    /// </summary>
    public static class CatalinaIslandRuntime
    {
        // Gazebo Catalina is ~998 x 851 m after glTF scale 0.024. Full 1:1
        // dwarfs the fleet in Unity's chase view, so keep a compact visual
        // silhouette about the same ROS origin. Gazebo collision stays authoritative.
        private const float VisualScaleXz = .62f;
        private const float VisualScaleY = .38f;

        public static GameObject CreateVisualTerrain(Vector3 rosAlignedPosition)
        {
            GameObject prefab = Resources.Load<GameObject>(
                "CatalinaIsland/CatalinaTerrain"
            );
            Texture2D landTexture = Resources.Load<Texture2D>(
                "CatalinaIsland/CatalinaLandCutout"
            );
            if (!prefab || !landTexture)
            {
                Debug.LogWarning(
                    "Catalina terrain resources are unavailable."
                );
                return null;
            }

            GameObject terrain = Object.Instantiate(prefab);
            terrain.name = "catalina_island_terrain";
            terrain.transform.position = rosAlignedPosition;
            terrain.transform.rotation = Quaternion.identity;
            terrain.transform.localScale = new Vector3(
                VisualScaleXz,
                VisualScaleY,
                VisualScaleXz
            );

            foreach (Collider collider in terrain.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);
            foreach (Rigidbody body in terrain.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(body);

            Shader terrainShader =
                Resources.Load<Shader>("CatalinaTerrain") ??
                Shader.Find("UavUsv/CatalinaTerrain");
            if (!terrainShader)
            {
                Debug.LogWarning("Catalina terrain shader is unavailable.");
                Object.Destroy(terrain);
                return null;
            }

            Material material = new Material(terrainShader)
            {
                name = "Catalina satellite terrain",
                color = new Color(.90f, .88f, .80f, 1f),
                mainTexture = landTexture
            };
            material.mainTexture = landTexture;
            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", .42f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", .18f);

            foreach (Renderer renderer in terrain.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            return terrain;
        }
    }
}
