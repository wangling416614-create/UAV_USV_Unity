using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class MultiAgentCaptureDefenseScenario : MonoBehaviour
    {
        public Transform shoreBase;
        public Transform[] dronePads;
        public Transform targetPoint;
        public Transform targetVessel;
        public Transform dynamicBarrier;
        public Transform[] obstacles;
        public Transform[] boats;
        public Transform[] drones;
        public DroneVisual[] droneVisuals;
        public AgentSensorSuite[] boatSensors;
        public AgentSensorSuite[] droneSensors;
        public ShoreBaseController baseController;
        public Transform coastlineCollisionRoot;
        public bool automatic = true;
        public float boatSpeed = 8.5f;
        public float searchBoatSpeed = 7f;
        public float droneSpeed = 11f;
        public float droneAltitude = 8f;
        public float captureRadius = 18f;
        public float defenseRadius = 30f;
        public float searchStartRadius = 72f;
        public float sensorRange = 38f;
        public float barrierSpeed = 4f;
        public float dispatchDelaySeconds = 0f;
        public float boatOrbitAngularSpeed = 0f;
        public float dronePatrolAngularSpeed = 0f;
        public float agentSeparation = 10f;
        public float droneSeparation = 10f;
        public float targetEscapeSpeed = 2.2f;
        public float targetPatrolSpeed = .5f;
        public float escapeArenaRadius = 24f;
        public float waterSafetyMargin = 4f;
        public float barrierDemoSeconds = 6f;
        public int barrierDemoPasses = 1;
        public float holdDistance = 3.5f;
        public float boatCloseDuration = 22f;
        public float droneApproachDelay = 3f;
        public float droneTakeoffDuration = 5f;
        public float droneTakeoffClimbSpeed = 1.8f;
        public float droneTakeoffStagger = .75f;
        public float searchForceContactSeconds = 16f;
        public float missionTimeLimitSeconds = 85f;
        public bool showDebugOverlays = true;

        [Header("Motion stability")]
        public float surfaceMaxTurnRate = 48f;
        public float surfaceAcceleration = 5f;
        public float surfaceTurnSlowdownAngle = 70f;
        public float airMaxTurnRate = 90f;
        public float airAcceleration = 9f;
        public float peerCorrectionSpeed = 2.5f;
        public float detourCommitSeconds = 3f;

        private readonly List<LineRenderer> commandLinks = new List<LineRenderer>();
        private readonly List<LineRenderer> sensorRings = new List<LineRenderer>();
        private readonly List<LineRenderer> tracks = new List<LineRenderer>();
        private readonly List<List<Vector3>> trackPoints = new List<List<Vector3>>();
        private LineRenderer captureRing;
        private LineRenderer defenseRing;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        // Keep the mission inside the open Sydney center channel (water).
        private Vector3 targetCenterEnu = new Vector3(40f, -20f, 0f);
        private Vector3 targetVelocityEnu;
        private Vector3 barrierStartPosition;
        private Vector3 barrierOriginPosition;
        private bool barrierOriginInitialized;
        private Collider[] coastlineColliders;
        private float scenarioStarted;
        private float captureStarted = -1f;
        private string phase = "UAVs on pad — USVs searching";
        private string detectReporter = "-";
        private bool targetDetected;
        private bool dronesAirborne;
        private bool dronesTakingOff;
        private float droneTakeoffStarted = -1f;
        private bool captureReady;
        private bool formationHolding;
        private bool captureComplete;
        private int activeAvoidanceCount;
        private Vector3 lockedTargetPosition;
        private Vector3[] boatDetours;
        private float[] boatDetourUntil;
        private readonly Dictionary<Transform, float> surfaceSpeedState =
            new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, Vector3> airVelocityState =
            new Dictionary<Transform, Vector3>();

        // Equilateral triangle on capture ring / defense ring (120° spacing, drones offset by 60°).
        private static readonly float[] BoatApproachAngles = { 0f, 120f, 240f };
        private static readonly float[] DroneDefenseAngles = { 60f, 180f, 300f };

        public string Status => phase;
        public bool CaptureReady => captureReady;
        public bool FormationHolding => formationHolding;
        public float MissionElapsed =>
            captureStarted >= 0f ? Time.time - captureStarted : 0f;

        private float[] boatProgressStamp;
        private Vector3[] boatProgressPos;

        public void SetCoastlineCollisionRoot(Transform root)
        {
            coastlineCollisionRoot = root;
            coastlineColliders = root
                ? root.GetComponentsInChildren<Collider>(true)
                : null;
        }

        private void Start()
        {
            scenarioStarted = Time.time;
            targetVelocityEnu = new Vector3(.22f, -.08f, 0f);
            if (coastlineCollisionRoot && (coastlineColliders == null || coastlineColliders.Length == 0))
                SetCoastlineCollisionRoot(coastlineCollisionRoot);
            BuildLineVisuals();
            ResetScenario();
        }

        public void NotifyBaseDispatch()
        {
            if (!targetDetected)
            {
                targetDetected = true;
                detectReporter = "base order";
            }

            if (captureStarted < 0f)
                captureStarted = Time.time;
            phase = "Base dispatch — launch UAVs / close capture";
            LaunchDrones();
        }

        public void ResetScenario()
        {
            scenarioStarted = Time.time;
            captureStarted = -1f;
            targetVelocityEnu = new Vector3(.22f, -.08f, 0f);
            targetDetected = false;
            dronesAirborne = false;
            dronesTakingOff = false;
            droneTakeoffStarted = -1f;
            captureReady = false;
            formationHolding = false;
            captureComplete = false;
            activeAvoidanceCount = 0;
            detectReporter = "-";
            ClearDetours();

            surfaceSpeedState.Clear();
            airVelocityState.Clear();
            if (dynamicBarrier)
            {
                if (!barrierOriginInitialized)
                {
                    barrierOriginPosition = dynamicBarrier.position;
                    barrierOriginInitialized = true;
                }
                barrierStartPosition = barrierOriginPosition;
                // Put the obstacle on its path before the first rendered frame.
                dynamicBarrier.position = barrierStartPosition + new Vector3(-8f, 0f, 0f);
            }
            phase = "UAVs on pad — USVs searching";
            SetTargetPose(Coordinates.ToUnity(targetCenterEnu.x, targetCenterEnu.y, .38f));

            Vector2 center = new Vector2(targetCenterEnu.x, targetCenterEnu.y);
            // Explicit open-water approach axes (ENU), then fall back to ring samples.
            Vector2[] preferredBoatStarts =
            {
                new Vector2(105f, -20f),
                new Vector2(-2f, 32f),
                new Vector2(-2f, -72f)
            };
            for (int i = 0; boats != null && i < boats.Length; i++)
            {
                if (!boats[i])
                    continue;

                Vector2 start = i < preferredBoatStarts.Length
                    ? preferredBoatStarts[i]
                    : PointAround(center, BoatApproachAngles[i % BoatApproachAngles.Length], searchStartRadius);
                start = FindNearestWater(start, center);
                boats[i].position = Coordinates.ToUnity(start.x, start.y, .42f);
                FaceToward(boats[i], targetPoint ? targetPoint.position : Vector3.zero);
            }

            ParkDronesOnPads();

            for (int i = 0; i < trackPoints.Count; i++)
                trackPoints[i].Clear();

            EnsureProgressBuffers();
            for (int i = 0; boatProgressStamp != null && i < boatProgressStamp.Length; i++)
            {
                boatProgressStamp[i] = Time.time;
                boatProgressPos[i] = boats != null && i < boats.Length && boats[i] ? boats[i].position : Vector3.zero;
            }

            if (baseController)
                baseController.ResetMission();
        }

        private void EnsureProgressBuffers()
        {
            int count = boats != null ? boats.Length : 0;
            if (boatProgressStamp != null && boatProgressStamp.Length == count &&
                boatDetours != null && boatDetours.Length == count)
                return;
            boatProgressStamp = new float[count];
            boatProgressPos = new Vector3[count];
            boatDetours = new Vector3[count];
            boatDetourUntil = new float[count];
        }

        private void ClearDetours()
        {
            EnsureProgressBuffers();
            for (int i = 0; i < boatDetourUntil.Length; i++)
            {
                boatDetourUntil[i] = -1f;
                boatDetours[i] = Vector3.zero;
            }
        }

        private static Vector2 PointAround(Vector2 center, float angleDegrees, float radius)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.M))
                automatic = !automatic;
            if (Input.GetKeyDown(KeyCode.R))
                ResetScenario();
            if (Input.GetKeyDown(KeyCode.V))
                showDebugOverlays = !showDebugOverlays;

            AnimateBarrier();
            if (!captureComplete)
                AnimateTarget();
            activeAvoidanceCount = 0;

            if (automatic && Time.time - scenarioStarted >= dispatchDelaySeconds)
            {
                float missionElapsed = Time.time - scenarioStarted;

                // Hard cap: finish within about one minute.
                if (!captureComplete && missionElapsed >= missionTimeLimitSeconds)
                    ForceCaptureSuccess("time limit");

                if (captureComplete)
                {
                    FreezeCaptureFormation();
                    phase = "Capture success — equilateral formation locked";
                }
                else if (!targetDetected)
                {
                    // Search from afar; only force contact if sensors never pick it up.
                    if (missionElapsed > searchForceContactSeconds)
                        ForceTargetContact("search timeout");

                    DriveSearchApproach();
                    TryDetectTarget();
                    phase = "USVs searching — approach from three axes";
                }
                else
                {
                    // Drones wait a beat after contact so the boat close-in is visible first.
                    if (!dronesAirborne && captureStarted >= 0f &&
                        Time.time - captureStarted >= droneApproachDelay)
                        LaunchDrones();

                    bool boatsHolding = DriveBoatsCapture();
                    bool dronesHolding = DriveDronesDefense();
                    captureReady = boatsHolding;
                    formationHolding = boatsHolding && dronesHolding;

                    if (formationHolding)
                        ForceCaptureSuccess("triangle locked");
                    else if (!boatsHolding)
                        phase = "USVs closing ring → equilateral triangle";
                    else if (!dronesAirborne)
                        phase = "Triangle forming — UAVs standing by";
                    else if (dronesTakingOff)
                        phase = "UAVs taking off from shore pads";
                    else
                        phase = "UAVs joining outer defense triangle";
                }
            }

            UpdateLineVisuals();
            UpdateMissionRings();
            UpdateTracks();
            ApplyOverlayVisibility();
        }

        private void DriveSearchApproach()
        {
            if (boats == null || targetPoint == null)
                return;

            Vector3 center = targetPoint.position;
            // Aim at a wide outer ring first — not the final capture triangle.
            float searchRing = Mathf.Max(captureRadius + 10f, searchStartRadius * .72f);
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                Vector3 slot = BoatRingSlot(i, center, searchRing);
                slot = CommitDetourIfBlocked(boat, i, slot);
                slot = SoftAvoidPeers(boat, slot, boats, agentSeparation);
                slot = ClampToWater(slot, .42f);
                MoveSurfaceAgent(boat, slot, searchBoatSpeed);
                TrackBoatProgress(boat, i, slot);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
            TryDetectTargetByProximity();
        }

        private void TryDetectTargetByProximity()
        {
            if (targetDetected || boats == null)
                return;

            Transform senseTarget = targetVessel ? targetVessel : targetPoint;
            if (!senseTarget)
                return;

            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                float range = boatSensors != null && i < boatSensors.Length && boatSensors[i]
                    ? Mathf.Max(boatSensors[i].lidarRange, boatSensors[i].radarRange)
                    : sensorRange;
                if (HorizontalDistance(boat.position, senseTarget.position) > range)
                    continue;

                targetDetected = true;
                detectReporter = boat.name + " proximity/radar";
                captureStarted = Time.time;
                phase = "Target detected by " + detectReporter;
                if (baseController)
                    baseController.NotifyTargetContact(detectReporter);
                else
                    LaunchDrones();
                return;
            }
        }

        private void TryDetectTarget()
        {
            Transform senseTarget = targetVessel ? targetVessel : targetPoint;
            if (!senseTarget)
                return;

            for (int i = 0; boatSensors != null && i < boatSensors.Length; i++)
            {
                AgentSensorSuite sensor = boatSensors[i];
                if (!sensor)
                    continue;

                sensor.Scan();
                if (!sensor.SeesTarget(senseTarget))
                    continue;

                targetDetected = true;
                detectReporter = boats != null && i < boats.Length && boats[i]
                    ? boats[i].name + " lidar/radar"
                    : "USV sensor";
                captureStarted = Time.time;
                phase = "Target detected by " + detectReporter;
                if (baseController)
                    baseController.NotifyTargetContact(detectReporter);
                else
                    LaunchDrones();
                return;
            }
        }

        private void ParkDronesOnPads()
        {
            dronesAirborne = false;
            dronesTakingOff = false;
            droneTakeoffStarted = -1f;
            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                Transform pad = dronePads != null && i < dronePads.Length ? dronePads[i] : null;
                if (pad)
                {
                    drone.SetParent(pad, false);
                    drone.localPosition = new Vector3(0f, .28f, 0f);
                    drone.localRotation = Quaternion.identity;
                }
                else if (shoreBase)
                {
                    drone.SetParent(null, true);
                    drone.position = shoreBase.position + new Vector3(-6f + i * 6f, .62f, 2.4f);
                }

                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = false;
            }
        }

        private void LaunchDrones()
        {
            if (dronesAirborne)
                return;

            dronesAirborne = true;
            dronesTakingOff = true;
            droneTakeoffStarted = Time.time;
            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                // Leave the pad, but stay low — climb is driven over time in DriveDronesDefense.
                drone.SetParent(null, true);
                Vector3 p = drone.position;
                drone.position = new Vector3(p.x, Mathf.Max(p.y, .55f), p.z);

                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }
        }

        private void AnimateTarget()
        {
            if (!targetPoint || captureComplete)
                return;

            if (targetDetected)
                EscapeFromPursuers();
            else
                PatrolBeforeContact();
        }

        private void PatrolBeforeContact()
        {
            Vector3 enu = Coordinates.ToEnu(targetPoint.position);
            if (targetVelocityEnu.sqrMagnitude < .01f)
                targetVelocityEnu = new Vector3(.22f, -.08f, 0f);

            Vector3 desired = targetVelocityEnu.normalized * targetPatrolSpeed;
            targetVelocityEnu = Vector3.Lerp(targetVelocityEnu, desired, 2f * Time.deltaTime);
            enu += targetVelocityEnu * Time.deltaTime;

            Vector3 offset = enu - targetCenterEnu;
            if (Mathf.Abs(offset.x) > 10f)
            {
                targetVelocityEnu.x *= -1f;
                enu.x = targetCenterEnu.x + Mathf.Sign(offset.x) * 10f;
            }
            if (Mathf.Abs(offset.y) > 7f)
            {
                targetVelocityEnu.y *= -1f;
                enu.y = targetCenterEnu.y + Mathf.Sign(offset.y) * 7f;
            }

            SetTargetPose(Coordinates.ToUnity(enu.x, enu.y, .38f));
        }

        private void EscapeFromPursuers()
        {
            Vector3 targetPos = targetPoint.position;
            Vector3 escapeDir = ComputeEscapeDirection(targetPos);
            Vector3 desiredVelocity = escapeDir * targetEscapeSpeed;

            float juke = Mathf.Sin(Time.time * 1.35f) * .45f;
            Vector3 side = Vector3.Cross(Vector3.up, escapeDir);
            desiredVelocity += side * juke;

            Vector3 currentUnityVel = Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f);
            currentUnityVel.y = 0f;
            Vector3 blended = Vector3.Lerp(currentUnityVel, desiredVelocity, 2.2f * Time.deltaTime);
            if (blended.sqrMagnitude > .001f)
                blended = blended.normalized * Mathf.Clamp(blended.magnitude, targetPatrolSpeed, targetEscapeSpeed);

            Vector3 next = targetPos + blended * Time.deltaTime;
            Vector3 center = Coordinates.ToUnity(targetCenterEnu.x, targetCenterEnu.y, 0f);
            Vector3 fromCenter = next - center;
            fromCenter.y = 0f;
            if (fromCenter.magnitude > escapeArenaRadius)
            {
                fromCenter = fromCenter.normalized * escapeArenaRadius;
                next = center + fromCenter;
                blended = Vector3.Reflect(blended, -fromCenter.normalized);
                if (blended.sqrMagnitude < .001f)
                    blended = -fromCenter.normalized * targetEscapeSpeed;
            }

            next = ClampToWater(next, .38f);
            Vector3 enuVel = Coordinates.ToEnu(blended);
            targetVelocityEnu = new Vector3(enuVel.x, enuVel.y, 0f);
            SetTargetPose(new Vector3(next.x, .38f, next.z));
        }

        private Vector3 ComputeEscapeDirection(Vector3 targetPos)
        {
            var bearings = new List<float>();
            Vector3 centroid = Vector3.zero;
            int count = 0;

            void AddPursuer(Transform agent)
            {
                if (!agent)
                    return;
                Vector3 delta = agent.position - targetPos;
                delta.y = 0f;
                if (delta.sqrMagnitude < .01f)
                    return;
                bearings.Add(Mathf.Atan2(delta.z, delta.x));
                centroid += agent.position;
                count++;
            }

            if (boats != null)
            {
                for (int i = 0; i < boats.Length; i++)
                    AddPursuer(boats[i]);
            }
            if (dronesAirborne && drones != null)
            {
                for (int i = 0; i < drones.Length; i++)
                    AddPursuer(drones[i]);
            }

            if (count == 0)
                return Vector3.forward;

            centroid /= count;
            Vector3 awayFromFleet = targetPos - centroid;
            awayFromFleet.y = 0f;
            if (awayFromFleet.sqrMagnitude < .001f)
                awayFromFleet = Vector3.right;
            awayFromFleet.Normalize();

            if (bearings.Count < 2)
                return awayFromFleet;

            bearings.Sort();
            float bestGap = -1f;
            float bestMid = 0f;
            for (int i = 0; i < bearings.Count; i++)
            {
                float a = bearings[i];
                float b = bearings[(i + 1) % bearings.Count];
                float gap = i + 1 < bearings.Count ? b - a : (b + Mathf.PI * 2f) - a;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestMid = a + gap * .5f;
                }
            }

            Vector3 gapDir = new Vector3(Mathf.Cos(bestMid), 0f, Mathf.Sin(bestMid));
            return (gapDir * .7f + awayFromFleet * .3f).normalized;
        }

        private void FreezeCaptureFormation()
        {
            if (!targetPoint)
                return;

            Vector3 center = lockedTargetPosition.sqrMagnitude > .01f
                ? lockedTargetPosition
                : targetPoint.position;
            lockedTargetPosition = center;
            targetVelocityEnu = Vector3.zero;
            SetTargetPose(center);

            for (int i = 0; boats != null && i < boats.Length; i++)
            {
                if (!boats[i])
                    continue;
                Vector3 slot = FixedBoatSlot(i, center);
                if (HorizontalDistance(boats[i].position, slot) > .2f)
                    MoveSurfaceAgent(boats[i], slot, boatSpeed * .45f);
                else
                {
                    BrakeSurfaceAgent(boats[i]);
                    RotateSurfaceToward(boats[i], center);
                }
            }

            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                if (!drones[i])
                    continue;
                Vector3 slot = FixedDroneSlot(i, center);
                if (Vector3.Distance(drones[i].position, slot) > .25f)
                    MoveAirAgent(drones[i], slot, droneSpeed * .45f);
                else
                {
                    BrakeAirAgent(drones[i]);
                    RotateAirToward(drones[i], center);
                }
                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }
        }

        private Vector3 FixedBoatSlot(int index, Vector3 center)
        {
            return BoatRingSlot(index, center, captureRadius);
        }

        private Vector3 BoatRingSlot(int index, Vector3 center, float radius)
        {
            float angle = BoatApproachAngles[index % BoatApproachAngles.Length] * Mathf.Deg2Rad;
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                .42f,
                center.z + Mathf.Sin(angle) * radius
            );
        }

        private Vector3 FixedDroneSlot(int index, Vector3 center)
        {
            return DroneRingSlot(index, center, defenseRadius);
        }

        private Vector3 DroneRingSlot(int index, Vector3 center, float radius)
        {
            float angle = DroneDefenseAngles[index % DroneDefenseAngles.Length] * Mathf.Deg2Rad;
            float altitude = DroneAltitudeFor(index);
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                altitude,
                center.z + Mathf.Sin(angle) * radius
            );
        }

        /// <summary>
        /// After contact, shrink the boat ring from a wide approach radius down to captureRadius.
        /// </summary>
        private float CurrentBoatRingRadius()
        {
            float outer = Mathf.Max(captureRadius + 12f, searchStartRadius * .72f);
            if (!targetDetected || captureStarted < 0f)
                return outer;

            float t = Mathf.Clamp01((Time.time - captureStarted) / Mathf.Max(.1f, boatCloseDuration));
            t = t * t * (3f - 2f * t); // smoothstep — visible gradual close-in
            return Mathf.Lerp(outer, captureRadius, t);
        }

        private bool BoatRingFullyClosed()
        {
            return CurrentBoatRingRadius() <= captureRadius + .35f;
        }

        private void SetTargetPose(Vector3 position)
        {
            position = ClampToWater(position, .38f);
            if (targetPoint)
            {
                targetPoint.gameObject.SetActive(true);
                targetPoint.position = new Vector3(position.x, .46f, position.z);
            }
            if (targetVessel)
            {
                targetVessel.gameObject.SetActive(true);
                targetVessel.position = new Vector3(position.x, .5f, position.z);
                Vector3 heading = Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f);
                heading.y = 0f;
                if (heading.sqrMagnitude > .001f)
                {
                    targetVessel.rotation = Quaternion.Slerp(
                        targetVessel.rotation,
                        Quaternion.LookRotation(heading.normalized, Vector3.up),
                        5f * Time.deltaTime
                    );
                }
            }
        }

        private void ForceTargetContact(string reason)
        {
            if (targetDetected)
                return;

            targetDetected = true;
            detectReporter = reason;
            captureStarted = Time.time;
            phase = "Target contact (" + reason + ")";
            if (baseController)
                baseController.NotifyTargetContact(reason);
            else
                LaunchDrones();
        }

        private void ForceCaptureSuccess(string reason)
        {
            if (captureComplete)
                return;

            if (!targetDetected)
                ForceTargetContact(reason);
            if (!dronesAirborne)
                LaunchDrones();

            captureComplete = true;
            captureReady = true;
            formationHolding = true;
            lockedTargetPosition = targetPoint ? targetPoint.position : lockedTargetPosition;
            targetVelocityEnu = Vector3.zero;
            FreezeCaptureFormation();
            phase = "Capture success — equilateral formation locked";
            if (baseController)
                baseController.NotifyCaptureComplete();
        }

        private bool DriveBoatsCapture()
        {
            if (boats == null || targetPoint == null)
                return true;

            Vector3 center = targetPoint.position;
            float ring = CurrentBoatRingRadius();
            float maxDistance = 0f;
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                // Follow the shrinking ring so the triangle forms over time.
                Vector3 slot = BoatRingSlot(i, center, ring);
                float toSlot = HorizontalDistance(boat.position, slot);
                maxDistance = Mathf.Max(
                    maxDistance,
                    HorizontalDistance(boat.position, FixedBoatSlot(i, center))
                );
                if (BoatRingFullyClosed() && toSlot <= holdDistance)
                {
                    BrakeSurfaceAgent(boat);
                    RotateSurfaceToward(boat, center);
                    continue;
                }

                slot = CommitDetourIfBlocked(boat, i, slot);
                slot = SoftAvoidPeers(boat, slot, boats, agentSeparation);
                slot = ClampToWater(slot, .42f);
                MoveSurfaceAgent(boat, slot, boatSpeed);
                TrackBoatProgress(boat, i, slot);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
            return BoatRingFullyClosed() && maxDistance < holdDistance;
        }

        private bool DriveDronesDefense()
        {
            if (!dronesAirborne || drones == null || targetPoint == null)
                return false;

            Vector3 center = targetPoint.position;
            float maxDistance = 0f;
            int climbing = 0;
            for (int i = 0; i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                float cruiseAlt = DroneAltitudeFor(i);
                float age = droneTakeoffStarted < 0f
                    ? 99f
                    : Time.time - droneTakeoffStarted - i * droneTakeoffStagger;

                // Staggered spool-up: still sitting on / above pad.
                if (age < 0f)
                {
                    climbing++;
                    maxDistance = Mathf.Max(maxDistance, Vector3.Distance(drone.position, FixedDroneSlot(i, center)));
                    continue;
                }

                Vector3 pos = drone.position;
                bool stillClimbing = pos.y < cruiseAlt - .25f && age < droneTakeoffDuration + 2.5f;
                if (stillClimbing)
                {
                    climbing++;
                    float climbT = Mathf.Clamp01(age / Mathf.Max(.1f, droneTakeoffDuration));
                    Vector3 next = pos;
                    next.y = Mathf.MoveTowards(pos.y, cruiseAlt, droneTakeoffClimbSpeed * Time.deltaTime);

                    // After roughly half climb, begin a gentle drift toward the defense slot.
                    if (climbT > .5f)
                    {
                        Vector3 slot = FixedDroneSlot(i, center);
                        Vector3 lateral = Vector3.MoveTowards(
                            new Vector3(pos.x, 0f, pos.z),
                            new Vector3(slot.x, 0f, slot.z),
                            droneSpeed * .4f * climbT * Time.deltaTime
                        );
                        next.x = lateral.x;
                        next.z = lateral.z;
                    }

                    drone.position = next;
                    if ((next - pos).sqrMagnitude > .0001f)
                        RotateAirToward(drone, next + (next - pos));
                    maxDistance = Mathf.Max(maxDistance, Vector3.Distance(drone.position, FixedDroneSlot(i, center)));
                    continue;
                }

                Vector3 goal = FixedDroneSlot(i, center);
                float toSlot = Vector3.Distance(drone.position, goal);
                maxDistance = Mathf.Max(
                    maxDistance,
                    Vector3.Distance(drone.position, FixedDroneSlot(i, center))
                );
                if (toSlot <= holdDistance)
                {
                    BrakeAirAgent(drone);
                    RotateAirToward(drone, center);
                    continue;
                }

                goal = SoftAvoidPeers(drone, goal, drones, droneSeparation);
                goal.y = cruiseAlt;
                MoveAirAgent(drone, goal, droneSpeed);
            }

            dronesTakingOff = climbing > 0;
            EnforcePeerClearance(drones, droneSeparation * .9f, false);
            return !dronesTakingOff && maxDistance < holdDistance;
        }

        private Vector3 CommitDetourIfBlocked(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();

            // Keep a committed detour briefly so the boat does not oscillate left/right.
            if (index < boatDetourUntil.Length && Time.time < boatDetourUntil[index])
            {
                Vector3 detour = boatDetours[index];
                if (HorizontalDistance(boat.position, detour) < 3f)
                    boatDetourUntil[index] = -1f;
                else
                    return detour;
            }

            if (!PathBlocked(boat.position, goal, 5f, out Transform blocker))
                return goal;

            Vector3 toGoal = goal - boat.position;
            toGoal.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toGoal.normalized);
            float sign = index % 2 == 0 ? 1f : -1f;
            float keep = blocker ? ObstacleRadius(blocker) + 8f : 10f;
            Vector3 pivot = blocker ? blocker.position : boat.position + toGoal.normalized * 8f;
            Vector3 detourA = pivot + side * (keep * sign) + toGoal.normalized * 6f;
            Vector3 detourB = pivot - side * (keep * sign) + toGoal.normalized * 6f;
            detourA = ClampToWater(detourA, .42f);
            detourB = ClampToWater(detourB, .42f);

            // Pick the detour that still progresses toward the goal.
            float scoreA = Vector3.Dot((detourA - boat.position).normalized, toGoal.normalized);
            float scoreB = Vector3.Dot((detourB - boat.position).normalized, toGoal.normalized);
            Vector3 chosen = scoreA >= scoreB ? detourA : detourB;
            boatDetours[index] = chosen;
            boatDetourUntil[index] = Time.time + Mathf.Max(1.2f, detourCommitSeconds);
            activeAvoidanceCount++;
            return chosen;
        }

        private bool PathBlocked(Vector3 from, Vector3 to, float clearance, out Transform blocker)
        {
            Transform found = null;
            float best = float.PositiveInfinity;

            void Consider(Transform obstacle)
            {
                if (!obstacle || !obstacle.gameObject.activeInHierarchy || obstacle.name.Contains("ShoreBase"))
                    return;
                float radius = ObstacleRadius(obstacle) + clearance;
                float dist = SegmentDistance(from, to, obstacle.position);
                if (dist < radius && dist < best)
                {
                    best = dist;
                    found = obstacle;
                }
            }

            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                    Consider(obstacles[i]);
            }
            Consider(dynamicBarrier);

            blocker = found;
            return found != null;
        }

        private Vector3 SoftAvoidPeers(Transform self, Vector3 desired, Transform[] peers, float minDistance)
        {
            if (peers == null)
                return desired;

            Vector3 adjusted = desired;
            for (int i = 0; i < peers.Length; i++)
            {
                Transform peer = peers[i];
                if (!peer || peer == self)
                    continue;
                float dist = HorizontalDistance(adjusted, peer.position);
                if (dist < minDistance)
                    adjusted = PushAway(adjusted, peer.position, minDistance);
            }
            adjusted.y = desired.y;
            return adjusted;
        }

        private Vector3 SeekWithLocalAvoidance(
            Transform agent,
            Vector3 goal,
            AgentSensorSuite[] sensors,
            int index,
            bool surfaceAgent)
        {
            Vector3 planned = goal;
            float peerSep = surfaceAgent ? agentSeparation : droneSeparation;
            float clearance = surfaceAgent ? 5.5f : 5f;

            // Light sensor dodge — only when a hit is close.
            if (sensors != null && index >= 0 && index < sensors.Length && sensors[index])
            {
                AgentSensorSuite sensor = sensors[index];
                sensor.Scan();
                if (sensor.hitCount > 0 && sensor.nearestDistance < clearance + 4f)
                {
                    planned = sensor.SteerAway(planned, clearance);
                    activeAvoidanceCount++;
                }
            }

            planned = AvoidBlockingObstacles(agent.position, planned, clearance);
            planned = AvoidDynamicBarrier(planned, clearance);

            if (targetVessel && targetVessel.gameObject.activeInHierarchy)
            {
                float keep = ObstacleRadius(targetVessel) + (surfaceAgent ? 6f : 4f);
                if (HorizontalDistance(planned, targetVessel.position) < keep)
                    planned = PushAway(planned, targetVessel.position, keep);
            }

            planned = SeparateFromPeers(agent, planned, boats, peerSep);
            // Boats stay on water; drones are airborne — no cross-domain peer dodge.
            if (!surfaceAgent && dronesAirborne)
                planned = SeparateFromPeers(agent, planned, drones, peerSep);

            if (surfaceAgent)
            {
                planned = KeepOutsideTarget(planned, captureRadius * .45f);
                planned = ClampToWater(planned, .42f);
            }

            planned.y = goal.y;
            return planned;
        }

        private void TrackBoatProgress(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();
            if (boat == null || index < 0 || index >= boatProgressStamp.Length)
                return;

            float moved = HorizontalDistance(boat.position, boatProgressPos[index]);
            float closer = HorizontalDistance(boatProgressPos[index], goal) - HorizontalDistance(boat.position, goal);
            if (moved > 1.5f || closer > 1f)
            {
                boatProgressStamp[index] = Time.time;
                boatProgressPos[index] = boat.position;
            }
        }

        private Vector3 UnstickIfNeeded(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();
            if (boat == null || index < 0 || index >= boatProgressStamp.Length)
                return goal;

            // If a boat barely moves for a few seconds, force a wide side detour around the blocker.
            if (Time.time - boatProgressStamp[index] < 2.2f)
                return goal;

            Transform blocker = NearestBlocker(boat.position);
            Vector3 toGoal = goal - boat.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude < .01f)
                toGoal = Vector3.forward;

            Vector3 side = Vector3.Cross(Vector3.up, toGoal.normalized);
            float sign = (index % 2 == 0) ? 1f : -1f;
            Vector3 detour = boat.position + side * (18f * sign) + toGoal.normalized * 12f;
            if (blocker)
            {
                float keep = ObstacleRadius(blocker) + 10f;
                if (HorizontalDistance(detour, blocker.position) < keep)
                    detour = PushAway(detour, blocker.position, keep + 4f);
            }

            detour = ClampToWater(detour, .42f);
            boatProgressStamp[index] = Time.time;
            boatProgressPos[index] = boat.position;
            activeAvoidanceCount++;
            phase = boats[index].name + " unsticking around obstacle";
            return detour;
        }

        private Transform NearestBlocker(Vector3 from)
        {
            Transform best = null;
            float bestDist = float.PositiveInfinity;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    Transform obstacle = obstacles[i];
                    if (!obstacle || obstacle.name.Contains("ShoreBase"))
                        continue;
                    float d = HorizontalDistance(from, obstacle.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = obstacle;
                    }
                }
            }
            if (dynamicBarrier)
            {
                float d = HorizontalDistance(from, dynamicBarrier.position);
                if (d < bestDist)
                    best = dynamicBarrier;
            }
            return best;
        }

        private Vector3 PlanSurfaceMove(
            Transform boat,
            Vector3 slot,
            AgentSensorSuite[] sensors,
            int index,
            float peerSeparation)
        {
            return SeekWithLocalAvoidance(boat, slot, sensors, index, true);
        }

        private Vector3 PlanAirMove(
            Transform drone,
            Vector3 slot,
            AgentSensorSuite[] sensors,
            int index,
            float altitude)
        {
            Vector3 planned = SeekWithLocalAvoidance(drone, slot, sensors, index, false);
            planned.y = altitude;
            return planned;
        }

        private Vector3 AvoidAllBodies(
            Transform self,
            Vector3 desired,
            float obstacleClearance,
            float peerClearance,
            bool surfaceAgent)
        {
            Vector3 adjusted = AvoidBlockingObstacles(self.position, desired, obstacleClearance);
            adjusted = AvoidDynamicBarrier(adjusted, obstacleClearance);

            if (targetVessel && targetVessel.gameObject.activeInHierarchy)
            {
                float keep = ObstacleRadius(targetVessel) + obstacleClearance;
                if (HorizontalDistance(adjusted, targetVessel.position) < keep)
                    adjusted = PushAway(adjusted, targetVessel.position, keep);
            }

            adjusted = SeparateFromPeers(self, adjusted, boats, peerClearance);
            // Surface craft ignore airborne drones (and vice versa for peer separation).
            if (!surfaceAgent && dronesAirborne)
                adjusted = SeparateFromPeers(self, adjusted, drones, peerClearance);

            adjusted.y = desired.y;
            return adjusted;
        }

        private void ResolveWorldCollisions()
        {
            // Only separate when agents are actually overlapping / too close.
            EnforcePeerClearance(boats, agentSeparation * .92f, true);
            if (dronesAirborne)
                EnforcePeerClearance(drones, droneSeparation * .92f, false);

            if (boats != null)
            {
                for (int i = 0; i < boats.Length; i++)
                {
                    Transform boat = boats[i];
                    if (!boat)
                        continue;
                    Vector3 fixedPos = AvoidBlockingObstacles(boat.position, boat.position, 4.5f);
                    fixedPos = AvoidDynamicBarrier(fixedPos, 4.5f);
                    fixedPos = ClampToWater(fixedPos, .42f);
                    if (HorizontalDistance(fixedPos, boat.position) > .2f)
                        boat.position = Vector3.MoveTowards(boat.position, fixedPos, 2.5f * Time.deltaTime);
                }
            }

            if (dronesAirborne && drones != null)
            {
                for (int i = 0; i < drones.Length; i++)
                {
                    Transform drone = drones[i];
                    if (!drone)
                        continue;
                    float altitude = DroneAltitudeFor(i);
                    Vector3 fixedPos = AvoidBlockingObstacles(drone.position, drone.position, 4f);
                    fixedPos.y = altitude;
                    if (HorizontalDistance(fixedPos, drone.position) > .2f)
                        drone.position = Vector3.MoveTowards(drone.position, fixedPos, 3f * Time.deltaTime);
                    else
                        drone.position = new Vector3(drone.position.x, altitude, drone.position.z);
                }
            }
        }

        private Vector3 ApplySensorAvoidance(
            Transform agent,
            Vector3 desired,
            AgentSensorSuite[] sensors,
            int index,
            float clearance)
        {
            if (sensors == null || index < 0 || index >= sensors.Length || !sensors[index])
                return desired;

            AgentSensorSuite sensor = sensors[index];
            sensor.Scan();
            Vector3 steered = sensor.SteerAway(desired, clearance);
            if (sensor.hitCount > 0 && sensor.nearestDistance < clearance + 6f)
                activeAvoidanceCount++;
            return steered;
        }

        private Vector3 SeparateFromPeers(Transform self, Vector3 desired, Transform[] peers, float minDistance)
        {
            if (peers == null)
                return desired;

            Vector3 adjusted = desired;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < peers.Length; i++)
                {
                    Transform peer = peers[i];
                    if (!peer || peer == self)
                        continue;

                    // Repel from both the peer's current pose and the candidate slot.
                    float distDesired = HorizontalDistance(adjusted, peer.position);
                    if (distDesired < minDistance)
                    {
                        float push = minDistance - distDesired + 1.25f;
                        adjusted = PushAway(adjusted, peer.position, distDesired + push);
                    }

                    // Extra tangential push around the mission target so agents don't stack on the ring.
                    if (targetPoint && distDesired < minDistance * 1.15f)
                    {
                        Vector3 fromTarget = adjusted - targetPoint.position;
                        fromTarget.y = 0f;
                        Vector3 peerFromTarget = peer.position - targetPoint.position;
                        peerFromTarget.y = 0f;
                        if (fromTarget.sqrMagnitude > .01f && peerFromTarget.sqrMagnitude > .01f)
                        {
                            float angSelf = Mathf.Atan2(fromTarget.z, fromTarget.x);
                            float angPeer = Mathf.Atan2(peerFromTarget.z, peerFromTarget.x);
                            float delta = Mathf.DeltaAngle(angPeer * Mathf.Rad2Deg, angSelf * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                            float minAngle = minDistance / Mathf.Max(captureRadius, 1f);
                            if (Mathf.Abs(delta) < minAngle)
                            {
                                float sign = delta >= 0f ? 1f : -1f;
                                if (Mathf.Abs(delta) < .001f)
                                    sign = self.GetInstanceID() > peer.GetInstanceID() ? 1f : -1f;
                                float newAng = angPeer + sign * minAngle;
                                float radius = fromTarget.magnitude;
                                adjusted = targetPoint.position + new Vector3(Mathf.Cos(newAng) * radius, 0f, Mathf.Sin(newAng) * radius);
                                adjusted.y = desired.y;
                            }
                        }
                    }
                }
            }

            adjusted.y = desired.y;
            return adjusted;
        }

        private void EnforcePeerClearance(Transform[] agents, float minDistance, bool clampWater)
        {
            if (agents == null)
                return;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    Transform a = agents[i];
                    if (!a)
                        continue;

                    for (int j = i + 1; j < agents.Length; j++)
                    {
                        Transform b = agents[j];
                        if (!b)
                            continue;

                        Vector3 pa = a.position;
                        Vector3 pb = b.position;
                        float dist = HorizontalDistance(pa, pb);
                        if (dist >= minDistance)
                            continue;

                        Vector3 separation = pa - pb;
                        separation.y = 0f;
                        if (separation.sqrMagnitude < .001f)
                        {
                            float angle = (i * 97f + j * 53f) * Mathf.Deg2Rad;
                            separation = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        }
                        else
                        {
                            separation.Normalize();
                        }

                        float overlap = minDistance - Mathf.Max(dist, .05f);
                        float correction = Mathf.Min(
                            overlap * .5f,
                            Mathf.Max(.1f, peerCorrectionSpeed) * Time.deltaTime
                        );
                        Vector3 awayA = pa + separation * correction;
                        Vector3 awayB = pb - separation * correction;
                        awayA.y = pa.y;
                        awayB.y = pb.y;

                        if (clampWater)
                        {
                            Vector3 enuA = Coordinates.ToEnu(awayA);
                            Vector3 enuB = Coordinates.ToEnu(awayB);
                            if (!IsLand(new Vector2(enuA.x, enuA.y)))
                                a.position = awayA;
                            if (!IsLand(new Vector2(enuB.x, enuB.y)))
                                b.position = awayB;
                        }
                        else
                        {
                            a.position = awayA;
                            b.position = awayB;
                        }

                        activeAvoidanceCount++;
                    }
                }
            }
        }

        private float DroneAltitudeFor(int index)
        {
            // Stagger heights so drones never occupy the same air slab.
            return droneAltitude + (index - 1) * 1.6f;
        }

        private Vector3 SlotAroundTarget(float angleDegrees, float radius, float height, float angularSpeed)
        {
            Vector3 target = targetPoint.position;
            float angle = angleDegrees * Mathf.Deg2Rad + MissionElapsed * angularSpeed;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            return new Vector3(target.x + offset.x, height, target.z + offset.z);
        }

        private Vector3 SoftProjectToRing(Vector3 point, float radius, float height, float inwardSlack, float outwardSlack)
        {
            if (!targetPoint)
                return point;

            Vector3 fromTarget = point - targetPoint.position;
            fromTarget.y = 0f;
            if (fromTarget.sqrMagnitude < .001f)
                fromTarget = Vector3.forward;

            float distance = fromTarget.magnitude;
            float clamped = Mathf.Clamp(distance, Mathf.Max(1f, radius - inwardSlack), radius + outwardSlack);
            Vector3 result = targetPoint.position + fromTarget.normalized * clamped;
            result.y = height;
            return result;
        }

        private Vector3 ProjectToRing(Vector3 point, float radius, float height)
        {
            return SoftProjectToRing(point, radius, height, 0f, 0f);
        }

        private Vector3 AvoidDynamicBarrier(Vector3 desired, float clearance)
        {
            if (!dynamicBarrier)
                return desired;

            return HorizontalDistance(desired, dynamicBarrier.position) < ObstacleRadius(dynamicBarrier) + clearance
                ? PushAway(desired, dynamicBarrier.position, ObstacleRadius(dynamicBarrier) + clearance)
                : desired;
        }

        private Vector3 KeepOutsideTarget(Vector3 point, float minimumRadius)
        {
            if (!targetPoint)
                return point;

            if (HorizontalDistance(point, targetPoint.position) >= minimumRadius)
                return point;

            return PushAway(point, targetPoint.position, minimumRadius);
        }

        private Vector3 AvoidStaticObstacles(Vector3 current, Vector3 desired, float clearance)
        {
            return AvoidBlockingObstacles(current, desired, clearance);
        }

        private Vector3 AvoidBlockingObstacles(Vector3 current, Vector3 desired, float clearance)
        {
            if (obstacles == null)
                return desired;

            Vector3 adjusted = desired;
            Vector3 toGoal = desired - current;
            toGoal.y = 0f;

            for (int i = 0; i < obstacles.Length; i++)
            {
                Transform obstacle = obstacles[i];
                if (!obstacle || !obstacle.gameObject.activeInHierarchy)
                    continue;

                // Shore base is a spawn pad, not a channel blocker — ignore once boats are underway.
                if (obstacle.name.Contains("ShoreBase"))
                    continue;

                Vector3 obstaclePosition = obstacle.position;
                float radius = ObstacleRadius(obstacle) + clearance;

                if (HorizontalDistance(adjusted, obstaclePosition) < radius)
                    adjusted = PushAway(adjusted, obstaclePosition, radius);

                if (SegmentDistance(current, adjusted, obstaclePosition) < radius)
                {
                    Vector3 path = toGoal.sqrMagnitude > .001f ? toGoal.normalized : Vector3.right;
                    Vector3 side = Vector3.Cross(Vector3.up, path).normalized;
                    float sign = Vector3.Dot(current - obstaclePosition, side) >= 0f ? 1f : -1f;

                    // Prefer a side waypoint that still advances toward the goal.
                    Vector3 waypointA = obstaclePosition + side * (radius * sign);
                    Vector3 waypointB = obstaclePosition - side * (radius * sign);
                    waypointA.y = adjusted.y;
                    waypointB.y = adjusted.y;
                    float scoreA = Vector3.Dot((waypointA - current).normalized, path);
                    float scoreB = Vector3.Dot((waypointB - current).normalized, path);
                    adjusted = scoreA >= scoreB ? waypointA : waypointB;
                }
            }
            return adjusted;
        }

        private static float ObstacleRadius(Transform obstacle)
        {
            string name = obstacle.name;
            if (name.Contains("Lighthouse"))
                return 6f;
            if (name.Contains("Buoy") || name.Contains("Barrier"))
                return 3.5f;
            if (name.Contains("ShoreBase"))
                return 4f;
            if (name.Contains("Target"))
                return 5f;
            return 4f;
        }

        private static Vector3 PushAway(Vector3 point, Vector3 obstacle, float radius)
        {
            Vector3 away = point - obstacle;
            away.y = 0f;
            if (away.sqrMagnitude < .001f)
                away = Vector3.right;
            Vector3 result = obstacle + away.normalized * radius;
            result.y = point.y;
            return result;
        }

        private static Vector3 Flatten(Vector3 point)
        {
            point.y = 0f;
            return point;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float SegmentDistance(Vector3 start, Vector3 end, Vector3 point)
        {
            Vector2 a = new Vector2(start.x, start.z);
            Vector2 b = new Vector2(end.x, end.z);
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > .001f
                ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude)
                : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private void MoveSurfaceAgent(Transform agent, Vector3 destination, float speed, bool fullSpeed = false)
        {
            destination = ClampToWater(destination, .42f);
            destination = SoftAvoidPeers(agent, destination, boats, agentSeparation * .85f);
            destination = ClampToWater(destination, .42f);

            Vector3 toDestination = destination - agent.position;
            toDestination.y = 0f;
            float distance = toDestination.magnitude;
            float currentSpeed = surfaceSpeedState.TryGetValue(agent, out float storedSpeed)
                ? storedSpeed
                : 0f;
            if (distance > .15f)
            {
                Vector3 desiredDirection = toDestination / distance;
                Quaternion desiredRotation =
                    Quaternion.LookRotation(desiredDirection, Vector3.up) *
                    Quaternion.Euler(0f, -90f, 0f);
                float turnError = Quaternion.Angle(agent.rotation, desiredRotation);
                agent.rotation = Quaternion.RotateTowards(
                    agent.rotation,
                    desiredRotation,
                    Mathf.Max(1f, surfaceMaxTurnRate) * Time.deltaTime
                );

                float factor = fullSpeed ? 1f : Mathf.Max(.5f, Mathf.Clamp01(distance / 12f));
                float turnScale = Mathf.Lerp(
                    1f,
                    .16f,
                    Mathf.Clamp01(turnError / Mathf.Max(10f, surfaceTurnSlowdownAngle))
                );
                float desiredSpeed = speed * factor * turnScale;
                currentSpeed = Mathf.MoveTowards(
                    currentSpeed,
                    desiredSpeed,
                    Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
                );

                Vector3 forward = agent.right;
                forward.y = 0f;
                if (forward.sqrMagnitude > .001f)
                    forward.Normalize();
                float step = Mathf.Min(currentSpeed * Time.deltaTime, distance);
                Vector3 next = agent.position + forward * step;
                next.y = .42f;
                Vector3 nextEnu = Coordinates.ToEnu(next);
                if (!IsLand(new Vector2(nextEnu.x, nextEnu.y)))
                    agent.position = next;
                else
                    currentSpeed = 0f;
            }
            else
            {
                currentSpeed = Mathf.MoveTowards(
                    currentSpeed,
                    0f,
                    Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
                );
            }
            surfaceSpeedState[agent] = currentSpeed;
        }

        private void BrakeSurfaceAgent(Transform agent)
        {
            float current = surfaceSpeedState.TryGetValue(agent, out float stored) ? stored : 0f;
            float nextSpeed = Mathf.MoveTowards(
                current,
                0f,
                Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
            );
            Vector3 forward = agent.right;
            forward.y = 0f;
            if (forward.sqrMagnitude > .001f && nextSpeed > .01f)
            {
                forward.Normalize();
                Vector3 next = agent.position + forward * nextSpeed * Time.deltaTime;
                next.y = .42f;
                Vector3 nextEnu = Coordinates.ToEnu(next);
                if (!IsLand(new Vector2(nextEnu.x, nextEnu.y)))
                    agent.position = next;
                else
                    nextSpeed = 0f;
            }
            surfaceSpeedState[agent] = nextSpeed;
        }

        private void RotateSurfaceToward(Transform agent, Vector3 point)
        {
            Vector3 direction = point - agent.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
                return;
            Quaternion facing =
                Quaternion.LookRotation(direction.normalized, Vector3.up) *
                Quaternion.Euler(0f, -90f, 0f);
            agent.rotation = Quaternion.RotateTowards(
                agent.rotation,
                facing,
                Mathf.Max(1f, surfaceMaxTurnRate) * Time.deltaTime
            );
        }

        private bool IsLand(Vector2 enu)
        {
            if (coastlineColliders == null || coastlineColliders.Length == 0)
                return enu.x < -100f || enu.x > 108f || enu.y < -140f || enu.y > 72f;

            // Center sample only — margin sampling was falsely marking open water as land
            // and trapping boats in local loops near the lighthouse/shore.
            Ray ray = new Ray(Coordinates.ToUnity(enu.x, enu.y, 80f), Vector3.down);
            for (int i = 0; i < coastlineColliders.Length; i++)
            {
                Collider coast = coastlineColliders[i];
                if (coast && coast.Raycast(ray, out _, 160f))
                    return true;
            }
            return false;
        }

        private Vector2 FindNearestWater(Vector2 enu, Vector2 preferToward)
        {
            if (!IsLand(enu))
                return enu;

            Vector2 toward = preferToward - enu;
            if (toward.sqrMagnitude < .01f)
                toward = new Vector2(targetCenterEnu.x, targetCenterEnu.y) - enu;
            if (toward.sqrMagnitude < .01f)
                toward = Vector2.right;
            toward.Normalize();

            for (float step = 2f; step <= 80f; step += 2f)
            {
                Vector2 candidate = enu + toward * step;
                if (!IsLand(candidate))
                    return candidate;

                // Spiral search around the preferred direction.
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * .785398f;
                    Vector2 offset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step;
                    candidate = enu + offset;
                    if (!IsLand(candidate))
                        return candidate;
                }
            }

            return new Vector2(targetCenterEnu.x, targetCenterEnu.y);
        }

        private Vector3 ClampToWater(Vector3 unityPos, float height)
        {
            Vector3 enu = Coordinates.ToEnu(unityPos);
            Vector2 xy = new Vector2(enu.x, enu.y);
            if (!IsLand(xy))
            {
                unityPos.y = height;
                return unityPos;
            }

            Vector2 safe = FindNearestWater(xy, new Vector2(targetCenterEnu.x, targetCenterEnu.y));
            return Coordinates.ToUnity(safe.x, safe.y, height);
        }

        private void MoveAirAgent(Transform agent, Vector3 destination, float speed, bool fullSpeed = false)
        {
            destination = SoftAvoidPeers(agent, destination, drones, droneSeparation * .85f);
            Vector3 toDestination = destination - agent.position;
            float distance = toDestination.magnitude;
            Vector3 velocity = airVelocityState.TryGetValue(agent, out Vector3 storedVelocity)
                ? storedVelocity
                : Vector3.zero;
            if (distance > .15f)
            {
                float factor = fullSpeed ? 1f : Mathf.Max(.45f, Mathf.Clamp01(distance / 10f));
                Vector3 desiredVelocity = toDestination.normalized * speed * factor;
                velocity = Vector3.MoveTowards(
                    velocity,
                    desiredVelocity,
                    Mathf.Max(.1f, airAcceleration) * Time.deltaTime
                );
                Vector3 step = velocity * Time.deltaTime;
                if (step.magnitude > distance)
                    step = toDestination;
                agent.position += step;
            }
            else
            {
                velocity = Vector3.MoveTowards(
                    velocity,
                    Vector3.zero,
                    Mathf.Max(.1f, airAcceleration) * Time.deltaTime
                );
            }

            airVelocityState[agent] = velocity;
            Vector3 horizontalVelocity = velocity;
            horizontalVelocity.y = 0f;
            if (horizontalVelocity.sqrMagnitude > .001f)
                RotateAirToward(agent, agent.position + horizontalVelocity);
        }

        private void BrakeAirAgent(Transform agent)
        {
            Vector3 velocity = airVelocityState.TryGetValue(agent, out Vector3 stored)
                ? stored
                : Vector3.zero;
            Vector3 nextVelocity = Vector3.MoveTowards(
                velocity,
                Vector3.zero,
                Mathf.Max(.1f, airAcceleration) * Time.deltaTime
            );
            agent.position += nextVelocity * Time.deltaTime;
            airVelocityState[agent] = nextVelocity;
        }

        private void RotateAirToward(Transform agent, Vector3 point)
        {
            Vector3 direction = point - agent.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
                return;
            Quaternion facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
            agent.rotation = Quaternion.RotateTowards(
                agent.rotation,
                facing,
                Mathf.Max(1f, airMaxTurnRate) * Time.deltaTime
            );
        }

        private void AnimateBarrier()
        {
            if (!dynamicBarrier)
                return;

            float missionTime = Time.time - scenarioStarted - dispatchDelaySeconds;
            if (missionTime < 0f)
            {
                dynamicBarrier.position = barrierStartPosition + new Vector3(-8f, 0f, 0f);
                return;
            }

            Vector3 start = barrierStartPosition + new Vector3(-8f, 0f, 0f);
            Vector3 end = barrierStartPosition + new Vector3(14f, 0f, 6f);

            // Only demo dynamic avoidance for a couple of passes, then park.
            if (missionTime >= barrierDemoSeconds)
            {
                dynamicBarrier.position = end;
                return;
            }

            float passDuration = Mathf.Max(.5f, barrierDemoSeconds / Mathf.Max(1, barrierDemoPasses));
            int pass = Mathf.Min(barrierDemoPasses - 1, Mathf.FloorToInt(missionTime / passDuration));
            float u = Mathf.Clamp01((missionTime - pass * passDuration) / passDuration);
            // Ease across the channel; odd passes reverse.
            bool reverse = (pass % 2) == 1;
            dynamicBarrier.position = Vector3.Lerp(reverse ? end : start, reverse ? start : end, u);
        }

        private static void FaceToward(Transform source, Vector3 target)
        {
            Vector3 delta = target - source.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .001f)
                source.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
        }

        private void BuildLineVisuals()
        {
            Transform[] agents = Agents();
            for (int i = 0; i < agents.Length; i++)
            {
                commandLinks.Add(CreateLine("Base Command Link " + i, new Color(.2f, .95f, 1f, .54f), .08f));
                sensorRings.Add(CreateLine("Sensor Coverage " + i, new Color(.18f, 1f, .42f, .34f), .07f));
                tracks.Add(CreateLine("Agent Track " + i, new Color(1f, .86f, .22f, .54f), .1f));
                trackPoints.Add(new List<Vector3>());
            }

            captureRing = CreateLine("USV Capture Ring", new Color(1f, .24f, .08f, .85f), .16f);
            defenseRing = CreateLine("UAV Defense Ring", new Color(.18f, .62f, 1f, .7f), .12f);
        }

        private LineRenderer CreateLine(string name, Color color, float width)
        {
            var go = new GameObject(name);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = SceneFactory.Material(name + " Material", color, 0f, .5f);
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.useWorldSpace = true;
            line.positionCount = 0;
            return line;
        }

        private Transform[] Agents()
        {
            var all = new List<Transform>();
            if (boats != null)
                all.AddRange(boats);
            if (drones != null)
                all.AddRange(drones);
            return all.ToArray();
        }

        private void UpdateLineVisuals()
        {
            Transform[] agents = Agents();
            Vector3 baseAnchor = shoreBase ? shoreBase.position + Vector3.up * 7.5f : Vector3.zero;
            int boatCount = boats?.Length ?? 0;
            for (int i = 0; i < agents.Length; i++)
            {
                Transform agent = agents[i];
                if (!agent)
                    continue;

                bool isBoat = i < boatCount;
                bool showLink = targetDetected || isBoat;
                LineRenderer link = commandLinks[i];
                if (showLink)
                {
                    link.positionCount = 2;
                    link.SetPosition(0, baseAnchor);
                    link.SetPosition(1, agent.position + Vector3.up * (isBoat ? 1.2f : .2f));
                }
                else
                {
                    link.positionCount = 0;
                }

                float range = isBoat
                    ? (boatSensors != null && i < boatSensors.Length && boatSensors[i] ? boatSensors[i].lidarRange : sensorRange * .7f)
                    : (droneSensors != null && (i - boatCount) < droneSensors.Length && droneSensors[i - boatCount]
                        ? droneSensors[i - boatCount].radarRange
                        : sensorRange);
                float height = isBoat ? .65f : (dronesAirborne ? droneAltitude : .8f);
                DrawCircle(sensorRings[i], agent.position, isBoat || dronesAirborne ? range : 4f, height);
            }
        }

        private void UpdateMissionRings()
        {
            if (!targetPoint || !targetDetected)
            {
                if (captureRing)
                    captureRing.positionCount = 0;
                if (defenseRing)
                    defenseRing.positionCount = 0;
                return;
            }

            // Red ring shrinks with the boats so the close-in process is visible.
            DrawCircle(captureRing, targetPoint.position, CurrentBoatRingRadius(), .72f);
            DrawCircle(defenseRing, targetPoint.position, defenseRadius, droneAltitude);
        }

        private void DrawCircle(LineRenderer line, Vector3 center, float radius, float height)
        {
            const int segments = 72;
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * radius, height, center.z + Mathf.Sin(angle) * radius));
            }
        }

        private void UpdateTracks()
        {
            Transform[] agents = Agents();
            int boatCount = boats?.Length ?? 0;
            for (int i = 0; i < agents.Length && i < trackPoints.Count; i++)
            {
                Transform agent = agents[i];
                if (!agent)
                    continue;

                // Drones only leave tracks after takeoff.
                if (i >= boatCount && !dronesAirborne)
                {
                    tracks[i].positionCount = 0;
                    continue;
                }

                List<Vector3> points = trackPoints[i];
                Vector3 p = agent.position;
                p.y = i < boatCount ? .72f : .18f;
                if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) > 1.25f)
                {
                    points.Add(p);
                    if (points.Count > 180)
                        points.RemoveAt(0);
                }

                tracks[i].positionCount = points.Count;
                for (int j = 0; j < points.Count; j++)
                    tracks[i].SetPosition(j, points[j]);
            }
        }

        private void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(.9f, .96f, 1f) }
            };

            string baseStatus = baseController ? baseController.status : "Local auto";
            int boatHits = CountHits(boatSensors);
            int droneHits = dronesAirborne ? CountHits(droneSensors) : 0;

            Rect panel = new Rect(16f, 16f, 410f, 248f);
            GUI.Box(panel, "");
            GUI.Label(new Rect(30f, 26f, 370f, 28f), "3 USV + 3 UAV + 1 Target", titleStyle);
            GUI.Label(
                new Rect(30f, 58f, 370f, 150f),
                "Control: shore base station\n" +
                "Phase: " + phase + "\n" +
                "Base: " + baseStatus + "\n" +
                "Contact: " + (targetDetected ? detectReporter : "none yet") + "\n" +
                "UAVs: " + (dronesTakingOff ? "taking off" : (dronesAirborne ? "airborne" : "on pad")) +
                (captureComplete ? "  LOCKED" : "") + "\n" +
                "Red=USV triangle  Blue=UAV triangle\n" +
                "M pause  R reset  B dispatch  V lines " + (showDebugOverlays ? "on" : "off"),
                bodyStyle
            );

            if (GUI.Button(new Rect(30f, 214f, 92f, 28f), automatic ? "Pause" : "Resume"))
                automatic = !automatic;
            if (GUI.Button(new Rect(132f, 214f, 92f, 28f), "Reset"))
                ResetScenario();
            if (GUI.Button(new Rect(234f, 214f, 108f, 28f), showDebugOverlays ? "Hide Extra" : "Show Extra"))
                showDebugOverlays = !showDebugOverlays;
        }

        private static int CountHits(AgentSensorSuite[] sensors)
        {
            if (sensors == null)
                return 0;
            int total = 0;
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i])
                    total += sensors[i].hitCount;
            }
            return total;
        }

        private void ApplyOverlayVisibility()
        {
            // V only toggles command links / sensor fans / tracks.
            // Red capture + blue defense rings stay visible after target contact.
            SetLinesEnabled(commandLinks, showDebugOverlays);
            SetLinesEnabled(sensorRings, showDebugOverlays);
            SetLinesEnabled(tracks, showDebugOverlays);
            if (captureRing)
                captureRing.enabled = targetDetected;
            if (defenseRing)
                defenseRing.enabled = targetDetected;
        }

        private static void SetLinesEnabled(List<LineRenderer> lines, bool enabled)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i])
                    lines[i].enabled = enabled;
            }
        }

        private void OnDestroy()
        {
            DestroyLines(commandLinks);
            DestroyLines(sensorRings);
            DestroyLines(tracks);
            DestroyLine(captureRing);
            DestroyLine(defenseRing);
        }

        private static void DestroyLines(List<LineRenderer> lines)
        {
            for (int i = 0; i < lines.Count; i++)
                DestroyLine(lines[i]);
        }

        private static void DestroyLine(LineRenderer line)
        {
            if (line && line.sharedMaterial)
                Destroy(line.sharedMaterial);
            if (line)
                Destroy(line.gameObject);
        }
    }
}
