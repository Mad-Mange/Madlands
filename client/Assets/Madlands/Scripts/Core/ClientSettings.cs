using System;
using System.Globalization;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Command-line options, used for quick testing and automated verification:
    ///   Madlands.exe -server 127.0.0.1:7777 -name Alice -autoconnect
    ///   Madlands.exe -autoconnect -screenshot shot.png -screenshotdelay 6 -quitafter 10
    ///   Framing for screenshots: -campitch 30 -camdistance 25 -camyaw 40 -hidedebug
    /// </summary>
    public sealed class ClientSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 7777;
        public string Name = "Player" + UnityEngine.Random.Range(100, 1000);
        public bool AutoConnect;
        public string ScreenshotPath;
        public float ScreenshotDelay = 5f;
        public float QuitAfter;
        public float? CameraPitch;
        public float? CameraDistance;
        public float? CameraYawOffset;
        public bool HideDebug;

        public static ClientSettings FromCommandLine()
        {
            var s = new ClientSettings();
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i].ToLowerInvariant())
                {
                    case "-server" when next != null:
                        var parts = next.Split(':');
                        s.Host = parts[0];
                        if (parts.Length > 1) s.Port = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-name" when next != null:
                        s.Name = next;
                        i++;
                        break;
                    case "-autoconnect":
                        s.AutoConnect = true;
                        break;
                    case "-screenshot" when next != null:
                        s.ScreenshotPath = next;
                        i++;
                        break;
                    case "-screenshotdelay" when next != null:
                        s.ScreenshotDelay = float.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-quitafter" when next != null:
                        s.QuitAfter = float.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-campitch" when next != null:
                        s.CameraPitch = float.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-camdistance" when next != null:
                        s.CameraDistance = float.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-camyaw" when next != null:
                        s.CameraYawOffset = float.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-hidedebug":
                        s.HideDebug = true;
                        break;
                }
            }
            if (!Application.isEditor && s.ScreenshotPath != null) s.AutoConnect = true;
            return s;
        }
    }
}
