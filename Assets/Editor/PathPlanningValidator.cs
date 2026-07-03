#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UavUsv.Editor
{
    public static class PathPlanningValidator
    {
        [MenuItem("UAV-USV/Validate AStar Planner")]
        public static void Validate()
        {
            var planner = new GridAStarPlanner(-20f, -20f, 20f, 20f, 1f);
            var obstacles = new List<GridAStarPlanner.CircularObstacle>
            {
                new GridAStarPlanner.CircularObstacle(Vector2.zero, 4f)
            };

            bool planned = planner.TryPlan(
                new Vector2(-15f, 0f),
                new Vector2(15f, 0f),
                obstacles,
                out List<Vector2> path,
                out int expanded
            );
            if (!planned || path.Count < 3 || expanded <= 0)
                throw new BuildFailedException("A* validation failed to find a detour.");

            for (int i = 0; i < path.Count - 1; i++)
            {
                if (SegmentIntersectsCircle(path[i], path[i + 1], Vector2.zero, 4f))
                    throw new BuildFailedException("A* validation path crosses an obstacle.");
            }

            Debug.Log($"A* validation passed: {path.Count} waypoints, {expanded} expanded nodes.");
        }

        private static bool SegmentIntersectsCircle(
            Vector2 start,
            Vector2 end,
            Vector2 center,
            float radius)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            float t = lengthSquared > .0001f
                ? Mathf.Clamp01(Vector2.Dot(center - start, segment) / lengthSquared)
                : 0f;
            return (start + segment * t - center).sqrMagnitude <= radius * radius;
        }
    }
}
#endif
