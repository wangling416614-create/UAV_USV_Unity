using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Receives authoritative Gazebo ENU poses from a ROS-side WebSocket bridge.
    /// Unity remains a visualizer in this mode; Gazebo/ROS owns the motion state.
    /// </summary>
    public sealed class ExternalPoseWebSocketClient : MonoBehaviour
    {
        [Serializable]
        private sealed class PoseData
        {
            public float[] position;
            public float[] orientation;
        }

        [Serializable]
        private sealed class PoseFrame
        {
            public long timestamp_ms;
            public uint sequence;
            public PoseData boat;
            public PoseData drone;
            public PoseData lighthouse;
            public PoseData buoy_west;
            public PoseData buoy_south;
            public PoseData buoy_east;
            public PoseData target_vessel;
        }

        public Transform boat;
        public Transform drone;
        public Transform lighthouse;
        public Transform buoyWest;
        public Transform buoySouth;
        public Transform buoyEast;
        public Transform targetVessel;
        [Tooltip("Unity visual offset from the Gazebo x500 model origin.")]
        public float droneHeightOffset = .28f;
        public string serverUrl = "ws://127.0.0.1:8765/uav_usv";
        public float smoothing = 14f;
        public float reconnectDelay = 1.0f;

        public string connectionStatus { get; private set; } = "Waiting for ROS WebSocket";

        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private volatile string pendingJson;
        private Vector3 boatTarget;
        private Vector3 droneTarget;
        private Quaternion boatRotation = Quaternion.identity;
        private Quaternion droneRotation = Quaternion.identity;
        private bool hasTargets;
        private bool droneDetached;
        private float lastPacketTime = -100f;
        private uint lastSequence;

        private void Start()
        {
            cancellation = new CancellationTokenSource();
            receiveTask = Task.Run(() => ReceiveLoop(cancellation.Token));
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var socket = new ClientWebSocket();
                try
                {
                    connectionStatus = "Connecting " + serverUrl;
                    await socket.ConnectAsync(new Uri(serverUrl), token);
                    connectionStatus = "Connected " + serverUrl;

                    var buffer = new byte[16384];
                    var builder = new StringBuilder();

                    while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        builder.Clear();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                                break;
                            }

                            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage && !token.IsCancellationRequested);

                        if (builder.Length > 0)
                            pendingJson = builder.ToString();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    connectionStatus = "WebSocket failed: " + ex.Message;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void Update()
        {
            string json = pendingJson;
            pendingJson = null;

            if (!string.IsNullOrEmpty(json))
                ParseFrame(json);

            if (hasTargets)
            {
                float alpha = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                boat.position = Vector3.Lerp(boat.position, boatTarget, alpha);
                boat.rotation = Quaternion.Slerp(boat.rotation, boatRotation, alpha);
                drone.position = Vector3.Lerp(drone.position, droneTarget, alpha);
                drone.rotation = Quaternion.Slerp(drone.rotation, droneRotation, alpha);
            }

            if (Time.realtimeSinceStartup - lastPacketTime > 1.5f && hasTargets)
                connectionStatus = "Connected, waiting for fresh pose";
        }

        private void ParseFrame(string json)
        {
            try
            {
                PoseFrame frame = JsonUtility.FromJson<PoseFrame>(json);
                if (frame == null || !Valid(frame.boat) || !Valid(frame.drone))
                {
                    connectionStatus = "Invalid WebSocket pose packet";
                    return;
                }

                if (hasTargets && frame.sequence != 0 && frame.sequence <= lastSequence)
                    return;

                lastSequence = frame.sequence;
                boatTarget = Coordinates.ToUnity(frame.boat.position[0], frame.boat.position[1], frame.boat.position[2]);
                droneTarget = Coordinates.ToUnity(frame.drone.position[0], frame.drone.position[1], frame.drone.position[2])
                    + Vector3.up * droneHeightOffset;
                boatRotation = EnuRotation(frame.boat.orientation);
                droneRotation = EnuRotation(frame.drone.orientation);
                ApplyOptionalPose(frame.lighthouse, lighthouse);
                ApplyOptionalPose(frame.buoy_west, buoyWest);
                ApplyOptionalPose(frame.buoy_south, buoySouth);
                ApplyOptionalPose(frame.buoy_east, buoyEast);
                ApplyOptionalPose(frame.target_vessel, targetVessel);

                if (!droneDetached)
                {
                    drone.SetParent(null, true);
                    droneDetached = true;
                }

                hasTargets = true;
                lastPacketTime = Time.realtimeSinceStartup;
                connectionStatus = "WebSocket pose seq " + frame.sequence;
            }
            catch (Exception ex)
            {
                connectionStatus = "WebSocket parse failed: " + ex.Message;
            }
        }

        private static bool Valid(PoseData pose)
        {
            return pose != null &&
                   pose.position != null &&
                   pose.position.Length >= 3 &&
                   pose.orientation != null &&
                   pose.orientation.Length >= 4;
        }

        private static Quaternion EnuRotation(float[] q)
        {
            var result = new Quaternion(-q[0], -q[2], -q[1], q[3]);
            return result.normalized;
        }

        private static void ApplyOptionalPose(PoseData pose, Transform target)
        {
            if (!target || !Valid(pose))
                return;

            target.gameObject.SetActive(true);
            target.position = Coordinates.ToUnity(
                pose.position[0],
                pose.position[1],
                pose.position[2]
            );
            target.rotation = EnuRotation(pose.orientation);
        }

        private void OnDestroy()
        {
            cancellation?.Cancel();
            try { receiveTask?.Wait(800); } catch (Exception) { }
            cancellation?.Dispose();
        }
    }
}
