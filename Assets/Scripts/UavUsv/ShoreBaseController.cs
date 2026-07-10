using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Shore base station: wait for sensor contact, then dispatch USV capture + UAV takeoff.
    /// </summary>
    public sealed class ShoreBaseController : MonoBehaviour
    {
        public enum MissionPhase
        {
            Standby,
            Searching,
            TargetDetected,
            Capture,
            DefenseHold,
            Complete
        }

        public Transform shoreBase;
        public Transform[] boats;
        public Transform[] drones;
        public Transform targetPoint;
        public MultiAgentCaptureDefenseScenario scenario;

        public MissionPhase phase { get; private set; } = MissionPhase.Standby;
        public string status { get; private set; } = "Base online — UAVs on pad";
        public bool automatic = true;
        public float dispatchHoldSeconds = .35f;

        private float phaseStarted;
        private Vector3[] boatSlots = new Vector3[3];
        private Vector3[] droneSlots = new Vector3[3];
        private bool targetContact;

        public bool HasTargetContact => targetContact;
        public bool ShouldLaunchDrones =>
            phase == MissionPhase.TargetDetected ||
            phase == MissionPhase.Capture ||
            phase == MissionPhase.DefenseHold ||
            phase == MissionPhase.Complete;

        public bool ShouldCloseCapture =>
            phase == MissionPhase.Capture ||
            phase == MissionPhase.DefenseHold ||
            phase == MissionPhase.Complete;

        public Vector3 GetBoatSlot(int index) =>
            boatSlots[Mathf.Clamp(index, 0, boatSlots.Length - 1)];

        public Vector3 GetDroneSlot(int index) =>
            droneSlots[Mathf.Clamp(index, 0, droneSlots.Length - 1)];

        public void BeginSearch()
        {
            phase = MissionPhase.Searching;
            phaseStarted = Time.time;
            targetContact = false;
            status = "Searching — USVs approach from 3 axes, UAVs standby";
            RecomputeSlots();
        }

        public void NotifyTargetContact(string reporter)
        {
            if (targetContact && phase != MissionPhase.Searching && phase != MissionPhase.Standby)
                return;

            targetContact = true;
            phase = MissionPhase.TargetDetected;
            phaseStarted = Time.time;
            status = "Target contact via " + reporter + " — dispatch capture";
            RecomputeSlots();
            if (scenario)
                scenario.NotifyBaseDispatch();
        }

        public void BeginMission()
        {
            // Manual force-dispatch (B key): skip search and treat as contact.
            NotifyTargetContact("manual base order");
        }

        public void NotifyCaptureComplete()
        {
            phase = MissionPhase.Complete;
            status = "Capture success — formation locked";
        }

        public void ResetMission()
        {
            phase = MissionPhase.Standby;
            phaseStarted = Time.time;
            targetContact = false;
            status = "Base standby — UAVs on pad";
            RecomputeSlots();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.B))
                BeginMission();

            RecomputeSlots();

            if (!automatic)
                return;

            switch (phase)
            {
                case MissionPhase.Standby:
                    if (Time.time - phaseStarted > .6f)
                        BeginSearch();
                    break;
                case MissionPhase.Searching:
                    status = "Searching — waiting for lidar/radar contact";
                    break;
                case MissionPhase.TargetDetected:
                    if (Time.time - phaseStarted >= dispatchHoldSeconds)
                    {
                        phase = MissionPhase.Capture;
                        phaseStarted = Time.time;
                        status = "Capture: USVs close ring, UAVs takeoff";
                    }
                    break;
                case MissionPhase.Capture:
                    if (scenario && scenario.CaptureReady)
                    {
                        phase = MissionPhase.DefenseHold;
                        phaseStarted = Time.time;
                        status = "Defense ring holding (UAV)";
                    }
                    else
                    {
                        status = "Capture: USVs close ring, UAVs inbound";
                    }
                    break;
                case MissionPhase.DefenseHold:
                    if (scenario && scenario.FormationHolding)
                    {
                        phase = MissionPhase.Complete;
                        status = "Capture-defense formation locked";
                    }
                    else
                    {
                        status = "Holding moving capture-defense formation";
                    }
                    break;
            }
        }

        private void RecomputeSlots()
        {
            if (!targetPoint)
                return;

            float captureRadius = scenario ? scenario.captureRadius : 28f;
            float defenseRadius = scenario ? scenario.defenseRadius : 48f;
            float droneAltitude = scenario ? scenario.droneAltitude : 8f;

            // Fixed equilateral triangles — no orbit after slots are assigned.
            float[] boatAngles = { 0f, 120f, 240f };
            float[] droneAngles = { 60f, 180f, 300f };

            for (int i = 0; i < boatSlots.Length; i++)
            {
                float angle = boatAngles[i] * Mathf.Deg2Rad;
                boatSlots[i] = new Vector3(
                    targetPoint.position.x + Mathf.Cos(angle) * captureRadius,
                    .42f,
                    targetPoint.position.z + Mathf.Sin(angle) * captureRadius
                );
            }

            for (int i = 0; i < droneSlots.Length; i++)
            {
                float angle = droneAngles[i] * Mathf.Deg2Rad;
                float alt = scenario ? scenario.droneAltitude + (i - 1) * 1.6f : droneAltitude;
                droneSlots[i] = new Vector3(
                    targetPoint.position.x + Mathf.Cos(angle) * defenseRadius,
                    alt,
                    targetPoint.position.z + Mathf.Sin(angle) * defenseRadius
                );
            }
        }
    }
}
