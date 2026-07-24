using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Builds the ROS/Gazebo heterogeneous_332 world used by current main.
    /// Motion remains authoritative in Gazebo and arrives through WebSocket.
    /// </summary>
    public sealed class SimulationBootstrap : MonoBehaviour
    {
        public bool useExternalPoseInEditor = true;
        public bool useWebSocketPose = true;
        public string webSocketUrl = "ws://127.0.0.1:8765/uav_usv";

        private ExternalPoseWebSocketClient receiver;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        private static readonly Color GazeboGrey = new Color(.36f, .37f, .38f);
        private static readonly Color GazeboDark = new Color(.075f, .08f, .085f);

        private void Awake()
        {
            Application.targetFrameRate = 60;
            ConfigureVisualQuality();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.46f, .54f, .56f);
            RenderSettings.fogDensity = .00105f;

            BuildLighting();
            BuildOcean();
            CatalinaIslandRuntime.CreateVisualTerrain(
                Enu(0f, 0f, -.8f)
            );
            // Green coastal rim (trees + greensward) outside the ROS ops area.
            SydneyCoastRuntime.CreateVisualBackdrop(Vector3.zero);
            BuildIslandUavBase();
            BuildShoreCommandBase();

            Vector3[] usvPos =
            {
                Enu(-120f, -305f, 0f),
                Enu(-75f, -320f, 0f),
                Enu(-30f, -305f, 0f)
            };
            float[] usvYaw = { .10f, .05f, -.05f };
            Color[] usvIds =
            {
                new Color(.02f, .2f, .9f),
                new Color(.03f, .78f, .18f),
                new Color(.02f, .82f, .9f)
            };

            var usvs = new Transform[3];
            for (int i = 0; i < usvs.Length; i++)
            {
                usvs[i] = BuildUsv("usv_0" + (i + 1), usvIds[i]);
                Place(usvs[i], usvPos[i], usvYaw[i]);
            }

            Vector3[] uavPos =
            {
                Enu(-86.86f, -222.43f, 19.75f),
                Enu(-75f, -215f, 19.75f),
                Enu(-63.14f, -207.57f, 19.75f)
            };
            var uavs = new Transform[3];
            for (int i = 0; i < uavs.Length; i++)
            {
                uavs[i] = BuildUav("uav_0" + (i + 1));
                Place(uavs[i], uavPos[i], .559f);
            }

            Transform friendly = BuildFriendlyShip();
            Place(friendly, Enu(-150f, -355f, 0f), .25f);
            Transform enemy = BuildEnemyShip();
            Place(enemy, Enu(-80f, -315f, 0f), 2.60f);

            bool externalSync =
                !HasArgument("--local-demo") &&
                (
                    HasArgument("--ros-sync") ||
                    HasArgument("--ros-ws") ||
                    (Application.isEditor && useExternalPoseInEditor)
                );
            if (externalSync && useWebSocketPose)
            {
                receiver = gameObject.AddComponent<ExternalPoseWebSocketClient>();
                receiver.serverUrl = ArgumentValue("--ros-ws-url=", webSocketUrl);
                receiver.boat = usvs[0];
                receiver.drone = uavs[0];
                receiver.boats = usvs;
                receiver.drones = uavs;
                receiver.friendlyShip = friendly;
                receiver.targetVessel = enemy;
            }
            else
            {
                // Local demo only: Gazebo wave follower is authoritative in ROS sync.
                foreach (Transform usv in usvs)
                    usv.gameObject.AddComponent<BoatWaveMotion>();
                friendly.gameObject.AddComponent<BoatWaveMotion>();
                enemy.gameObject.AddComponent<BoatWaveMotion>();
            }

            BuildCamera(usvs, uavs, friendly, enemy);
        }

        private static void ConfigureVisualQuality()
        {
            int highestQuality = QualitySettings.names.Length - 1;
            if (highestQuality >= 0 && QualitySettings.GetQualityLevel() < highestQuality)
                QualitySettings.SetQualityLevel(highestQuality, true);

            QualitySettings.antiAliasing = Application.platform == RuntimePlatform.WebGLPlayer ? 4 : 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 720f;
            QualitySettings.shadowNearPlaneOffset = 2f;
            QualitySettings.softParticles = true;
            QualitySettings.realtimeReflectionProbes = true;
            QualitySettings.lodBias = 2f;
        }

        private static void BuildLighting()
        {
            Material skyTemplate =
                Resources.Load<Material>("Sky/PureOceanSky");
            Material sky = skyTemplate ? new Material(skyTemplate) : null;
            if (!sky)
            {
                Shader skyShader = Resources.Load<Shader>("MaritimeSky") ??
                                   Shader.Find("UavUsv/MaritimeSky");
                if (skyShader)
                {
                    sky = new Material(skyShader)
                    {
                        name = "Runtime Maritime Sky"
                    };
                    sky.SetFloat("_CloudSpeed", .012f);
                    sky.SetFloat("_CloudAmount", .62f);
                    sky.SetFloat("_Exposure", 1.15f);
                }
            }

            if (sky)
            {
                sky.name = "heterogeneous_332 Maritime Sky";
                if (sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", 1.08f);
                if (sky.HasProperty("_Rotation"))
                    sky.SetFloat("_Rotation", 72f);
                if (sky.HasProperty("_CloudAmount"))
                    sky.SetFloat("_CloudAmount", .48f);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = .92f;
                RenderSettings.defaultReflectionMode =
                    UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                RenderSettings.defaultReflectionResolution = 256;
                RenderSettings.reflectionIntensity = .82f;
                RenderSettings.reflectionBounces = 1;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.68f, .78f, .88f);
                RenderSettings.ambientEquatorColor = new Color(.42f, .52f, .58f);
                RenderSettings.ambientGroundColor = new Color(.16f, .20f, .22f);
            }

            // Direction matches Gazebo sun: direction -0.35 0.2 -0.92
            Light sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(.96f, .94f, .88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .78f;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .25f;
            sun.renderMode = LightRenderMode.ForcePixel;
            sun.transform.rotation = Quaternion.LookRotation(
                new Vector3(-.35f, -.92f, .20f).normalized
            );
            RenderSettings.sun = sun;
        }

        private static void BuildOcean()
        {
            var water = new GameObject(
                "ocean_plane",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(OceanSurface),
                typeof(PlanarWaterReflection)
            );
            water.layer = 4;
            // Match Gazebo waves pose z=0.015.
            water.transform.position = new Vector3(0f, .015f, 0f);
            MeshRenderer renderer = water.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            OceanSurface ocean = water.GetComponent<OceanSurface>();
            // Visual ocean is larger than the Gazebo 1050 x 900 ops domain so it
            // runs under the green coastal rim instead of leaving sky gaps.
            ocean.width = 1480f;
            ocean.length = 1360f;
            ocean.resolution = 340;
            ocean.edgeIrregularity = 0f;
            ocean.waveAmplitude = .68f;
            ocean.windSpeed = 9.2f;
            ocean.windDirectionDegrees = 35f;

            PlanarWaterReflection reflection =
                water.GetComponent<PlanarWaterReflection>();
            reflection.resolutionScale = .55f;
            reflection.clipPlaneOffset = .04f;
        }

        private static Transform[] BuildIslandUavBase()
        {
            Transform root = new GameObject("island_uav_base").transform;
            Place(root, Enu(-75f, -215f, 0f), .559f);

            Material foundation = Mat("Base foundation", new Color(.28f, .3f, .31f));
            Material deck = Mat("Base flight deck", new Color(.19f, .2f, .21f));
            Material legs = Mat("Base columns", new Color(.32f, .34f, .35f), .25f);
            Material pad = Mat("Landing pads", new Color(.34f, .35f, .35f));
            Material white = Mat("Landing H", Color.white);
            Material edge = Mat("Safety yellow", new Color(.98f, .75f, .04f));

            Box("foundation", root, 0f, 0f, 1.5f, 44f, 15f, 3f, foundation);
            Box("flight_deck", root, 0f, 0f, 19f, 44f, 15f, 1f, deck);
            foreach (float x in new[] { -18f, 0f, 18f })
            foreach (float y in new[] { -5f, 5f })
                Box("support_column", root, x, y, 10.6f, 1.2f, 1.2f, 16.8f, legs);

            var pads = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                float x = -14f + 14f * i;
                Transform p = new GameObject("pad_0" + (i + 1)).transform;
                p.SetParent(root, false);
                p.localPosition = LocalEnu(x, 0f, 19.54f);
                pads[i] = p;
                Cylinder("pad_surface", p, 0f, 0f, 0f, 5.5f, .08f, pad);
                // Match Gazebo sim332_island_uav_base H marks (two uprights + cross).
                Box("H_left", p, -1.8f, 0f, .06f, .8f, 5f, .08f, white);
                Box("H_right", p, 1.8f, 0f, .06f, .8f, 5f, .08f, white);
                Box("H_cross", p, 0f, 0f, .07f, 3.6f, .8f, .08f, white);
                Cylinder("yellow_edge", p, 0f, 0f, .05f, 5.6f, .035f, edge);
            }
            return pads;
        }

        private static void BuildShoreCommandBase()
        {
            Transform root = new GameObject("shore_command_base").transform;
            // Gazebo pose is z=17.5; Unity Catalina is height-compressed, so sit
            // the visual command base lower to match the shoreline silhouette.
            Place(root, Enu(-35f, -190f, 9.5f), .559f);

            Material concrete = Mat("Command concrete", new Color(.48f, .47f, .44f));
            Material wall = Mat("Command building", new Color(.74f, .7f, .6f));
            Material roof = Mat("Command roof", new Color(.16f, .18f, .2f));
            Material glass = Mat("Command windows", new Color(.035f, .23f, .34f), .15f, .82f);
            Material metal = Mat("Command metal", new Color(.32f, .34f, .35f), .35f);
            Material yellow = Mat("Command safety rail", new Color(.98f, .75f, .04f));

            Box("foundation", root, 0f, 0f, .5f, 28f, 20f, 1f, concrete);
            Box("building", root, 0f, 0f, 4.2f, 20f, 14f, 7.2f, wall);
            Box("roof", root, 0f, 0f, 8f, 22f, 16f, .5f, roof);
            Box("front_windows", root, 10.04f, 0f, 5.1f, .08f, 9f, 2.2f, glass);
            Box("bridge_to_pad", root, -24f, 0f, -1.5f, 50f, 5f, .5f, concrete, 0f, -.06f, 0f);
            Box("bridge_rail_left", root, -24f, 2.35f, -.65f, 50f, .12f, 1.2f, yellow, 0f, -.06f, 0f);
            Box("bridge_rail_right", root, -24f, -2.35f, -.65f, 50f, .12f, 1.2f, yellow, 0f, -.06f, 0f);
            Cylinder("radar_tower", root, 3f, 0f, 13f, .5f, 10f, metal);
            Cylinder("radar_pedestal", root, 3f, 0f, 18.2f, 1.2f, .5f, metal);
            SceneFactory.Primitive(
                "radar_dish",
                PrimitiveType.Sphere,
                root,
                LocalEnu(3f, 0f, 19.2f),
                new Vector3(3.8f, .45f, 2.3f),
                metal,
                new Vector3(0f, 0f, -22f)
            );
            Cylinder("antenna_mast", root, -5f, 3f, 13.5f, .18f, 10f, metal);
            Box("generator", root, -6f, -5f, 9.1f, 3.8f, 2.6f, 1.8f, roof);
            Box("equipment_console", root, 5f, -4.8f, 9f, 3f, 1.5f, 1.6f, metal);
        }

        private static Transform BuildUsv(string name, Color idColor)
        {
            Transform root = new GameObject(name).transform;
            // Gazebo defense/sim332 USV hull is about 9.0 x 3.4 m.
            root.localScale = Vector3.one * 3.65f;
            Material hull = Mat(name + " hull", new Color(.035f, .05f, .065f), .28f, .55f);
            Material upper = Mat(name + " upper hull", new Color(.86f, .88f, .84f), .04f, .38f);
            Material deck = Mat(name + " deck", new Color(.12f, .14f, .15f), .12f, .42f);
            Material cabin = Mat(name + " cabin", new Color(.94f, .92f, .82f), 0f, .32f);
            Material window = Mat(name + " windows", new Color(.03f, .30f, .48f, .86f), .18f, .94f);
            Material white = Mat(name + " landing marks", Color.white, 0f, .35f);
            Material id = Mat(
                name + " ID " + ColorUtility.ToHtmlStringRGB(idColor),
                idColor,
                .12f,
                .55f
            );

            SceneFactory.Primitive("lower_hull", PrimitiveType.Cube, root, new Vector3(-.05f, -.16f, 0f), new Vector3(2.45f, .28f, .82f), hull);
            SceneFactory.Primitive("upper_hull", PrimitiveType.Cube, root, new Vector3(-.1f, .05f, 0f), new Vector3(2.35f, .32f, .94f), upper);
            SceneFactory.Cone("bow", root, new Vector3(1.3f, .01f, 0f), .49f, .72f, upper, new Vector3(0f, 0f, -90f));
            SceneFactory.Primitive("main_deck", PrimitiveType.Cube, root, new Vector3(-.15f, .25f, 0f), new Vector3(2.05f, .08f, .78f), deck);
            SceneFactory.Primitive("cabin", PrimitiveType.Cube, root, new Vector3(.2f, .58f, 0f), new Vector3(.78f, .48f, .62f), cabin);
            SceneFactory.Primitive("front_window", PrimitiveType.Cube, root, new Vector3(.6f, .64f, 0f), new Vector3(.035f, .24f, .48f), window);
            SceneFactory.Primitive("port_window", PrimitiveType.Cube, root, new Vector3(.2f, .64f, .325f), new Vector3(.45f, .22f, .025f), window);
            SceneFactory.Primitive("starboard_window", PrimitiveType.Cube, root, new Vector3(.2f, .64f, -.325f), new Vector3(.45f, .22f, .025f), window);
            SceneFactory.Primitive("cabin_roof", PrimitiveType.Cube, root, new Vector3(.15f, .85f, 0f), new Vector3(.92f, .08f, .72f), deck);
            // Match sim332 identity bands (~4.5 m long on a 9 m hull).
            SceneFactory.Primitive("port_id_band", PrimitiveType.Cube, root, new Vector3(0f, .12f, .50f), new Vector3(1.25f, .14f, .035f), id);
            SceneFactory.Primitive("starboard_id_band", PrimitiveType.Cube, root, new Vector3(0f, .12f, -.50f), new Vector3(1.25f, .14f, .035f), id);
            SceneFactory.Primitive("roof_id", PrimitiveType.Cube, root, new Vector3(-.15f, .92f, 0f), new Vector3(.42f, .03f, .42f), id);

            Transform landingDeck = new GameObject("landing_deck").transform;
            landingDeck.SetParent(root, false);
            landingDeck.localPosition = new Vector3(-.92f, .43f, 0f);
            SceneFactory.Primitive("pad", PrimitiveType.Cube, landingDeck, Vector3.zero, new Vector3(1.15f, .08f, 1.15f), deck);
            SceneFactory.Primitive("ring", PrimitiveType.Cylinder, landingDeck, new Vector3(0f, .048f, 0f), new Vector3(.96f, .012f, .96f), white);
            SceneFactory.Primitive("center", PrimitiveType.Cylinder, landingDeck, new Vector3(0f, .056f, 0f), new Vector3(.72f, .014f, .72f), deck);
            SceneFactory.Primitive("landing_h_x", PrimitiveType.Cube, landingDeck, new Vector3(0f, .068f, 0f), new Vector3(.48f, .012f, .08f), white);
            SceneFactory.Primitive("landing_h_z", PrimitiveType.Cube, landingDeck, new Vector3(0f, .07f, 0f), new Vector3(.08f, .012f, .48f), white);
            SceneFactory.Primitive("sensor_mast", PrimitiveType.Cylinder, root, new Vector3(-.05f, 1.1f, 0f), new Vector3(.036f, .275f, .036f), deck);
            SceneFactory.Primitive("mid360", PrimitiveType.Cylinder, root, new Vector3(-.05f, 1.42f, 0f), new Vector3(.13f, .07f, .13f), id);
            return root;
        }

        private static Transform BuildFriendlyShip()
        {
            Transform root = new GameObject("friendly_ship").transform;
            Material red = Mat("Friendly red", new Color(.82f, .035f, .02f), .08f);
            Material yellow = Mat("Friendly yellow", new Color(.98f, .65f, .025f));
            Material dark = Mat("Friendly dark", GazeboDark, .2f);
            Material glass = Mat("Friendly windows", new Color(.025f, .16f, .22f), .15f, .88f);

            Box("lower_hull", root, -.3f, 0f, .25f, 13.5f, 4.7f, 1f, red);
            Box("upper_hull", root, -.6f, 0f, .85f, 12.4f, 5.1f, .55f, yellow);
            Box("main_deck", root, -.7f, 0f, 1.18f, 11.5f, 4.3f, .18f, dark);
            Box("cabin", root, -2.1f, 0f, 2.25f, 4.2f, 3.55f, 1.9f, yellow);
            Box("bridge_windows", root, .03f, 0f, 2.55f, .08f, 2.9f, .65f, glass);
            Box("roof", root, -2.2f, 0f, 3.3f, 4.8f, 4f, .22f, red);
            Cylinder("mast", root, -2.1f, 0f, 4.65f, .12f, 2.6f, dark);
            Box("radar", root, -2.1f, 0f, 5.75f, 2.2f, .18f, .12f, yellow);
            Box("port_stripe", root, -.2f, 2.42f, .92f, 9.8f, .08f, .35f, yellow);
            Box("starboard_stripe", root, -.2f, -2.42f, .92f, 9.8f, .08f, .35f, yellow);
            return root;
        }

        private static Transform BuildEnemyShip()
        {
            Transform root = new GameObject("enemy_ship").transform;
            Material hull = Mat("Enemy hull", new Color(.035f, .04f, .045f), .22f, .48f);
            Material deck = Mat("Enemy deck", new Color(.06f, .065f, .07f), .08f, .32f);
            Material white = Mat("Enemy identification", new Color(.92f, .92f, .88f), 0f, .28f);
            Material red = Mat("Enemy hostile panel", new Color(.82f, .025f, .02f), .1f, .42f);
            Material glass = Mat("Enemy windows", new Color(.04f, .16f, .22f), .18f, .92f);

            Box("lower_hull", root, -.2f, 0f, .35f, 8.8f, 3.2f, .8f, hull);
            Box("base_deck", root, -.5f, 0f, .95f, 7.4f, 3f, .25f, deck);
            Box("black_deck", root, -.25f, 0f, 1.12f, 7.1f, 2.85f, .12f, hull);
            Box("cabin", root, -1.5f, 0f, 1.75f, 2.4f, 2.2f, 1.5f, white);
            Box("cabin_windows", root, -.27f, 0f, 2f, .05f, 1.7f, .52f, glass);
            Box("hostile_roof", root, -1.1f, 0f, 2.62f, 1.5f, 2.25f, .2f, red);
            Box("port_white_band", root, 0f, 1.64f, .52f, 6.8f, .08f, .55f, white);
            Box("starboard_white_band", root, 0f, -1.64f, .52f, 6.8f, .08f, .55f, white);
            Box("bow_left", root, 4.3f, .8f, .7f, 1.8f, .3f, 1f, hull, 0f, 0f, .42f);
            Box("bow_right", root, 4.3f, -.8f, .7f, 1.8f, .3f, 1f, hull, 0f, 0f, -.42f);
            return root;
        }

        private static Transform BuildUav(string name)
        {
            Transform root = new GameObject(name).transform;
            root.localScale = Vector3.one * 3f;
            Material carbon = Mat(name + " carbon", new Color(.025f, .03f, .035f), .35f, .65f);
            Material accent = Mat(name + " red", new Color(.88f, .08f, .02f), .1f, .48f);
            var rotors = new List<Transform>();

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
                body.name = "x500_base";
                body.transform.localPosition = new Vector3(0f, .025f, 0f);
                body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                RemoveImportedCamerasAndLights(body);

                for (int i = 0; i < motorPositions.Length; i++)
                {
                    InstantiatePx4Part(
                        motorBasePrefab,
                        root,
                        "5010_motor_base_" + i,
                        motorPositions[i] + Vector3.down * .028f,
                        Quaternion.Euler(0f, -25.8f, 0f)
                    );
                    InstantiatePx4Part(
                        motorBellPrefab,
                        root,
                        "5010_motor_bell_" + i,
                        motorPositions[i],
                        Quaternion.identity
                    );

                    GameObject propPrefab = i < 2
                        ? propCcwPrefab
                        : propCwPrefab;
                    Transform rotor = InstantiatePx4Part(
                        propPrefab,
                        root,
                        "x500_propeller_" + i,
                        motorPositions[i] + Vector3.up * .028f,
                        Quaternion.Euler(-90f, 0f, 0f)
                    );
                    if (rotor)
                    {
                        CenterImportedMesh(
                            rotor,
                            root.TransformPoint(
                                motorPositions[i] + Vector3.up * .028f
                            )
                        );
                        rotors.Add(rotor);
                    }
                }
            }
            else
            {
                SceneFactory.Primitive("body", PrimitiveType.Cube, root, Vector3.zero, new Vector3(.36f, .13f, .27f), carbon);
                for (int i = 0; i < motorPositions.Length; i++)
                {
                    Vector3 corner = new Vector3(
                        Mathf.Sign(motorPositions[i].x) * .32f,
                        0f,
                        Mathf.Sign(motorPositions[i].z) * .32f
                    );
                    SceneFactory.Primitive(
                        "arm_" + i,
                        PrimitiveType.Cube,
                        root,
                        corner * .5f,
                        new Vector3(.055f, .045f, .48f),
                        carbon,
                        new Vector3(0f, i % 2 == 0 ? 45f : -45f, 0f)
                    );
                    SceneFactory.Primitive(
                        "motor_" + i,
                        PrimitiveType.Cylinder,
                        root,
                        corner,
                        new Vector3(.09f, .06f, .09f),
                        accent
                    );
                    rotors.Add(
                        SceneFactory.Primitive(
                            "rotor_" + i,
                            PrimitiveType.Cube,
                            root,
                            corner + Vector3.up * .07f,
                            new Vector3(.64f, .012f, .035f),
                            carbon
                        ).transform
                    );
                }
            }
            DroneVisual visual = root.gameObject.AddComponent<DroneVisual>();
            visual.rotors = rotors.ToArray();
            visual.spinning = true;
            return root;
        }

        private static Transform InstantiatePx4Part(
            GameObject prefab,
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
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

        private static void CenterImportedMesh(
            Transform instance,
            Vector3 targetWorldPosition)
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
            Transform[] usvs,
            Transform[] uavs,
            Transform friendly,
            Transform enemy)
        {
            GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.nearClipPlane = .2f;
            camera.farClipPlane = 1800f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(.42f, .62f, .74f);
            // Gazebo GUI camera_pose: -430 -560 420 with pitch/yaw toward the fleet.
            go.transform.position = Enu(-430f, -560f, 420f);
            go.transform.LookAt(Enu(-75f, -280f, 8f));

            ChaseCamera chase = go.AddComponent<ChaseCamera>();
            chase.target = usvs[0];
            chase.companion = uavs[0];
            chase.lookAt = enemy;
            chase.showTacticalInset = false;
            chase.actionYaw = -42f;
            chase.actionPitch = 28f;
            chase.actionFitPadding = 1.12f;
            chase.actionMinDistance = 55f;
            chase.actionMaxDistance = 280f;
            chase.actionSecondBoatRadius = 75f;
            chase.actionAllBoatsRadius = 65f;
            chase.overviewMinDistance = 120f;
            chase.overviewMaxDistance = 620f;
            var subjects = new List<Transform>();
            subjects.AddRange(usvs);
            subjects.AddRange(uavs);
            subjects.Add(friendly);
            subjects.Add(enemy);
            chase.SetGroupTargets(subjects.ToArray());

            SensorViewPip pip = go.AddComponent<SensorViewPip>();
            pip.poseClient = receiver;
            pip.usvs = usvs;
            pip.uavs = uavs;
            pip.lookAt = enemy;
            pip.visible = true;
            pip.preferGazeboStream = true;
            pip.activeView = SensorViewPip.SensorView.Usv01Forward;
        }

        private void OnGUI()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

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

            GUI.Box(new Rect(16f, 16f, 470f, 168f), "");
            GUI.Label(new Rect(30f, 27f, 430f, 28f), "UAV-USV · heterogeneous_332", titleStyle);
            string status = receiver
                ? receiver.connectionStatus + "\n" + receiver.fleetStatus + "\n" +
                  receiver.missionStatus + "\n" + receiver.cameraStatus
                : "ROS synchronization is disabled";
            GUI.Label(new Rect(30f, 60f, 440f, 110f), status, bodyStyle);
        }

        private static Material Mat(
            string name,
            Color color,
            float metallic = 0f,
            float smoothness = .35f)
        {
            return SceneFactory.Material(name, color, metallic, smoothness);
        }

        private static GameObject Box(
            string name,
            Transform parent,
            float x,
            float y,
            float z,
            float sx,
            float sy,
            float sz,
            Material material,
            float roll = 0f,
            float pitch = 0f,
            float yaw = 0f)
        {
            return SceneFactory.Primitive(
                name,
                PrimitiveType.Cube,
                parent,
                LocalEnu(x, y, z),
                new Vector3(sx, sz, sy),
                material,
                new Vector3(-pitch * Mathf.Rad2Deg, -yaw * Mathf.Rad2Deg, roll * Mathf.Rad2Deg)
            );
        }

        private static GameObject Cylinder(
            string name,
            Transform parent,
            float x,
            float y,
            float z,
            float radius,
            float length,
            Material material)
        {
            return SceneFactory.Primitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                LocalEnu(x, y, z),
                new Vector3(radius * 2f, length * .5f, radius * 2f),
                material
            );
        }

        private static Vector3 Enu(float x, float y, float z)
        {
            return Coordinates.ToUnity(x, y, z);
        }

        private static Vector3 LocalEnu(float x, float y, float z)
        {
            return new Vector3(x, z, y);
        }

        private static void Place(Transform target, Vector3 position, float yawRadians)
        {
            target.position = position;
            target.rotation = Quaternion.Euler(0f, -yawRadians * Mathf.Rad2Deg, 0f);
        }

        private static bool HasArgument(string value)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
                if (argument == value)
                    return true;
            return false;
        }

        private static string ArgumentValue(string prefix, string fallback)
        {
            foreach (string argument in System.Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix))
                    return argument.Substring(prefix.Length);
            return fallback;
        }
    }
}
