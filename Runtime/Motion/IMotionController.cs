using Fofuxo.JoySense.Controls;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace Fofuxo.JoySense.Motion
{
    /// <summary>
    /// A controller with motion sensors, so that a game can ask for one without naming a model. Everything is in Unity's
    /// axes: lying flat, face up and its grips towards the player, x to the right, y up, z away from the player.
    /// </summary>
    public interface IMotionController
    {
        /// <summary>Angular velocity of the latest sample, in degrees per second, without its drift.</summary>
        Vector3Control gyro { get; }

        /// <summary>The latest accelerometer sample, in g.</summary>
        Vector3Control accelerometer { get; }

        /// <summary>How far the controller turned this input update, for binding to an action that turns a camera.</summary>
        MotionLookControl gyroLook { get; }

        /// <summary>The space <see cref="gyroLook"/> and <see cref="ConsumeRotation"/> report in.</summary>
        GyroSpace gyroSpace { get; set; }

        Quaternion orientation { get; }
        Vector3 gravity { get; }

        /// <summary>Returns how far the controller has turned since the last call, in degrees.</summary>
        Vector3 ConsumeRotation();

        /// <summary>Makes the current pose the origin of <see cref="orientation"/>.</summary>
        void ResetOrientation();
    }
}
