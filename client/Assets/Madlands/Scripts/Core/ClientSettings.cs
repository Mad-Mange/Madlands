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
    ///   Combat checks: -class shardmage (new characters), -exec ".level 5;.spawn 2" (chat lines
    ///   sent after entering the world), -autofight (targets the nearest enemy and fights it)
    ///   Item and quest checks: -open bags,character,questlog (windows), -talkto "Old Tamsin" (opens
    ///   that NPC's dialog), -quest 1 (with -talkto: shows that quest's page), -loot (opens the loot
    ///   of nearby corpses), -tooltip 1224 (shows the tooltip of that item in the open bags),
    ///   -equip 1020,1210 (equips those items from the bags)
    /// </summary>
    public sealed class ClientSettings
    {
        /// <summary>Password used by -autoconnect when none is given. Development convenience only.</summary>
        public const string DevPassword = "madlands-dev";

        /// <summary>
        /// Temporary playtest shortcut: the login screen's quick-login button logs in to this account with
        /// <see cref="DevPassword"/>. Offered only for servers on this machine or the local network.
        /// </summary>
        public const string QuickLoginAccount = "mange";

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
        /// <summary>Class name or id for characters created by -autoconnect.</summary>
        public string ClassName;
        /// <summary>Race name or id for characters created by -autoconnect.</summary>
        public string RaceName;
        /// <summary>Chat lines (usually GM commands) to send after entering the world, ';'-separated.</summary>
        public string[] Exec = new string[0];
        public bool AutoFight;
        /// <summary>Windows to open after entering the world (bags, character, questlog).</summary>
        public string[] OpenWindows = new string[0];
        /// <summary>Name of an NPC to talk to once the -exec lines ran.</summary>
        public string TalkTo;
        /// <summary>With TalkTo: a quest id to click in the dialog.</summary>
        public uint QuestId;
        /// <summary>Open the loot window of lootable corpses in reach.</summary>
        public bool AutoLoot;
        /// <summary>An item id whose tooltip is shown once the bags are open.</summary>
        public uint TooltipItem;
        /// <summary>Item ids to equip from the bags once the -exec lines ran.</summary>
        public uint[] Equip = new uint[0];

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
                    case "-class" when next != null:
                        s.ClassName = next;
                        i++;
                        break;
                    case "-race" when next != null:
                        s.RaceName = next;
                        i++;
                        break;
                    case "-exec" when next != null:
                        s.Exec = next.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        i++;
                        break;
                    case "-autofight":
                        s.AutoFight = true;
                        break;
                    case "-open" when next != null:
                        s.OpenWindows = next.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        i++;
                        break;
                    case "-talkto" when next != null:
                        s.TalkTo = next;
                        i++;
                        break;
                    case "-quest" when next != null:
                        uint.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.QuestId);
                        i++;
                        break;
                    case "-loot":
                        s.AutoLoot = true;
                        break;
                    case "-equip" when next != null:
                        s.Equip = Array.ConvertAll(next.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            id => uint.Parse(id, NumberStyles.Integer, CultureInfo.InvariantCulture));
                        i++;
                        break;
                    case "-tooltip" when next != null:
                        uint.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.TooltipItem);
                        i++;
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
        /// <summary>A front-end screen or the Esc menu is open (keyboard and mouse belong to the UI).</summary>
        public static bool MenuOpen;
        /// <summary>The chat line has focus (the keyboard belongs to it; the mouse still steers).</summary>
        public static bool Typing;

        public static bool Blocked => MenuOpen;
        public static bool KeyboardBlocked => MenuOpen || Typing;
    }
}
