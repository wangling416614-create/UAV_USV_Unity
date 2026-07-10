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
            ValidateSydneyRoute();
        }

        private static void ValidateSydneyRoute()
        {
            SydneyCoastRuntime coast = SydneyCoastRuntime.Create();
            try
            {
                Collider[] colliders = coast.collisionRoot
                    ? coast.collisionRoot.GetComponentsInChildren<Collider>(true)
                    : new Collider[0];
                if (colliders.Length == 0)
                    throw new BuildFailedException("Sydney route validation has no coastline collider.");

                Physics.SyncTransforms();
                var planner = new GridAStarPlanner(-180f, -180f, 180f, 180f, 2f);
                var obstacles = new List<GridAStarPlanner.CircularObstacle>
                {
                    new GridAStarPlanner.CircularObstacle(new Vector2(35f, 18f), 8f),
                    new GridAStarPlanner.CircularObstacle(new Vector2(-42f, 44f), 4.1f),
                    new GridAStarPlanner.CircularObstacle(new Vector2(34f, -56f), 4.1f),
                    new GridAStarPlanner.CircularObstacle(new Vector2(78f, 28f), 4.1f)
                };
                Vector2 start = new Vector2(30f, 14f);
                Vector2[] candidates =
                {
                    Vector2.zero,
                    new Vector2(-40f, -20f),
                    new Vector2(80f, 0f),
                    new Vector2(-120f, -100f),
                    new Vector2(120f, 100f),
                    new Vector2(-120f, 100f),
                    new Vector2(120f, -100f)
                };
                Vector2 goal = start;
                List<Vector2> path = null;
                int expanded = 0;
                bool planned = false;
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (IsCoastBlocked(colliders, candidates[i]))
                        continue;

                    if (!planner.TryPlan(
                            start,
                            candidates[i],
                            obstacles,
                            point => IsCoastBlocked(colliders, point),
                            out List<Vector2> candidatePath,
                            out int candidateExpanded))
                        continue;

                    goal = candidates[i];
                    path = candidatePath;
                    expanded = candidateExpanded;
                    planned = true;
                    break;
                }
                if (!planned || path.Count < 2 || expanded <= 0)
                    throw new BuildFailedException("A* failed to plan through the Sydney waterway.");

                for (int i = 0; i < path.Count - 1; i++)
                {
                    float length = Vector2.Distance(path[i], path[i + 1]);
                    int samples = Mathf.Max(1, Mathf.CeilToInt(length));
                    for (int sample = 1; sample < samples; sample++)
                    {
                        Vector2 point = Vector2.Lerp(
                            path[i],
                            path[i + 1],
                            (float)sample / samples
                        );
                        if (IsCoastBlocked(colliders, point))
                            throw new BuildFailedException(
                                $"A* Sydney route crosses land at {point}."
                            );
                    }
                }

                Debug.Log(
                    $"Sydney A* route passed: start={start}, goal={goal}, " +
                    $"waypoints={path.Count}, expanded={expanded}"
                );
            }
            finally
            {
                Object.DestroyImmediate(coast.gameObject);
            }
        }

        private static bool IsCoastBlocked(Collider[] colliders, Vector2 enu)
        {
            Ray ray = new Ray(Coordinates.ToUnity(enu.x, enu.y, 80f), Vector3.down);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] && colliders[i].Raycast(ray, out _, 160f))
                    return true;
            }
            return false;
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
