using System;
using System.Globalization;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Command-line options, used for quick testing and automated verification:
    ///   Madlands.exe -name Alice -autoconnect            (dev: logs in as "alice", creating account + character)
    ///   Madlands.exe -user alice -password secret1 -character Alice -autoconnect
    ///   Madlands.exe -server 192.168.1.10 -httpport 7780
    ///   Madlands.exe -autoconnect -stopat charselect     (stop at the character screen)
    ///   Madlands.exe -autoconnect -screenshot shot.png -screenshotdelay 6 -quitafter 10
    ///   Framing for screenshots: -campitch 30 -camdistance 25 -camyaw 40 -hidedebug
    /// </summary>
    public sealed class ClientSettings
    {
        /// <summary>Password used by -autoconnect when none is given. Development convenience only.</summary>
        public const string DevPassword = "madlands-dev";

        public string Server = "127.0.0.1";
        public int HttpPort = 7780;
        public string User;
        public string Password;
        public string CharacterName;
        public bool AutoConnect;
        /// <summary>"charselect" stops the auto-connect flow at the character screen.</summary>
        public string StopAt;
        public string ScreenshotPath;
        public float ScreenshotDelay = 5f;
        public float QuitAfter;
        public float? CameraPitch;
        public float? CameraDistance;
        public float? CameraYawOffset;
        public bool HideDebug;

        public string AccountApiUrl => "http://" + Server + ":" + HttpPort;

        /// <summary>True when the auto-connect flow goes all the way into the world.</summary>
        public bool AutoEntersWorld => AutoConnect && StopAt == null;

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
                        s.Server = next;
                        i++;
                        break;
                    case "-httpport" when next != null:
                        s.HttpPort = int.Parse(next, CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "-user" when next != null:
                        s.User = next;
                        i++;
                        break;
                    case "-password" when next != null:
                        s.Password = next;
                        i++;
                        break;
                    case "-name" when next != null:
                    case "-character" when next != null:
                        s.CharacterName = next;
                        i++;
                        break;
                    case "-autoconnect":
                        s.AutoConnect = true;
                        break;
                    case "-stopat" when next != null:
                        s.StopAt = next.ToLowerInvariant();
                        i++;
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

            if (s.AutoConnect)
            {
                if (s.CharacterName == null) s.CharacterName = "Player" + RandomLetters(3);
                if (s.User == null) s.User = s.CharacterName.ToLowerInvariant();
                if (s.Password == null) s.Password = DevPassword;
            }
            return s;
        }

        private static string RandomLetters(int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i++) chars[i] = (char)('a' + UnityEngine.Random.Range(0, 26));
            return new string(chars);
        }
    }

    /// <summary>Set while a menu or text input owns the keyboard and mouse, so the game ignores them.</summary>
    public static class InputGate
    {
        public static bool Blocked;
    }
}
