using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DomeProjection
{
    [Serializable]
    public sealed class ProjectionSharedState
    {
        public long sequence;
        public DomeProjectionSettings settings;
        public bool mediaIsPlaying;
        public bool mediaIsScrubbing;
        public double mediaTime;
    }

    /// <summary>
    /// Low-latency, machine-local communication between Control and Output.
    /// Only loopback UDP is used; packets never leave this computer.
    /// </summary>
    public static class ProjectionProcessBridge
    {
        private const int StatePort = 47831;
        private const int HeartbeatPort = 47832;
        private const int StopPort = 47833;
        private const string SharedFolderName = "DomeProjectionShared";

        private static readonly IPEndPoint LoopbackState = new IPEndPoint(IPAddress.Loopback, StatePort);
        private static readonly IPEndPoint LoopbackHeartbeat = new IPEndPoint(IPAddress.Loopback, HeartbeatPort);
        private static readonly IPEndPoint LoopbackStop = new IPEndPoint(IPAddress.Loopback, StopPort);
        private static UdpClient stateSender;
        private static UdpClient stateReceiver;
        private static UdpClient heartbeatSender;
        private static UdpClient heartbeatReceiver;
        private static UdpClient stopSender;
        private static UdpClient stopReceiver;
        private static DateTime lastHeartbeatUtc = DateTime.MinValue;
        private static bool outputHasCapturePermission;

        public static bool OutputHasCapturePermission => outputHasCapturePermission;

        private static string SharedDirectory
        {
            get
            {
                var productDirectory = new DirectoryInfo(Application.persistentDataPath);
                var parent = productDirectory.Parent?.FullName ?? Application.persistentDataPath;
                return Path.Combine(parent, SharedFolderName);
            }
        }

        public static string SettingsPath => Path.Combine(SharedDirectory, "dome-projection-settings.json");
        public static string ControlLockPath => Path.Combine(SharedDirectory, "dome-projection-control.lock");
        public static string OutputLockPath => Path.Combine(SharedDirectory, "dome-projection-output.lock");

        public static void InitializeControl()
        {
            Shutdown();
            outputHasCapturePermission = false;
            stateSender = new UdpClient();
            stopSender = new UdpClient();
            heartbeatReceiver = BindReceiver(HeartbeatPort);
            lastHeartbeatUtc = DateTime.MinValue;
        }

        public static void InitializeOutput()
        {
            Shutdown();
            stateReceiver = BindReceiver(StatePort);
            stopReceiver = BindReceiver(StopPort);
            heartbeatSender = new UdpClient();
        }

        public static void Shutdown()
        {
            stateSender?.Close();
            stateReceiver?.Close();
            heartbeatSender?.Close();
            heartbeatReceiver?.Close();
            stopSender?.Close();
            stopReceiver?.Close();
            stateSender = stateReceiver = heartbeatSender = heartbeatReceiver = stopSender = stopReceiver = null;
        }

        public static void WriteState(ProjectionSharedState state)
        {
            if (stateSender == null || state == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(state));
                stateSender.Send(bytes, bytes.Length, LoopbackState);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not send projection state: " + exception.Message);
            }
        }

        public static bool TryReadState(out ProjectionSharedState state)
        {
            state = null;
            if (stateReceiver == null) return false;
            try
            {
                while (stateReceiver.Available > 0)
                {
                    IPEndPoint sender = null;
                    var bytes = stateReceiver.Receive(ref sender);
                    var candidate = JsonUtility.FromJson<ProjectionSharedState>(Encoding.UTF8.GetString(bytes));
                    if (candidate?.settings != null) state = candidate;
                }
                return state != null;
            }
            catch { return false; }
        }

        public static void WriteHeartbeat(bool hasCapturePermission)
        {
            if (heartbeatSender == null) return;
            try
            {
                heartbeatSender.Send(new[] { (byte)1, hasCapturePermission ? (byte)1 : (byte)0 },
                    2, LoopbackHeartbeat);
            }
            catch { }
        }

        public static bool IsOutputAlive()
        {
            if (heartbeatReceiver != null)
            {
                try
                {
                    while (heartbeatReceiver.Available > 0)
                    {
                        IPEndPoint sender = null;
                        var bytes = heartbeatReceiver.Receive(ref sender);
                        if (bytes.Length > 1) outputHasCapturePermission = bytes[1] != 0;
                        lastHeartbeatUtc = DateTime.UtcNow;
                    }
                }
                catch { }
            }
            return DateTime.UtcNow - lastHeartbeatUtc < TimeSpan.FromSeconds(2.5);
        }

        public static void RequestOutputStop()
        {
            if (stopSender == null) return;
            try { stopSender.Send(new byte[] { 1 }, 1, LoopbackStop); }
            catch { }
        }

        public static bool StopWasRequestedAfter(long ignoredStartTicks)
        {
            if (stopReceiver == null) return false;
            try
            {
                var requested = false;
                while (stopReceiver.Available > 0)
                {
                    IPEndPoint sender = null;
                    stopReceiver.Receive(ref sender);
                    requested = true;
                }
                return requested;
            }
            catch { return false; }
        }

        private static UdpClient BindReceiver(int port)
        {
            var client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return client;
        }
    }
}
