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
        static bool s_ControllersEnabled = true;

#if UNITY_EDITOR
        const string ControllersDisabledKey = "Fofuxo.JoySense.ControllersDisabledInEditor";
        const string DisableControllersMenuPath = "Tools/Fofuxo JoySense/Disable Controllers in Editor";
#endif

        static JoySenseSupport()
        {
            Initialize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeInPlayer()
        {
            Initialize();
        }

        /// <summary>
        /// Whether the package talks to controllers: connects over Bluetooth, sets them up on the cable and feeds
        /// their input to the game. Always in players. In the Editor, until Tools > Fofuxo JoySense > Disable
        /// Controllers in Editor is checked, which leaves the controllers to another program on this PC, such as
        /// another engine with its own controller support.
        /// </summary>
        /// <remarks>
        /// The layouts stay registered either way, so bindings and code that name this package's controls keep
        /// working; the controllers just do not show up, or show up disabled.
        /// </remarks>
        public static bool controllersEnabled => s_ControllersEnabled;

#if UNITY_EDITOR
        // An Editor preference, so it holds for every project on this machine, which is where the other program is.
        [UnityEditor.MenuItem(DisableControllersMenuPath)]
        static void ToggleControllers()
        {
            s_ControllersEnabled = !s_ControllersEnabled;
            UnityEditor.EditorPrefs.SetBool(ControllersDisabledKey, !s_ControllersEnabled);
            if (Application.isBatchMode)
            {
                return;
            }

#if UNITY_EDITOR_WIN
            if (s_ControllersEnabled)
            {
                Switch2Bluetooth.Start();
            }
            else
            {
                Switch2Bluetooth.Stop();
            }
#endif
            foreach (var device in InputSystem.devices)
            {
                ApplyControllersEnabled(device);
            }
            Debug.Log(s_ControllersEnabled
                ? "[JoySense] Controllers turned back on."
                : "[JoySense] Controllers turned off in the Editor: other programs on this PC can use them.");
        }

        [UnityEditor.MenuItem(DisableControllersMenuPath, true)]
        static bool ToggleControllersValidate()
        {
            UnityEditor.Menu.SetChecked(DisableControllersMenuPath, !s_ControllersEnabled);
            return true;
        }
#endif

        // A cable controller stays a device while turned off, since Unity's HID backend is what finds it; it is
        // disabled instead, so its input reaches nothing, and set up again when turned back on, since the other
        // program may have left it in a mode of its own. Bluetooth ones are removed and added by Switch2Bluetooth.
        static void ApplyControllersEnabled(InputDevice device)
        {
            if (!(device is Switch2ProController controller) || controller.isBluetooth)
            {
                return;
            }

            if (!s_ControllersEnabled)
            {
                if (controller.enabled)
                {
                    InputSystem.DisableDevice(controller);
                }
                return;
            }

            if (!controller.enabled)
            {
                InputSystem.EnableDevice(controller);
                Switch2Setup.Request(controller);
            }
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added && !s_ControllersEnabled)
            {
                ApplyControllersEnabled(device);
            }
        }

        static void FilterReports(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is Switch2ProController controller && controller.enabled)
            {
                controller.FilterReport(eventPtr);
            }
        }

        /// <summary>
        /// Takes the Input System's global dead zone off every stick, so that the value reaches bindings untouched and
        /// each one can set its own with the Input System's Stick Deadzone processor, giving both values:
        /// <c>StickDeadzone(min=0.15,max=0.925)</c>. Left out, either one falls back to the global setting.
        /// </summary>
        /// <remarks>
        /// Applies to all gamepads, not just this package's, and anything that reads a stick without such a binding has
        /// no dead zone left. So it is the game's choice, made once at startup. Setting Default Deadzone Min to 0 and
        /// Max to 1 in the Input System's project settings does the same without code, and keeps working if this
        /// package is removed. Bindings to a stick's up, down, left and right are not affected in practice, since those
        /// still press at half deflection.
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

#if UNITY_EDITOR
            s_ControllersEnabled = !UnityEditor.EditorPrefs.GetBool(ControllersDisabledKey, false);
#endif

            InputSystem.RegisterLayout<CalibratedStickAxis>(nameof(CalibratedStickAxis));
            InputSystem.RegisterLayout<CalibratedStickButton>(nameof(CalibratedStickButton));
            InputSystem.RegisterLayout<MotionSensorAxis>(nameof(MotionSensorAxis));
            InputSystem.RegisterLayout<MotionLookControl>(nameof(MotionLookControl));

            // Before the layouts that match controllers, so that one found while turned off is disabled as it arrives.
            InputSystem.onDeviceChange += OnDeviceChange;

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

            // Not in batch mode, which is also how the Editor's asset import workers run: this code loads in them too,
            // and each one would connect to the controller alongside the Editor and keep the link up after it let go.
            if (!Application.isBatchMode && s_ControllersEnabled)
            {
                Switch2Bluetooth.Start();
            }
#endif

            // Devices a domain reload restored keep their own state; one restored enabled is turned off here.
            if (!s_ControllersEnabled)
            {
                foreach (var device in InputSystem.devices)
                {
                    ApplyControllersEnabled(device);
                }
            }

            InputSystem.onEvent += FilterReports;
        }
    }
}
