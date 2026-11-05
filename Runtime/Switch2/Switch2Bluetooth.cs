#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Fofuxo.JoySense.Controls;
using Fofuxo.JoySense.Windows;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Fofuxo.JoySense.Switch2
{
    /// <summary>
    /// One Switch 2 input report as the device's state expects it: the 64 bytes Unity's HID backend delivers over
    /// USB, report id first.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = Size)]
    internal unsafe struct Switch2BluetoothReport : IInputStateTypeInfo
    {
        public const int Size = 64;

        public fixed byte bytes[Size];

        public FourCC format => Switch2ProControllerHIDInputState.Format;
    }

    // The Switch 2 Pro Controller over Bluetooth LE. Windows does not see these controllers as HID devices over
    // the air, so nothing reaches Unity's HID backend: this scans for them, connects, sends the same kind of init
    // the cable needs, and feeds each input report to an Input System device of the same layout. A GATT
    // notification carries the report body alone, while the same report over USB arrives behind its report id;
    // putting that byte back is what lets one layout read both transports.
    //
    // Connections live in the native DLL and outlive a domain reload, so recompiling scripts does not drop a
    // controller: the new domain finds them again and reattaches them to the devices the Input System restored.
    internal static class Switch2Bluetooth
    {
        const byte InputReportId = 0x05;
        const int ScanIntervalMs = 250;
        const int ReadTimeoutMs = 100;
        const int CommandTimeoutMs = 1000;
        // Over Bluetooth a button press is what brings a paired controller back, so its first reports carry that
        // press. Buttons read as released until they are, or until this has passed since the link came up, so
        // the press that woke the controller does not land in the game. A button still held after it reads as a
        // fresh press, which is right for one the player is holding on purpose.
        const int SettleMs = 1000;
        // A link can come up with the controller not taking its setup: connected, but silent. Seen right after the
        // Editor started, with every setup command failing, and after a reconnection that sent a report or two and
        // stopped, with every command answered. A controller on the air streams even at rest, so past this without
        // a report the setup is sent again, and past it a second time the link is dropped, so the next button press
        // starts a fresh one.
        const int NoInputTimeoutMs = 3000;
        // Reports wait here for the next input update. Only a game that stops updating input fills this, and an
        // old report is worth nothing next to a new one.
        const int MaxPendingReports = 512;

        sealed class Link
        {
            public int handle;
            public ulong address;
            public string serial;
            public Thread reader;
            // What the connection looked like as it came up, for the log. -1 for one a previous domain connected.
            public long connectMs;
            public bool pairingMode;
            public bool calibrationValid;
            // Main thread only.
            public Switch2ProController device;
        }

        struct Calibration
        {
            public bool valid;
            public StickCalibration left;
            public StickCalibration right;
        }

        // The init sequence over the radio, as [command, subcommand, data...]. Not the cable's: it also commits a
        // pairing slot (0x15:0x03), and its feature select (0x94: motion, mouse, magnetometer) is narrower, since
        // asking for every feature turns on report fields that read as stuck triggers. Input report mode 0x09,
        // whose body is the cable's report 0x05 without the id.
        static readonly byte[][] s_InitCommands =
        {
            new byte[] { 0x03, 0x0d, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff },
            new byte[] { 0x07, 0x01 },
            new byte[] { 0x16, 0x01 },
            new byte[] { 0x15, 0x03, 0x00 },
            new byte[] { 0x0c, 0x02, 0x94, 0x00, 0x00, 0x00 }, // feature select
            new byte[] { 0x11, 0x03 },
            new byte[] { 0x0a, 0x08, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x35, 0x00, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x0c, 0x04, 0x94, 0x00, 0x00, 0x00 },
            new byte[] { 0x03, 0x0a, 0x09, 0x00, 0x00, 0x00 }, // input report mode 0x09
            new byte[] { 0x10, 0x01 },
            new byte[] { 0x01, 0x0c },
            new byte[] { 0x09, 0x07, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, // player light 1
        };

        // Bonding: the host address the controller should remember, two halves of a link key, and a commit. The
        // keys are fixed values -- the controller stores whatever it is given and uses it to recognise this host
        // later, so they identify the pairing rather than secure it.
        static readonly byte[] s_LinkKeyFirst =
            { 0x00, 0xea, 0xbd, 0x47, 0x13, 0x89, 0x35, 0x42, 0xc6, 0x79, 0xee, 0x07, 0xf2, 0x53, 0x2c, 0x6c, 0x31 };
        static readonly byte[] s_LinkKeySecond =
            { 0x00, 0x40, 0xb0, 0x8a, 0x5f, 0xcd, 0x1f, 0x9b, 0x41, 0x12, 0x5c, 0xac, 0xc6, 0x3f, 0x38, 0xa0, 0x73 };

        static readonly List<Link> s_Links = new List<Link>();
        static readonly ConcurrentQueue<Action> s_MainThreadWork = new ConcurrentQueue<Action>();
        static readonly ConcurrentQueue<(Link link, Switch2BluetoothReport report, long receivedAt)> s_Reports =
            new ConcurrentQueue<(Link, Switch2BluetoothReport, long)>();
        static readonly ManualResetEvent s_StopSignal = new ManualResetEvent(false);
        static readonly StringBuilder s_LogBuffer = new StringBuilder(512);

        // How long a controller paired with another device is kept back before it is looked at again: soon enough
        // that holding SYNC on it is noticed within a moment.
        const int OtherHostRetryMs = 2000;

        // The Bluetooth address of this PC's adapter, which a controller paired with this PC advertises. 0 when it
        // could not be read. Scan thread only.
        static ulong s_HostAddress;
        // Controllers already reported as paired with another device, so the note appears once per controller.
        static readonly HashSet<ulong> s_OtherHostReported = new HashSet<ulong>();

        static int s_PendingReports;
        static volatile bool s_Stopping;
        static bool s_Started;
        static bool s_NativeMissing;
        static Thread s_Scanner;
        // Since this domain started: in the Editor, since the last recompile.
        static readonly System.Diagnostics.Stopwatch s_DomainClock = new System.Diagnostics.Stopwatch();

        public static void Start()
        {
            if (s_Started)
            {
                return;
            }
            s_Started = true;
            s_DomainClock.Restart();
            Switch2BluetoothLog.Initialize();

            InputSystem.onBeforeUpdate += Pump;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopThreads;
            UnityEditor.EditorApplication.quitting += Shutdown;
#else
            // Not in the Editor, where this is raised on leaving Play Mode.
            Application.quitting += Shutdown;
#endif

            s_Scanner = new Thread(ScanLoop) { IsBackground = true, Name = "JoySense Bluetooth" };
            s_Scanner.Start();
        }

        // Stops scanning and lets go of every controller, so another program can connect to them. Start picks them
        // up again. Main thread.
        public static void Stop()
        {
            if (!s_Started)
            {
                return;
            }
            s_Started = false;

            InputSystem.onBeforeUpdate -= Pump;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopThreads;
            UnityEditor.EditorApplication.quitting -= Shutdown;
#else
            Application.quitting -= Shutdown;
#endif

            Shutdown();

            // The links' devices go with them; work and reports still queued belong to links that no longer exist.
            lock (s_Links)
            {
                s_Links.Clear();
            }
            while (s_MainThreadWork.TryDequeue(out _))
            {
            }
            while (s_Reports.TryDequeue(out _))
            {
            }
            s_PendingReports = 0;
            RemoveOrphanedDevices();

            s_Scanner = null;
            s_Stopping = false;
            s_StopSignal.Reset();
        }

        // Before a domain reload: the threads go, the connections stay for the next domain to pick up.
        static void StopThreads()
        {
            s_Stopping = true;
            s_StopSignal.Set();

            // A scan pass may be in the middle of a connection, which takes a second or two; finishing it is what
            // leaves the controller in a state the next domain can adopt.
            s_Scanner?.Join(5000);
            lock (s_Links)
            {
                foreach (var link in s_Links)
                {
                    link.reader?.Join(ReadTimeoutMs * 5);
                }
            }
        }

        static void Shutdown()
        {
            StopThreads();
            if (s_NativeMissing)
            {
                return;
            }
            try
            {
                JoySenseBle.JsBle_Shutdown();
            }
            catch (DllNotFoundException)
            {
            }
        }

        // Runs on the scan thread for the life of the domain.
        static void ScanLoop()
        {
            try
            {
                AdoptConnections();

                if (JoySenseBle.JsBle_IsSupported() == 0)
                {
                    return;
                }
                s_HostAddress = JoySenseBle.JsBle_GetHostAddress();

                var found = new JoySenseBle.Advertisement[8];
                while (!s_Stopping)
                {
                    // A no-op while the scan runs; restarts it if the radio stopped it (adapter reset, radio off).
                    JoySenseBle.JsBle_StartScan();

                    var count = JoySenseBle.JsBle_DrainDiscovered(found, found.Length);
                    for (var index = 0; index < count && !s_Stopping; index++)
                    {
                        Bring(found[index]);
                    }

                    s_StopSignal.WaitOne(ScanIntervalMs);
                }
            }
            catch (DllNotFoundException)
            {
                s_NativeMissing = true;
                Debug.LogWarning("[JoySense] JoySenseBle.dll is missing, so Switch 2 controllers only work over USB.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        // The controllers a previous domain connected. Their devices were restored by the Input System, and only
        // the managed side of the link -- the reader, and the calibration on the device -- has to be rebuilt.
        static void AdoptConnections()
        {
            var handles = new int[16];
            var count = Math.Min(JoySenseBle.JsBle_GetConnections(handles, handles.Length), handles.Length);
            for (var index = 0; index < count; index++)
            {
                if (JoySenseBle.JsBle_GetConnectionInfo(handles[index], out var address, out _) == 0)
                {
                    continue;
                }
                StartLink(handles[index], address, ReadCalibration(handles[index]), -1, false);
            }

            // Queued after the links, so it runs once they have claimed their devices: anything left over belongs to
            // a connection that ended while no domain was there to see it.
            s_MainThreadWork.Enqueue(RemoveOrphanedDevices);
        }

        static void Bring(JoySenseBle.Advertisement advertisement)
        {
            if (advertisement.productId != Switch2ProController.ProductId)
            {
                Debug.Log($"[JoySense] Ignoring Switch 2 controller {advertisement.address:X12} (product " +
                    $"{advertisement.productId:X4}): only the Pro Controller is supported so far.");
                return;
            }

            // Only a controller in pairing mode (no host in its advertisement: SYNC held) or one paired with this PC
            // is taken (D-008). One paired with something else -- a console in the same room, woken by its owner --
            // would otherwise be taken away from it by a button press meant for that console.
            var pairingMode = advertisement.reconnectAddress == 0;
            if (!pairingMode && (s_HostAddress == 0 || advertisement.reconnectAddress != s_HostAddress))
            {
                if (s_OtherHostReported.Add(advertisement.address))
                {
                    Debug.Log($"[JoySense] Switch 2 Pro Controller {advertisement.address:X12} is paired with another " +
                        "device; hold SYNC on it to pair it with this PC.");
                }
                JoySenseBle.JsBle_SuppressFor(advertisement.address, OtherHostRetryMs);
                return;
            }

            var connectClock = System.Diagnostics.Stopwatch.StartNew();
            var handle = JoySenseBle.JsBle_Connect(advertisement.address, advertisement.productId);
            var connectMs = connectClock.ElapsedMilliseconds;
            if (handle == 0)
            {
                return;
            }

            // A controller in pairing mode is paired with this PC, which is what the player asked for by holding
            // SYNC: from now on a button press reconnects it.
            if (pairingMode)
            {
                var host = s_HostAddress;
                if (host != 0 && Bond(handle, host))
                {
                    Debug.Log($"[JoySense] Paired Switch 2 Pro Controller {advertisement.address:X12} to this PC; " +
                        "a button press reconnects it from now on.");
                }
            }

            if (!Initialize(handle))
            {
                JoySenseBle.JsBle_Disconnect(handle);
                return;
            }

            StartLink(handle, advertisement.address, ReadCalibration(handle), connectMs, pairingMode);
        }

        static void StartLink(int handle, ulong address, Calibration calibration, long connectMs, bool pairingMode)
        {
            var link = new Link
            {
                handle = handle,
                address = address,
                serial = address.ToString("X12"),
                connectMs = connectMs,
                pairingMode = pairingMode,
                calibrationValid = calibration.valid,
            };
            lock (s_Links)
            {
                s_Links.Add(link);
            }

            // The device comes first in the queue, so the reports the reader starts producing have one to go to.
            s_MainThreadWork.Enqueue(() => AttachDevice(link, calibration));

            link.reader = new Thread(ReadLoop) { IsBackground = true, Name = $"JoySense Bluetooth {link.serial}" };
            link.reader.Start(link);
        }

        static unsafe void ReadLoop(object state)
        {
            var link = (Link)state;
            var settled = false;
            var settleTimer = System.Diagnostics.Stopwatch.StartNew();
            var silenceTimer = System.Diagnostics.Stopwatch.StartNew();
            var setupRepeated = false;
            var reports = 0L;
            try
            {
                while (!s_Stopping)
                {
                    var report = default(Switch2BluetoothReport);
                    var length = JoySenseBle.JsBle_ReadInputReport(link.handle, report.bytes + 1,
                        Switch2BluetoothReport.Size - 1, ReadTimeoutMs);
                    if (length < 0)
                    {
                        s_MainThreadWork.Enqueue(() => Drop(link));
                        return;
                    }
                    if (length == 0)
                    {
                        if (silenceTimer.ElapsedMilliseconds >= NoInputTimeoutMs)
                        {
                            if (!setupRepeated)
                            {
                                setupRepeated = true;
                                Debug.LogWarning($"[JoySense] Switch 2 Pro Controller {link.serial} sends no input; " +
                                    "setting it up again.");
                                Initialize(link.handle);
                                silenceTimer.Restart();
                            }
                            else
                            {
                                Debug.LogWarning($"[JoySense] Switch 2 Pro Controller {link.serial} still sends no " +
                                    "input; disconnecting it. Press a button on it to connect again; if that does not " +
                                    "work, turn Windows Bluetooth off and on.");
                                LogSilentLink(link, reports, settleTimer.ElapsedMilliseconds);
                                s_MainThreadWork.Enqueue(() => Drop(link));
                                WatchRelease(link);
                                return;
                            }
                        }
                        continue;
                    }
                    silenceTimer.Restart();
                    if (setupRepeated)
                    {
                        Switch2BluetoothLog.Write("recovered", $"{Describe(link)} reportsBefore={reports}");
                    }
                    setupRepeated = false;
                    if (reports++ == 0)
                    {
                        Switch2BluetoothLog.Write("streaming",
                            $"{Describe(link)} firstReportMs={settleTimer.ElapsedMilliseconds} {DescribeRadio(link)}");
                    }

                    report.bytes[0] = InputReportId;
                    if (!settled)
                    {
                        settled = !HasButtonsDown(report.bytes) || settleTimer.ElapsedMilliseconds >= SettleMs;
                        if (!settled)
                        {
                            ClearButtons(report.bytes);
                        }
                    }

                    if (Interlocked.Increment(ref s_PendingReports) > MaxPendingReports)
                    {
                        Interlocked.Decrement(ref s_PendingReports);
                        continue;
                    }
                    s_Reports.Enqueue((link, report, System.Diagnostics.Stopwatch.GetTimestamp()));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static string Describe(Link link)
        {
            return $"controller={link.serial} sinceDomainStartS={s_DomainClock.ElapsedMilliseconds / 1000} " +
                $"connectMs={link.connectMs} pairing={link.pairingMode} calibrationAtConnect={link.calibrationValid}";
        }

        static string DescribeRadio(Link link)
        {
            if (JoySenseBle.JsBle_GetLinkInfo(link.handle, out var info) == 0)
            {
                return "radio=unknown";
            }
            return $"intervalMs={info.connectionInterval * 1.25f} latency={info.connectionLatency} " +
                $"timeoutMs={info.linkTimeout * 10} phyTx={info.transmitPhy} phyRx={info.receivePhy}";
        }

        // Asks what is still alive on a silent link before it is dropped. Worker thread: each answer takes a while.
        static void LogSilentLink(Link link, long reports, long connectedMs)
        {
            var commandClock = System.Diagnostics.Stopwatch.StartNew();
            var commandAnswered = SendCommand(link.handle, 0x10, 0x01, Array.Empty<byte>());
            var commandMs = commandClock.ElapsedMilliseconds;
            var calibrationNow = ReadCalibrationOnce(link.handle);

            bool steamRunning;
            try
            {
                steamRunning = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
            }
            catch (Exception)
            {
                steamRunning = false;
            }

            Switch2BluetoothLog.Write("silent",
                $"{Describe(link)} reportsBefore={reports} connectedMs={connectedMs} {DescribeRadio(link)} " +
                $"commandAnswered={commandAnswered} commandMs={commandMs} calibrationNow={calibrationNow.valid} " +
                $"steam={steamRunning}");
            Debug.LogWarning($"[JoySense] The silent link was recorded in {Switch2BluetoothLog.FilePath}.");
        }

        // Whether Windows lets go of the controller after the drop. A link that stays is what keeps a button press
        // from reconnecting it. Its own thread, since it waits; a domain reload ends it, losing only this answer.
        static void WatchRelease(Link link)
        {
            var address = link.address;
            var serial = link.serial;
            new Thread(() =>
            {
                try
                {
                    var waited = 0;
                    foreach (var seconds in new[] { 10, 30 })
                    {
                        if (s_StopSignal.WaitOne((seconds - waited) * 1000))
                        {
                            return;
                        }
                        waited = seconds;
                        var status = JoySenseBle.JsBle_GetWindowsConnectionStatus(address);
                        Switch2BluetoothLog.Write("afterDrop", $"controller={serial} afterS={seconds} windowsConnected={status}");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }) { IsBackground = true, Name = $"JoySense release watch {serial}" }.Start();
        }

        // The button word is bytes 5-8, little-endian. Its top six bits are not buttons: a Joy-Con reports status
        // there.
        static unsafe bool HasButtonsDown(byte* report)
        {
            var word = report[5] | (report[6] << 8) | (report[7] << 16) | (report[8] << 24);
            return (word & 0x03FFFFFF) != 0;
        }

        static unsafe void ClearButtons(byte* report)
        {
            report[5] = 0;
            report[6] = 0;
            report[7] = 0;
            report[8] &= 0xFC;
        }

        // Main thread, from the Input System's own update: events queued here are processed in that same update.
        static void Pump()
        {
            DrainNativeLog();

            while (s_MainThreadWork.TryDequeue(out var work))
            {
                work();
            }

            while (s_Reports.TryDequeue(out var entry))
            {
                Interlocked.Decrement(ref s_PendingReports);
                var device = entry.link.device;
                if (device != null && device.added)
                {
                    // Stamped with when it arrived, not now: all of a frame's reports are queued together here, and
                    // the motion fusion needs the real time between them.
                    var age = (System.Diagnostics.Stopwatch.GetTimestamp() - entry.receivedAt) / (double)System.Diagnostics.Stopwatch.Frequency;
                    InputSystem.QueueStateEvent(device, entry.report, InputState.currentTime - age);
                }
            }
        }

        static void DrainNativeLog()
        {
            if (s_NativeMissing)
            {
                return;
            }
            try
            {
                int level;
                while ((level = JoySenseBle.JsBle_PopLog(s_LogBuffer, s_LogBuffer.Capacity)) >= 0)
                {
                    var message = "[JoySense] " + s_LogBuffer;
                    if (level == JoySenseBle.LogWarning)
                    {
                        Debug.LogWarning(message);
                    }
                    else
                    {
                        Debug.Log(message);
                    }
                }
            }
            catch (DllNotFoundException)
            {
                s_NativeMissing = true;
            }
        }

        static void AttachDevice(Link link, Calibration calibration)
        {
            foreach (var existing in InputSystem.devices)
            {
                if (existing is Switch2ProController controller && controller.isBluetooth
                    && controller.description.serial == link.serial)
                {
                    link.device = controller;
                    break;
                }
            }

            if (link.device == null)
            {
                link.device = (Switch2ProController)InputSystem.AddDevice(new InputDeviceDescription
                {
                    interfaceName = Switch2ProController.BluetoothInterface,
                    manufacturer = "Nintendo",
                    product = "Switch 2 Pro Controller",
                    serial = link.serial,
                    capabilities = $"{{\"vendorId\":{Switch2ProController.VendorId},\"productId\":{Switch2ProController.ProductId}}}",
                });
                Debug.Log($"[JoySense] Switch 2 Pro Controller {link.serial} connected over Bluetooth.");
            }

            if (calibration.valid)
            {
                link.device.ApplyCalibration(calibration.left, calibration.right);
            }
            else
            {
                Debug.LogWarning("[JoySense] Could not read the Switch 2 Pro Controller's stick calibration over " +
                    "Bluetooth; its sticks use a fixed range.");
            }
        }

        static void RemoveOrphanedDevices()
        {
            var orphans = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
            {
                if (device is Switch2ProController controller && controller.isBluetooth && !IsLinked(controller))
                {
                    orphans.Add(device);
                }
            }
            foreach (var device in orphans)
            {
                InputSystem.RemoveDevice(device);
            }
        }

        static bool IsLinked(Switch2ProController controller)
        {
            lock (s_Links)
            {
                foreach (var link in s_Links)
                {
                    if (link.device == controller)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Main thread: the controller was switched off, went out of range, or took its cable.
        static void Drop(Link link)
        {
            lock (s_Links)
            {
                s_Links.Remove(link);
            }

            if (link.device != null && link.device.added)
            {
                InputSystem.RemoveDevice(link.device);
                Debug.Log($"[JoySense] Switch 2 Pro Controller {link.serial} disconnected from Bluetooth.");
            }

            // Tearing the link down closes WinRT objects, which is best kept off the main thread.
            var handle = link.handle;
            ThreadPool.QueueUserWorkItem(_ => JoySenseBle.JsBle_Disconnect(handle));
        }

        // ---- Commands. Worker threads only: each one waits for the controller's answer. ----

        static bool SendCommand(int handle, byte commandId, byte subcommandId, byte[] data)
        {
            return JoySenseBle.JsBle_SendCommand(handle, commandId, subcommandId, data, data?.Length ?? 0,
                null, 0, out _, 0, CommandTimeoutMs) != 0;
        }

        static bool Initialize(int handle)
        {
            // A single command the controller does not like is normal -- the sequence covers several controller
            // shapes and not all of them implement all of it. A run of them means the link is not really up, and
            // carrying on would leave a device that never streams while looking connected.
            var consecutiveFailures = 0;
            var failures = 0;
            foreach (var command in s_InitCommands)
            {
                var data = new byte[command.Length - 2];
                Array.Copy(command, 2, data, 0, data.Length);
                if (SendCommand(handle, command[0], command[1], data))
                {
                    consecutiveFailures = 0;
                    continue;
                }

                failures++;
                if (++consecutiveFailures >= 3)
                {
                    Debug.LogWarning("[JoySense] Switch 2 Pro Controller setup over Bluetooth was abandoned after " +
                        "three commands in a row went unanswered.");
                    return false;
                }
            }

            if (failures > 0)
            {
                Debug.Log($"[JoySense] {failures} of {s_InitCommands.Length} Switch 2 Pro Controller setup commands over " +
                    "Bluetooth went unanswered.");
            }
            return true;
        }

        static bool Bond(int handle, ulong host)
        {
            var addressPayload = new byte[14];
            addressPayload[1] = 0x02;
            for (var index = 0; index < 6; index++)
            {
                var value = (byte)(host >> (index * 8));
                addressPayload[2 + index] = value;
                addressPayload[8 + index] = value;
            }

            return SendCommand(handle, 0x15, 0x01, addressPayload)
                && SendCommand(handle, 0x15, 0x04, s_LinkKeyFirst)
                && SendCommand(handle, 0x15, 0x02, s_LinkKeySecond)
                && SendCommand(handle, 0x15, 0x03, new byte[] { 0x00 });
        }

        // Memory read, command 0x02:0x04 with data [length][0x7e][0][0][address u32 LE]. The answer echoes both
        // before the data. Tried three times: a wrong answer is usually one lost on a radio that is also carrying a
        // report every 7.5ms, and a failed read leaves the controller on a guessed calibration until it reconnects.
        static byte[] ReadMemory(int handle, uint address, int length)
        {
            var payload = new byte[8];
            payload[0] = (byte)length;
            payload[1] = 0x7e;
            payload[4] = (byte)address;
            payload[5] = (byte)(address >> 8);
            payload[6] = (byte)(address >> 16);
            payload[7] = (byte)(address >> 24);

            var response = new byte[128];
            for (var attempt = 0; attempt < 3; attempt++)
            {
                // 8 + length: without saying how much is expected, the plain acknowledgement -- same command id,
                // status accepted, eight bytes and no data -- would be taken as the answer.
                if (JoySenseBle.JsBle_SendCommand(handle, 0x02, 0x04, payload, payload.Length, response,
                        response.Length, out var responseLength, 8 + length, CommandTimeoutMs) == 0
                    || responseLength < 8 + length || response[0] != length)
                {
                    continue;
                }

                // A mismatched echo is the answer to some earlier read.
                var echoed = (uint)(response[4] | (response[5] << 8) | (response[6] << 16) | (response[7] << 24));
                if (echoed != address)
                {
                    continue;
                }

                var data = new byte[length];
                Array.Copy(response, 8, data, 0, length);
                return data;
            }
            return null;
        }

        // A read can be lost to a radio that is also streaming a report every few milliseconds, and its own
        // retries happen back to back; a short pause before trying the whole set again gets past a busy moment.
        static Calibration ReadCalibration(int handle)
        {
            for (var attempt = 0; ; attempt++)
            {
                var result = ReadCalibrationOnce(handle);
                if (result.valid || attempt == 2 || s_Stopping)
                {
                    return result;
                }
                Thread.Sleep(300);
            }
        }

        static Calibration ReadCalibrationOnce(int handle)
        {
            var result = new Calibration();
            var hasLeft = ReadStickCalibration(handle, Switch2Calibration.UserBlock + (uint)Switch2Calibration.UserLeftOffset,
                Switch2Calibration.FactoryLeftBlock + (uint)Switch2Calibration.FactoryOffset, out result.left);
            var hasRight = ReadStickCalibration(handle, Switch2Calibration.UserBlock + (uint)Switch2Calibration.UserRightOffset,
                Switch2Calibration.FactoryRightBlock + (uint)Switch2Calibration.FactoryOffset, out result.right);
            result.valid = hasLeft && hasRight;
            return result;
        }

        // The player's own calibration first, the factory's when the user block is erased or holds no stick.
        static bool ReadStickCalibration(int handle, uint userAddress, uint factoryAddress, out StickCalibration calibration)
        {
            foreach (var address in new[] { userAddress, factoryAddress })
            {
                var block = ReadMemory(handle, address, Switch2Calibration.StickBytes);
                if (block == null || !Switch2Calibration.IsWritten(block, 0))
                {
                    continue;
                }

                calibration = Switch2Calibration.Parse(block, 0);
                if (Switch2Calibration.IsPlausible(calibration))
                {
                    return true;
                }
            }

            calibration = default;
            return false;
        }
    }
}
#endif
