using Fofuxo.JoySense.Controls;
using Fofuxo.JoySense.Switch2;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace Fofuxo.JoySense
{
    /// <summary>
    /// Registers the package's layouts with the Input System and starts looking for Bluetooth controllers, in the
    /// Editor on load and in players before the first scene.
    /// </summary>
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public static class JoySenseSupport
    {
        static bool s_Initialized;

        static JoySenseSupport()
        {
            Initialize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeInPlayer()
        {
            Initialize();
        }

        static void FilterReports(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is Switch2ProController controller)
            {
                controller.FilterReport(eventPtr);
            }
        }

        /// <summary>
        /// Takes the Input System's global dead zone off every stick, so that the value reaches bindings untouched and
        /// each one can set its own with <see cref="JoySenseStickDeadzoneProcessor"/>.
        /// </summary>
        /// <remarks>
        /// Applies to all gamepads, not just this package's, and anything that reads a stick without such a binding has
        /// no dead zone left. So it is the game's choice, made once at startup. Bindings to a stick's up, down, left and
        /// right are not affected in practice, since those still press at half deflection.
        /// </remarks>
        public static void RemoveGlobalStickDeadzone()
        {
            InputSystem.settings.defaultDeadzoneMin = 0f;
            InputSystem.settings.defaultDeadzoneMax = 1f;
        }

        public static void Initialize()
        {
            if (s_Initialized)
            {
                return;
            }
            s_Initialized = true;

            InputSystem.RegisterLayout<CalibratedStickAxis>(nameof(CalibratedStickAxis));
            InputSystem.RegisterLayout<CalibratedStickButton>(nameof(CalibratedStickButton));
            InputSystem.RegisterLayout<MotionSensorAxis>(nameof(MotionSensorAxis));
            InputSystem.RegisterLayout<MotionLookControl>(nameof(MotionLookControl));
            InputSystem.RegisterProcessor<JoySenseStickDeadzoneProcessor>("JoySenseStickDeadzone");
            InputSystem.RegisterProcessor<JoySenseGyroDeadzoneProcessor>("JoySenseGyroDeadzone");

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            InputSystem.RegisterLayout<Switch2ProController>(
                matches: new InputDeviceMatcher()
                    .WithInterface("HID")
                    .WithCapability("vendorId", Switch2ProController.VendorId)
                    .WithCapability("productId", Switch2ProController.ProductId));
            InputSystem.RegisterLayoutMatcher<Switch2ProController>(
                new InputDeviceMatcher()
                    .WithInterface(Switch2ProController.BluetoothInterface)
                    .WithCapability("productId", Switch2ProController.ProductId));

            Switch2Bluetooth.Start();
#endif

            InputSystem.onEvent += FilterReports;
        }
    }
}
