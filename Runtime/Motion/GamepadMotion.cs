// Copyright (c) 2020-2023 Julian "Jibb" Smart
// Released under the MIT license. See https://github.com/JibbSmart/GamepadMotionHelpers/blob/main/LICENSE for more info
// Version 9
//
// Ported from the copy in JoyShockLibrary4Unreal (GamepadMotion.hpp), which keeps RawQuaternion as an addition to
// upstream. Everything here works in the library's own space: right-handed and Y-up, gyro in degrees per second,
// acceleration in g. Converting to Unity's axes is left to the caller.

using UnityEngine;

namespace Fofuxo.JoySense.Motion
{
    [System.Flags]
    internal enum MotionCalibrationMode
    {
        Manual = 0,
        Stillness = 1,
        SensorFusion = 2,
    }

    internal sealed class MotionSettings
    {
        public int minStillnessSamples = 10;
        public float minStillnessCollectionTime = 0.5f;
        public float minStillnessCorrectionTime = 2f;
        public float maxStillnessError = 2f;
        public float stillnessSampleDeteriorationRate = 0.2f;
        public float stillnessErrorClimbRate = 0.1f;
        public float stillnessErrorDropOnRecalibrate = 0.1f;
        public float stillnessCalibrationEaseInTime = 3f;
        public float stillnessCalibrationHalfTime = 0.1f;
        public float stillnessConfidenceRate = 1f;

        public float stillnessGyroDelta = -1f;
        public float stillnessAccelDelta = -1f;

        public float sensorFusionCalibrationSmoothingStrength = 2f;
        public float sensorFusionAngularAccelerationThreshold = 20f;
        public float sensorFusionCalibrationEaseInTime = 3f;
        public float sensorFusionCalibrationHalfTime = 0.1f;
        public float sensorFusionConfidenceRate = 1f;

        public float gravityCorrectionShakinessMaxThreshold = 0.4f;
        public float gravityCorrectionShakinessMinThreshold = 0.01f;

        public float gravityCorrectionStillSpeed = 1f;
        public float gravityCorrectionShakySpeed = 0.1f;

        public float gravityCorrectionGyroFactor = 0.1f;
        public float gravityCorrectionGyroMinThreshold = 0.05f;
        public float gravityCorrectionGyroMaxThreshold = 0.25f;

        public float gravityCorrectionMinimumSpeed = 0.01f;
    }

    internal sealed class GyroCalibrationData
    {
        public Vector3 sum;
        public float accelMagnitude;
        public int numSamples;

        public void Clear()
        {
            sum = Vector3.zero;
            accelMagnitude = 0f;
            numSamples = 0;
        }
    }

    /// <summary>
    /// Sensor fusion for one controller: gyro drift calibration, gravity and orientation from its motion samples.
    /// </summary>
    internal sealed class GamepadMotion
    {
        readonly MotionSettings m_Settings = new MotionSettings();
        readonly GyroCalibrationData m_Calibration = new GyroCalibrationData();
        readonly OrientationFilter m_Filter;
        readonly AutoCalibration m_AutoCalibration;

        bool m_IsCalibrating;

        public GamepadMotion()
        {
            m_Filter = new OrientationFilter(m_Settings);
            m_AutoCalibration = new AutoCalibration(m_Settings, m_Calibration);
        }

        public MotionCalibrationMode calibrationMode { get; set; } = MotionCalibrationMode.Manual;
        public Vector3 calibratedGyro { get; private set; }
        public Vector3 gravity => m_Filter.gravity;
        public Vector3 processedAcceleration => m_Filter.acceleration;
        public Quaternion orientation => m_Filter.orientation;
        public Quaternion rawOrientation => m_Filter.rawOrientation;

        public float autoCalibrationConfidence
        {
            get => m_AutoCalibration.confidence;
            set => m_AutoCalibration.confidence = value;
        }

        public bool autoCalibrationIsSteady => m_AutoCalibration.isSteady;

        public void Reset()
        {
            m_Calibration.Clear();
            calibratedGyro = Vector3.zero;
            m_Filter.Reset();
        }

        public void ResetMotion()
        {
            m_Filter.Reset();
        }

        public void ProcessMotion(Vector3 gyro, Vector3 accel, float deltaTime)
        {
            if (gyro == Vector3.zero && accel == Vector3.zero)
            {
                // all zeroes are almost certainly not valid inputs
                return;
            }

            var accelMagnitude = accel.magnitude;

            if (m_IsCalibrating)
            {
                // manual calibration
                PushSensorSamples(gyro, accelMagnitude);
                m_AutoCalibration.NoSampleSensorFusion();
                m_AutoCalibration.NoSampleStillness();
            }
            else if ((calibrationMode & MotionCalibrationMode.Stillness) != 0)
            {
                m_AutoCalibration.AddSampleStillness(gyro, accel, deltaTime, (calibrationMode & MotionCalibrationMode.SensorFusion) != 0);
                m_AutoCalibration.NoSampleSensorFusion();
            }
            else
            {
                m_AutoCalibration.NoSampleStillness();
                if ((calibrationMode & MotionCalibrationMode.SensorFusion) != 0)
                {
                    m_AutoCalibration.AddSampleSensorFusion(gyro, accel, deltaTime);
                }
                else
                {
                    m_AutoCalibration.NoSampleSensorFusion();
                }
            }

            GetCalibratedSensor(out var gyroOffset, out accelMagnitude);
            gyro -= gyroOffset;

            m_Filter.Update(gyro, accel, accelMagnitude, deltaTime);

            calibratedGyro = gyro;
        }

        /// <summary>The gyro in player space: pitch in x, yaw in y.</summary>
        /// <remarks>
        /// Takes gravity into account without taking on any error from gravity. Explained in depth at
        /// http://gyrowiki.jibbsmart.com/blog:player-space-gyro-and-alternatives-explained#toc7
        /// </remarks>
        public static Vector2 CalculatePlayerSpaceGyro(Vector3 gyro, Vector3 gravity, float yawRelaxFactor = 1.41f)
        {
            var worldYaw = -(gravity.y * gyro.y + gravity.z * gyro.z);
            var worldYawSign = worldYaw < 0f ? -1f : 1f;
            var yaw = worldYawSign * Mathf.Min(Mathf.Abs(worldYaw) * yawRelaxFactor, Mathf.Sqrt(gyro.y * gyro.y + gyro.z * gyro.z));
            return new Vector2(gyro.x, yaw);
        }

        /// <summary>The gyro in world space: pitch in x, yaw in y.</summary>
        /// <remarks>
        /// Uses the gravity direction as the yaw axis, and derives an appropriate pitch axis. Explained in depth at
        /// http://gyrowiki.jibbsmart.com/blog:player-space-gyro-and-alternatives-explained#toc6
        /// </remarks>
        public static Vector2 CalculateWorldSpaceGyro(Vector3 gyro, Vector3 gravity, float sideReductionThreshold = 0.125f)
        {
            var worldYaw = -gravity.x * gyro.x - gravity.y * gyro.y - gravity.z * gyro.z;
            // project local pitch axis (X) onto gravity plane
            var gravDotPitchAxis = gravity.x;
            var pitchAxis = new Vector3(1f - gravity.x * gravDotPitchAxis, -gravity.y * gravDotPitchAxis, -gravity.z * gravDotPitchAxis);
            var pitch = 0f;
            var pitchAxisLengthSquared = pitchAxis.sqrMagnitude;
            if (pitchAxisLengthSquared > 0f)
            {
                pitchAxis *= 1f / Mathf.Sqrt(pitchAxisLengthSquared);

                var flatness = Mathf.Abs(gravity.y);
                var upness = Mathf.Abs(gravity.z);
                var sideReduction = sideReductionThreshold <= 0f
                    ? 1f
                    : Mathf.Clamp01((Mathf.Max(flatness, upness) - sideReductionThreshold) / sideReductionThreshold);

                pitch = sideReduction * Vector3.Dot(pitchAxis, gyro);
            }

            return new Vector2(pitch, worldYaw);
        }

        public void StartContinuousCalibration()
        {
            m_IsCalibrating = true;
        }

        public void PauseContinuousCalibration()
        {
            m_IsCalibrating = false;
        }

        public void ResetContinuousCalibration()
        {
            m_Calibration.Clear();
            m_AutoCalibration.Reset();
        }

        public Vector3 GetCalibrationOffset()
        {
            GetCalibratedSensor(out var offset, out _);
            return offset;
        }

        public void SetCalibrationOffset(Vector3 offset, int weight)
        {
            if (m_Calibration.numSamples > 1)
            {
                m_Calibration.accelMagnitude *= (float)weight / m_Calibration.numSamples;
            }
            else
            {
                m_Calibration.accelMagnitude = weight;
            }

            m_Calibration.numSamples = weight;
            m_Calibration.sum = offset * weight;
        }

        void PushSensorSamples(Vector3 gyro, float accelMagnitude)
        {
            // accumulate
            m_Calibration.numSamples++;
            m_Calibration.sum += gyro;
            m_Calibration.accelMagnitude += accelMagnitude;
        }

        void GetCalibratedSensor(out Vector3 gyroOffset, out float accelMagnitude)
        {
            if (m_Calibration.numSamples <= 0)
            {
                gyroOffset = Vector3.zero;
                accelMagnitude = 1f;
                return;
            }

            var inverseSamples = 1f / m_Calibration.numSamples;
            gyroOffset = m_Calibration.sum * inverseSamples;
            accelMagnitude = m_Calibration.accelMagnitude * inverseSamples;
        }
    }

    internal static class MotionMath
    {
        public static float Exp2(float value)
        {
            return Mathf.Pow(2f, value);
        }

        // Normalises the axis itself, and is in radians, unlike Quaternion.AngleAxis.
        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            var sinHalfAngle = Mathf.Sin(angle * 0.5f);
            var scaled = axis.normalized * sinHalfAngle;
            return new Quaternion(scaled.x, scaled.y, scaled.z, Mathf.Cos(angle * 0.5f));
        }

        public static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        public static Vector3 Lerp(Vector3 from, Vector3 to, Vector3 factor)
        {
            return new Vector3(
                from.x + (to.x - from.x) * factor.x,
                from.y + (to.y - from.y) * factor.y,
                from.z + (to.z - from.z) * factor.z);
        }

        public static bool AllLessOrEqual(Vector3 value, Vector3 limit, float scale)
        {
            return value.x <= limit.x * scale && value.y <= limit.y * scale && value.z <= limit.z * scale;
        }

        // The rate at which a measured direction turned, as an angular velocity in degrees per second.
        public static Vector3 AngularVelocityBetween(Vector3 thisNormal, Vector3 previousNormal, float seconds)
        {
            var angularVelocity = Vector3.Cross(thisNormal, previousNormal);
            var crossLength = angularVelocity.magnitude;
            if (crossLength > 0f)
            {
                var thisDotPrev = Mathf.Clamp(Vector3.Dot(thisNormal, previousNormal), -1f, 1f);
                var angleChange = Mathf.Acos(thisDotPrev) * Mathf.Rad2Deg;
                var anglePerSecond = angleChange / seconds;
                angularVelocity *= anglePerSecond / crossLength;
            }
            return angularVelocity;
        }
    }

    /// <summary>Integrates the gyro into an orientation and pulls it towards the measured gravity.</summary>
    internal sealed class OrientationFilter
    {
        const float ShortSteadinessHalfTime = 0.25f;

        readonly MotionSettings m_Settings;

        Vector3 m_SmoothAccel;
        float m_Shakiness;

        public OrientationFilter(MotionSettings settings)
        {
            m_Settings = settings;
            Reset();
        }

        // RawOrientation is like Orientation, except gravity correction isn't applied to it.
        public Quaternion rawOrientation { get; private set; }
        public Quaternion orientation { get; private set; }
        public Vector3 acceleration { get; private set; }
        public Vector3 gravity { get; private set; }

        public void Reset()
        {
            rawOrientation = Quaternion.identity;
            orientation = Quaternion.identity;
            acceleration = Vector3.zero;
            gravity = Vector3.zero;
            m_SmoothAccel = Vector3.zero;
            m_Shakiness = 0f;
        }

        /// <summary>
        /// The gyro input should be calibrated degrees per second but have no other processing. Acceleration is in G
        /// units (1 = approx. 9.8m/s^2).
        /// </summary>
        public void Update(Vector3 gyro, Vector3 accel, float gravityLength, float deltaTime)
        {
            var settings = m_Settings;
            var angleSpeed = gyro.magnitude * Mathf.Deg2Rad;
            var angle = angleSpeed * deltaTime;

            // rotate
            var rotation = MotionMath.AngleAxis(angle, gyro);
            var inverseRotation = Quaternion.Inverse(rotation);

            var newOrientation = orientation * rotation; // do it this way because it's a local rotation, not global
            rawOrientation = rawOrientation * rotation;

            var accelMagnitude = accel.magnitude;
            if (accelMagnitude > 0f)
            {
                var accelNorm = accel / accelMagnitude;
                // account for rotation when tracking smoothed acceleration
                m_SmoothAccel = inverseRotation * m_SmoothAccel;
                var smoothFactor = ShortSteadinessHalfTime <= 0f ? 0f : MotionMath.Exp2(-deltaTime / ShortSteadinessHalfTime);
                m_Shakiness *= smoothFactor;
                m_Shakiness = Mathf.Max(m_Shakiness, (accel - m_SmoothAccel).magnitude);
                m_SmoothAccel = Vector3.LerpUnclamped(accel, m_SmoothAccel, smoothFactor);

                // update grav by rotation
                var grav = inverseRotation * gravity;
                // we want to close the gap between grav and raw acceleration. What's the difference
                var gravToAccel = accelNorm * -gravityLength - grav;
                var gravToAccelDir = gravToAccel.normalized;
                // adjustment rate
                float gravCorrectionSpeed;
                if (settings.gravityCorrectionShakinessMinThreshold < settings.gravityCorrectionShakinessMaxThreshold)
                {
                    gravCorrectionSpeed = settings.gravityCorrectionStillSpeed + (settings.gravityCorrectionShakySpeed - settings.gravityCorrectionStillSpeed)
                        * Mathf.Clamp01((m_Shakiness - settings.gravityCorrectionShakinessMinThreshold) / (settings.gravityCorrectionShakinessMaxThreshold - settings.gravityCorrectionShakinessMinThreshold));
                }
                else
                {
                    gravCorrectionSpeed = m_Shakiness < settings.gravityCorrectionShakinessMaxThreshold ? settings.gravityCorrectionStillSpeed : settings.gravityCorrectionShakySpeed;
                }
                // we also limit it to be no faster than a given proportion of the gyro rate, or the minimum gravity correction speed
                var gyroGravCorrectionLimit = Mathf.Max(angleSpeed * settings.gravityCorrectionGyroFactor, settings.gravityCorrectionMinimumSpeed);
                if (gravCorrectionSpeed > gyroGravCorrectionLimit)
                {
                    float closeEnoughFactor;
                    if (settings.gravityCorrectionGyroMinThreshold < settings.gravityCorrectionGyroMaxThreshold)
                    {
                        closeEnoughFactor = Mathf.Clamp01((gravToAccel.magnitude - settings.gravityCorrectionGyroMinThreshold) / (settings.gravityCorrectionGyroMaxThreshold - settings.gravityCorrectionGyroMinThreshold));
                    }
                    else
                    {
                        closeEnoughFactor = gravToAccel.magnitude < settings.gravityCorrectionGyroMaxThreshold ? 0f : 1f;
                    }
                    gravCorrectionSpeed = gyroGravCorrectionLimit + (gravCorrectionSpeed - gyroGravCorrectionLimit) * closeEnoughFactor;
                }
                var gravToAccelDelta = gravToAccelDir * gravCorrectionSpeed * deltaTime;
                if (gravToAccelDelta.sqrMagnitude < gravToAccel.sqrMagnitude)
                {
                    grav += gravToAccelDelta;
                }
                else
                {
                    grav = accelNorm * -gravityLength;
                }
                gravity = grav;

                // JoyShockLibrary4Unreal found this block had drifted from upstream in three places, together the
                // long-standing "gravity correction is buggy" problem, and restored it: the measured gravity is
                // rotated by the orientation (not its inverse), the cross takes (gravityDirection, down) in that
                // order, and the correction is applied on the left, since it is derived in absolute space.
                var gravityDirection = newOrientation * gravity.normalized; // absolute gravity direction
                var errorAngle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(Vector3.down, gravityDirection), -1f, 1f));
                var flattened = Vector3.Cross(gravityDirection, Vector3.down);
                var correction = MotionMath.AngleAxis(errorAngle, flattened);

                newOrientation = correction * newOrientation;

                acceleration = accel + gravity;
            }
            else
            {
                gravity = inverseRotation * gravity;
                acceleration = gravity;
            }

            orientation = Quaternion.Normalize(newOrientation);
            rawOrientation = Quaternion.Normalize(rawOrientation);
        }
    }

    /// <summary>Works out the gyro's drift while the controller is held still or moved steadily.</summary>
    internal sealed class AutoCalibration
    {
        sealed class SensorMinMaxWindow
        {
            public Vector3 minGyro;
            public Vector3 maxGyro;
            public Vector3 meanGyro;
            public Vector3 minAccel;
            public Vector3 maxAccel;
            public Vector3 meanAccel;
            public Vector3 startAccel;
            public int numSamples;
            public float timeSampled;

            public void Reset(float remainder)
            {
                numSamples = 0;
                timeSampled = remainder;
            }

            public void AddSample(Vector3 gyro, Vector3 accel, float deltaTime)
            {
                if (numSamples == 0)
                {
                    maxGyro = gyro;
                    minGyro = gyro;
                    meanGyro = gyro;
                    maxAccel = accel;
                    minAccel = accel;
                    meanAccel = accel;
                    startAccel = accel;
                    numSamples = 1;
                    timeSampled += deltaTime;
                    return;
                }

                maxGyro = Vector3.Max(maxGyro, gyro);
                minGyro = Vector3.Min(minGyro, gyro);
                maxAccel = Vector3.Max(maxAccel, accel);
                minAccel = Vector3.Min(minAccel, accel);

                numSamples++;
                timeSampled += deltaTime;

                meanGyro += (gyro - meanGyro) * (1f / numSamples);
                meanAccel += (accel - meanAccel) * (1f / numSamples);
            }
        }

        readonly MotionSettings m_Settings;
        readonly GyroCalibrationData m_Calibration;
        readonly SensorMinMaxWindow m_Window = new SensorMinMaxWindow();

        Vector3 m_SmoothedAngularVelocityGyro;
        Vector3 m_SmoothedAngularVelocityAccel;
        Vector3 m_SmoothedPreviousAccel;
        Vector3 m_PreviousAccel;
        Vector3 m_MinDeltaGyro;
        Vector3 m_MinDeltaAccel;
        float m_RecalibrateThreshold;
        float m_SensorFusionSkippedTime;
        float m_TimeSteadySensorFusion;
        float m_TimeSteadyStillness;

        public AutoCalibration(MotionSettings settings, GyroCalibrationData calibration)
        {
            m_Settings = settings;
            m_Calibration = calibration;
            Reset();
        }

        public float confidence { get; set; }
        public bool isSteady { get; private set; }

        public void Reset()
        {
            m_Window.Reset(0f);
            confidence = 0f;
            isSteady = false;
            m_MinDeltaGyro = Vector3.one;
            m_MinDeltaAccel = Vector3.one * 0.25f;
            m_RecalibrateThreshold = 1f;
            m_SensorFusionSkippedTime = 0f;
            m_TimeSteadySensorFusion = 0f;
            m_TimeSteadyStillness = 0f;
        }

        public bool AddSampleStillness(Vector3 gyro, Vector3 accel, float deltaTime, bool doSensorFusion)
        {
            if (gyro == Vector3.zero && accel == Vector3.zero)
            {
                // zeroes are almost certainly not valid inputs
                return false;
            }

            var settings = m_Settings;
            var calibrationHalfTime = settings.stillnessCalibrationHalfTime * confidence;

            m_Window.AddSample(gyro, accel, deltaTime);
            // get deltas
            var gyroDelta = m_Window.maxGyro - m_Window.minGyro;
            var accelDelta = m_Window.maxAccel - m_Window.minAccel;

            var calibrated = false;
            var steady = false;
            var climbThisTick = Vector3.one * (settings.stillnessSampleDeteriorationRate * deltaTime);
            if (settings.stillnessGyroDelta < 0f)
            {
                if (confidence < 1f)
                {
                    m_MinDeltaGyro += climbThisTick;
                }
            }
            else
            {
                m_MinDeltaGyro = Vector3.one * settings.stillnessGyroDelta;
            }
            if (settings.stillnessAccelDelta < 0f)
            {
                if (confidence < 1f)
                {
                    m_MinDeltaAccel += climbThisTick;
                }
            }
            else
            {
                m_MinDeltaAccel = Vector3.one * settings.stillnessAccelDelta;
            }

            if (m_Window.numSamples >= settings.minStillnessSamples && m_Window.timeSampled >= settings.minStillnessCollectionTime)
            {
                m_MinDeltaGyro = Vector3.Min(m_MinDeltaGyro, gyroDelta);
                m_MinDeltaAccel = Vector3.Min(m_MinDeltaAccel, accelDelta);
            }
            else
            {
                m_RecalibrateThreshold = Mathf.Min(m_RecalibrateThreshold + settings.stillnessErrorClimbRate * deltaTime, settings.maxStillnessError);
                return false;
            }

            // check that all inputs are below appropriate thresholds to be considered "still"
            if (MotionMath.AllLessOrEqual(gyroDelta, m_MinDeltaGyro, m_RecalibrateThreshold)
                && MotionMath.AllLessOrEqual(accelDelta, m_MinDeltaAccel, m_RecalibrateThreshold))
            {
                if (m_Window.numSamples >= settings.minStillnessSamples && m_Window.timeSampled >= settings.minStillnessCorrectionTime)
                {
                    m_TimeSteadyStillness = Mathf.Min(m_TimeSteadyStillness + deltaTime, settings.stillnessCalibrationEaseInTime);
                    var calibrationEaseIn = settings.stillnessCalibrationEaseInTime <= 0f ? 1f : m_TimeSteadyStillness / settings.stillnessCalibrationEaseInTime;

                    var calibratedGyro = m_Window.meanGyro;

                    var oldGyroBias = m_Calibration.sum / Mathf.Max(m_Calibration.numSamples, 1f);
                    var stillnessLerpFactor = calibrationHalfTime <= 0f ? 0f : MotionMath.Exp2(-calibrationEaseIn * deltaTime / calibrationHalfTime);
                    var newGyroBias = Vector3.LerpUnclamped(calibratedGyro, oldGyroBias, stillnessLerpFactor);
                    confidence = Mathf.Min(confidence + deltaTime * settings.stillnessConfidenceRate, 1f);
                    steady = true;

                    if (doSensorFusion)
                    {
                        var previousNormal = m_Window.startAccel.normalized;
                        var thisNormal = accel.normalized;
                        var angularVelocity = MotionMath.AngularVelocityBetween(thisNormal, previousNormal, m_Window.timeSampled);

                        var axisCalibrationStrength = MotionMath.Abs(thisNormal);
                        var sensorFusionBias = Vector3.LerpUnclamped(calibratedGyro - angularVelocity, oldGyroBias, stillnessLerpFactor);
                        if (axisCalibrationStrength.x <= 0.7f)
                        {
                            newGyroBias.x = sensorFusionBias.x;
                        }
                        if (axisCalibrationStrength.y <= 0.7f)
                        {
                            newGyroBias.y = sensorFusionBias.y;
                        }
                        if (axisCalibrationStrength.z <= 0.7f)
                        {
                            newGyroBias.z = sensorFusionBias.z;
                        }
                    }

                    m_Calibration.sum = newGyroBias;
                    m_Calibration.accelMagnitude = m_Window.meanAccel.magnitude;
                    m_Calibration.numSamples = 1;

                    calibrated = true;
                }
                else
                {
                    m_RecalibrateThreshold = Mathf.Min(m_RecalibrateThreshold + settings.stillnessErrorClimbRate * deltaTime, settings.maxStillnessError);
                }
            }
            else if (m_TimeSteadyStillness > 0f)
            {
                m_RecalibrateThreshold -= settings.stillnessErrorDropOnRecalibrate;
                if (m_RecalibrateThreshold < 1f)
                {
                    m_RecalibrateThreshold = 1f;
                }

                m_TimeSteadyStillness = 0f;
                m_Window.Reset(0f);
            }
            else
            {
                m_RecalibrateThreshold = Mathf.Min(m_RecalibrateThreshold + settings.stillnessErrorClimbRate * deltaTime, settings.maxStillnessError);
                m_Window.Reset(0f);
            }

            isSteady = steady;
            return calibrated;
        }

        public void NoSampleStillness()
        {
            m_Window.Reset(0f);
        }

        public bool AddSampleSensorFusion(Vector3 gyro, Vector3 accel, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            if ((gyro == Vector3.zero && accel == Vector3.zero) || m_PreviousAccel == Vector3.zero)
            {
                // all zeroes are almost certainly not valid inputs
                m_TimeSteadySensorFusion = 0f;
                m_SensorFusionSkippedTime = 0f;
                m_PreviousAccel = accel;
                m_SmoothedPreviousAccel = accel;
                m_SmoothedAngularVelocityGyro = Vector3.zero;
                m_SmoothedAngularVelocityAccel = Vector3.zero;
                return false;
            }

            // in case the controller state hasn't updated between samples
            if (accel.x == m_PreviousAccel.x && accel.y == m_PreviousAccel.y && accel.z == m_PreviousAccel.z)
            {
                m_SensorFusionSkippedTime += deltaTime;
                return false;
            }

            var settings = m_Settings;
            var calibrationHalfTime = settings.sensorFusionCalibrationHalfTime * confidence;

            deltaTime += m_SensorFusionSkippedTime;
            m_SensorFusionSkippedTime = 0f;
            var calibrated = false;
            var steady = false;

            // framerate independent lerp smoothing: https://www.gamasutra.com/blogs/ScottLembcke/20180404/316046/Improved_Lerp_Smoothing.php
            var smoothingLerpFactor = MotionMath.Exp2(-settings.sensorFusionCalibrationSmoothingStrength * deltaTime);
            // velocity from smoothed accel matches better if we also smooth gyro
            var previousGyro = m_SmoothedAngularVelocityGyro;
            m_SmoothedAngularVelocityGyro = Vector3.LerpUnclamped(gyro, m_SmoothedAngularVelocityGyro, smoothingLerpFactor); // smooth what remains
            var gyroAccelerationMag = (m_SmoothedAngularVelocityGyro - previousGyro).magnitude / deltaTime;
            // get angle between old and new accel
            var previousNormal = m_SmoothedPreviousAccel.normalized;
            var thisAccel = Vector3.LerpUnclamped(accel, m_SmoothedPreviousAccel, smoothingLerpFactor);
            var thisNormal = thisAccel.normalized;
            m_SmoothedAngularVelocityAccel = MotionMath.AngularVelocityBetween(thisNormal, previousNormal, deltaTime);

            // apply corrections
            if (gyroAccelerationMag > settings.sensorFusionAngularAccelerationThreshold)
            {
                m_TimeSteadySensorFusion = 0f;
            }
            else
            {
                m_TimeSteadySensorFusion = Mathf.Min(m_TimeSteadySensorFusion + deltaTime, settings.sensorFusionCalibrationEaseInTime);
                var calibrationEaseIn = settings.sensorFusionCalibrationEaseInTime <= 0f ? 1f : m_TimeSteadySensorFusion / settings.sensorFusionCalibrationEaseInTime;
                var oldGyroBias = m_Calibration.sum / Mathf.Max(m_Calibration.numSamples, 1f);
                // recalibrate over time proportional to the difference between the calculated bias and the current assumed bias
                var sensorFusionLerpFactor = calibrationHalfTime <= 0f ? 0f : MotionMath.Exp2(-calibrationEaseIn * deltaTime / calibrationHalfTime);
                var newGyroBias = Vector3.LerpUnclamped(m_SmoothedAngularVelocityGyro - m_SmoothedAngularVelocityAccel, oldGyroBias, sensorFusionLerpFactor);
                confidence = Mathf.Min(confidence + deltaTime * settings.sensorFusionConfidenceRate, 1f);
                steady = true;
                // don't change bias in axes that can't be affected by the gravity direction
                var axisCalibrationStrength = MotionMath.Abs(thisNormal);
                if (axisCalibrationStrength.x > 0.7f)
                {
                    axisCalibrationStrength.x = 1f;
                }
                if (axisCalibrationStrength.y > 0.7f)
                {
                    axisCalibrationStrength.y = 1f;
                }
                if (axisCalibrationStrength.z > 0.7f)
                {
                    axisCalibrationStrength.z = 1f;
                }
                newGyroBias = MotionMath.Lerp(newGyroBias, oldGyroBias, Vector3.Min(axisCalibrationStrength, Vector3.one));

                m_Calibration.sum = newGyroBias;
                m_Calibration.accelMagnitude = thisAccel.magnitude;
                m_Calibration.numSamples = 1;

                calibrated = true;
            }

            m_SmoothedPreviousAccel = thisAccel;
            m_PreviousAccel = accel;

            isSteady = steady;

            return calibrated;
        }

        public void NoSampleSensorFusion()
        {
            m_TimeSteadySensorFusion = 0f;
            m_SensorFusionSkippedTime = 0f;
            m_PreviousAccel = Vector3.zero;
            m_SmoothedPreviousAccel = Vector3.zero;
            m_SmoothedAngularVelocityGyro = Vector3.zero;
            m_SmoothedAngularVelocityAccel = Vector3.zero;
        }
    }
}
