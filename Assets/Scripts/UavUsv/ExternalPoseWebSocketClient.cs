using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Exchanges authoritative Gazebo poses and Unity planning commands with ROS.
    /// Gazebo remains the owner of motion state.
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
            public ControlData control;
        }

        [Serializable]
        private sealed class ControlData
        {
            public string state;
            public string message;
            public long path_id;
            public int waypoint_index;
            public int waypoint_count;
        }

        [Serializable]
        private sealed class PathPoint
        {
            public float x;
            public float y;
        }

        [Serializable]
        private sealed class BoatPathCommand
        {
            public string type = "boat_path";
            public long path_id;
            public PathPoint[] points;
        }

        [Serializable]
        private sealed class BoatStopCommand
        {
            public string type = "boat_stop";
            public long command_id;
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
        public string controlStatus { get; private set; } = "Controller idle";
        public bool isConnected { get; private set; }

        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
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
                using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task sendTask = null;
                try
                {
                    connectionStatus = "Connecting " + serverUrl;
                    await socket.ConnectAsync(new Uri(serverUrl), token);
                    connectionStatus = "Connected " + serverUrl;
                    isConnected = true;
                    sendTask = SendLoop(socket, connectionCancellation.Token);

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
                finally
                {
                    isConnected = false;
                    connectionCancellation.Cancel();
                    if (sendTask != null)
                    {
                        try { await sendTask; } catch (OperationCanceledException) { }
                    }
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

        private async Task SendLoop(ClientWebSocket socket, CancellationToken token)
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                if (!outgoing.TryDequeue(out string json))
                {
                    await Task.Delay(20, token);
                    continue;
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Text,
                    true,
                    token
                );
            }
        }

        public long SendBoatPath(IReadOnlyList<Vector2> path)
        {
            if (path == null || path.Count < 2)
                return 0;

            long pathId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var points = new PathPoint[path.Count];
            for (int i = 0; i < path.Count; i++)
                points[i] = new PathPoint { x = path[i].x, y = path[i].y };

            outgoing.Enqueue(JsonUtility.ToJson(new BoatPathCommand
            {
                path_id = pathId,
                points = points
            }));
            return pathId;
        }

        public void SendBoatStop()
        {
            outgoing.Enqueue(JsonUtility.ToJson(new BoatStopCommand
            {
                command_id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }));
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
                if (frame.control != null && !string.IsNullOrEmpty(frame.control.state))
                {
                    controlStatus = frame.control.state;
                    if (frame.control.waypoint_count > 0)
                    {
                        controlStatus += $" {Mathf.Min(frame.control.waypoint_index + 1, frame.control.waypoint_count)}" +
                                         $"/{frame.control.waypoint_count}";
                    }
                    if (!string.IsNullOrEmpty(frame.control.message))
                        controlStatus += ": " + frame.control.message;
                }

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
            if (isConnected)
                SendBoatStop();
            cancellation?.Cancel();
            try { receiveTask?.Wait(800); } catch (Exception) { }
            cancellation?.Dispose();
        }
    }
}
