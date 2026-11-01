using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fofuxo.JoySense.Switch2
{
    // Brings a Switch 2 controller from "plugged in" to "streaming, with calibrated sticks". The commands block,
    // so they run on a worker; the result is applied back on the main thread, where the Input System lives.
    internal static class Switch2Setup
    {
        // Steam, or another controller tool, holds the command interface while it runs. Retried for a while so
        // closing it after plugging the controller in is enough.
        const int AccessDeniedAttempts = 5;
        const int AccessDeniedRetryDelayMs = 2000;

        // Work handed back to the main thread, run from the Input System's own update.
        static readonly ConcurrentQueue<Action> s_MainThreadWork = new ConcurrentQueue<Action>();
        static bool s_Draining;

        static bool s_AccessDeniedReported;
        static bool s_AmbiguousCalibrationReported;

        // A controller that has just arrived on its cable, especially one coming off Bluetooth, can miss the flash
        // reads of the first setup while answering everything else. One more try a moment later usually reads it.
        const int CalibrationRetryDelayMs = 1000;

        public static void Request(Switch2ProController device)
        {
            Request(device, false);
        }

        static void Request(Switch2ProController device, bool calibrationRetry)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!s_Draining)
            {
                s_Draining = true;
                InputSystem.onBeforeUpdate += DrainMainThreadWork;
            }

            Task.Run(() =>
            {
                if (calibrationRetry)
                {
                    Thread.Sleep(CalibrationRetryDelayMs);
                }
                return InitializeWithRetries();
            }).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    return;
                }
                s_MainThreadWork.Enqueue(() => Apply(device, task.Result, calibrationRetry));
            });
#endif
        }

        static void DrainMainThreadWork()
        {
            while (s_MainThreadWork.TryDequeue(out var work))
            {
                work();
            }
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        static List<Switch2CommandInterface.Result> InitializeWithRetries()
        {
            List<Switch2CommandInterface.Result> results = null;
            for (var attempt = 0; attempt < AccessDeniedAttempts; attempt++)
            {
                results = Switch2CommandInterface.InitializeAll();
                if (!results.Exists(result => result.outcome == Switch2CommandInterface.Outcome.AccessDenied))
                {
                    break;
                }
                Thread.Sleep(AccessDeniedRetryDelayMs);
            }
            return results;
        }

        static void Apply(Switch2ProController device, List<Switch2CommandInterface.Result> results, bool calibrationRetry)
        {
            if (!device.added)
            {
                return;
            }

            var initialized = new List<Switch2CommandInterface.Result>();
            foreach (var result in results)
            {
                switch (result.outcome)
                {
                    case Switch2CommandInterface.Outcome.Initialized:
                        initialized.Add(result);
                        break;
                    case Switch2CommandInterface.Outcome.AccessDenied:
                        if (!s_AccessDeniedReported)
                        {
                            s_AccessDeniedReported = true;
                            Debug.LogWarning("[JoySense] Another application (usually Steam) holds the Switch 2 Pro " +
                                "Controller's command interface, so it cannot be set up. Close it and reconnect the controller.");
                        }
                        break;
                    default:
                        Debug.LogWarning($"[JoySense] Switch 2 Pro Controller setup failed: {result.detail}.");
                        break;
                }
            }

            if (initialized.Count == 0)
            {
                return;
            }
            s_AccessDeniedReported = false;

            // Which USB interface belongs to which Input System device cannot be told apart yet: Unity reports no
            // device path for a HID device, and this controller's serial number is the same on every unit. With
            // one controller there is nothing to tell apart.
            var count = 0;
            foreach (var other in InputSystem.devices)
            {
                if (other is Switch2ProController controller && !controller.isBluetooth)
                {
                    count++;
                }
            }
            if (count > 1 || initialized.Count > 1)
            {
                if (!s_AmbiguousCalibrationReported)
                {
                    s_AmbiguousCalibrationReported = true;
                    Debug.LogWarning("[JoySense] Several Switch 2 Pro Controllers are connected; their sticks use a " +
                        "fixed range instead of each controller's own calibration.");
                }
                return;
            }

            var setup = initialized[0];
            if (setup.hasLeftStick && setup.hasRightStick)
            {
                device.ApplyCalibration(setup.leftStick, setup.rightStick);
            }
            // A device that already has its calibration keeps it when a repeated setup misses the read.
            else if (!device.isCalibrated)
            {
                if (!calibrationRetry)
                {
                    Request(device, true);
                }
                else
                {
                    Debug.LogWarning("[JoySense] Could not read the Switch 2 Pro Controller's stick calibration; " +
                        "its sticks use a fixed range.");
                }
            }
        }
#endif
    }
}
