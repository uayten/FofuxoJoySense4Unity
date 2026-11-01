using Fofuxo.JoySense.Switch2;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Fofuxo.JoySense.Diagnostics
{
    /// <summary>
    /// Draws the current gamepad's two sticks over the game view: a dot per stick inside its circle, with the
    /// value the game reads and the raw value before the deadzone. A controller with motion sensors also shows its
    /// attitude and readings.
    /// </summary>
    /// <remarks>
    /// Turned on and off from <c>Tools > Fofuxo JoySense > Stick Overlay</c>; the choice is remembered per
    /// machine and only applies in the Editor's Play Mode.
    /// </remarks>
    [AddComponentMenu("")]
    internal sealed class StickOverlay : MonoBehaviour
    {
        const float Radius = 70f;
        const float Margin = 16f;
        const int TextureSize = 128;
        const float MotionPanelWidth = 230f;

        static StickOverlay s_Instance;

        Texture2D m_Ring;
        Texture2D m_Dot;
        Texture2D m_Background;
        GUIStyle m_Label;
        double m_RateWindowStart;
        long m_RateWindowCount;
        double m_SampleRate;

#if UNITY_EDITOR
        const string EnabledKey = "Fofuxo.JoySense.StickOverlay";
        const string MenuPath = "Tools/Fofuxo JoySense/Stick Overlay";

        static bool enabledInEditor
        {
            get => UnityEditor.EditorPrefs.GetBool(EnabledKey, false);
            set => UnityEditor.EditorPrefs.SetBool(EnabledKey, value);
        }

        [UnityEditor.MenuItem(MenuPath)]
        static void Toggle()
        {
            enabledInEditor = !enabledInEditor;
            if (Application.isPlaying)
            {
                SetVisible(enabledInEditor);
            }
        }

        [UnityEditor.MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, enabledInEditor);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ShowIfEnabled()
        {
            SetVisible(enabledInEditor);
        }
#endif

        static void SetVisible(bool visible)
        {
            if (visible && s_Instance == null)
            {
                var host = new GameObject("JoySense Stick Overlay");
                DontDestroyOnLoad(host);
                s_Instance = host.AddComponent<StickOverlay>();
            }
            else if (!visible && s_Instance != null)
            {
                Destroy(s_Instance.gameObject);
                s_Instance = null;
            }
        }

        void OnDestroy()
        {
            Destroy(m_Ring);
            Destroy(m_Dot);
            Destroy(m_Background);
        }

        void OnGUI()
        {
            if (m_Ring == null)
            {
                m_Ring = CreateCircle(ring: true);
                m_Dot = CreateCircle(ring: false);
                m_Background = new Texture2D(1, 1);
                m_Background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                m_Background.Apply();
                m_Label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
                m_Label.normal.textColor = Color.white;
            }

            var gamepad = Gamepad.current;
            var size = Radius * 2f;
            var panel = new Rect(Margin, Screen.height - Margin - size - 90f, size * 2f + Margin * 3f, size + 90f);
            GUI.DrawTexture(panel, m_Background);

            var title = new Rect(panel.x, panel.y + 6f, panel.width, 20f);
            GUI.Label(title, gamepad != null ? gamepad.displayName : "No gamepad", m_Label);

            if (gamepad == null)
            {
                return;
            }

            DrawStick(gamepad.leftStick, "Left", new Vector2(panel.x + Margin + Radius, panel.y + 32f + Radius));
            DrawStick(gamepad.rightStick, "Right", new Vector2(panel.x + Margin * 2f + Radius * 3f, panel.y + 32f + Radius));

            if (gamepad is Switch2ProController controller)
            {
                DrawMotion(controller, new Rect(panel.xMax + Margin, panel.y, MotionPanelWidth, panel.height));
            }
        }

        // The three axes turn with the controller, drawn at a slight angle so that z does not collapse to a point. Flat on
        // the table, red points right, green up and blue away from the player.
        void DrawMotion(Switch2ProController controller, Rect panel)
        {
            GUI.DrawTexture(panel, m_Background);

            var now = Time.realtimeSinceStartupAsDouble;
            if (now - m_RateWindowStart >= 1.0)
            {
                m_SampleRate = (controller.motionSampleCount - m_RateWindowCount) / (now - m_RateWindowStart);
                m_RateWindowCount = controller.motionSampleCount;
                m_RateWindowStart = now;
            }

            var center = new Vector2(panel.x + panel.width * 0.5f, panel.y + 72f);
            var orientation = controller.orientation;
            DrawAxis(center, orientation * Vector3.right, new Color(0.95f, 0.35f, 0.3f));
            DrawAxis(center, orientation * Vector3.up, new Color(0.35f, 0.9f, 0.45f));
            DrawAxis(center, orientation * Vector3.forward, new Color(0.35f, 0.6f, 1f));

            var gyro = controller.gyro.ReadValue();
            var accelerometer = controller.accelerometer.ReadValue();
            var gravity = controller.gravity;
            var offset = controller.gyroCalibrationOffset;
            var text = $"{m_SampleRate:0} samples/s\n" +
                $"gyro {gyro.x:+000;-000} {gyro.y:+000;-000} {gyro.z:+000;-000}\n" +
                $"accel {accelerometer.x:+0.00;-0.00} {accelerometer.y:+0.00;-0.00} {accelerometer.z:+0.00;-0.00}\n" +
                $"gravity {gravity.x:+0.00;-0.00} {gravity.y:+0.00;-0.00} {gravity.z:+0.00;-0.00}\n" +
                $"drift {offset.x:+0.00;-0.00} {offset.y:+0.00;-0.00} {offset.z:+0.00;-0.00}\n" +
                $"confidence {controller.gyroCalibrationConfidence:0.00}{(controller.isGyroSteady ? ", steady" : "")}";
            GUI.Label(new Rect(panel.x, panel.y + 134f, panel.width, panel.height - 134f), text, m_Label);
        }

        static void DrawAxis(Vector2 center, Vector3 direction, Color color)
        {
            const float length = 50f;
            const float thickness = 3f;

            var end = new Vector2(direction.x + direction.z * 0.35f, -direction.y - direction.z * 0.35f) * length;
            var matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y, end.x) * Mathf.Rad2Deg, center);
            GUI.DrawTexture(new Rect(center.x, center.y - thickness * 0.5f, end.magnitude, thickness), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = Color.white;
        }

        void DrawStick(StickControl stick, string name, Vector2 center)
        {
            var value = stick.ReadValue();
            var raw = new Vector2(stick.x.ReadUnprocessedValue(), stick.y.ReadUnprocessedValue());

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(center.x - Radius, center.y - Radius, Radius * 2f, Radius * 2f), m_Ring);

            // Screen space grows downwards, stick space upwards.
            var dotPosition = center + new Vector2(value.x, -value.y) * Radius;
            const float dotRadius = 9f;
            GUI.color = stick.IsActuated() ? new Color(0.35f, 0.9f, 0.45f) : Color.white;
            GUI.DrawTexture(new Rect(dotPosition.x - dotRadius, dotPosition.y - dotRadius, dotRadius * 2f, dotRadius * 2f), m_Dot);
            GUI.color = Color.white;

            var direction = stick.up.isPressed ? "up" : stick.down.isPressed ? "down" : "";
            if (stick.left.isPressed)
            {
                direction += direction.Length > 0 ? " + left" : "left";
            }
            else if (stick.right.isPressed)
            {
                direction += direction.Length > 0 ? " + right" : "right";
            }

            var text = $"{name}  {value.x:+0.00;-0.00} {value.y:+0.00;-0.00}\nraw {raw.x:+0.00;-0.00} {raw.y:+0.00;-0.00}\n{direction}";
            GUI.Label(new Rect(center.x - Radius - 10f, center.y + Radius + 4f, Radius * 2f + 20f, 60f), text, m_Label);
        }

        static Texture2D CreateCircle(bool ring)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var half = TextureSize * 0.5f;
            const float thickness = 3f;
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                    var alpha = ring
                        ? Mathf.Clamp01(1f - Mathf.Abs(distance - (half - thickness)) / thickness)
                        : Mathf.Clamp01(half - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
