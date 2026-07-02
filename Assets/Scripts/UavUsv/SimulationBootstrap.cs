using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class SimulationBootstrap : MonoBehaviour
    {
        [Tooltip("Use external ROS/Gazebo pose data in Unity Editor Play mode.")]
        public bool useExternalPoseInEditor = true;

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

        private CooperativeMission mission;
        private RosUdpBridge bridge;
        private ExternalPoseReceiver externalReceiver;
        private ExternalPoseWebSocketClient webSocketReceiver;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.56f, .7f, .82f);
            RenderSettings.fogDensity = .0012f;

            BuildLighting();
            BuildOcean();

            Transform lighthouse = BuildLighthouse();
            Transform boat = BuildBoat();
            Transform deck = boat.Find("LandingDeck");
            Transform drone = BuildDrone(deck, out DroneVisual droneVisual);
            Transform buoyWest = BuildBuoy("WestChannelBuoy", -42f, 44f, .25f);
            Transform buoySouth = BuildBuoy("SouthChannelBuoy", 34f, -56f, -.35f);
            Transform buoyEast = BuildBuoy("EastChannelBuoy", 78f, 28f, .7f);
            Transform targetVessel = BuildTargetVessel();
            targetVessel.gameObject.SetActive(false);

            mission = gameObject.AddComponent<CooperativeMission>();
            mission.boat = boat;
            mission.deck = deck;
            mission.drone = drone;
            mission.droneVisual = droneVisual;
            mission.cruiseWithDroneOnDeck = false;

            if (Application.isEditor)
                mission.automatic = playMissionInEditor;

            string urlArgument = GetArgumentValue("--ros-ws-url=");
            if (!string.IsNullOrEmpty(urlArgument))
                webSocketUrl = urlArgument;

            string platformUrlArgument = GetArgumentValue("--platform-url=");
            if (!string.IsNullOrEmpty(platformUrlArgument))
                platformUrl = platformUrlArgument;
            string platformTokenArgument = GetArgumentValue("--platform-token=");
            if (!string.IsNullOrEmpty(platformTokenArgument))
                platformToken = platformTokenArgument;

            bool externalSync = HasArgument("--ros-sync") || HasArgument("--ros-ws") || (Application.isEditor && useExternalPoseInEditor);
            if (externalSync)
            {
                mission.automatic = false;
                mission.enabled = false;

                BoatWaveMotion boatWave = boat.GetComponent<BoatWaveMotion>();
                if (boatWave)
                    boatWave.enabled = false;

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
                bridge = gameObject.AddComponent<RosUdpBridge>();
                bridge.mission = mission;
                bridge.enabledBridge = HasArgument("--ros-udp");
            }

            BuildCamera(boat, drone, lighthouse);
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
            var ocean = new GameObject("Ocean") { layer = 4 };
            ocean.AddComponent<MeshFilter>();

            MeshRenderer renderer = ocean.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ocean.AddComponent<OceanSurface>();
            ocean.AddComponent<PlanarWaterReflection>();
        }

        private Transform BuildLighthouse()
        {
            Transform root = new GameObject("NavigationLighthouse").transform;
            root.position = Coordinates.ToUnity(35, 18, 0);

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

        private Transform BuildBoat()
        {
            Transform root = new GameObject("LandingBoat").transform;
            root.position = Coordinates.ToUnity(0, 0, .42f);

            Material dark = SceneFactory.Material("HullDark", new Color(.06f, .07f, .08f), .2f, .45f);
            Material orange = SceneFactory.Material("RescueOrange", new Color(.95f, .24f, .035f), .12f, .42f);
            Material deckMat = SceneFactory.Material("Deck", new Color(.17f, .17f, .16f));
            Material cabin = SceneFactory.Material("Cabin", new Color(.92f, .9f, .78f));
            Material window = SceneFactory.Material("Window", new Color(.04f, .26f, .48f, .72f), .1f, .9f);
            Material white = SceneFactory.Material("PadWhite", Color.white, 0, .25f);

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
            Transform root = new GameObject("TargetVessel").transform;

            Material hull = SceneFactory.Material("Target Hull", new Color(.06f, .2f, .36f), .15f, .42f);
            Material upper = SceneFactory.Material("Target Upper", new Color(.88f, .88f, .82f), 0, .25f);
            Material window = SceneFactory.Material("Target Window", new Color(.04f, .28f, .46f), .1f, .85f);
            Material dark = SceneFactory.Material("Target Mast", new Color(.1f, .1f, .1f), .15f, .4f);

            SceneFactory.Primitive("LowerHull", PrimitiveType.Cube, root, new Vector3(-.2f, -.2f, 0), new Vector3(6.7f, .55f, 2.3f), hull);
            SceneFactory.Primitive("UpperHull", PrimitiveType.Cube, root, new Vector3(-.35f, .18f, 0), new Vector3(6.2f, .35f, 2.4f), upper);
            SceneFactory.Primitive("Cabin", PrimitiveType.Cube, root, new Vector3(-.4f, .85f, 0), new Vector3(2.1f, 1.25f, 1.65f), upper);
            SceneFactory.Primitive("BridgeWindow", PrimitiveType.Cube, root, new Vector3(.68f, 1f, 0), new Vector3(.04f, .55f, 1.35f), window);
            SceneFactory.Primitive("Mast", PrimitiveType.Cylinder, root, new Vector3(-.6f, 2.1f, 0), new Vector3(.12f, 1.1f, .12f), dark);

            Light navigation = new GameObject("NavigationLight").AddComponent<Light>();
            navigation.transform.SetParent(root, false);
            navigation.transform.localPosition = new Vector3(-.6f, 3.25f, 0);
            navigation.type = LightType.Point;
            navigation.range = 30f;
            navigation.intensity = 1.2f;
            navigation.color = new Color(.2f, 1f, .35f);
            return root;
        }

        private Transform BuildDrone(Transform deck, out DroneVisual visual)
        {
            Transform root = new GameObject("X500Drone").transform;
            root.SetParent(deck, false);
            root.localPosition = new Vector3(0, .28f, 0);
            root.localScale = Vector3.one * 1.18f;

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

        private void BuildCamera(Transform target, Transform companion, Transform lighthouse)
        {
            GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 58f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 500f;
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

            GUI.Box(new Rect(16, 16, 360, 142), "");
            GUI.Label(new Rect(30, 27, 330, 28), "UAV-USV Maritime Simulation", titleStyle);

            string syncStatus = webSocketReceiver
                ? webSocketReceiver.connectionStatus
                : externalReceiver ? externalReceiver.connectionStatus : null;

            string status = syncStatus != null
                ? "External sync: " + syncStatus
                : "Mission phase: " + (mission ? mission.Status : "Initializing");

            string controls = syncStatus != null
                ? "Motion driven by ROS/Gazebo pose data\n"
                : "SPACE: launch mission    R: reset\n";

            string bridgeText = syncStatus != null
                ? (webSocketReceiver ? "WebSocket: " + webSocketUrl : "UDP pose: 14582") + "\nCoordinate: Gazebo ENU -> Unity"
                : "ROS UDP: " + (bridge && bridge.enabledBridge ? bridge.lastPacket : "off, use --ros-udp to enable");

            GUI.Label(
                new Rect(30, 60, 330, 84),
                status + "\n" +
                controls +
                "Default view: chase camera behind the boat\n" +
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
}
