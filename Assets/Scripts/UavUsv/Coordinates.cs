using UnityEngine;

namespace UavUsv
{
    public static class Coordinates
    {
        // Gazebo ENU (x, y, z-up) -> Unity (x, y-up, z).
        public static Vector3 ToUnity(float eastX, float northY, float upZ) => new Vector3(eastX, upZ, northY);
        public static Vector3 ToEnu(Vector3 unity) => new Vector3(unity.x, unity.z, unity.y);
    }
}
