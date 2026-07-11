using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class SimulationBootstrap : MonoBehaviour
    {
        [Tooltip("Use external ROS/Gazebo pose data in Unity Editor Play mode.")]
        public bool useExternalPoseInEditor = false;

        [Tooltip("Prefer WebSocket instead of UDP for external ROS/Gazebo pose data.")]
        public bool useWebSocketPose = true;

        [Tooltip("ROS-side WebSocket bridge URL. Override with --ros-ws-url=ws://host:8765/uav_usv.")]
        public string webSocketUrl = "ws://127.0.0.1:8765/uav_usv";

        [Tooltip("Spring Boot heartbeat endpoint. Override with --platform-url=http://host:8081/api/integration/heartbeat.")]
        public string platformUrl = "http://127.0.0.1:8081/api/integration/heartbeat";

        [Tooltip("Local integration token. Override with --platform-token=value.")]
        public string platformToken = "uav-usv-local-agent";

        [Tooltip("Automatically run demo motion after pressing Play.")]
        public bool playMissionInEditor = false;

        [Tooltip("Run the local 3 USV + 3 UAV + 1 target capture-defense scenario when ROS sync is not active.")]
        public bool runLocalMultiAgentScenarioInEditor = true;

        private CooperativeMission mission;
        private RosUdpBridge bridge;
        private ExternalPoseReceiver externalReceiver;
        private ExternalPoseWebSocketClient webSocketReceiver;
        private MultiAgentCaptureDefenseScenario multiAgentScenario;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.6f, .73f, .82f);
            RenderSettings.fogDensity = .0017f;

            BuildLighting();
            BuildOcean();

            SydneyCoastRuntime coastline = SydneyCoastRuntime.Create();
            Transform lighthouse = BuildLighthouse();
            Transform shoreBase = BuildShoreBaseStation(out Transform[] dronePads);
            var boats = new List<Transform>();
            var drones = new List<Transform>();
            var droneVisuals = new List<DroneVisual>();

            for (int i = 0; i < 3; i++)
            {
                Transform builtBoat = BuildBoat("USV-" + (i + 1));
                boats.Add(builtBoat);

                Transform builtDrone = BuildDrone(dronePads[Mathf.Min(i, dronePads.Length - 1)], out DroneVisual droneVisual, "UAV-" + (i + 1));
                drones.Add(builtDrone);
                droneVisuals.Add(droneVisual);
            }

            Transform boat = boats[0];
            Transform drone = drones[0];
            Transform buoyWest = BuildBuoy("WestChannelBuoy", -42f, 44f, .25f);
            Transform buoySouth = BuildBuoy("SouthChannelBuoy", 34f, -56f, -.35f);
            Transform buoyEast = BuildBuoy("EastChannelBuoy", 78f, 28f, .7f);
            Transform movingBarrier = BuildBuoy("DynamicBarrierBuoy", 28f, -8f, 0f);
            Transform crossingBarrier = BuildBuoy("CrossingBarrierBuoy", 55f, -30f, .4f);
            Transform targetVessel = BuildTargetVessel();
            Transform targetPoint = BuildTargetPoint();
            DeploySearchFormation(boats, drones, droneVisuals, dronePads, targetPoint, shoreBase);

            Transform[] senseObstacles =
            {
                lighthouse, buoyWest, buoySouth, buoyEast, shoreBase, movingBarrier, crossingBarrier, targetVessel
            };
            AttachObstacleColliders(senseObstacles);
            var boatSensors = new List<AgentSensorSuite>();
            var droneSensors = new List<AgentSensorSuite>();
            // During search, use a wider FOV so approaching USVs can acquire the target earlier.
            for (int i = 0; i < boats.Count; i++)
            {
                AgentSensorSuite sensor = AttachSensor(boats[i], AgentSensorSuite.SensorKind.Surface, 38f, 52f, senseObstacles);
                sensor.horizontalFovDegrees = 160f;
                boatSensors.Add(sensor);
            }
            for (int i = 0; i < drones.Count; i++)
                droneSensors.Add(AttachSensor(drones[i], AgentSensorSuite.SensorKind.Air, 30f, 52f, senseObstacles));

            string urlArgument = GetArgumentValue("--ros-ws-url=");
            if (!string.IsNullOrEmpty(urlArgument))
                webSocketUrl = urlArgument;

            string platformUrlArgument = GetArgumentValue("--platform-url=");
            if (!string.IsNullOrEmpty(platformUrlArgument))
                platformUrl = platformUrlArgument;
            string platformTokenArgument = GetArgumentValue("--platform-token=");
            if (!string.IsNullOrEmpty(platformTokenArgument))
                platformToken = platformTokenArgument;

            bool externalSync = HasArgument("--ros-sync") || HasArgument("--ros-ws");
            if (externalSync)
            {
                foreach (Transform syncedBoat in boats)
                {
                    BoatWaveMotion boatWave = syncedBoat ? syncedBoat.GetComponent<BoatWaveMotion>() : null;
                    if (boatWave)
                        boatWave.enabled = false;
                }

                bool useWebSocket = !HasArgument("--ros-udp-pose") && (useWebSocketPose || HasArgument("--ros-ws"));
                if (useWebSocket)
                {
                    webSocketReceiver = gameObject.AddComponent<ExternalPoseWebSocketClient>();
                    webSocketReceiver.boat = boat;
                    webSocketReceiver.drone = drone;
                    webSocketReceiver.lighthouse = lighthouse;
                    webSocketReceiver.buoyWest = buoyWest;
                    webSocketReceiver.buoySouth = buoySouth;
                    webSocketReceiver.buoyEast = buoyEast;
                    webSocketReceiver.targetVessel = targetVessel;
                    webSocketReceiver.droneHeightOffset = .28f;
                    webSocketReceiver.serverUrl = webSocketUrl;

                    BoatPathPlanningController pathPlanning =
                        gameObject.AddComponent<BoatPathPlanningController>();
                    pathPlanning.boat = boat;
                    pathPlanning.lighthouse = lighthouse;
                    pathPlanning.buoyWest = buoyWest;
                    pathPlanning.buoySouth = buoySouth;
                    pathPlanning.buoyEast = buoyEast;
                    pathPlanning.targetVessel = targetVessel;
                    pathPlanning.webSocket = webSocketReceiver;
                    pathPlanning.SetCoastlineCollisionRoot(
                        coastline ? coastline.collisionRoot : null
                    );
                }
                else
                {
                    externalReceiver = gameObject.AddComponent<ExternalPoseReceiver>();
                    externalReceiver.boat = boat;
                    externalReceiver.drone = drone;
                    externalReceiver.droneHeightOffset = .28f;
                }
            }
            else
            {
                bool legacyDeckDemo = HasArgument("--legacy-deck-demo");
                MultiAgentCaptureDefenseScenario scenario =
                    gameObject.AddComponent<MultiAgentCaptureDefenseScenario>();
                scenario.shoreBase = shoreBase;
                scenario.targetPoint = targetPoint;
                scenario.targetVessel = targetVessel;
                scenario.dynamicBarrier = movingBarrier;
                scenario.obstacles = new[]
                {
                    lighthouse, buoyWest, buoySouth, buoyEast, shoreBase, movingBarrier, crossingBarrier
                };
                scenario.boats = boats.ToArray();
                scenario.drones = drones.ToArray();
                scenario.dronePads = dronePads;
                scenario.droneVisuals = droneVisuals.ToArray();
                scenario.boatSensors = boatSensors.ToArray();
                scenario.droneSensors = droneSensors.ToArray();
                scenario.searchStartRadius = 72f;
                scenario.SetCoastlineCollisionRoot(coastline ? coastline.collisionRoot : null);
                scenario.automatic = !Application.isEditor || runLocalMultiAgentScenarioInEditor || playMissionInEditor;
                scenario.enabled = !legacyDeckDemo;
                multiAgentScenario = scenario;

                ShoreBaseController baseController = gameObject.AddComponent<ShoreBaseController>();
                baseController.shoreBase = shoreBase;
                baseController.boats = boats.ToArray();
                baseController.drones = drones.ToArray();
                baseController.targetPoint = targetPoint;
                baseController.scenario = scenario;
                baseController.automatic = scenario.automatic;
                scenario.baseController = baseController;

                CrossingBarrierMotion crossing = crossingBarrier.gameObject.AddComponent<CrossingBarrierMotion>();
                crossing.amplitude = 14f;
                crossing.passDuration = 8f;
                crossing.maxPasses = 2;

                if (legacyDeckDemo)
                {
                    Transform deck = boat.Find("LandingDeck");
                    mission = gameObject.AddComponent<CooperativeMission>();
                    mission.boat = boat;
                    mission.deck = deck;
                    mission.drone = drone;
                    mission.droneVisual = droneVisuals[0];
                    mission.cruiseWithDroneOnDeck = false;
                    mission.automatic = playMissionInEditor;

                    bridge = gameObject.AddComponent<RosUdpBridge>();
                    bridge.mission = mission;
                    bridge.enabledBridge = HasArgument("--ros-udp");
                }
            }

            BuildCamera(
                boat,
                drone,
                targetPoint ? targetPoint : lighthouse,
                boats,
                drones,
                targetVessel
            );
            lighthouse.gameObject.AddComponent<BeaconSweep>();

            PlatformHeartbeat heartbeat = gameObject.AddComponent<PlatformHeartbeat>();
            heartbeat.endpoint = platformUrl;
            heartbeat.token = platformToken;
        }

        private static bool HasArgument(string value)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
            {
                if (argument == value)
                    return true;
            }

            return false;
        }

        private static string GetArgumentValue(string prefix)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith(prefix))
                    return argument.Substring(prefix.Length);
            }

            return null;
        }

        private static void DeploySearchFormation(
            IReadOnlyList<Transform> boats,
            IReadOnlyList<Transform> drones,
            IReadOnlyList<DroneVisual> droneVisuals,
            Transform[] dronePads,
            Transform targetPoint,
            Transform shoreBase)
        {
            Vector2 center = new Vector2(40f, -20f);
            float[] boatAngles = { 0f, 120f, 240f };
            const float startRadius = 72f;
            Vector2[] preferredBoatStarts =
            {
                new Vector2(105f, -20f),
                new Vector2(-2f, 32f),
                new Vector2(-2f, -72f)
            };

            Vector3 lookTarget = targetPoint
                ? targetPoint.position
                : Coordinates.ToUnity(40f, -20f, .42f);

            for (int i = 0; boats != null && i < boats.Count; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                Vector2 start = i < preferredBoatStarts.Length
                    ? preferredBoatStarts[i]
                    : PointAround(center, boatAngles[i % boatAngles.Length], startRadius);
                boat.position = Coordinates.ToUnity(start.x, start.y, .42f);
                FaceSurfaceAgent(boat, lookTarget);
            }

            for (int i = 0; drones != null && i < drones.Count; i++)
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

                if (droneVisuals != null && i < droneVisuals.Count && droneVisuals[i])
                    droneVisuals[i].spinning = false;
            }
        }

        private static Vector2 PointAround(Vector2 center, float angleDegrees, float radius)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private static AgentSensorSuite AttachSensor(
            Transform agent,
            AgentSensorSuite.SensorKind kind,
            float lidarRange,
            float radarRange,
            Transform[] obstacles)
        {
            AgentSensorSuite sensor = agent.gameObject.AddComponent<AgentSensorSuite>();
            sensor.Configure(kind, lidarRange, radarRange, obstacles);
            sensor.drawDebugRays = false;
            return sensor;
        }

        private static void AttachObstacleColliders(Transform[] obstacles)
        {
            if (obstacles == null)
                return;

            for (int i = 0; i < obstacles.Length; i++)
            {
                Transform obstacle = obstacles[i];
                if (!obstacle || obstacle.GetComponent<Collider>())
                    continue;

                SphereCollider collider = obstacle.gameObject.AddComponent<SphereCollider>();
                collider.isTrigger = false;
                string name = obstacle.name;
                if (name.Contains("Lighthouse"))
                    collider.radius = 2.2f;
                else if (name.Contains("ShoreBase"))
                    collider.radius = 6f;
                else if (name.Contains("Target"))
                    collider.radius = 4.2f;
                else
                    collider.radius = 1.4f;
                collider.center = new Vector3(0f, collider.radius, 0f);
            }
        }

        private static void FaceSurfaceAgent(Transform source, Vector3 target)
        {
            Vector3 delta = target - source.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .001f)
                source.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
        }

        private static void FaceAirAgent(Transform source, Vector3 target)
        {
            Vector3 delta = target - source.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .001f)
                source.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        private void BuildLighting()
        {
            RenderSettings.ambientLight = new Color(.58f, .68f, .78f);
            RenderSettings.reflectionIntensity = .72f;
            RenderSettings.reflectionBounces = 1;

            var hdriTemplate = Resources.Load<Material>("Sky/PureOceanSky");
            var hdriTexture = Resources.Load<Texture>("Sky/kloofendal_partly_cloudy_puresky_1k");

            Material sky = hdriTemplate ? new Material(hdriTemplate) : null;

            if (!sky && hdriTexture)
            {
                Shader panoramicShader = Shader.Find("Skybox/Panoramic");
                if (panoramicShader)
                {
                    sky = new Material(panoramicShader) { name = "Partly Cloudy Pure Ocean Sky" };
                    sky.SetTexture("_MainTex", hdriTexture);
                    sky.SetFloat("_Exposure", .82f);
                    sky.SetFloat("_Rotation", 72f);
                }
            }

            if (!sky)
            {
                Shader skyShader = Resources.Load<Shader>("MaritimeSky") ?? Shader.Find("UavUsv/MaritimeSky");
                if (skyShader)
                {
                    sky = new Material(skyShader) { name = "Procedural Maritime Sky" };
                    sky.SetFloat("_CloudSpeed", .012f);
                    sky.SetFloat("_CloudAmount", .62f);
                    sky.SetFloat("_Exposure", 1.22f);
                }
            }

            if (sky)
            {
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = .68f;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                DynamicGI.UpdateEnvironment();
            }

            Light sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.05f;
            sun.color = new Color(1f, .94f, .82f);
            sun.transform.rotation = Quaternion.Euler(28f, -58f, 0f);
            sun.shadows = LightShadows.Soft;
        }

        private void BuildOcean()
        {
            BuildOceanUnderlay();

            var ocean = new GameObject("Ocean") { layer = 4 };
            ocean.AddComponent<MeshFilter>();

            MeshRenderer renderer = ocean.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            OceanSurface surface = ocean.AddComponent<OceanSurface>();
            surface.size = 2200f;
            surface.resolution = 260;
            surface.edgeIrregularity = .02f;
        }

        private static void BuildOceanUnderlay()
        {
            var underlay = new GameObject("Coastal Water Fill") { layer = 4 };
            underlay.transform.position = new Vector3(0f, -.16f, 0f);

            Mesh mesh = new Mesh
            {
                name = "Coastal Water Fill Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            float half = 760f;
            mesh.vertices = new[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3(-half, 0f, half),
                new Vector3(half, 0f, -half),
                new Vector3(half, 0f, half)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = underlay.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = underlay.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Shader shader = Resources.Load<Shader>("WindOcean") ??
                            Shader.Find("UavUsv/WindOcean");
            if (shader)
            {
                Material material = new Material(shader)
                {
                    name = "Coastal Water Fill Material"
                };
                material.SetColor("_DeepColor", new Color(.035f, .17f, .22f, 1f));
                material.SetColor("_ShallowColor", new Color(.075f, .31f, .37f, 1f));
                material.SetColor("_FoamColor", new Color(.72f, .86f, .88f, 1f));
                material.SetFloat("_WaveAmplitude", .06f);
                material.SetFloat("_WindSpeed", 3.2f);
                renderer.sharedMaterial = material;
            }
        }

        private Transform BuildLighthouse()
        {
            Transform root = new GameObject("NavigationLighthouse").transform;
            // Keep lighthouse as a channel obstacle, but off the USV approach axes.
            root.position = Coordinates.ToUnity(-55f, 35f, 0f);

            Material stone = SceneFactory.Material("Stone", new Color(.16f, .16f, .14f));
            Material white = SceneFactory.Material("Ivory", new Color(.95f, .93f, .84f), 0, .22f);
            Material red = SceneFactory.Material("SignalRed", new Color(.78f, .035f, .025f), .1f, .4f);
            Material glass = SceneFactory.Material("LanternGlass", new Color(1f, .78f, .2f, .72f), .05f, .92f);

            SceneFactory.Primitive("RockBase", PrimitiveType.Cylinder, root, new Vector3(0, .3f, 0), new Vector3(4.4f, .3f, 4.4f), stone);
            SceneFactory.Primitive("Tower", PrimitiveType.Cylinder, root, new Vector3(0, 4.4f, 0), new Vector3(1.7f, 4.1f, 1.7f), white);
            SceneFactory.Primitive("RedBandLower", PrimitiveType.Cylinder, root, new Vector3(0, 2.7f, 0), new Vector3(1.76f, .325f, 1.76f), red);
            SceneFactory.Primitive("RedBandUpper", PrimitiveType.Cylinder, root, new Vector3(0, 5.8f, 0), new Vector3(1.76f, .325f, 1.76f), red);
            SceneFactory.Primitive("LanternRoom", PrimitiveType.Cylinder, root, new Vector3(0, 8.8f, 0), new Vector3(2.1f, .45f, 2.1f), glass);
            SceneFactory.Primitive("Roof", PrimitiveType.Cylinder, root, new Vector3(0, 9.55f, 0), new Vector3(2.4f, .225f, 2.4f), red);

            Light beacon = new GameObject("BeaconLight").AddComponent<Light>();
            beacon.transform.SetParent(root, false);
            beacon.transform.localPosition = new Vector3(0, 8.95f, 0);
            beacon.type = LightType.Point;
            beacon.range = 18f;
            beacon.intensity = 1.25f;
            beacon.color = new Color(1f, .82f, .5f);

            return root;
        }

        private Transform BuildShoreBaseStation(out Transform[] dronePads)
        {
            Transform root = new GameObject("ShoreBaseStation").transform;
            root.position = Coordinates.ToUnity(-10f, -32f, 0f);

            Material deck = SceneFactory.Material("Helipad Deck", new Color(.17f, .19f, .19f), .01f, .3f);
            Material walkway = SceneFactory.Material("Helipad Walkway", new Color(.34f, .36f, .36f), .01f, .28f);
            Material cabin = SceneFactory.Material("Base Cabin", new Color(.08f, .09f, .1f), .08f, .4f);
            Material glass = SceneFactory.Material("Base Glass", new Color(.08f, .55f, .95f, .62f), .05f, .92f);
            Material white = SceneFactory.Material("Helipad H", new Color(1f, 1f, .96f), 0f, .3f);
            Material yellow = SceneFactory.Material("Helipad Edge", new Color(1f, .82f, .1f), .03f, .45f);
            Material blue = SceneFactory.Material("Command Antenna", new Color(.05f, .36f, .9f), .12f, .55f);

            dronePads = new Transform[3];
            for (int i = 0; i < dronePads.Length; i++)
            {
                Transform pad = new GameObject("UAVPad-" + (i + 1)).transform;
                pad.SetParent(root, false);
                pad.localPosition = new Vector3(-6f + i * 6f, .34f, 2.4f);
                dronePads[i] = pad;

                SceneFactory.Primitive("DeckSurface", PrimitiveType.Cylinder, pad, Vector3.zero, new Vector3(2.2f, .035f, 2.2f), deck);
                SceneFactory.Primitive("HLeft", PrimitiveType.Cube, pad, new Vector3(0f, .045f, -.35f), new Vector3(1f, .018f, .12f), white);
                SceneFactory.Primitive("HRight", PrimitiveType.Cube, pad, new Vector3(0f, .045f, .35f), new Vector3(1f, .018f, .12f), white);
                SceneFactory.Primitive("HCrossbar", PrimitiveType.Cube, pad, new Vector3(0f, .05f, 0f), new Vector3(.16f, .02f, .82f), white);
                SceneFactory.Primitive("PadEdge", PrimitiveType.Cylinder, pad, new Vector3(0f, .02f, 0f), new Vector3(2.45f, .012f, 2.45f), yellow);
            }

            SceneFactory.Primitive("ShoreWalkway", PrimitiveType.Cube, root, new Vector3(0f, .25f, 0f), new Vector3(18f, .22f, 5.5f), walkway);
            SceneFactory.Primitive("CommandCabin", PrimitiveType.Cube, root, new Vector3(-6.6f, 1.2f, -1.5f), new Vector3(3.2f, 1.7f, 2.2f), cabin);
            SceneFactory.Primitive("CommandWindow", PrimitiveType.Cube, root, new Vector3(-4.95f, 1.35f, -1.5f), new Vector3(.06f, .7f, 1.7f), glass);
            SceneFactory.Primitive("AntennaMast", PrimitiveType.Cylinder, root, new Vector3(-7.7f, 3.15f, -1.5f), new Vector3(.08f, 1.55f, .08f), blue);
            SceneFactory.Primitive("AntennaDish", PrimitiveType.Sphere, root, new Vector3(-7.7f, 4.75f, -1.5f), Vector3.one * .28f, glass);

            Light signal = new GameObject("BaseSignalLight").AddComponent<Light>();
            signal.transform.SetParent(root, false);
            signal.transform.localPosition = new Vector3(-7.7f, 4.75f, -1.5f);
            signal.type = LightType.Point;
            signal.range = 34f;
            signal.intensity = 1.4f;
            signal.color = new Color(.2f, .75f, 1f);

            return root;
        }

        private Transform BuildTargetPoint()
        {
            Transform root = new GameObject("CaptureTargetPoint").transform;
            root.position = Coordinates.ToUnity(40f, -20f, .38f);

            Material amber = SceneFactory.Material("Target Point Amber", new Color(1f, .54f, .05f, .9f), .05f, .72f);
            Material red = SceneFactory.Material("Target Point Red", new Color(1f, .08f, .02f, .68f), .05f, .65f);
            SceneFactory.Primitive("TargetRingOuter", PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(1f, .02f, 1f), amber);
            SceneFactory.Primitive("TargetRingInner", PrimitiveType.Cylinder, root, new Vector3(0f, .026f, 0f), new Vector3(.55f, .018f, .55f), red);
            SceneFactory.Primitive("TargetBeacon", PrimitiveType.Sphere, root, new Vector3(0f, .55f, 0f), Vector3.one * .18f, amber);
            return root;
        }

        private Transform BuildBoat(string name = "LandingBoat")
        {
            Transform root = new GameObject(name).transform;
            root.position = Coordinates.ToUnity(0, 0, .42f);
            root.localScale = Vector3.one * 2f;

            Material dark = SceneFactory.Material(name + " HullDark", new Color(.06f, .07f, .08f), .2f, .45f);
            Material orange = SceneFactory.Material(name + " RescueOrange", new Color(.95f, .24f, .035f), .12f, .42f);
            Material deckMat = SceneFactory.Material(name + " Deck", new Color(.17f, .17f, .16f));
            Material cabin = SceneFactory.Material(name + " Cabin", new Color(.92f, .9f, .78f));
            Material window = SceneFactory.Material("Window", new Color(.04f, .26f, .48f, .72f), .1f, .9f);
            Material white = SceneFactory.Material(name + " PadWhite", Color.white, 0, .25f);

            SceneFactory.Primitive("LowerHull", PrimitiveType.Cube, root, new Vector3(-.05f, -.16f, 0), new Vector3(2.45f, .28f, .82f), dark);
            SceneFactory.Primitive("UpperHull", PrimitiveType.Cube, root, new Vector3(-.1f, .05f, 0), new Vector3(2.35f, .32f, .94f), orange);
            SceneFactory.Cone("Bow", root, new Vector3(1.3f, .01f, 0), .49f, .72f, orange, new Vector3(0, 0, -90));

            SceneFactory.Primitive("MainDeck", PrimitiveType.Cube, root, new Vector3(-.15f, .25f, 0), new Vector3(2.05f, .08f, .78f), deckMat);
            SceneFactory.Primitive("Cabin", PrimitiveType.Cube, root, new Vector3(.2f, .58f, 0), new Vector3(.78f, .48f, .62f), cabin);
            SceneFactory.Primitive("FrontWindow", PrimitiveType.Cube, root, new Vector3(.6f, .64f, 0), new Vector3(.035f, .24f, .48f), window);
            SceneFactory.Primitive("LeftWindow", PrimitiveType.Cube, root, new Vector3(.2f, .64f, .325f), new Vector3(.45f, .22f, .025f), window);
            SceneFactory.Primitive("RightWindow", PrimitiveType.Cube, root, new Vector3(.2f, .64f, -.325f), new Vector3(.45f, .22f, .025f), window);
            SceneFactory.Primitive("CabinRoof", PrimitiveType.Cube, root, new Vector3(.15f, .85f, 0), new Vector3(.92f, .08f, .72f), dark);

            SceneFactory.Primitive("RubRailLeft", PrimitiveType.Cylinder, root, new Vector3(0, .15f, .51f), new Vector3(.05f, 1.125f, .05f), dark, new Vector3(0, 0, 90));
            SceneFactory.Primitive("RubRailRight", PrimitiveType.Cylinder, root, new Vector3(0, .15f, -.51f), new Vector3(.05f, 1.125f, .05f), dark, new Vector3(0, 0, 90));

            Transform landingDeck = new GameObject("LandingDeck").transform;
            landingDeck.SetParent(root, false);
            landingDeck.localPosition = new Vector3(-.92f, .43f, 0);

            SceneFactory.Primitive("Pad", PrimitiveType.Cube, landingDeck, Vector3.zero, new Vector3(1.15f, .08f, 1.15f), dark);
            SceneFactory.Primitive("Ring", PrimitiveType.Cylinder, landingDeck, new Vector3(0, .048f, 0), new Vector3(.96f, .012f, .96f), white);
            SceneFactory.Primitive("Center", PrimitiveType.Cylinder, landingDeck, new Vector3(0, .056f, 0), new Vector3(.72f, .014f, .72f), dark);
            SceneFactory.Primitive("H1", PrimitiveType.Cube, landingDeck, new Vector3(0, .068f, 0), new Vector3(.48f, .012f, .08f), white);
            SceneFactory.Primitive("H2", PrimitiveType.Cube, landingDeck, new Vector3(0, .07f, 0), new Vector3(.08f, .012f, .48f), white);

            SceneFactory.Primitive("Mast", PrimitiveType.Cylinder, root, new Vector3(-.05f, 1.1f, 0), new Vector3(.036f, .275f, .036f), dark);
            SceneFactory.Primitive("NavLight", PrimitiveType.Sphere, root, new Vector3(-.05f, 1.39f, 0), Vector3.one * .11f, white);

            root.gameObject.AddComponent<BoatWaveMotion>();
            return root;
        }

        private Transform BuildBuoy(string name, float x, float y, float yaw)
        {
            Transform root = new GameObject(name).transform;
            root.position = Coordinates.ToUnity(x, y, 0);
            root.rotation = Quaternion.Euler(0, -yaw * Mathf.Rad2Deg, 0);

            Material red = SceneFactory.Material(name + " Red", new Color(.92f, .07f, .05f), .05f, .4f);
            Material white = SceneFactory.Material(name + " White", new Color(1f, .98f, .88f), 0, .3f);
            Material dark = SceneFactory.Material(name + " Mast", new Color(.06f, .06f, .06f), .15f, .45f);
            Material amber = SceneFactory.Material(name + " Beacon", new Color(1f, .78f, .1f), .05f, .9f);

            SceneFactory.Primitive("BaseFloat", PrimitiveType.Cylinder, root, new Vector3(0, .45f, 0), new Vector3(1.44f, .45f, 1.44f), red);
            SceneFactory.Primitive("WhiteBand", PrimitiveType.Cylinder, root, new Vector3(0, .82f, 0), new Vector3(1.46f, .08f, 1.46f), white);
            SceneFactory.Primitive("TowerMast", PrimitiveType.Cylinder, root, new Vector3(0, 2.55f, 0), new Vector3(.16f, 1.65f, .16f), dark);
            SceneFactory.Primitive("DaymarkLower", PrimitiveType.Cube, root, new Vector3(0, 2f, 0), new Vector3(.95f, .55f, .08f), red);
            SceneFactory.Primitive("DaymarkUpper", PrimitiveType.Cube, root, new Vector3(0, 3.1f, 0), new Vector3(.08f, .55f, .95f), red);
            SceneFactory.Primitive("TopBeacon", PrimitiveType.Sphere, root, new Vector3(0, 4.35f, 0), Vector3.one * .44f, amber);

            Light light = new GameObject("BuoyLight").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0, 4.35f, 0);
            light.type = LightType.Point;
            light.range = 25f;
            light.intensity = 1.2f;
            light.color = new Color(1f, .78f, .1f);
            return root;
        }

        private Transform BuildTargetVessel()
        {
            // Hostile/neutral freighter look — distinct from orange rescue USVs.
            Transform root = new GameObject("TargetVessel").transform;
            root.position = Coordinates.ToUnity(40f, -20f, .5f);

            Material hull = SceneFactory.Material("Target Hull", new Color(.12f, .14f, .16f), .25f, .38f);
            Material bootTop = SceneFactory.Material("Target BootTop", new Color(.72f, .08f, .05f), .08f, .42f);
            Material deck = SceneFactory.Material("Target Deck", new Color(.55f, .52f, .46f), .05f, .28f);
            Material white = SceneFactory.Material("Target Superstructure", new Color(.93f, .93f, .9f), 0f, .22f);
            Material window = SceneFactory.Material("Target Window", new Color(.05f, .55f, .75f, .82f), .08f, .9f);
            Material funnel = SceneFactory.Material("Target Funnel", new Color(.78f, .12f, .08f), .1f, .4f);
            Material dark = SceneFactory.Material("Target Rig", new Color(.08f, .08f, .09f), .2f, .5f);
            Material crane = SceneFactory.Material("Target Crane", new Color(.75f, .55f, .12f), .15f, .45f);
            Material amber = SceneFactory.Material("Target Beacon", new Color(1f, .55f, .08f), .05f, .85f);

            // Long cargo hull with pointed bow — not the short orange USV silhouette.
            SceneFactory.Primitive("LowerHull", PrimitiveType.Cube, root, new Vector3(-.15f, -.22f, 0f), new Vector3(9.2f, .55f, 2.55f), hull);
            SceneFactory.Primitive("BootTopStripe", PrimitiveType.Cube, root, new Vector3(-.15f, .08f, 0f), new Vector3(9.15f, .08f, 2.62f), bootTop);
            SceneFactory.Primitive("MainDeck", PrimitiveType.Cube, root, new Vector3(-.35f, .28f, 0f), new Vector3(8.4f, .18f, 2.45f), deck);
            SceneFactory.Cone("Bow", root, new Vector3(4.85f, .05f, 0f), .95f, 1.55f, hull, new Vector3(0f, 0f, -90f));
            SceneFactory.Primitive("SternTransom", PrimitiveType.Cube, root, new Vector3(-4.55f, .05f, 0f), new Vector3(.35f, .7f, 2.35f), hull);

            // Raised white bridge / accommodation block aft of midships.
            SceneFactory.Primitive("BridgeHouse", PrimitiveType.Cube, root, new Vector3(-1.8f, 1.05f, 0f), new Vector3(2.6f, 1.35f, 2.05f), white);
            SceneFactory.Primitive("BridgeWing", PrimitiveType.Cube, root, new Vector3(-1.1f, 1.55f, 0f), new Vector3(1.1f, .35f, 2.55f), white);
            SceneFactory.Primitive("BridgeWindowFront", PrimitiveType.Cube, root, new Vector3(-.48f, 1.35f, 0f), new Vector3(.05f, .55f, 1.7f), window);
            SceneFactory.Primitive("BridgeWindowPort", PrimitiveType.Cube, root, new Vector3(-1.8f, 1.3f, 1.05f), new Vector3(1.6f, .4f, .05f), window);
            SceneFactory.Primitive("BridgeWindowStbd", PrimitiveType.Cube, root, new Vector3(-1.8f, 1.3f, -1.05f), new Vector3(1.6f, .4f, .05f), window);
            SceneFactory.Primitive("RadarMast", PrimitiveType.Cylinder, root, new Vector3(-1.6f, 2.55f, 0f), new Vector3(.07f, .85f, .07f), dark);
            SceneFactory.Primitive("RadarDome", PrimitiveType.Sphere, root, new Vector3(-1.6f, 3.45f, 0f), Vector3.one * .32f, white);

            // Cargo hatches + deck crane forward — reads as freighter, not rescue boat.
            SceneFactory.Primitive("HatchForward", PrimitiveType.Cube, root, new Vector3(2.1f, .42f, 0f), new Vector3(2.4f, .12f, 1.7f), dark);
            SceneFactory.Primitive("HatchMid", PrimitiveType.Cube, root, new Vector3(.2f, .42f, 0f), new Vector3(2.1f, .12f, 1.7f), dark);
            SceneFactory.Primitive("CranePedestal", PrimitiveType.Cylinder, root, new Vector3(1.2f, 1.05f, 0f), new Vector3(.22f, .55f, .22f), crane);
            SceneFactory.Primitive("CraneBoom", PrimitiveType.Cube, root, new Vector3(2.35f, 1.55f, 0f), new Vector3(2.6f, .12f, .16f), crane, new Vector3(0f, 0f, -18f));

            SceneFactory.Primitive("Funnel", PrimitiveType.Cylinder, root, new Vector3(-2.85f, 2.15f, 0f), new Vector3(.55f, .75f, .55f), funnel);
            SceneFactory.Primitive("FunnelCap", PrimitiveType.Cylinder, root, new Vector3(-2.85f, 2.95f, 0f), new Vector3(.62f, .08f, .62f), dark);

            Light nav = new GameObject("TargetNavLight").AddComponent<Light>();
            nav.transform.SetParent(root, false);
            nav.transform.localPosition = new Vector3(-1.6f, 3.55f, 0f);
            nav.type = LightType.Point;
            nav.range = 34f;
            nav.intensity = 1.35f;
            nav.color = new Color(1f, .45f, .08f);

            SceneFactory.Primitive("BowBeacon", PrimitiveType.Sphere, root, new Vector3(4.2f, 1.05f, 0f), Vector3.one * .18f, amber);

            BoatWaveMotion wave = root.gameObject.AddComponent<BoatWaveMotion>();
            wave.meanHeight = .5f;
            return root;
        }

        private Transform BuildDrone(Transform deck, out DroneVisual visual, string name = "X500Drone")
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(deck, false);
            root.localPosition = new Vector3(0, .28f, 0);
            root.localScale = Vector3.one * 3f;

            Material carbon = SceneFactory.Material("Carbon", new Color(.035f, .04f, .045f), .35f, .65f);
            Material accent = SceneFactory.Material("DroneAccent", new Color(.9f, .14f, .025f), .15f, .55f);

            List<Transform> rotors = new List<Transform>();

            GameObject bodyPrefab = Resources.Load<GameObject>("PX4/x500_base/meshes/NXP-HGD-CF");
            GameObject motorBasePrefab = Resources.Load<GameObject>("PX4/x500_base/meshes/5010Base");
            GameObject motorBellPrefab = Resources.Load<GameObject>("PX4/x500_base/meshes/5010Bell");
            GameObject propCcwPrefab = Resources.Load<GameObject>("PX4/x500_base/meshes/1345_prop_ccw");
            GameObject propCwPrefab = Resources.Load<GameObject>("PX4/x500_base/meshes/1345_prop_cw");

            Vector3[] motorPositions =
            {
                new Vector3(.174f, .06f, -.174f),
                new Vector3(-.174f, .06f, .174f),
                new Vector3(.174f, .06f, .174f),
                new Vector3(-.174f, .06f, -.174f)
            };

            if (bodyPrefab)
            {
                GameObject body = Instantiate(bodyPrefab, root);
                body.name = "PX4 Official X500 Frame";
                body.transform.localPosition = new Vector3(0, .025f, 0);
                body.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                RemoveImportedCamerasAndLights(body);

                for (int i = 0; i < motorPositions.Length; i++)
                {
                    InstantiatePx4Part(motorBasePrefab, root, "5010 Motor Base " + i, motorPositions[i] + Vector3.down * .028f, Quaternion.Euler(0, -25.8f, 0));
                    InstantiatePx4Part(motorBellPrefab, root, "5010 Motor Bell " + i, motorPositions[i], Quaternion.identity);

                    GameObject propPrefab = i < 2 ? propCcwPrefab : propCwPrefab;
                    Transform rotor = InstantiatePx4Part(propPrefab, root, "PX4 1345 Propeller " + i, motorPositions[i] + Vector3.up * .028f, Quaternion.Euler(-90f, 0, 0));

                    if (rotor)
                    {
                        CenterImportedMesh(rotor, root.TransformPoint(motorPositions[i] + Vector3.up * .028f));
                        rotors.Add(rotor);
                    }
                }
            }
            else
            {
                SceneFactory.Primitive("Body", PrimitiveType.Cube, root, Vector3.zero, new Vector3(.36f, .13f, .27f), carbon);

                for (int i = 0; i < motorPositions.Length; i++)
                {
                    Vector3 corner = new Vector3(Mathf.Sign(motorPositions[i].x) * .32f, 0, Mathf.Sign(motorPositions[i].z) * .32f);

                    SceneFactory.Primitive("Arm" + i, PrimitiveType.Cube, root, corner * .5f, new Vector3(.055f, .055f, .48f), carbon, new Vector3(0, i % 2 == 0 ? 45 : -45, 0));
                    SceneFactory.Primitive("Motor" + i, PrimitiveType.Cylinder, root, corner + Vector3.up * .035f, new Vector3(.09f, .06f, .09f), accent);

                    Transform rotor = SceneFactory.Primitive("Rotor" + i, PrimitiveType.Cube, root, corner + Vector3.up * .09f, new Vector3(.62f, .012f, .035f), carbon).transform;
                    rotors.Add(rotor);
                }
            }

            visual = root.gameObject.AddComponent<DroneVisual>();
            visual.rotors = rotors.ToArray();

            return root;
        }

        private static Transform InstantiatePx4Part(GameObject prefab, Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            if (!prefab)
                return null;

            GameObject instance = Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;

            RemoveImportedCamerasAndLights(instance);
            return instance.transform;
        }

        private static void RemoveImportedCamerasAndLights(GameObject instance)
        {
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
                Destroy(camera.gameObject);

            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
                Destroy(light.gameObject);
        }

        private static void CenterImportedMesh(Transform instance, Vector3 targetWorldPosition)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            instance.position += targetWorldPosition - bounds.center;
        }

        private void BuildCamera(
            Transform target,
            Transform companion,
            Transform lighthouse,
            IReadOnlyList<Transform> boats,
            IReadOnlyList<Transform> drones,
            Transform targetVessel)
        {
            GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 900f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            ChaseCamera chase = go.AddComponent<ChaseCamera>();
            chase.target = target;
            chase.companion = companion;
            chase.lookAt = lighthouse;
            chase.distance = 10.5f;
            chase.height = 4.6f;
            chase.sideOffset = 0f;
            chase.minDistance = 10.5f;
            chase.maxDistance = 32f;
            chase.minHeight = 4.6f;
            chase.maxHeight = 12f;
            chase.lookHeight = 1.6f;
            chase.lighthouseInfluence = .1f;
            chase.useTargetRightAsForward = true;

            var cameraTargets = new List<Transform>();
            if (boats != null)
            {
                for (int i = 0; i < boats.Count; i++)
                {
                    if (boats[i])
                        cameraTargets.Add(boats[i]);
                }
            }
            if (drones != null)
            {
                for (int i = 0; i < drones.Count; i++)
                {
                    if (drones[i])
                        cameraTargets.Add(drones[i]);
                }
            }
            if (targetVessel)
                cameraTargets.Add(targetVessel);
            chase.SetGroupTargets(cameraTargets.ToArray());
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
                fontSize = 14,
                normal = { textColor = new Color(.9f, .96f, 1f) }
            };

            string syncStatus = webSocketReceiver
                ? webSocketReceiver.connectionStatus
                : externalReceiver ? externalReceiver.connectionStatus : null;
            bool localMultiAgent = syncStatus == null && multiAgentScenario && multiAgentScenario.enabled;
            float panelY = localMultiAgent ? 280f : 16f;

            GUI.Box(new Rect(16, panelY, 360, 142), "");
            GUI.Label(new Rect(30, panelY + 11, 330, 28), "UAV-USV Maritime Simulation", titleStyle);

            string status = syncStatus != null
                ? "External sync: " + syncStatus
                : localMultiAgent
                    ? "Mission phase: " + multiAgentScenario.Status
                    : "Mission phase: " + (mission ? mission.Status : "Initializing");

            string controls = syncStatus != null
                ? "ROS/Gazebo authoritative motion\n"
                : localMultiAgent
                    ? "M: pause  R: reset  B: force dispatch\n"
                    : "SPACE: launch mission    R: reset\n";

            string bridgeText = syncStatus != null
                ? (webSocketReceiver ? "WebSocket: " + webSocketUrl : "UDP pose: 14582") + "\nCoordinate: Gazebo ENU -> Unity"
                : "ROS UDP: " + (bridge && bridge.enabledBridge ? bridge.lastPacket : "off, use --ros-udp to enable");

            GUI.Label(
                new Rect(30, panelY + 44, 330, 84),
                status + "\n" +
                controls +
                "Default view: readable action + tactical overview\n" +
                bridgeText,
                bodyStyle
            );
        }
    }

    public sealed class BeaconSweep : MonoBehaviour
    {
        private void Update()
        {
            transform.Rotate(Vector3.up, 18f * Time.deltaTime, Space.World);
        }
    }

    public sealed class CrossingBarrierMotion : MonoBehaviour
    {
        public float amplitude = 14f;
        public float passDuration = 8f;
        public int maxPasses = 2;
        private Vector3 start;
        private Vector3 pointA;
        private Vector3 pointB;
        private float startedAt;
        private bool parked;

        private void Start()
        {
            start = transform.position;
            pointA = start + new Vector3(-amplitude, 0f, 0f);
            pointB = start + new Vector3(amplitude, 0f, amplitude * .35f);
            // Initialize on the path before the first rendered frame.
            transform.position = pointA;
            startedAt = Time.time;
        }

        private void Update()
        {
            if (parked)
                return;

            float elapsed = Time.time - startedAt;
            float total = Mathf.Max(.5f, passDuration) * Mathf.Max(1, maxPasses);
            if (elapsed >= total)
            {
                transform.position = (Mathf.Max(1, maxPasses) % 2) == 1
                    ? pointB
                    : pointA;
                parked = true;
                enabled = false;
                return;
            }

            float local = elapsed % Mathf.Max(.5f, passDuration);
            float u = Mathf.Clamp01(local / Mathf.Max(.5f, passDuration));
            int pass = Mathf.FloorToInt(elapsed / Mathf.Max(.5f, passDuration));
            bool reverse = (pass % 2) == 1;
            transform.position = Vector3.Lerp(
                reverse ? pointB : pointA,
                reverse ? pointA : pointB,
                u
            );
        }
    }
}
