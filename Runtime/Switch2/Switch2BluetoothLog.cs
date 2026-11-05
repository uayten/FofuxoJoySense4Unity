#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Fofuxo.JoySense.Switch2
{
    // A file with one line per Bluetooth connection and per silent link, which outlives the console and the Editor.
    // A link that connects and sends no input is rare and leaves nothing behind (DECISIONS.md, F-013): the healthy
    // connections are recorded too, so the silent ones can be compared with them. Written from worker threads.
    internal static class Switch2BluetoothLog
    {
        // Past this the file becomes bluetooth.old.log, replacing the previous one.
        const long MaxFileBytes = 256 * 1024;

        static readonly object s_Lock = new object();
        static string s_Path;

        public static string FilePath => s_Path;

        // Main thread: Unity's paths cannot be read from a worker.
        public static void Initialize()
        {
            try
            {
                s_Path = Path.Combine(Application.persistentDataPath, "JoySense", "bluetooth.log");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[JoySense] No Bluetooth log this session: {exception.Message}");
            }
        }

        public static void Write(string entry, string details)
        {
            if (s_Path == null)
            {
                return;
            }

            var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {entry} {details}\n";
            try
            {
                lock (s_Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(s_Path));
                    var file = new FileInfo(s_Path);
                    if (file.Exists && file.Length > MaxFileBytes)
                    {
                        File.Copy(s_Path, Path.ChangeExtension(s_Path, ".old.log"), true);
                        File.Delete(s_Path);
                    }
                    File.AppendAllText(s_Path, line);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[JoySense] Could not write the Bluetooth log: {exception.Message}");
            }
        }
    }
}
#endif
