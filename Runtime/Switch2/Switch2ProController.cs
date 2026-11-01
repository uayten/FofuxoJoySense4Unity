using System.Runtime.InteropServices;
using Fofuxo.JoySense.Controls;
using Fofuxo.JoySense.Motion;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting;

namespace Fofuxo.JoySense.Switch2
{
    /// <summary>
    /// The Switch 2 controllers' input report 0x05 as Unity's HID backend delivers it: 64 bytes, report id first.
    /// </summary>
    /// <remarks>
    /// Decoded from raw dumps and a USB capture of Steam, in JoyShockLibrary4Unreal. One layout serves all three
    /// Switch 2 shapes; a controller leaves at zero every field it has no hardware for. Buttons follow Unity's
    /// positional convention: <c>buttonSouth</c> is the bottom face button, which is B on a Nintendo controller.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct Switch2ProControllerHIDInputState : IInputStateTypeInfo
    {
        public static FourCC Format => new FourCC('H', 'I', 'D');

        public FourCC format => Format;

        [FieldOffset(0)] public byte reportId;

        // The button field is one little-endian 32-bit word at bytes 5-8, read here a byte at a time. Its top six
        // bits are not buttons: a Joy-Con reports status there.
        [InputControl(name = "buttonWest", displayName = "Y", shortDisplayName = "Y", bit = 0)]
        [InputControl(name = "buttonNorth", displayName = "X", shortDisplayName = "X", bit = 1)]
        // Positions stay positional, but the usages follow Nintendo's scheme, as in Unity's
        // SwitchProControllerHID: A confirms and B goes back, so {Submit}/{Cancel} bindings land on them.
        [InputControl(name = "buttonSouth", displayName = "B", shortDisplayName = "B", bit = 2, usages = new[] { "Back", "Cancel" })]
        [InputControl(name = "buttonEast", displayName = "A", shortDisplayName = "A", bit = 3, usages = new[] { "PrimaryAction", "Submit" })]
        [InputControl(name = "rightShoulder", displayName = "R", shortDisplayName = "R", bit = 6)]
        [InputControl(name = "rightTrigger", displayName = "ZR", shortDisplayName = "ZR", format = "BIT", bit = 7, sizeInBits = 1)]
        [FieldOffset(5)] public byte buttons0;

        [InputControl(name = "select", displayName = "Minus", bit = 0)]
        [InputControl(name = "start", displayName = "Plus", bit = 1)]
        [InputControl(name = "rightStickPress", displayName = "Right Stick Press", bit = 2)]
        [InputControl(name = "leftStickPress", displayName = "Left Stick Press", bit = 3)]
        [InputControl(name = "home", layout = "Button", displayName = "Home", bit = 4)]
        [InputControl(name = "capture", layout = "Button", displayName = "Capture", bit = 5)]
        [InputControl(name = "chat", layout = "Button", displayName = "C", shortDisplayName = "C", bit = 6)]
        [FieldOffset(6)] public byte buttons1;

        [InputControl(name = "dpad", format = "BIT", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/down", bit = 0)]
        [InputControl(name = "dpad/up", bit = 1)]
        [InputControl(name = "dpad/right", bit = 2)]
        [InputControl(name = "dpad/left", bit = 3)]
        [InputControl(name = "leftShoulder", displayName = "L", shortDisplayName = "L", bit = 6)]
        [InputControl(name = "leftTrigger", displayName = "ZL", shortDisplayName = "ZL", format = "BIT", bit = 7, sizeInBits = 1)]
        [FieldOffset(7)] public byte buttons2;

        // The rear grip buttons exist only on the Pro Controller 2.
        [InputControl(name = "rightGrip", layout = "Button", displayName = "GR", shortDisplayName = "GR", bit = 0)]
        [InputControl(name = "leftGrip", layout = "Button", displayName = "GL", shortDisplayName = "GL", bit = 1)]
        [FieldOffset(8)] public byte buttons3;

        // Sticks: two 12-bit axes packed into 3 bytes each, centre around 0x800. The values go through each
        // controller's own calibration (see CalibratedStickAxis); the up/down/left/right children take their
        // state from x and y, as in Unity's StickControl, and are calibrated the same way.
        [InputControl(name = "leftStick", layout = "Stick", format = "BIT", sizeInBits = 24)]
        [InputControl(name = "leftStick/x", layout = "CalibratedStickAxis", format = "BIT", offset = 0, bit = 0, sizeInBits = 12)]
        [InputControl(name = "leftStick/y", layout = "CalibratedStickAxis", format = "BIT", offset = 1, bit = 4, sizeInBits = 12)]
        [InputControl(name = "leftStick/up", layout = "CalibratedStickButton")]
        [InputControl(name = "leftStick/down", layout = "CalibratedStickButton")]
        [InputControl(name = "leftStick/left", layout = "CalibratedStickButton")]
        [InputControl(name = "leftStick/right", layout = "CalibratedStickButton")]
        [FieldOffset(11)] public byte leftStick;

        [InputControl(name = "rightStick", layout = "Stick", format = "BIT", sizeInBits = 24)]
        [InputControl(name = "rightStick/x", layout = "CalibratedStickAxis", format = "BIT", offset = 0, bit = 0, sizeInBits = 12)]
        [InputControl(name = "rightStick/y", layout = "CalibratedStickAxis", format = "BIT", offset = 1, bit = 4, sizeInBits = 12)]
        [InputControl(name = "rightStick/up", layout = "CalibratedStickButton")]
        [InputControl(name = "rightStick/down", layout = "CalibratedStickButton")]
        [InputControl(name = "rightStick/left", layout = "CalibratedStickButton")]
        [InputControl(name = "rightStick/right", layout = "CalibratedStickButton")]
        [FieldOffset(14)] public byte rightStick;

        // IMU: int16 LE triplets at bytes 49-54 (accel) and 55-60 (gyro), one sample per report. Bytes 61-62 are always
        // zero (padding) -- reading the gyro from 57 leaves one axis permanently dead. The word order was measured on
        // hardware in JoyShockLibrary4Unreal against a Switch 1 controller held in identical poses: X, Z and Y for both
        // sensors, so y reads the third word and z the second. The scale and the signs are set by the device (see
        // Switch2ProController.FinishSetup). Noisy, since they change on every report even at rest.
        [InputControl(name = "accelerometer", layout = "Vector3", displayName = "Accelerometer", format = "BIT", sizeInBits = 48, noisy = true)]
        [InputControl(name = "accelerometer/x", layout = "MotionSensorAxis", format = "SHRT", offset = 0, sizeInBits = 16, noisy = true)]
        [InputControl(name = "accelerometer/y", layout = "MotionSensorAxis", format = "SHRT", offset = 4, sizeInBits = 16, noisy = true)]
        [InputControl(name = "accelerometer/z", layout = "MotionSensorAxis", format = "SHRT", offset = 2, sizeInBits = 16, noisy = true)]
        [FieldOffset(49)] public short accelerometerWord0;
        [FieldOffset(51)] public short accelerometerWord1;
        [FieldOffset(53)] public short accelerometerWord2;

        [InputControl(name = "gyroLook", layout = "MotionLookControl", displayName = "Gyro Look", format = "BIT", sizeInBits = 48, noisy = true)]
        [InputControl(name = "gyro", layout = "Vector3", displayName = "Gyro", format = "BIT", sizeInBits = 48, noisy = true)]
        [InputControl(name = "gyro/x", layout = "MotionSensorAxis", format = "SHRT", offset = 0, sizeInBits = 16, noisy = true)]
        [InputControl(name = "gyro/y", layout = "MotionSensorAxis", format = "SHRT", offset = 4, sizeInBits = 16, noisy = true)]
        [InputControl(name = "gyro/z", layout = "MotionSensorAxis", format = "SHRT", offset = 2, sizeInBits = 16, noisy = true)]
        [FieldOffset(55)] public short gyroWord0;
        [FieldOffset(57)] public short gyroWord1;
        [FieldOffset(59)] public short gyroWord2;
    }

    /// <summary>
    /// A Nintendo Switch 2 Pro Controller, connected over USB or Bluetooth.
    /// </summary>
    /// <remarks>
    /// Reaches the game as an ordinary <see cref="Gamepad"/>. The controller only streams input once it has been
    /// sent its init sequence: over USB through its WinUSB interface, when Unity's HID backend adds the device;
    /// over Bluetooth by the package itself, which connects to it and feeds its reports to a device of this
    /// layout. Its sticks read a fixed fallback range until its own calibration has been read back.
    /// <para>
    /// Motion is in Unity's axes, with the controller lying flat, face up and its grips towards the player: x to the
    /// right, y up, z away from the player. Rotations follow the left-hand rule, as in
    /// <see cref="Transform.Rotate(Vector3)"/>, so <see cref="orientation"/> can be assigned to a transform directly.
    /// </para>
    /// </remarks>
    [Preserve]
    [InputControlLayout(stateType = typeof(Switch2ProControllerHIDInputState), displayName = "Switch 2 Pro Controller")]
    public class Switch2ProController : Gamepad, IMotionController
    {
        internal const int VendorId = 0x057E;
        internal const int ProductId = 0x2069;

        // The only input report the layout reads.
        const byte InputReportId = 0x05;

        // How often a controller streaming the wrong report is set up again while it keeps doing so.
        const double SetupRetrySeconds = 0.75;

        // The interface name of the devices the package adds for controllers connected over Bluetooth, which
        // Unity's HID backend cannot see.
        internal const string BluetoothInterface = "JoySenseBLE";

        // A stick's travel from centre on a Pro Controller 2 is about 1600 counts. Only used until the
        // controller's own calibration arrives, or when it cannot be read.
        static readonly StickCalibration s_FallbackCalibration = new StickCalibration(
            new AxisCalibration(2048 - 1600, 2048, 2048 + 1600),
            new AxisCalibration(2048 - 1600, 2048, 2048 + 1600));

        // A report long enough to carry the IMU, and where its two triplets start.
        const int MotionReportLength = 61;
        const int AccelerometerOffset = 49;
        const int GyroOffset = 55;

        // Accel is about 1 g = 4096 counts. A Pro Controller 2 reads 14.2857 counts per degree per second (the Switch 1's
        // 936/13371); a Joy-Con 2 reads 16.384, so this is per shape once there are other shapes.
        const float AccelerometerScale = 1f / 4096f;
        const float GyroScale = 936f / 13371f;

        // A longer gap between samples is the game or the link having stalled. The rotation during it is lost either way,
        // and multiplying the next sample by the whole gap would invent one.
        const float MaxMotionInterval = 0.1f;

        // On from the moment the controller exists, as in JoyShockLibrary4Unreal: a game that never thinks about
        // calibration behaves correctly, and one that wants to own it can switch to manual.
        const MotionCalibrationMode AutomaticCalibration = MotionCalibrationMode.SensorFusion | MotionCalibrationMode.Stillness;

        // A longer gap between calls to ConsumeRotation means nobody was reading: a menu, or a screen without a camera.
        const double ConsumeStaleSeconds = 0.25;

        public ButtonControl homeButton { get; protected set; }
        public ButtonControl captureButton { get; protected set; }

        /// <summary>The C (GameChat) button.</summary>
        public ButtonControl chatButton { get; protected set; }

        public ButtonControl leftGripButton { get; protected set; }
        public ButtonControl rightGripButton { get; protected set; }

        /// <summary>Whether the controller is connected over Bluetooth rather than USB.</summary>
        public bool isBluetooth => description.interfaceName == BluetoothInterface;

        /// <summary>Whether the sticks read the controller's own calibration rather than the fallback range.</summary>
        public bool isCalibrated { get; private set; }

        /// <summary>Angular velocity of the latest sample, in degrees per second, without its drift.</summary>
        /// <remarks>
        /// The controller sends far more samples than a game has frames. To turn something by the controller's motion,
        /// use <see cref="gyroLook"/> or <see cref="ConsumeRotation"/>, which count all of them.
        /// </remarks>
        public Vector3Control gyro { get; protected set; }

        /// <summary>How far the controller turned this input update, in <see cref="gyroSpace"/>, for binding to an action.</summary>
        public MotionLookControl gyroLook { get; protected set; }

        /// <summary>The latest accelerometer sample, in g. At rest it reads 1 g upwards.</summary>
        public Vector3Control accelerometer { get; protected set; }

        /// <summary>Whether the controller has sent any motion sample yet.</summary>
        public bool hasMotion { get; private set; }

        /// <summary>The controller's attitude, levelled by gravity. Its yaw is relative and drifts slowly.</summary>
        public Quaternion orientation => ToUnityRotation(m_Motion.orientation);

        /// <summary>The attitude from the gyro alone, without gravity's correction: smoother, but its tilt drifts.</summary>
        public Quaternion rawOrientation => ToUnityRotation(m_Motion.rawOrientation);

        /// <summary>The direction of gravity in the controller's own frame, about 1 g long.</summary>
        public Vector3 gravity => ToUnityVector(m_Motion.gravity);

        /// <summary>The acceleration from moving the controller, in g, with gravity taken out.</summary>
        public Vector3 linearAcceleration => ToUnityVector(m_Motion.processedAcceleration);

        /// <summary>
        /// The space <see cref="gyroLook"/> and <see cref="ConsumeRotation"/> report in. Player by default, which works
        /// however the controller is held.
        /// </summary>
        public GyroSpace gyroSpace { get; set; } = GyroSpace.Player;

        /// <summary>
        /// Turning slower than this, in degrees per second, counts as no turn in <see cref="ConsumeRotation"/>. Faster
        /// turning loses this much speed, so the camera does not jump at the threshold. A binding to
        /// <see cref="gyroLook"/> sets its own with <see cref="JoySenseGyroDeadzoneProcessor"/> instead.
        /// </summary>
        public float gyroDeadZone { get; set; }

        /// <summary>Automatic works out the drift by itself. Manual leaves it to <see cref="StartGyroCalibration"/>.</summary>
        public GyroCalibrationMode gyroCalibrationMode
        {
            get => m_Motion.calibrationMode == MotionCalibrationMode.Manual ? GyroCalibrationMode.Manual : GyroCalibrationMode.Automatic;
            set
            {
                m_Motion.calibrationMode = value == GyroCalibrationMode.Automatic ? AutomaticCalibration : MotionCalibrationMode.Manual;
                if (value == GyroCalibrationMode.Automatic)
                {
                    isGyroCalibrating = false;
                    m_Motion.PauseContinuousCalibration();
                }
            }
        }

        /// <summary>Whether a manual calibration is gathering samples.</summary>
        public bool isGyroCalibrating { get; private set; }

        /// <summary>How sure the automatic calibration is of the drift, from 0 to 1.</summary>
        public float gyroCalibrationConfidence => m_Motion.autoCalibrationConfidence;

        /// <summary>Whether the automatic calibration currently takes the controller to be steady.</summary>
        public bool isGyroSteady => m_Motion.autoCalibrationIsSteady;

        /// <summary>The drift being taken out of <see cref="gyro"/>, in degrees per second. Can be saved and restored.</summary>
        public Vector3 gyroCalibrationOffset
        {
            get => ToUnityAngular(m_Motion.GetCalibrationOffset());
            set
            {
                m_Motion.SetCalibrationOffset(ToUnityAngular(value), 1);
                ApplyGyroBias();
            }
        }

        // How many motion samples have arrived, for the overlay to show their rate.
        internal long motionSampleCount { get; private set; }

        readonly GamepadMotion m_Motion = new GamepadMotion { calibrationMode = AutomaticCalibration };
        MotionSensorAxis m_GyroX;
        MotionSensorAxis m_GyroY;
        MotionSensorAxis m_GyroZ;
        bool m_CueMotionReset;
        double m_LastMotionTime = double.NegativeInfinity;
        Vector3 m_AccumulatedRotation;
        double m_LastConsumeTime = double.NegativeInfinity;

        bool m_ReceivedValidReport;
        double m_LastSetupRequestTime = double.NegativeInfinity;

        protected override void FinishSetup()
        {
            base.FinishSetup();

            homeButton = GetChildControl<ButtonControl>("home");
            captureButton = GetChildControl<ButtonControl>("capture");
            chatButton = GetChildControl<ButtonControl>("chat");
            leftGripButton = GetChildControl<ButtonControl>("leftGrip");
            rightGripButton = GetChildControl<ButtonControl>("rightGrip");
            gyro = GetChildControl<Vector3Control>("gyro");
            gyroLook = GetChildControl<MotionLookControl>("gyroLook");
            accelerometer = GetChildControl<Vector3Control>("accelerometer");

            m_GyroX = (MotionSensorAxis)gyro.x;
            m_GyroY = (MotionSensorAxis)gyro.y;
            m_GyroZ = (MotionSensorAxis)gyro.z;

            // Unity's axes from the sensors' words, through the same conversion as ToUnityVector and ToUnityAngular.
            m_GyroX.sensorScale = -GyroScale;
            m_GyroY.sensorScale = -GyroScale;
            m_GyroZ.sensorScale = -GyroScale;
            ((MotionSensorAxis)accelerometer.x).sensorScale = AccelerometerScale;
            ((MotionSensorAxis)accelerometer.y).sensorScale = AccelerometerScale;
            ((MotionSensorAxis)accelerometer.z).sensorScale = AccelerometerScale;

            SetStickCalibration(s_FallbackCalibration, s_FallbackCalibration);
        }

        /// <summary>
        /// Returns how far the controller has turned since the last call, in degrees and in <see cref="gyroSpace"/>, and
        /// starts counting again.
        /// </summary>
        /// <remarks>
        /// Meant to be called once a frame by whatever the gyro aims. A second caller only gets what was left since the
        /// first. In world and player space, x is pitch and y is yaw, and z stays at zero. After a gap with no calls, what
        /// the controller did meanwhile is dropped, so coming back from a menu does not jump the camera.
        /// </remarks>
        public Vector3 ConsumeRotation()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var rotation = now - m_LastConsumeTime > ConsumeStaleSeconds ? Vector3.zero : m_AccumulatedRotation;
            m_LastConsumeTime = now;
            m_AccumulatedRotation = Vector3.zero;
            return rotation;
        }

        /// <summary>Makes the current pose the origin of <see cref="orientation"/>, which gravity levels again.</summary>
        public void ResetOrientation()
        {
            m_Motion.ResetMotion();
        }

        /// <summary>
        /// Starts gathering samples for a manual calibration, with the controller held still, and throws away the previous
        /// one. The longer it gathers, the better. Only has an effect in <see cref="GyroCalibrationMode.Manual"/>.
        /// </summary>
        public void StartGyroCalibration()
        {
            isGyroCalibrating = true;
            m_CueMotionReset = true;
        }

        /// <summary>Stops gathering samples, keeping what they gave.</summary>
        public void StopGyroCalibration()
        {
            isGyroCalibrating = false;
        }

        /// <summary>Forgets the calibration, whichever way it was obtained.</summary>
        public void ResetGyroCalibration()
        {
            m_Motion.ResetContinuousCalibration();
            ApplyGyroBias();
        }

        protected override void OnAdded()
        {
            base.OnAdded();
            InputSystem.onBeforeUpdate += ClearFrameTurn;
            if (!isBluetooth)
            {
                Switch2Setup.Request(this);
            }
        }

        protected override void OnRemoved()
        {
            InputSystem.onBeforeUpdate -= ClearFrameTurn;
            base.OnRemoved();
        }

        // Events are processed right after this, so gyroLook holds exactly the samples of the current input update.
        void ClearFrameTurn()
        {
            gyroLook.frameTurn = Vector2.zero;
            gyroLook.frameSeconds = 0f;
        }

        /// <summary>
        /// Keeps reports the layout cannot read from reaching the controls. Called for each of this device's events
        /// before the Input System applies it.
        /// </summary>
        /// <remarks>
        /// The controller keeps its report mode across transports: one set up over Bluetooth, which asks for report
        /// 0x09, goes on sending 0x09 when its cable is plugged in, and over USB that report puts the left stick
        /// where this layout reads buttons -- every button pressing and releasing at random. Plugging in also
        /// starts the cable's setup, which asks for 0x05 again, but a request that lands while the controller is
        /// still leaving the radio is ignored. So a report in any other format is rewritten into "nothing changed"
        /// before the Input System sees it, and the setup is repeated for as long as such reports keep coming.
        /// </remarks>
        internal unsafe void FilterReport(InputEventPtr eventPtr)
        {
            if (!eventPtr.IsA<StateEvent>())
            {
                return;
            }

            var stateEvent = StateEvent.From(eventPtr);
            var size = (int)stateEvent->stateSizeInBytes;
            if (stateEvent->stateFormat != Switch2ProControllerHIDInputState.Format || size == 0)
            {
                return;
            }

            var report = (byte*)stateEvent->state;
            if (report[0] == InputReportId)
            {
                m_ReceivedValidReport = true;
                if (size >= MotionReportLength)
                {
                    ProcessMotion(report, eventPtr.time);
                }
                return;
            }

            // The device's own state covers the start of the report, so writing it back over the event leaves every
            // control where it was. Before any valid report there is nothing to keep, and the event becomes a
            // controller at rest instead: no buttons, both sticks centred.
            var stateSize = (int)((stateBlock.sizeInBits + 7) / 8);
            var length = size < stateSize ? size : stateSize;
            if (m_ReceivedValidReport)
            {
                UnsafeUtility.MemCpy(report, (byte*)currentStatePtr + stateBlock.byteOffset, length);
            }
            else
            {
                UnsafeUtility.MemClear(report, length);
                if (length >= 17)
                {
                    // Two 12-bit axes of 0x800 per stick, packed as in the report.
                    report[12] = 0x08;
                    report[13] = 0x80;
                    report[15] = 0x08;
                    report[16] = 0x80;
                }
            }
            report[0] = InputReportId;

            var now = Time.realtimeSinceStartupAsDouble;
            if (!isBluetooth && now - m_LastSetupRequestTime >= SetupRetrySeconds)
            {
                m_LastSetupRequestTime = now;
                Debug.Log("[JoySense] The Switch 2 Pro Controller is sending input in a format this layout does not read; " +
                    "setting it up again.");
                Switch2Setup.Request(this);
            }
        }

        // Every sample has to reach the fusion, not just the one still in the state when a game reads it, and this is the
        // one place that sees them all. Works in the fusion's own space, which is JoyShockLibrary4Unreal's: right-handed
        // and Y-up. Accel words map (X,Y,Z) = (w0, w2, -w1), gyro words map pitch = w0, yaw = w2, roll = -w1.
        unsafe void ProcessMotion(byte* report, double time)
        {
            var accelerometerSample = new Vector3(
                ReadWord(report, AccelerometerOffset),
                ReadWord(report, AccelerometerOffset + 4),
                -ReadWord(report, AccelerometerOffset + 2)) * AccelerometerScale;
            var gyroSample = new Vector3(
                ReadWord(report, GyroOffset),
                ReadWord(report, GyroOffset + 4),
                -ReadWord(report, GyroOffset + 2)) * GyroScale;

            var deltaTime = Mathf.Clamp((float)(time - m_LastMotionTime), 0f, MaxMotionInterval);
            if (double.IsNegativeInfinity(m_LastMotionTime))
            {
                deltaTime = 0f;
            }
            m_LastMotionTime = time;

            if (m_CueMotionReset)
            {
                m_CueMotionReset = false;
                m_Motion.Reset();
            }
            if (m_Motion.calibrationMode == MotionCalibrationMode.Manual)
            {
                if (isGyroCalibrating)
                {
                    m_Motion.StartContinuousCalibration();
                }
                else
                {
                    m_Motion.PauseContinuousCalibration();
                }
            }

            m_Motion.ProcessMotion(gyroSample, accelerometerSample, deltaTime);
            ApplyGyroBias();

            hasMotion |= accelerometerSample != Vector3.zero || gyroSample != Vector3.zero;
            motionSampleCount++;

            var spaceSample = ToGyroSpace(m_Motion.calibratedGyro);
            m_AccumulatedRotation += ToUnityAngular(ApplyDeadZone(spaceSample)) * deltaTime;

            var turn = ToUnityAngular(spaceSample) * deltaTime;
            gyroLook.frameTurn += new Vector2(turn.y, -turn.x);
            gyroLook.frameSeconds += deltaTime;
        }

        Vector3 ApplyDeadZone(Vector3 rate)
        {
            var speed = rate.magnitude;
            return speed > gyroDeadZone ? rate * ((speed - gyroDeadZone) / speed) : Vector3.zero;
        }

        Vector3 ToGyroSpace(Vector3 sample)
        {
            switch (gyroSpace)
            {
                case GyroSpace.World:
                    return GamepadMotion.CalculateWorldSpaceGyro(sample, m_Motion.gravity);
                case GyroSpace.Player:
                    return GamepadMotion.CalculatePlayerSpaceGyro(sample, m_Motion.gravity);
                default:
                    return sample;
            }
        }

        void ApplyGyroBias()
        {
            var offset = ToUnityAngular(m_Motion.GetCalibrationOffset());
            m_GyroX.sensorBias = offset.x;
            m_GyroY.sensorBias = offset.y;
            m_GyroZ.sensorBias = offset.z;
        }

        static unsafe short ReadWord(byte* report, int offset)
        {
            return (short)(report[offset] | (report[offset + 1] << 8));
        }

        // Unity's left-handed axes point z the other way from the fusion's right-handed ones. A direction flips its z. A
        // rotation is a pseudovector, so it flips the other two instead, which is the same conversion in both directions.
        static Vector3 ToUnityVector(Vector3 value) => new Vector3(value.x, value.y, -value.z);
        static Vector3 ToUnityAngular(Vector3 value) => new Vector3(-value.x, -value.y, value.z);
        static Quaternion ToUnityRotation(Quaternion value) => new Quaternion(-value.x, -value.y, value.z, value.w);

        internal void ApplyCalibration(StickCalibration left, StickCalibration right)
        {
            SetStickCalibration(left, right);
            isCalibrated = true;
        }

        void SetStickCalibration(StickCalibration left, StickCalibration right)
        {
            SetStickCalibration(leftStick, left);

            // Both sticks report Y with the same sign: up is positive (verified on a Pro Controller 2 with
            // the Stick Overlay). JoyShockLibrary4Unreal negates the right Y in parse_switch2_report and
            // again in FJoyShockInterface, so its net result is the raw sign as well; porting only the
            // first negation turned the right stick upside down.
            SetStickCalibration(rightStick, right);
        }

        static void SetStickCalibration(StickControl stick, StickCalibration calibration)
        {
            SetAxisCalibration(stick.x, calibration.x);
            SetAxisCalibration(stick.left, calibration.x);
            SetAxisCalibration(stick.right, calibration.x);
            SetAxisCalibration(stick.y, calibration.y);
            SetAxisCalibration(stick.up, calibration.y);
            SetAxisCalibration(stick.down, calibration.y);
        }

        static void SetAxisCalibration(InputControl control, AxisCalibration calibration)
        {
            switch (control)
            {
                case CalibratedStickAxis axis:
                    axis.calibration = calibration;
                    break;
                case CalibratedStickButton button:
                    button.calibration = calibration;
                    break;
            }
        }
    }
}
