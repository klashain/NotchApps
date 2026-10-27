// PowerNotch.cs - combines the Windows power plan (battery, maximum saver, normal, performance), screen refresh rate,
// the notch (Dynamic Island-like) and the game FPS indicator in one app.
// Build: build\build.ps1
//
// Build: build\build.ps1 (produces PowerNotch.exe and the installer)
//
// Usage:
//   PowerNotch.exe              -> opens the window; closing it minimizes to the system tray
//   PowerNotch.exe pil          -> applies Battery Saver directly (maks, normal, performans also work)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Management;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace PowerNotch
{
    static class Program
    {
        static Mutex singleInstance;

        // Sets the timer resolution to 1 ms for animations (default ~15.6 ms; needed for 60 fps)
        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Embedded libraries (LibreHardwareMonitor etc.) are loaded from inside the exe
            AppDomain.CurrentDomain.AssemblyResolve += CpuSensor.ResolveEmbedded;
            timeBeginPeriod(1);
            Settings.Load();
            Loc.Set(string.IsNullOrEmpty(Settings.Language) ? Loc.Detect() : Settings.Language);
            // Re-enables a graphics card left disabled by the previous session (the app may have exited unexpectedly)
            if (Settings.GpuOff && Power.ActiveKey() != "maks") GpuSwitch.Restore();

            string arg = args.Length > 0 ? args[0] : "";
            if (arg == "--gorev") return args.Length > 1 ? Admin.CreateTaskFor(args[1]) : 2;
            if (arg != "" && arg != "--yonetici") return RunCommandLine(arg);
            if (arg == "--yonetici" && Admin.Elevated) Admin.LockFolder(Path.GetDirectoryName(Application.ExecutablePath));

            // If the admin task is installed and the app is not running, this process runs the task and exits;
            // the app then opens as admin without a UAC prompt. "--yonetici" is the process started by the task.
            if (arg != "--yonetici" && !Admin.Elevated && !AlreadyRunning() && Admin.TaskPointsHere() && Admin.StartTask() == 0) return 0;

            // Do not open a second app instance or a second tray icon
            bool first = false;
            try
            {
                singleInstance = new Mutex(true, "PowerNotch.SingleInstance", out first);
            }
            catch (UnauthorizedAccessException)
            {
                // If the mutex belongs to the admin process, access is denied: that also means a second instance
            }
            if (!first)
            {
                DarkBox.Show(null, Loc.F("already_running", "PowerNotch"), "PowerNotch");
                return 0;
            }

            Autostart.Apply(Settings.AutoStart);
            MainForm form = new MainForm();
            form.StartTelemetry();
            Application.Run(form);
            return 0;
        }

        // Is the app already running? Also counts as running if the admin process's mutex is inaccessible from another privilege level
        static bool AlreadyRunning()
        {
            try
            {
                Mutex.OpenExisting("PowerNotch.SingleInstance").Close();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        static int RunCommandLine(string arg)
        {
            // The app is built without a console window (WinExe); to write to the console attached to cmd
            WinNative.AttachConsole(-1);

            string mode = arg.ToLowerInvariant();
            if (mode != "pil" && mode != "maks" && mode != "normal" && mode != "performans")
            {
                Console.WriteLine("Kullanim: PowerNotch.exe [pil | maks | normal | performans]");
                return 2;
            }

            try
            {
                string msg = Power.Apply(mode);
                string displayErr = Display.Apply(mode);
                Console.WriteLine(displayErr == null ? msg : msg + " (" + displayErr + ")");
                Console.WriteLine("Aktif mod: " + Power.ModeName(Power.ActiveKey()));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.T("error_prefix") + ex.Message);
                return 1;
            }
        }
    }

    // Power plan operations via powercfg (with PowerNotch-specific plan names)
    static class Power
    {
        // ---- Settings (changed from the settings window) ----
        public static int BatteryCpuMax = 30;      // Battery: CPU cap on battery (%)
        public static int BatteryBrightness = 25;  // Battery: screen brightness on battery (%)
        public static int MaksCpuMax = 15;         // Maximum Saver: CPU cap on battery (%)
        public static int MaksBrightness = 10;     // Maximum Saver: screen brightness on battery (%)

        // ---- GUIDs of Windows' built-in power plans (language independent) ----
        public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a"; // Power saver
        public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";   // Balanced
        public const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";   // High performance

        // Extra setting GUIDs used by the battery modes (those without a powercfg alias)
        const string EppGuid = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";
        const string BatterySaverLevelGuid = "e69653ca-cf7f-4f05-aa73-cb833fa90ad4";
        const string WirelessGroupGuid = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
        const string WirelessPowerModeGuid = "12bbebe6-58d6-4636-95bb-3217ef867c1a";

        // PowerNotch's own plan copies; does not touch other plans
        const string PilName = "PowerNotch Pil Tasarrufu";
        const string MaksName = "PowerNotch Maksimum Tasarruf";
        const string PerfName = "PowerNotch Performans";

        const string GuidPat = "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}";
        static readonly Regex GuidRx = new Regex(GuidPat);
        static readonly Regex NameRx = new Regex("(" + GuidPat + @")\s*\((.+?)\)");

        public static string Apply(string mode)
        {
            switch (mode)
            {
                case "normal":
                    // In Normal mode, sleep and hibernate are never (0 = never); for both battery and AC
                    Run("-setacvalueindex", BalancedGuid, "SUB_SLEEP", "STANDBYIDLE", "0");
                    Run("-setdcvalueindex", BalancedGuid, "SUB_SLEEP", "STANDBYIDLE", "0");
                    Run("-setacvalueindex", BalancedGuid, "SUB_SLEEP", "HIBERNATEIDLE", "0");
                    Run("-setdcvalueindex", BalancedGuid, "SUB_SLEEP", "HIBERNATEIDLE", "0");
                    // Turn off the screen after 5 minutes (AC and battery); no sleep, as above
                    Run("-setacvalueindex", BalancedGuid, "SUB_VIDEO", "VIDEOIDLE", "300");
                    Run("-setdcvalueindex", BalancedGuid, "SUB_VIDEO", "VIDEOIDLE", "300");
                    // CPU returns to normal: Max/Battery limits are not carried to the Balanced plan, they are explicitly reset here
                    Run("-setacvalueindex", BalancedGuid, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "5");
                    Run("-setdcvalueindex", BalancedGuid, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "5");
                    Run("-setacvalueindex", BalancedGuid, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "100");
                    Run("-setdcvalueindex", BalancedGuid, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "100");
                    Run("-setactive", BalancedGuid);
                    return Loc.T("normal_applied");

                case "pil":
                    {
                        string g = GetOrCreate(PilName, PowerSaverGuid);
                        // Activate the plan first: activation recalculates DC values (energy preference, brightness)
                        Run("-setactive", g);
                        Run("-setacvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "100");

                        // ---- Battery only (DC) ----
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "0");
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMAX", BatteryCpuMax.ToString());
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PERFBOOSTMODE", "0");
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", EppGuid, "100");
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "VIDEONORMALLEVEL", BatteryBrightness.ToString());
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "ADAPTBRIGHT", "0");
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "VIDEOIDLE", "60");
                        Run("-setdcvalueindex", g, "SUB_SLEEP", "STANDBYIDLE", "600");
                        Run("-setdcvalueindex", g, WirelessGroupGuid, WirelessPowerModeGuid, "3");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", "ESPOLICY", "1");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", BatterySaverLevelGuid, "100");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", "ESBRIGHTNESS", "30");
                        return Loc.F("pil_applied", BatteryCpuMax, BatteryBrightness);
                    }

                case "maks":
                    {
                        string g = GetOrCreate(MaksName, PowerSaverGuid);
                        Run("-setactive", g);
                        Run("-setacvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "100");

                        // ---- Battery only (DC): most aggressive settings ----
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "0");
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMAX", MaksCpuMax.ToString());
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PERFBOOSTMODE", "0");
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", EppGuid, "100");
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "VIDEONORMALLEVEL", MaksBrightness.ToString());
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "ADAPTBRIGHT", "0");
                        Run("-setdcvalueindex", g, "SUB_VIDEO", "VIDEOIDLE", "180"); // on battery the screen turns off after 3 min
                        // No sleep in Max mode (AC and battery)
                        Run("-setacvalueindex", g, "SUB_SLEEP", "STANDBYIDLE", "0");
                        Run("-setdcvalueindex", g, "SUB_SLEEP", "STANDBYIDLE", "0");
                        Run("-setacvalueindex", g, "SUB_SLEEP", "HIBERNATEIDLE", "0");
                        Run("-setdcvalueindex", g, "SUB_SLEEP", "HIBERNATEIDLE", "0");
                        Run("-setdcvalueindex", g, WirelessGroupGuid, WirelessPowerModeGuid, "3");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", "ESPOLICY", "1");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", BatterySaverLevelGuid, "100");
                        Run("-setdcvalueindex", g, "SUB_ENERGYSAVER", "ESBRIGHTNESS", "20");
                        return Loc.F("maks_applied", MaksCpuMax, MaksBrightness);
                    }

                case "performans":
                    {
                        string g = GetOrCreate(PerfName, HighPerfGuid);
                        Run("-setactive", g);
                        Run("-setacvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "100");
                        Run("-setdcvalueindex", g, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "100");
                        return Loc.T("perf_applied");
                    }

                default:
                    throw new ArgumentException(Loc.F("unknown_mode", mode));
            }
        }

        // Returns the active plan as a key: pil, maks, normal, performans or diger
        public static string ActiveKey()
        {
            string active = ActiveGuid();
            if (active.Length == 0) return "diger";
            if (string.Equals(active, BalancedGuid, StringComparison.OrdinalIgnoreCase)) return "normal";
            if (string.Equals(active, CachedGuidByName(PilName), StringComparison.OrdinalIgnoreCase)) return "pil";
            if (string.Equals(active, CachedGuidByName(MaksName), StringComparison.OrdinalIgnoreCase)) return "maks";
            if (string.Equals(active, CachedGuidByName(PerfName), StringComparison.OrdinalIgnoreCase)) return "performans";
            return "diger";
        }

        public static string ModeName(string key)
        {
            switch (key)
            {
                case "pil": return Loc.T("mode_pil");
                case "maks": return Loc.T("mode_maks");
                case "normal": return Loc.T("mode_normal_long");
                case "performans": return Loc.T("mode_perf");
                default: return Loc.T("mode_other");
            }
        }

        // Uses an existing copy with the same name, otherwise copies from the source
        static string GetOrCreate(string name, string sourceGuid)
        {
            string guid = FindByName(name);
            if (guid != null) return guid;

            guid = GuidRx.Match(Run("-duplicatescheme", sourceGuid)).Value;
            if (guid.Length == 0) throw new InvalidOperationException(Loc.T("copy_fail"));

            Run("-changename", guid, name);
            lock (nameCache) nameCache[name] = guid;
            return guid;
        }

        static string FindByName(string name)
        {
            foreach (string line in Run("-list").Split('\n'))
            {
                Match m = NameRx.Match(line);
                if (m.Success && m.Groups[2].Value.Trim() == name) return m.Groups[1].Value;
            }
            return null;
        }

        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr hMem);

        // Active power plan GUID. Read without starting powercfg (avoids stutter while gaming).
        static string ActiveGuid()
        {
            IntPtr p;
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return "";
            try { return ((Guid)Marshal.PtrToStructure(p, typeof(Guid))).ToString(); }
            finally { LocalFree(p); }
        }

        // Plan copy GUIDs are cached for 60 s; powercfg -list is not run every cycle
        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>();
        static DateTime nameCacheAt = DateTime.MinValue;

        static string CachedGuidByName(string name)
        {
            lock (nameCache)
            {
                if ((DateTime.UtcNow - nameCacheAt).TotalSeconds > 60) { nameCache.Clear(); nameCacheAt = DateTime.UtcNow; }
                string guid;
                if (nameCache.TryGetValue(name, out guid)) return guid;
                guid = FindByName(name);
                nameCache[name] = guid;
                return guid;
            }
        }

        static string Run(params string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"));
            psi.Arguments = string.Join(" ", Array.ConvertAll<string, string>(args, Quote));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(Loc.F("powercfg_fail", psi.Arguments, p.ExitCode));
                return output;
            }
        }

        static string Quote(string a)
        {
            return a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a;
        }
    }

    // Screen refresh rate: drops to 60 Hz in Maximum Saver, returns to the previous value in other modes
    static class Display
    {
        const int ENUM_CURRENT_SETTINGS = -1;
        const int DM_BITSPERPEL = 0x40000;
        const int DM_PELSWIDTH = 0x80000;
        const int DM_PELSHEIGHT = 0x100000;
        const int DM_DISPLAYFREQUENCY = 0x400000;
        const int DISP_CHANGE_SUCCESSFUL = 0;
        const int LowRefresh = 60;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);

        // The original rate is kept in this file; restored when another mode is selected
        static string ConfigFile
        {
            get
            {
                // If a record exists in the old-name folder (created before the first install) it is read; otherwise the new folder
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PilModu", "display.txt");
                string current = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerNotch", "display.txt");
                return !File.Exists(current) && File.Exists(legacy) ? legacy : current;
            }
        }

        // Returns a message on error, otherwise null
        public static string Apply(string mode)
        {
            try
            {
                return mode == "maks" && Settings.MaksRefresh ? LowerRefresh() : Restore();
            }
            catch (Exception ex)
            {
                return Loc.F("display_fail", ex.Message);
            }
        }

        static string LowerRefresh()
        {
            DEVMODE cur = Current();
            if (cur.dmDisplayFrequency <= LowRefresh) return null;

            DEVMODE target;
            if (!FindMode(cur.dmPelsWidth, cur.dmPelsHeight, cur.dmBitsPerPel, LowRefresh, out target))
                return Loc.T("display_no60");

            // If the original rate is already saved, it is not overwritten
            if (!File.Exists(ConfigFile)) Save(cur);
            return SetMode(target) ? null : Loc.T("display_change_fail");
        }

        static string Restore()
        {
            if (!File.Exists(ConfigFile)) return null;

            string[] p = File.ReadAllText(ConfigFile).Split(' ');
            int w = int.Parse(p[0]), h = int.Parse(p[1]), bpp = int.Parse(p[2]), freq = int.Parse(p[3]);
            File.Delete(ConfigFile);

            DEVMODE target;
            if (!FindMode(w, h, bpp, freq, out target)) return null; // display changed, no mode to restore
            return SetMode(target) ? null : Loc.T("display_restore_fail");
        }

        static DEVMODE NewMode()
        {
            DEVMODE m = new DEVMODE();
            m.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return m;
        }

        static DEVMODE Current()
        {
            DEVMODE m = NewMode();
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref m))
                throw new InvalidOperationException(Loc.T("display_info_fail"));
            return m;
        }

        static bool FindMode(int w, int h, int bpp, int freq, out DEVMODE result)
        {
            DEVMODE m = NewMode();
            for (int i = 0; EnumDisplaySettings(null, i, ref m); i++)
            {
                if (m.dmPelsWidth == w && m.dmPelsHeight == h && m.dmBitsPerPel == bpp && m.dmDisplayFrequency == freq)
                {
                    result = m;
                    return true;
                }
            }
            result = new DEVMODE();
            return false;
        }

        static void Save(DEVMODE m)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
            File.WriteAllText(ConfigFile, m.dmPelsWidth + " " + m.dmPelsHeight + " " + m.dmBitsPerPel + " " + m.dmDisplayFrequency);
        }

        static bool SetMode(DEVMODE m)
        {
            m.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
            return ChangeDisplaySettingsEx(null, ref m, IntPtr.Zero, 0, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
        }
    }


    static class Settings
    {
        public static bool NotchEnabled = true;     // show the notch
        public static int NotchX = -1;              // notch center on screen (x); -1 = screen center
        public static bool MaksRefresh = true;      // set the screen to 60 Hz in Maximum Saver
        public static bool ShowFps = false;         // show game FPS in the notch
        public static bool PetEnabled = false;      // show a small pet in the notch
        public static int PetKind = 0;              // pet type: 0 cat, 1 dog, 2 chick, 3 rabbit, 4 bird
        public static bool GpuOff = false;          // was the discrete graphics card disabled in Max mode (so it can be re-enabled)
        public static string GpuId = "";            // device ID of the disabled discrete graphics card
        public static string Language = "";        // selected UI language (tr, en, es, de, fr, zh); empty = Windows language
        public static bool AutoStart = true;        // start PowerNotch when Windows starts
        public static bool ShowGpuTemp = false;     // show GPU temperature in the notch (nvidia-smi)

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerNotch", "settings.txt"); }
        }

        // Read path for settings saved under the old name (PilModu)
        static string LegacyFilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PilModu", "settings.txt"); }
        }

        static int Clamp(int n, int min, int max)
        {
            return Math.Max(min, Math.Min(max, n));
        }

        public static void Load()
        {
            // If the new folder has no settings, the settings in the old-name (PilModu) folder are read
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] kv = line.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim();
                string v = kv[1].Trim();
                if (k == "maksRefresh") { MaksRefresh = v == "1"; continue; }
                if (k == "notch") { NotchEnabled = v == "1"; continue; }
                if (k == "fps") { ShowFps = v == "1"; continue; }
                if (k == "pet") { PetEnabled = v == "1"; continue; }
                if (k == "gpuoff") { GpuOff = v == "1"; continue; }
                if (k == "gpuid") { GpuId = v; continue; }
                if (k == "autostart") { AutoStart = v == "1"; continue; }
                if (k == "lang") { Language = v; continue; }
                if (k == "gpu") { ShowGpuTemp = v == "1"; continue; }

                int n;
                if (!int.TryParse(v, out n)) continue;
                switch (k)
                {
                    case "pilCpu": Power.BatteryCpuMax = Clamp(n, 5, 100); break;
                    case "pilEkran": Power.BatteryBrightness = Clamp(n, 0, 100); break;
                    case "maksCpu": Power.MaksCpuMax = Clamp(n, 5, 100); break;
                    case "maksEkran": Power.MaksBrightness = Clamp(n, 0, 100); break;
                    case "notchx": NotchX = n; break;
                    case "petkind": PetKind = Clamp(n, 0, 4); break;
                }
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, new string[] {
                "pilCpu=" + Power.BatteryCpuMax,
                "pilEkran=" + Power.BatteryBrightness,
                "maksCpu=" + Power.MaksCpuMax,
                "maksEkran=" + Power.MaksBrightness,
                "maksRefresh=" + (MaksRefresh ? "1" : "0"),
                "notch=" + (NotchEnabled ? "1" : "0"),
                "fps=" + (ShowFps ? "1" : "0"),
                "pet=" + (PetEnabled ? "1" : "0"),
                "petkind=" + PetKind,
                "gpuoff=" + (GpuOff ? "1" : "0"),
                "gpuid=" + GpuId,
                "autostart=" + (AutoStart ? "1" : "0"),
                "lang=" + Language,
                "gpu=" + (ShowGpuTemp ? "1" : "0"),
                "notchx=" + NotchX
            });
        }
    }
    // Battery info (percent, plugged in, time remaining)
    public class BatteryInfo
    {
        public int Percent = -1;
        public bool NoBattery = false;
        public bool Charging = false;
        public string State = "";
        public string Short = "";

        public static BatteryInfo Read()
        {
            PowerStatus p = SystemInformation.PowerStatus;
            BatteryInfo info = new BatteryInfo();

            if (p.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
            {
                info.NoBattery = true;
                info.State = Loc.T("bat_none");
                info.Short = "--";
                return info;
            }

            info.Charging = p.PowerLineStatus == PowerLineStatus.Online;
            if (p.BatteryLifePercent >= 0) info.Percent = (int)Math.Round(p.BatteryLifePercent * 100);

            if (info.Charging)
            {
                info.State = info.Percent >= 100 ? Loc.T("bat_full") : Loc.T("bat_charging");
                info.Short = info.Percent >= 100 ? Loc.T("bat_short_full") : Loc.T("bat_short_charging");
            }
            else if (p.BatteryLifeRemaining > 0)
            {
                int h = p.BatteryLifeRemaining / 3600;
                int m = (p.BatteryLifeRemaining % 3600) / 60;
                info.State = Loc.F("bat_remaining", h, m);
                info.Short = Loc.F("bat_short_remaining", h, m);
            }
            else
            {
                info.State = Loc.T("bat_calc");
                info.Short = Loc.T("bat_calc_short");
            }
            return info;
        }
    }

    // GPU temperature: read via the NVIDIA driver's nvml.dll (no process started). Returns NaN ("--") if there is no NVIDIA card.
    static class GpuTemp
    {
        const uint NvmlTemperatureGpu = 0;
        static bool missing = false;      // If there is no NVIDIA driver, it is not tried again
        static bool ready = false;
        static IntPtr gpu = IntPtr.Zero;

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        static extern int NvmlGetHandle(uint index, out IntPtr handle);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        static extern int NvmlGetTemperature(IntPtr handle, uint sensorType, out uint temp);

        public static double Read()
        {
            if (missing) return double.NaN;
            try
            {
                if (!ready)
                {
                    if (NvmlInit() != 0 || NvmlGetHandle(0, out gpu) != 0) { missing = true; return double.NaN; }
                    ready = true;
                }
                uint t;
                if (NvmlGetTemperature(gpu, NvmlTemperatureGpu, out t) != 0) return double.NaN;
                return t;
            }
            catch (DllNotFoundException) { missing = true; }
            catch (EntryPointNotFoundException) { missing = true; }
            catch (Exception) { }
            return double.NaN;
        }
    }
    // CPU temperature (Intel/AMD): LibreHardwareMonitor reads the core temperature through the PawnIO driver.
    // Requires admin rights and PawnIO; otherwise returns NaN and falls back to the Windows sensor.
    static class CpuSensor
    {
        static readonly object gate = new object();
        static LibreHardwareMonitor.Hardware.Computer computer;
        static DateTime retryAt = DateTime.MinValue;

        // Loads the embedded DLLs (from resources inside the exe)
        public static System.Reflection.Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
        {
            string name = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
            using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    s.CopyTo(ms);
                    return System.Reflection.Assembly.Load(ms.ToArray());
                }
            }
        }

        public static double Read()
        {
            lock (gate)
            {
                // If it cannot be opened, retried after a minute (PawnIO may have been installed later)
                if (DateTime.UtcNow < retryAt) return double.NaN;
                try
                {
                    if (computer == null)
                    {
                        computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true };
                        computer.Open();
                    }
                    double best = double.NaN;
                    double package = double.NaN;
                    foreach (LibreHardwareMonitor.Hardware.IHardware hw in computer.Hardware)
                    {
                        if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu) continue;
                        hw.Update();
                        foreach (LibreHardwareMonitor.Hardware.ISensor s in hw.Sensors)
                        {
                            if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature || !s.Value.HasValue) continue;
                            if (s.Name.Contains("Distance")) continue;   // "Distance to TjMax" is not a temperature, it is the remaining margin
                            double v = s.Value.Value;
                            if (v < 10 || v > 110) continue;
                            // Intel "CPU Package", AMD "Core (Tctl/Tdie)": overall CPU temperature
                            if (s.Name.Contains("Package") || s.Name.Contains("Tctl") || s.Name.Contains("Tdie")) package = v;
                            if (double.IsNaN(best) || v > best) best = v;
                        }
                    }
                    return !double.IsNaN(package) ? package : best;
                }
                catch (Exception)
                {
                    computer = null;
                    retryAt = DateTime.UtcNow.AddSeconds(60);
                    return double.NaN;
                }
            }
        }
    }

    // Temperature: Windows ACPI thermal zone (no admin needed). Returns Celsius, NaN if unreadable.
    static class Thermal
    {
        // All thermal zones are read. So that a stuck or wrong zone (e.g. always 28 C) does not decide on its own,
        // the hottest zone in the valid range is used; the CPU is usually the hottest.
        public static double ReadCelsius()
        {
            // First the CPU's own sensor (LibreHardwareMonitor); otherwise fall back to the Windows sensor reading
            double cpu = CpuSensor.Read();
            if (!double.IsNaN(cpu)) return cpu;

            List<double> readings = new List<double>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2",
                    "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                {
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        double kelvin = Convert.ToDouble(o["Temperature"]);
                        if (kelvin > 0) readings.Add(kelvin - 273.15);
                    }
                }
            }
            catch (Exception)
            {
                // "--" is shown if the sensor cannot be read
            }

            // Values outside the valid range are dropped; the hottest remaining one is used. NaN ("--") if none.
            double best = double.NaN;
            foreach (double c in readings)
            {
                if (c < 10 || c > 110) continue;
                if (double.IsNaN(best) || c > best) best = c;
            }
            return best;
        }
    }


    // Start with Windows: adds/removes a PowerNotch shortcut in the Startup folder. No admin needed;
    // since the shortcut opens normally, it is opened as admin through the task.
    static class Autostart
    {
        const string LinkName = "PowerNotch.lnk";

        static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName); }
        }

        public static void Apply(bool on)
        {
            try
            {
                if (on)
                {
                    if (!File.Exists(LinkPath)) Create(LinkPath);
                }
                else if (File.Exists(LinkPath))
                {
                    File.Delete(LinkPath);
                }
            }
            catch (Exception) { }
        }

        static void Create(string lnk)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type scType = sc.GetType();
            string exe = Application.ExecutablePath;
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "PowerNotch" });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    // Game FPS: DXGI "Present" events are counted via ETW (PresentMon's method, no external package).
    // A real-time ETW session requires admin; without it, or with no measurement, Fps() returns -1.
    // Struct sizes (x64, evntrace.h / evntcons.h) were computed by hand and written with offsets.
    static class FpsMonitor
    {
        delegate void EventRecordDelegate(IntPtr eventRecord);

        const string SessionName = "PowerNotchFps";
        static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        const ushort PresentStartId = 0x2A;          // DXGI Present_Start
        const ulong DxgiKeyword = 0x8000000000000002; // DXGI manifest keywords
        const byte LevelVerbose = 5;
        const uint EnableProviderCode = 1;
        const uint ControlStop = 1;
        const uint ErrAlreadyExists = 183;
        const uint WnodeFlagTracedGuid = 0x00020000;
        const uint EventTraceRealTimeMode = 0x00000100;
        const uint ProcessTraceRealTime = 0x00000100;
        const uint ProcessTraceEventRecord = 0x10000000;
        const int PropsSize = 120;    // EVENT_TRACE_PROPERTIES (LoggerName is written after it)
        const int LogfileSize = 448;  // EVENT_TRACE_LOGFILEW
        const int WindowMs = 1000;    // sliding count window
        const int KeepMs = 2000;      // events older than this are dropped
        static readonly ulong InvalidHandle = ulong.MaxValue;

        static readonly object gate = new object();
        static readonly object countGate = new object();
        static readonly Dictionary<uint, Queue<int>> presents = new Dictionary<uint, Queue<int>>();
        static ulong sessionHandle = 0;
        static ulong traceHandle = InvalidHandle;
        static IntPtr propsPtr = IntPtr.Zero;
        static IntPtr logfilePtr = IntPtr.Zero;
        static IntPtr loggerNamePtr = IntPtr.Zero;
        static Thread consumer;
        static EventRecordDelegate onRecord; // kept so it is not garbage collected
        static bool running = false;
        static volatile uint watchedPid = 0;   // ID of the process currently being counted (foreground)

        [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint StartTrace(out ulong handle, string name, IntPtr properties);

        [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint control);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ulong OpenTrace(IntPtr logfile);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint ProcessTrace(ref ulong handles, uint count, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint CloseTrace(ulong handle);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Starts when the setting is turned on, stops when turned off
        public static void Apply(bool on)
        {
            if (on) Start(); else Stop();
        }

        static void Start()
        {
            lock (gate)
            {
                if (running) return;
                running = StartSession();
            }
        }

        static void Stop()
        {
            lock (gate)
            {
                if (!running) return;
                running = false;
                StopSession();
            }
        }

        static bool StartSession()
        {
            propsPtr = Marshal.AllocHGlobal(PropsSize + (SessionName.Length + 1) * 2);
            InitProps(propsPtr, PropsSize + (SessionName.Length + 1) * 2);
            uint rc = StartTrace(out sessionHandle, SessionName, propsPtr);
            if (rc == ErrAlreadyExists)
            {
                // A previous session is still around: stop it and retry
                ControlTrace(0, SessionName, propsPtr, ControlStop);
                InitProps(propsPtr, PropsSize + (SessionName.Length + 1) * 2);
                rc = StartTrace(out sessionHandle, SessionName, propsPtr);
            }
            if (rc != 0) { FreeBuffers(); return false; }

            Guid dxgi = DxgiProvider;
            rc = EnableTraceEx2(sessionHandle, ref dxgi, EnableProviderCode, LevelVerbose, DxgiKeyword, 0, 0, IntPtr.Zero);
            if (rc != 0) { StopSession(); return false; }

            loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);
            logfilePtr = Marshal.AllocHGlobal(LogfileSize);
            Marshal.Copy(new byte[LogfileSize], 0, logfilePtr, LogfileSize);
            Marshal.WriteIntPtr(logfilePtr, 8, loggerNamePtr);                                // LoggerName
            Marshal.WriteInt32(logfilePtr, 28, (int)(ProcessTraceRealTime | ProcessTraceEventRecord)); // ProcessTraceMode
            onRecord = OnEventRecord;
            Marshal.WriteIntPtr(logfilePtr, 424, Marshal.GetFunctionPointerForDelegate(onRecord));   // EventRecordCallback

            traceHandle = OpenTrace(logfilePtr);
            if (traceHandle == InvalidHandle) { StopSession(); return false; }

            ulong handle = traceHandle;
            consumer = new Thread(delegate () { ProcessTrace(ref handle, 1, IntPtr.Zero, IntPtr.Zero); });
            consumer.IsBackground = true;
            consumer.Start();
            return true;
        }

        static void StopSession()
        {
            if (sessionHandle != 0) ControlTrace(sessionHandle, null, propsPtr, ControlStop);
            if (traceHandle != InvalidHandle) CloseTrace(traceHandle);
            if (consumer != null) consumer.Join(2000);
            sessionHandle = 0;
            traceHandle = InvalidHandle;
            consumer = null;
            FreeBuffers();
            lock (countGate) presents.Clear();
        }

        static void FreeBuffers()
        {
            if (propsPtr != IntPtr.Zero) { Marshal.FreeHGlobal(propsPtr); propsPtr = IntPtr.Zero; }
            if (logfilePtr != IntPtr.Zero) { Marshal.FreeHGlobal(logfilePtr); logfilePtr = IntPtr.Zero; }
            if (loggerNamePtr != IntPtr.Zero) { Marshal.FreeHGlobal(loggerNamePtr); loggerNamePtr = IntPtr.Zero; }
        }

        // EVENT_TRACE_PROPERTIES: Wnode.BufferSize, Wnode.Flags, Wnode.Guid, LogFileMode, LoggerNameOffset
        static void InitProps(IntPtr p, int size)
        {
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size);
            Marshal.WriteInt32(p, 44, (int)WnodeFlagTracedGuid);
            Marshal.Copy(Guid.NewGuid().ToByteArray(), 0, IntPtr.Add(p, 24), 16);
            Marshal.WriteInt32(p, 64, (int)EventTraceRealTimeMode);
            Marshal.WriteInt32(p, 116, PropsSize);
        }

        // Comes from the consumer thread. Exceptions must not escape (native code boundary).
        static void OnEventRecord(IntPtr record)
        {
            try
            {
                if (Marshal.ReadInt16(record, 40) != PresentStartId) return; // EventDescriptor.Id
                uint pid = (uint)Marshal.ReadInt32(record, 12); // EventHeader.ProcessId
                // Only frames of the foreground process are counted; all other processes (many events) are skipped
                if (pid != watchedPid) return;
                Guid provider = (Guid)Marshal.PtrToStructure(IntPtr.Add(record, 24), typeof(Guid));
                if (provider != DxgiProvider) return;
                int now = Environment.TickCount;
                lock (countGate)
                {
                    Queue<int> q;
                    if (!presents.TryGetValue(pid, out q))
                    {
                        q = new Queue<int>();
                        presents[pid] = q;
                    }
                    q.Enqueue(now);
                }
            }
            catch (Exception) { }
        }

        // Present count (FPS) of the foreground window over the last 1 second. -1 if there is no measurement.
        public static int Fps()
        {
            if (!running) return -1;
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) { watchedPid = 0; return -1; }
            watchedPid = pid;
            int now = Environment.TickCount;
            lock (countGate)
            {
                Queue<int> q;
                if (!presents.TryGetValue(pid, out q)) return 0;
                while (q.Count > 0 && now - q.Peek() > KeepMs) q.Dequeue();
                int count = 0;
                foreach (int t in q)
                {
                    if (now - t <= WindowMs) count++;
                }
                return count;
            }
        }
    }

    // Admin mode: some features require admin rights. A "PowerNotch" scheduled task is installed once
    // (highest privileges, runs without UAC). On later launches PowerNotch.exe runs the task and exits;
    // the admin process opens without a UAC prompt.
    static class Admin
    {
        const string TaskName = "PowerNotch";
        public static readonly bool Elevated = IsElevated();

        // Compatibility layer: if the exe has a "RUNASADMIN" entry, Windows prompts UAC on every launch (it also overrides asInvoker).
        // The HKLM entry requires admin, so the installer (an elevated process) deletes it.
        const string LayerKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

        public static bool LayerSet()
        {
            string exe = Application.ExecutablePath;
            return ReadLayer(Registry.LocalMachine, exe) || ReadLayer(Registry.CurrentUser, exe);
        }

        static bool ReadLayer(RegistryKey root, string exe)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(LayerKey, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(exe) as string;
                    return value != null && value.Contains("RUNASADMIN");
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void ClearLayer()
        {
            string exe = Application.ExecutablePath;
            foreach (RegistryKey root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    if (!ReadLayer(root, exe)) continue;
                    using (RegistryKey key = root.OpenSubKey(LayerKey, true))
                    {
                        if (key != null) key.DeleteValue(exe, false);
                    }
                }
                catch (Exception) { }
            }
        }

        // Is setup needed: if the task is missing or the compatibility entry forces UAC
        public static bool NeedsSetup()
        {
            return !TaskExists() || LayerSet();
        }

        static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Does the task run this exe? The task of an old/moved install may point to another path;
        // in that case the app does not try to relaunch as admin (so that nothing silently opens).
        public static bool TaskPointsHere()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Query /TN \"" + TaskName + "\" /XML");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string xml = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0 && xml.IndexOf(SecurityElement.Escape(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TaskExists()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0;
        }

        // Runs the task. Returns 1 if there is no task.
        public static int StartTask()
        {
            return RunSchtasks("/Run /TN \"" + TaskName + "\"");
        }

        // Writes the task (requires admin). Called from the process elevated with "--gorev" or from the app already running as admin.
        public static int CreateTaskFor(string user)
        {
            ClearLayer();
            // The task runs this exe as admin without UAC; the task is not created if the folder cannot be locked
            if (!LockFolder(Path.GetDirectoryName(Application.ExecutablePath))) return -3;
            string xml = Path.Combine(Path.GetTempPath(), "PowerNotch_gorev_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                // The file is kept open until schtasks reads it: no other process can change its content in between
                byte[] data = Encoding.Unicode.GetPreamble();
                byte[] body = Encoding.Unicode.GetBytes(TaskXml(user));
                using (FileStream fs = new FileStream(xml, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Write(body, 0, body.Length);
                    fs.Flush();
                    return RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + xml + "\" /F");
                }
            }
            catch (Exception)
            {
                return -2;
            }
            finally
            {
                try { File.Delete(xml); } catch (Exception) { }
            }
        }

        // Makes the program folder modifiable by administrators only (like Program Files).
        // If the folder stayed user-writable, any program could replace the exe and gain admin rights
        // through the admin task. Ownership also moves to Administrators; otherwise the owner could reopen permissions.
        public static bool LockFolder(string folder)
        {
            try
            {
                SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

                DirectorySecurity ds = new DirectorySecurity();
                ds.SetOwner(admins);
                ds.SetAccessRuleProtection(true, false);
                ds.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                ds.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                ds.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(folder, ds);

                // Files inside: explicit permissions are removed, only those inherited from the folder remain
                foreach (string f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    FileSecurity fs = new FileSecurity();
                    fs.SetOwner(admins);
                    fs.SetAccessRuleProtection(false, false);
                    File.SetAccessControl(f, fs);
                }
                foreach (string d in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories))
                {
                    DirectorySecurity sub = new DirectorySecurity();
                    sub.SetOwner(admins);
                    sub.SetAccessRuleProtection(false, false);
                    Directory.SetAccessControl(d, sub);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Called from settings. If not admin, asks for UAC approval once. Success: null, error: message.
        public static string Install()
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            int code;
            if (Elevated)
            {
                code = CreateTaskFor(user);
            }
            else
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--gorev \"" + user + "\"");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit();
                        code = p.ExitCode;
                    }
                }
                catch (Exception)
                {
                    return Loc.T("admin_uac_denied");
                }
            }
            return code == 0 ? null : Loc.F("admin_task_fail", code);
        }

        static string TaskXml(string user)
        {
            string exe = Application.ExecutablePath;
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>PowerNotch - yonetici olarak, UAC sormadan</Description></RegistrationInfo>\r\n" +
                "  <Triggers />\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <UserId>" + SecurityElement.Escape(user) + "</UserId>\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>" + SecurityElement.Escape(exe) + "</Command>\r\n" +
                "      <Arguments>--yonetici</Arguments>\r\n" +
                "      <WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>\r\n";
        }

        static int RunSchtasks(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }

    // Runs long jobs (BIOS writes, powercfg) in the background and brings the result to the UI thread
    static class Actions
    {
        public static void Run(Func<bool> work, Action<bool> done)
        {
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = work(); }
                catch (Exception) { ok = false; }
                if (ui != null) ui.Post(delegate { done(ok); }, null);
                else done(ok);
            });
        }
    }



    // A single telemetry snapshot (battery and CPU temperature)
    public class HpSnapshot
    {
        public BatteryInfo Battery = new BatteryInfo();
        public string PowerMode = "diger";
        public double CpuTemp = double.NaN;
        public double GpuTemp = double.NaN;
    }

    // Reads happen in the background; the UI does not freeze
    // In Maximum Saver the discrete graphics card (NVIDIA) is disabled in Windows; re-enabled in other modes.
    // Only done when the display is driven by the non-NVIDIA (integrated) card; otherwise the screen could go dark.
    static class GpuSwitch
    {
        // NVIDIA device ID (PCI\VEN_10DE...); null if not found
        static string DiscreteId()
        {
            foreach (ManagementObject o in new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, PNPDeviceID FROM Win32_VideoController").Get())
            {
                string name = o["Name"] as string ?? "";
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return o["PNPDeviceID"] as string;
            }
            return null;
        }

        // Is the display driven by the non-NVIDIA (integrated) graphics card?
        static bool DisplayOnIntegrated()
        {
            foreach (ManagementObject o in new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, CurrentHorizontalResolution FROM Win32_VideoController").Get())
            {
                string name = o["Name"] as string ?? "";
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (o["CurrentHorizontalResolution"] != null && Convert.ToInt32(o["CurrentHorizontalResolution"]) > 0) return true;
            }
            return false;
        }

        // Enables/disables the device with pnputil (requires admin). Returns a description on error.
        static string Pnp(string id, bool enable)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), (enable ? "/enable-device \"" : "/disable-device \"") + id + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0) return null;
                return "ekran karti " + (enable ? "acilamadi" : "kapatilamadi") + " (kod " + p.ExitCode + ")";
            }
        }

        // Called when the mode changes. Max: the discrete card is disabled. Other modes: re-enabled if it was disabled.
        public static string Apply(string mode)
        {
            try
            {
                if (mode == "maks")
                {
                    if (Settings.GpuOff) return null;
                    string id = DiscreteId();
                    if (id == null) return null;
                    if (!DisplayOnIntegrated()) return "ekran NVIDIA'da, ekran karti kapatilmadi";
                    string err = Pnp(id, false);
                    if (err == null)
                    {
                        Settings.GpuId = id;
                        Settings.GpuOff = true;
                        Settings.Save();
                    }
                    return err;
                }
                return Restore();
            }
            catch (Exception ex)
            {
                return "ekran karti: " + ex.Message;
            }
        }

        // Re-enables a disabled discrete graphics card (on mode change, on exit, on startup)
        public static string Restore()
        {
            if (!Settings.GpuOff) return null;
            try
            {
                string err = Pnp(Settings.GpuId, true);
                if (err == null)
                {
                    Settings.GpuOff = false;
                    Settings.Save();
                }
                return err;
            }
            catch (Exception ex)
            {
                return "ekran karti: " + ex.Message;
            }
        }
    }

    static class Telemetry
    {
        static int busy = 0;

        public static void Poll(Action<HpSnapshot> done)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    HpSnapshot s = Work();
                    if (ui != null) ui.Post(delegate { done(s); }, null);
                    else done(s);
                }
                catch (Exception)
                {
                    // This cycle is skipped
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }
            });
        }

        // CPU temperature only (every 1 s): the full cycle runs every 3 s, this read more often. Only one read runs at a time.
        static int cpuBusy = 0;

        public static void PollCpu(Action<double> done)
        {
            if (Interlocked.CompareExchange(ref cpuBusy, 1, 0) != 0) return;
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    double celsius = Thermal.ReadCelsius();
                    if (ui != null) ui.Post(delegate { done(celsius); }, null);
                    else done(celsius);
                }
                catch (Exception)
                {
                    // This read is skipped
                }
                finally
                {
                    Interlocked.Exchange(ref cpuBusy, 0);
                }
            });
        }

        static HpSnapshot Work()
        {
            HpSnapshot s = new HpSnapshot();
            s.Battery = BatteryInfo.Read();
            s.PowerMode = Power.ActiveKey();
            s.CpuTemp = Thermal.ReadCelsius();
            if (Settings.ShowGpuTemp) s.GpuTemp = GpuTemp.Read();
            return s;
        }
    }

    public static class PilUi
    {
        public static readonly Color Bg        = Color.FromArgb(15, 19, 26);
        public static readonly Color Card      = Color.FromArgb(24, 29, 39);
        public static readonly Color CardHover = Color.FromArgb(31, 38, 51);
        public static readonly Color Border    = Color.FromArgb(40, 47, 62);
        public static readonly Color TextMain  = Color.FromArgb(243, 244, 246);
        public static readonly Color Muted     = Color.FromArgb(139, 148, 168);
        public static readonly Color Green     = Color.FromArgb(52, 211, 153);
        public static readonly Color Blue      = Color.FromArgb(96, 165, 250);
        public static readonly Color Orange    = Color.FromArgb(251, 146, 60);
        public static readonly Color Violet    = Color.FromArgb(167, 139, 250);
        public static readonly Color Cyan      = Color.FromArgb(34, 211, 238);
        public static readonly Color Amber     = Color.FromArgb(251, 191, 36);
        public static readonly Color Red       = Color.FromArgb(248, 113, 113);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2f;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Region RoundedRegion(int w, int h, float radius)
        {
            using (GraphicsPath p = Rounded(new RectangleF(0, 0, w, h), radius))
            {
                return new Region(p);
            }
        }

        public static void Text(Graphics g, string s, string face, float size, Color c, float x, float y)
        {
            using (Font f = new Font(Loc.Face(face), size, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush b = new SolidBrush(c))
            {
                g.DrawString(s, f, b, x, y);
            }
        }

        // Icons: battery, gauge, lightning, leaf, fan (3-blade propeller)
        public static void Icon(Graphics g, string kind, RectangleF box, Color c)
        {
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            using (Pen pen = new Pen(c, 2.2f))
            using (SolidBrush brush = new SolidBrush(c))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                if (kind == "battery")
                {
                    RectangleF body = new RectangleF(cx - 14f, cy - 8f, 25f, 16f);
                    using (GraphicsPath gp = Rounded(body, 3.5f)) g.DrawPath(pen, gp);
                    g.FillRectangle(brush, cx + 13f, cy - 4f, 3f, 8f);
                    g.FillRectangle(brush, cx - 11f, cy - 5f, 13f, 10f);
                }
                else if (kind == "gauge")
                {
                    RectangleF arc = new RectangleF(cx - 14f, cy - 13f, 28f, 28f);
                    g.DrawArc(pen, arc, 180f, 180f);
                    g.DrawLine(pen, cx, cy + 1f, cx + 7f, cy - 6f);
                    g.FillEllipse(brush, cx - 2.5f, cy - 1.5f, 5f, 5f);
                }
                else if (kind == "bolt")
                {
                    g.FillPolygon(brush, new PointF[] {
                        new PointF(cx + 4f, cy - 14f), new PointF(cx - 9f, cy + 2f), new PointF(cx - 1f, cy + 2f),
                        new PointF(cx - 4f, cy + 14f), new PointF(cx + 9f, cy - 3f), new PointF(cx + 1f, cy - 3f)
                    });
                }
                else if (kind == "leaf")
                {
                    using (GraphicsPath lf = new GraphicsPath())
                    {
                        lf.AddBezier(cx - 13f, cy + 12f, cx - 15f, cy - 6f, cx - 2f, cy - 15f, cx + 14f, cy - 13f);
                        lf.AddBezier(cx + 14f, cy - 13f, cx + 15f, cy + 4f, cx + 4f, cy + 14f, cx - 13f, cy + 12f);
                        g.FillPath(brush, lf);
                    }
                    using (Pen vein = new Pen(PilUi.Bg, 1.6f)) g.DrawLine(vein, cx - 10f, cy + 10f, cx + 7f, cy - 7f);
                }
                else if (kind == "fan")
                {
                    for (int i = 0; i < 3; i++)
                    {
                        using (GraphicsPath blade = new GraphicsPath())
                        {
                            blade.AddEllipse(cx - 4.5f, cy - 15f, 9f, 14f);
                            using (Matrix m = new Matrix())
                            {
                                m.RotateAt(i * 120f, new PointF(cx, cy));
                                blade.Transform(m);
                            }
                            g.FillPath(brush, blade);
                        }
                    }
                    using (SolidBrush hub = new SolidBrush(PilUi.Bg))
                    {
                        g.FillEllipse(hub, cx - 3f, cy - 3f, 6f, 6f);
                    }
                }
            }
        }
    }

    // Mode texts and colors (shared by the card, status panel and notch)
    static class TempInfo
    {



        public static string TempValue(double t)
        {
            return double.IsNaN(t) ? "--°C" : ((int)Math.Round(t)).ToString() + "°C";
        }

        public static Color TempColor(double t)
        {
            if (double.IsNaN(t)) return PilUi.Muted;
            if (t < 65) return PilUi.Green;
            if (t < 80) return PilUi.Amber;
            return PilUi.Red;
        }
    }

    // Mode card: icon, title, description, active badge
    public class ModeCard : Control
    {
        public string Title = "";
        public string Subtitle = "";
        public string Badge = "";
        public string IconKind = "battery";
        public Color Accent = PilUi.Blue;
        public bool IsCurrent = false;
        private bool hover = false;

        public ModeCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = PilUi.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(PilUi.Bg);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = PilUi.Rounded(r, 16f))
            {
                using (SolidBrush b = new SolidBrush(hover ? PilUi.CardHover : PilUi.Card)) g.FillPath(b, path);
                using (Pen p = new Pen(IsCurrent ? Accent : PilUi.Border, IsCurrent ? 2f : 1f)) g.DrawPath(p, path);
            }

            RectangleF iconBox = new RectangleF(18f, (Height - 52f) / 2f, 52f, 52f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, Accent))) g.FillEllipse(b, iconBox);
            PilUi.Icon(g, IconKind, iconBox, Accent);

            PilUi.Text(g, Title, "Segoe UI Semibold", 13f, PilUi.TextMain, 88f, Height / 2f - 19f);
            PilUi.Text(g, Subtitle, "Segoe UI", 9f, PilUi.Muted, 88f, Height / 2f + 5f);

            if (Badge.Length > 0)
            {
                using (Font f = new Font(Loc.Face("Segoe UI Semibold"), 8f, FontStyle.Regular, GraphicsUnit.Point))
                {
                    SizeF sz = g.MeasureString(Badge, f);
                    float w = sz.Width + 18f;
                    RectangleF pill = new RectangleF(Width - w - 16f, (Height - 22f) / 2f, w, 22f);
                    using (GraphicsPath pp = PilUi.Rounded(pill, 11f))
                    using (SolidBrush b = new SolidBrush(Accent)) g.FillPath(b, pp);
                    using (SolidBrush t = new SolidBrush(PilUi.Bg)) g.DrawString(Badge, f, t, pill.X + 9f, pill.Y + 4f);
                }
            }
        }
    }

    // Battery panel: percent, battery fill, status and active mode
    public class BatteryPanel : Control
    {
        public int Percent = -1;
        public bool NoBattery = false;
        public bool Charging = false;
        public string StateText = "";
        public string ActiveText = "";
        public Color ActiveColor = PilUi.Muted;

        public BatteryPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = PilUi.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(PilUi.Bg);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = PilUi.Rounded(r, 18f))
            {
                using (SolidBrush b = new SolidBrush(PilUi.Card)) g.FillPath(b, path);
                using (Pen p = new Pen(PilUi.Border, 1f)) g.DrawPath(p, path);
            }

            string pct = Percent >= 0 ? (Percent.ToString() + "%") : "--";
            PilUi.Text(g, pct, "Segoe UI Semibold", 30f, PilUi.TextMain, 22f, 12f);
            // The time-remaining text fits left of the battery icon (x=232); if long it is cut with ...
            using (StringFormat sf = new StringFormat())
            using (Font sf9 = new Font(Loc.Face("Segoe UI"), 9.5f))
            using (SolidBrush sb = new SolidBrush(PilUi.Muted))
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(StateText, sf9, sb, new RectangleF(24f, 62f, 196f, 20f), sf);
            }

            if (!NoBattery)
            {
                RectangleF body = new RectangleF(232f, 38f, 118f, 52f);
                using (GraphicsPath bp = PilUi.Rounded(body, 10f))
                using (Pen p = new Pen(PilUi.Muted, 2f)) g.DrawPath(p, bp);

                using (SolidBrush b = new SolidBrush(PilUi.Muted)) g.FillRectangle(b, 352f, 53f, 5f, 22f);

                if (Percent >= 0)
                {
                    Color level = Percent >= 50 ? PilUi.Green : (Percent >= 20 ? PilUi.Amber : PilUi.Red);
                    float fillW = 108f * Percent / 100f;
                    if (fillW >= 2f)
                    {
                        RectangleF fill = new RectangleF(237f, 43f, fillW, 42f);
                        using (GraphicsPath fp = PilUi.Rounded(fill, Math.Min(6f, fillW / 2f)))
                        using (SolidBrush b = new SolidBrush(level)) g.FillPath(b, fp);
                    }
                }

                if (Charging) PilUi.Icon(g, "bolt", body, PilUi.Bg);
            }

            using (SolidBrush b = new SolidBrush(ActiveColor)) g.FillEllipse(b, 24f, 106f, 9f, 9f);
            PilUi.Text(g, ActiveText, "Segoe UI Semibold", 10f, PilUi.TextMain, 40f, 101f);
        }
    }


    // On/off toggle (switch)
    public class ToggleSwitch : Control
    {
        bool on = false;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            BackColor = PilUi.Card;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath tp = PilUi.Rounded(track, Height / 2f))
            using (SolidBrush b = new SolidBrush(on ? PilUi.Cyan : PilUi.Border)) g.FillPath(b, tp);
            float knob = Height - 6f;
            float kx = on ? Width - knob - 3f : 3f;
            using (SolidBrush w = new SolidBrush(Color.White)) g.FillEllipse(w, kx, 3f, knob, knob);
        }
    }


    // Notch at the top center (iPhone-like). Its width follows its content (no padding).
    // Collapsed: battery, time remaining, CPU temperature.
    // Expands on hover and shows the power mode name.
    // Opens downward on click: power mode buttons.
    // Can be dragged horizontally. Drawn with a layered window (per-pixel alpha).
    // Smoothly moves the window from one rectangle and opacity to another (ease-out).
    // The timer runs on the UI thread; it also advances inside the modal settings window.
    static class Morph
    {
        // Spring motion (macOS/iOS feel): settles with a slight overshoot. Since zeta < 1 it overshoots a bit.
        static double Spring(double t)
        {
            const double zeta = 0.72;
            const double omega = 12.0;
            double wd = omega * Math.Sqrt(1.0 - zeta * zeta);
            return 1.0 - Math.Exp(-zeta * omega * t) * (Math.Cos(wd * t) + (zeta * omega / wd) * Math.Sin(wd * t));
        }

        // Accelerates in on close and stops softly at the end (the pull of the genie effect)
        static double EaseInOut(double k)
        {
            return k < 0.5 ? 4.0 * k * k * k : 1.0 - Math.Pow(-2.0 * k + 2.0, 3.0) / 2.0;
        }

        static double Smooth(double x)
        {
            double c = Math.Max(0.0, Math.Min(1.0, x));
            return c * c * (3.0 - 2.0 * c);
        }

        // Notch width (same as IslandForm.VisualRect)
        public const int NotchWidth = 240;

        // Genie shape. n = 0: normal window; n = 1: notch shape.
        // The top edge narrows into a "neck", the body stays below; the sides bend along bezier curves.
        public static GraphicsPath GeniePath(int w, int h, double n)
        {
            n = Math.Max(0.0, Math.Min(1.0, n));
            float neckW = (float)(w + (Math.Min(NotchWidth, w) - w) * Math.Pow(n, 0.7));
            float cx = w / 2f;
            float nl = cx - neckW / 2f;
            float nr = cx + neckW / 2f;
            float rt = Math.Max(0.5f, Math.Min((float)(18.0 * (1.0 - n)), Math.Min(neckW, h) / 2f));
            float rb = Math.Max(0.5f, Math.Min((float)(18.0 - 4.0 * n), Math.Min(w, h) / 2f));
            float depth = (float)Math.Max(0.0, Math.Min(h - rt - rb - 1.0, h * 0.6 * n));

            GraphicsPath p = new GraphicsPath();
            {
                p.AddLine(nl + rt, 0f, nr - rt, 0f);
                p.AddArc(nr - 2f * rt, 0f, 2f * rt, 2f * rt, -90f, 90f);
                p.AddBezier(nr, rt, nr, rt + 0.55f * depth, w, rt + 0.45f * depth, w, rt + depth);
                p.AddLine(w, rt + depth, w, h - rb);
                p.AddArc(w - 2f * rb, h - 2f * rb, 2f * rb, 2f * rb, 0f, 90f);
                p.AddLine(w - rb, h, rb, h);
                p.AddArc(0f, h - 2f * rb, 2f * rb, 2f * rb, 90f, 90f);
                p.AddLine(0f, h - rb, 0f, rt + depth);
                p.AddBezier(0f, rt + depth, 0f, rt + 0.45f * depth, nl, rt + 0.55f * depth, nl, rt);
                p.AddArc(nl, 0f, 2f * rt, 2f * rt, 180f, 90f);
                p.CloseFigure();
                return p;
            }
        }

        // Frame timing: 60 fps (16.67 ms). Frames advance by time; UI messages are processed every frame.
        const double FrameMs = 1000.0 / 60.0;
        static bool busy = false;
        public static bool Busy { get { return busy; } }

        public static void Play(int ms, Action<double, double> frame)
        {
            busy = true;
            try
            {
                Stopwatch clock = Stopwatch.StartNew();
                while (true)
                {
                    double sec = clock.Elapsed.TotalSeconds;
                    double k = Math.Min(1.0, sec / (ms / 1000.0));
                    frame(sec, k);
                    Application.DoEvents();
                    if (k >= 1.0) break;
                    // Wait until the next 60 fps frame time (1 ms timer resolution is on)
                    double elapsed = clock.Elapsed.TotalMilliseconds;
                    double next = (Math.Floor(elapsed / FrameMs) + 1.0) * FrameMs;
                    int wait = (int)Math.Ceiling(next - elapsed);
                    if (wait > 0) Thread.Sleep(wait);
                }
            }
            finally
            {
                busy = false;
            }
        }

        // Out of the notch: the layer flies out of the notch and stays at the target; the real window is not visible yet
        public static MorphOverlay Open(Rectangle from, Rectangle to, int ms)
        {
            MorphOverlay ov = new MorphOverlay(null);
            ov.Show();
            Play(ms, delegate(double sec, double k)
            {
                double e = k >= 1.0 ? 1.0 : Spring(sec);
                double n = Math.Max(0.0, Math.Min(1.0, 1.0 - e));
                ov.Push(Lerp(from, to, e), n, 1.0);
            });
            return ov;
        }

        // The real window fades in in place of the layer; then the layer closes
        public static void Reveal(Form f, MorphOverlay ov, Action after)
        {
            Play(160, delegate(double sec, double k)
            {
                f.Opacity = Math.Max(0.0, Math.Min(1.0, k));
                ov.Push(ov.Rect, 0.0, 1.0 - k);
            });
            ov.Close();
            if (after != null) after();
        }

        // A snapshot of the window flies into the notch as a genie on the layer. hideWithOpacity: the modal window is not closed, it becomes invisible.
        public static void Close(Form f, Rectangle from, Rectangle to, bool hideWithOpacity, Action done)
        {
            Bitmap snap = new Bitmap(Math.Max(2, f.Width), Math.Max(2, f.Height));
            f.DrawToBitmap(snap, new Rectangle(0, 0, snap.Width, snap.Height));
            MorphOverlay ov = new MorphOverlay(snap);
            ov.Show();
            if (hideWithOpacity) f.Opacity = 0; else f.Hide();
            Play(420, delegate(double sec, double k)
            {
                double e = EaseInOut(k);
                ov.Push(Lerp(from, to, e), e, 1.0 - Smooth((k - 0.75) / 0.25));
            });
            ov.Close();
            snap.Dispose();
            if (done != null) done();
        }

        static Rectangle Lerp(Rectangle a, Rectangle b, double e)
        {
            return new Rectangle(
                (int)Math.Round(a.X + (b.X - a.X) * e),
                (int)Math.Round(a.Y + (b.Y - a.Y) * e),
                Math.Max(2, (int)Math.Round(a.Width + (b.Width - a.Width) * e)),
                Math.Max(2, (int)Math.Round(a.Height + (b.Height - a.Height) * e)));
        }
    }

    // Temporary layered (UpdateLayeredWindow) window: animation frames are drawn here; the real window is not painted during the animation
    public class MorphOverlay : Form
    {
        readonly Bitmap snap;
        public Rectangle Rect = new Rectangle(0, 0, 2, 2);

        public MorphOverlay(Bitmap snapshot)
        {
            snap = snapshot;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(2, 2);
        }

        // Shown without taking focus
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;     // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x80000;  // WS_EX_LAYERED
                return cp;
            }
        }

        // Draws a frame in the given rectangle with the genie shape (n) and opacity
        public void Push(Rectangle r, double n, double opacity)
        {
            Rect = r;
            if (!IsHandleCreated) return;
            int w = Math.Max(2, r.Width);
            int h = Math.Max(2, r.Height);
            using (Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Morph.GeniePath(w, h, n))
                    {
                        using (SolidBrush b = new SolidBrush(PilUi.Bg)) g.FillPath(b, p);
                        if (snap != null)
                        {
                            g.SetClip(p);
                            g.InterpolationMode = InterpolationMode.Bilinear;
                            g.DrawImage(snap, new Rectangle(0, 0, w, h));
                            g.ResetClip();
                        }
                    }
                }
                byte alpha = (byte)Math.Round(255.0 * Math.Max(0.0, Math.Min(1.0, opacity)));
                PushBitmap(bmp, r.Location, w, h, alpha);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MPoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSize { public int Cx; public int Cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct MBlend
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref MPoint pptDst, ref MSize psize,
            IntPtr hdcSrc, ref MPoint pptSrc, int crKey, ref MBlend pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);

        void PushBitmap(Bitmap bmp, Point pos, int w, int h, byte alpha)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);
            try
            {
                MPoint dst = new MPoint { X = pos.X, Y = pos.Y };
                MSize size = new MSize { Cx = w, Cy = h };
                MPoint src = new MPoint { X = 0, Y = 0 };
                MBlend blend = new MBlend { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    // Drawn small pets (no image files). kind: 0 cat, 1 dog, 2 chick, 3 rabbit, 4 bird.
    // react (0..1): touch reaction; the pet jumps.
    static class Pet
    {
        public const int Count = 5;
        static readonly Color Dark = Color.FromArgb(40, 36, 44);
        static readonly Color Pink = Color.FromArgb(240, 150, 170);

        public static void Draw(Graphics g, int kind, RectangleF r, float react)
        {
            float hop = (float)Math.Sin(react * Math.PI) * r.Height * 0.28f;
            float w = r.Width;
            float h = r.Height;
            float cx = r.X + w / 2f;
            float bottom = r.Bottom - hop;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (kind)
            {
                case 0: DrawCat(g, cx, bottom, w, h); break;
                case 1: DrawDog(g, cx, bottom, w, h); break;
                case 2: DrawChick(g, cx, bottom, w, h); break;
                case 3: DrawRabbit(g, cx, bottom, w, h); break;
                default: DrawBird(g, cx, bottom, w, h); break;
            }
            g.SmoothingMode = old;
        }

        static void DrawCat(Graphics g, float cx, float bottom, float w, float h)
        {
            Color fur = Color.FromArgb(205, 205, 215);
            float hr = w * 0.27f;
            float hy = bottom - h * 0.55f;
            using (Pen tail = new Pen(fur, Math.Max(1.5f, w * 0.07f)))
                g.DrawCurve(tail, new PointF[] { new PointF(cx + w * 0.25f, bottom - h * 0.2f), new PointF(cx + w * 0.5f, bottom - h * 0.3f), new PointF(cx + w * 0.42f, bottom - h * 0.6f) });
            Ell(g, fur, cx, bottom - h * 0.22f, w * 0.30f, h * 0.20f);
            Tri(g, fur, new PointF(cx - hr * 0.95f, hy - hr * 0.1f), new PointF(cx - hr * 0.7f, hy - hr * 1.1f), new PointF(cx - hr * 0.1f, hy - hr * 0.8f));
            Tri(g, fur, new PointF(cx + hr * 0.95f, hy - hr * 0.1f), new PointF(cx + hr * 0.7f, hy - hr * 1.1f), new PointF(cx + hr * 0.1f, hy - hr * 0.8f));
            Ell(g, fur, cx, hy, hr, hr);
            Dot(g, Dark, cx - hr * 0.4f, hy - hr * 0.1f, w * 0.05f);
            Dot(g, Dark, cx + hr * 0.4f, hy - hr * 0.1f, w * 0.05f);
            Tri(g, Pink, new PointF(cx - hr * 0.12f, hy + hr * 0.2f), new PointF(cx + hr * 0.12f, hy + hr * 0.2f), new PointF(cx, hy + hr * 0.38f));
        }

        static void DrawDog(Graphics g, float cx, float bottom, float w, float h)
        {
            Color fur = Color.FromArgb(196, 140, 82);
            Color ear = Color.FromArgb(120, 78, 46);
            Color snout = Color.FromArgb(232, 196, 150);
            float hr = w * 0.27f;
            float hy = bottom - h * 0.55f;
            Ell(g, fur, cx, bottom - h * 0.12f, w * 0.24f, h * 0.14f);
            Ell(g, ear, cx - hr * 0.92f, hy + hr * 0.25f, hr * 0.45f, hr * 0.85f);
            Ell(g, ear, cx + hr * 0.92f, hy + hr * 0.25f, hr * 0.45f, hr * 0.85f);
            Ell(g, fur, cx, hy, hr, hr);
            Ell(g, snout, cx, hy + hr * 0.42f, hr * 0.5f, hr * 0.36f);
            Dot(g, Dark, cx - hr * 0.4f, hy - hr * 0.15f, w * 0.05f);
            Dot(g, Dark, cx + hr * 0.4f, hy - hr * 0.15f, w * 0.05f);
            Dot(g, Dark, cx, hy + hr * 0.25f, w * 0.07f);
        }

        static void DrawChick(Graphics g, float cx, float bottom, float w, float h)
        {
            Color yel = Color.FromArgb(255, 212, 64);
            Color wing = Color.FromArgb(240, 180, 40);
            Color beak = Color.FromArgb(255, 140, 40);
            Color comb = Color.FromArgb(230, 60, 60);
            float br = w * 0.36f;
            float by = bottom - h * 0.42f;
            using (Pen legs = new Pen(beak, 1.5f))
            {
                g.DrawLine(legs, cx - br * 0.3f, by + br * 0.8f, cx - br * 0.3f, bottom);
                g.DrawLine(legs, cx + br * 0.3f, by + br * 0.8f, cx + br * 0.3f, bottom);
            }
            Ell(g, yel, cx, by, br, br * 0.92f);
            Ell(g, wing, cx - br * 0.25f, by + br * 0.1f, br * 0.42f, br * 0.28f);
            Dot(g, comb, cx + br * 0.05f, by - br * 0.9f, w * 0.09f);
            Dot(g, Dark, cx + br * 0.45f, by - br * 0.2f, w * 0.05f);
            Tri(g, beak, new PointF(cx + br * 0.7f, by - br * 0.05f), new PointF(cx + br * 1.05f, by + br * 0.1f), new PointF(cx + br * 0.7f, by + br * 0.25f));
        }

        static void DrawRabbit(Graphics g, float cx, float bottom, float w, float h)
        {
            Color fur = Color.FromArgb(238, 238, 242);
            float hr = w * 0.27f;
            float hy = bottom - h * 0.44f;
            Ell(g, fur, cx, bottom - h * 0.12f, w * 0.24f, h * 0.14f);
            Ell(g, fur, cx - hr * 0.42f, hy - hr * 1.25f, hr * 0.26f, hr * 0.75f);
            Ell(g, fur, cx + hr * 0.42f, hy - hr * 1.25f, hr * 0.26f, hr * 0.75f);
            Ell(g, Pink, cx - hr * 0.42f, hy - hr * 1.2f, hr * 0.12f, hr * 0.5f);
            Ell(g, Pink, cx + hr * 0.42f, hy - hr * 1.2f, hr * 0.12f, hr * 0.5f);
            Ell(g, fur, cx, hy, hr, hr * 0.95f);
            Dot(g, Dark, cx - hr * 0.4f, hy - hr * 0.1f, w * 0.05f);
            Dot(g, Dark, cx + hr * 0.4f, hy - hr * 0.1f, w * 0.05f);
            Dot(g, Pink, cx, hy + hr * 0.25f, w * 0.05f);
        }

        static void DrawBird(Graphics g, float cx, float bottom, float w, float h)
        {
            Color blue = Color.FromArgb(80, 150, 240);
            Color belly = Color.FromArgb(200, 225, 255);
            Color beak = Color.FromArgb(255, 180, 60);
            float br = w * 0.34f;
            float by = bottom - h * 0.42f;
            Tri(g, blue, new PointF(cx - br * 0.4f, by - br * 0.7f), new PointF(cx - br * 0.1f, by - br * 1.35f), new PointF(cx + br * 0.25f, by - br * 0.75f));   // crest feather
            Ell(g, blue, cx, by, br, br * 0.95f);
            Ell(g, belly, cx + br * 0.1f, by + br * 0.35f, br * 0.6f, br * 0.5f);
            Tri(g, beak, new PointF(cx + br * 0.85f, by - br * 0.15f), new PointF(cx + br * 1.3f, by + br * 0.05f), new PointF(cx + br * 0.85f, by + br * 0.25f));
            Ell(g, Color.FromArgb(55, 120, 210), cx - br * 0.15f, by - br * 0.1f, br * 0.55f, br * 0.3f);   // wing
            Dot(g, Dark, cx + br * 0.4f, by - br * 0.3f, w * 0.06f);
        }

        static void Ell(Graphics g, Color c, float cx, float cy, float rx, float ry)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - rx, cy - ry, rx * 2f, ry * 2f);
        }

        static void Dot(Graphics g, Color c, float cx, float cy, float r)
        {
            Ell(g, c, cx, cy, r, r);
        }

        static void Tri(Graphics g, Color c, PointF a, PointF b, PointF d)
        {
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, new PointF[] { a, b, d });
        }
    }

    // Pet picker: five animals; the selected one is shown with a frame
    public class PetPicker : Control
    {
        public event EventHandler SelectionChanged;
        const int Cell = 40;
        const int Gap = 8;
        int selected = 0;

        public PetPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = PilUi.Card;
            Size = new Size(Pet.Count * Cell + (Pet.Count - 1) * Gap, Cell);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = Math.Max(0, Math.Min(Pet.Count - 1, value)); Invalidate(); }
        }

        Rectangle CellRect(int i)
        {
            return new Rectangle(i * (Cell + Gap), 0, Cell, Cell);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            for (int i = 0; i < Pet.Count; i++)
            {
                RectangleF r = CellRect(i);
                bool on = i == selected;
                using (GraphicsPath cp = PilUi.Rounded(r, 10f))
                using (SolidBrush fill = new SolidBrush(on ? PilUi.CardHover : PilUi.Card))
                {
                    g.FillPath(fill, cp);
                    if (on)
                    {
                        using (Pen edge = new Pen(PilUi.Cyan, 1.5f)) g.DrawPath(edge, cp);
                    }
                }
                Pet.Draw(g, i, new RectangleF(r.X + 6f, r.Y + 6f, r.Width - 12f, r.Height - 12f), 0f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            for (int i = 0; i < Pet.Count; i++)
            {
                if (!CellRect(i).Contains(e.Location)) continue;
                SelectedIndex = i;
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                return;
            }
        }
    }

    public class IslandForm : Form
    {
        public const int WinW = 480;
        public const int WinH = 116;
        const int BarH = 34;
        const int OpenW = 380;
        const int OpenH = 70;
        const float NotchRadius = 14f;
        const float Pad = 11f;
        const int ChipW = 66;
        const int ChipH = 24;
        const int ChipGap = 6;
        const int PowerRowY = 40;
        static readonly string[] PowerKeys = { "maks", "pil", "normal", "performans" };
        static string[] PowerLabels { get { return new[] { Loc.T("chip_maks"), Loc.T("chip_pil"), Loc.T("chip_denge"), Loc.T("chip_perf") }; } }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeSize { public int Cx; public int Cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);

        const int ULW_ALPHA = 2;
        const byte AC_SRC_OVER = 0;
        const byte AC_SRC_ALPHA = 1;

        readonly Action<string> onPower;
        readonly Action onOpenMain;
        readonly Font fMid = new Font(Loc.Face("Segoe UI Semibold"), 9f, FontStyle.Regular, GraphicsUnit.Point);
        readonly Font fSmall = new Font(Loc.Face("Segoe UI"), 9f, FontStyle.Regular, GraphicsUnit.Point);
        readonly Font fChip = new Font(Loc.Face("Segoe UI Semibold"), 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        HpSnapshot snap = new HpSnapshot();
        bool hovering = false;
        bool open = false;
        bool pressed = false;
        bool dragging = false;
        Point pressScreen;
        int pressCenter;

        // Animation: hover and open values (0..1) ease toward their target; one draw per frame
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        float hoverT = 0f;
        float openT = 0f;
        readonly Stopwatch animClock = new Stopwatch();

        // Bounce on return to the notch (macOS/iOS-style spring): a short sine that slowly decays
        readonly Stopwatch pulseClock = new Stopwatch();
        bool pulsing = false;

        // Pet: at the right end of the bar; jumps on click. petRect is computed in the last draw (click area)
        const float PetW = 28f;
        const float PetH = 26f;
        RectangleF petRect;
        bool petReacting = false;
        readonly Stopwatch petClock = new Stopwatch();

        public IslandForm(Action<string> onPower, Action onOpenMain)
        {
            this.onPower = onPower;
            this.onOpenMain = onOpenMain;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Cursor = Cursors.Hand;
            Size = new Size(WinW, WinH);

            anim.Interval = 16;
            anim.Tick += OnAnimTick;
            MouseEnter += (s, e) => { hovering = true; StartAnim(); };
            MouseLeave += (s, e) =>
            {
                hovering = false;
                if (open && !pressed) open = false;
                StartAnim();
            };
        }

        // Shown without taking focus: does not interrupt the main window
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;     // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x80000;  // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PlaceAtCenter();
            Render();
        }

        // Rebuilds the drawing when a setting changes (applied instantly)
        public void Redraw()
        {
            Render();
        }
        public void Apply(HpSnapshot s)
        {
            snap = s;
            Render();
        }


        // Rough rectangle of the collapsed notch: windows open from here and return here
        public Rectangle VisualRect
        {
            get { return new Rectangle(ResolvedCenterX() - 120, Location.Y, 240, BarH); }
        }

        // Saved center (or screen center), clamped to the screen bounds
        public int ResolvedCenterX()
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            int c = Settings.NotchX < 0 ? b.Left + b.Width / 2 : Settings.NotchX;
            return ClampCenter(c);
        }

        int ClampCenter(int c)
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            int half = WinW / 2;
            return Math.Max(b.Left + half, Math.Min(b.Right - half, c));
        }

        // Positioned horizontally only; the vertical position is always the top of the screen
        public void PlaceAtCenter()
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            Location = new Point(ResolvedCenterX() - WinW / 2, b.Top);
            if (IsHandleCreated) Render();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                dragging = false;
                pressScreen = System.Windows.Forms.Cursor.Position;
                pressCenter = ResolvedCenterX();
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pressed)
            {
                int dx = System.Windows.Forms.Cursor.Position.X - pressScreen.X;
                if (!dragging && Math.Abs(dx) > 4) dragging = true;
                if (dragging)
                {
                    Settings.NotchX = ClampCenter(pressCenter + dx);
                    PlaceAtCenter();
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && pressed)
            {
                pressed = false;
                Capture = false;
                if (dragging) Settings.Save();
                else HandleClick(e.Location);
            }
            base.OnMouseUp(e);
        }

        static RectangleF PowerChip(int i)
        {
            float totalW = 5 * ChipW + 4 * ChipGap;
            float startX = (WinW - totalW) / 2f;
            return new RectangleF(startX + i * (ChipW + ChipGap), PowerRowY, ChipW, ChipH);
        }


        void HandleClick(Point p)
        {
            // Clicking the pet only triggers a reaction; it does not open the panel
            if (Settings.PetEnabled && petRect.Contains(p.X, p.Y))
            {
                PetTouch();
                return;
            }
            if (open)
            {
                for (int i = 0; i < PowerKeys.Length; i++)
                {
                    if (PowerChip(i).Contains(p.X, p.Y))
                    {
                        SetOpen(false);
                        onPower(PowerKeys[i]);
                        return;
                    }
                }
                if (PowerChip(4).Contains(p.X, p.Y))
                {
                    SetOpen(false);
                    onOpenMain();
                    return;
                }
                if (p.Y < BarH)
                {
                    SetOpen(false);
                }
                return;
            }

            SetOpen(true);
        }

        void SetOpen(bool value)
        {
            open = value;
            StartAnim();
        }

        // Called when the genie effect ends: the notch does a short bounce
        public void Pulse()
        {
            pulseClock.Restart();
            pulsing = true;
            StartAnim();
        }

        // Bounce amplitude (-1..1); decays within 650 ms
        float PulseAmount()
        {
            if (!pulsing) return 0f;
            double t = pulseClock.Elapsed.TotalMilliseconds / 650.0;
            if (t >= 1.0) { pulsing = false; return 0f; }
            return (float)(Math.Sin(2.0 * Math.PI * t) * Math.Exp(-3.0 * t));
        }

        // Touch reaction: between 0..1 (one jump within 700 ms); 0 when done
        float PetReact()
        {
            if (!petReacting) return 0f;
            double t = petClock.Elapsed.TotalMilliseconds / 700.0;
            if (t >= 1.0) { petReacting = false; return 0f; }
            return (float)Math.Sin(Math.PI * t);
        }

        void PetTouch()
        {
            petClock.Restart();
            petReacting = true;
            StartAnim();
        }

        void StartAnim()
        {
            if (!anim.Enabled)
            {
                animClock.Restart();
                anim.Start();
            }
        }

        void OnAnimTick(object sender, EventArgs e)
        {
            float hoverTarget = hovering ? 1f : 0f;
            float openTarget = open ? 1f : 0f;
            // Advances by real elapsed time: motion speed stays constant even if the frame rate drops
            double dt = animClock.Elapsed.TotalSeconds;
            animClock.Restart();
            hoverT = Approach(hoverT, hoverTarget, dt);
            openT = Approach(openT, openTarget, dt);
            Render();
            if (!pulsing && !petReacting && hoverT == hoverTarget && openT == openTarget) anim.Stop();
        }

        // Each frame moves a fraction closer to the target (soft finish); snaps to the target when the remaining gap is small
        static float Approach(float cur, float target, double dt)
        {
            float d = target - cur;
            if (Math.Abs(d) < 0.002f) return target;
            float f = 1f - (float)Math.Exp(-dt * 16.0);
            return cur + d * f;
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        static float Clamp01(float v)
        {
            return Math.Max(0f, Math.Min(1f, v));
        }

        static Color WithAlpha(Color c, float a)
        {
            return Color.FromArgb((int)Math.Round(c.A * Clamp01(a)), c.R, c.G, c.B);
        }

        void Render()
        {
            if (!IsHandleCreated) return;
            using (Bitmap bmp = new Bitmap(WinW, WinH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    DrawNotch(g);
                }
                PushBitmap(bmp);
            }
        }

        void PushBitmap(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);
            try
            {
                NativePoint dst = new NativePoint { X = Location.X, Y = Location.Y };
                NativeSize size = new NativeSize { Cx = WinW, Cy = WinH };
                NativePoint src = new NativePoint { X = 0, Y = 0 };
                BlendFunction blend = new BlendFunction
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        static GraphicsPath NotchPath(float x, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddLine(x, 0f, x + w, 0f);
            p.AddLine(x + w, 0f, x + w, h - r);
            p.AddArc(x + w - 2f * r, h - 2f * r, 2f * r, 2f * r, 0f, 90f);
            p.AddLine(x + w - r, h, x + r, h);
            p.AddArc(x, h - 2f * r, 2f * r, 2f * r, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        static void Txt(Graphics g, string s, Font f, Color c, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
        }

        static Color LevelColor(int percent, bool noBattery)
        {
            if (noBattery) return PilUi.Muted;
            if (percent >= 50) return PilUi.Green;
            if (percent >= 20) return PilUi.Amber;
            return PilUi.Red;
        }

        static Color PowerColorOf(string key)
        {
            switch (key)
            {
                case "maks": return PilUi.Violet;
                case "pil": return PilUi.Green;
                case "normal": return PilUi.Blue;
                case "performans": return PilUi.Orange;
                default: return PilUi.Muted;
            }
        }

        // Appends a part to the right of x (only measures width if draw=false); returns the new x
        // Text is aligned to the vertical center of the bar: different fonts sit on the same line
        float Put(Graphics g, bool draw, float x, string s, Font f, Color c, float gap)
        {
            x += gap;
            if (draw) Txt(g, s, f, c, x, CenterY(g, s, f));
            return x + g.MeasureString(s, f).Width;
        }

        static float CenterY(Graphics g, string s, Font f)
        {
            // +2: the center of the text box sits slightly above the visual center of the letters
            return (BarH - g.MeasureString(s, f).Height) / 2f + 2f;
        }

        // Battery icon: outer frame, fill colored by percent, lightning when charging (24 px wide)
        void DrawBattery(Graphics g, float x, int pct)
        {
            RectangleF body = new RectangleF(x, 11f, 22f, 12f);
            using (GraphicsPath bp = PilUi.Rounded(body, 3f))
            using (Pen pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1.2f))
            {
                g.DrawPath(pen, bp);
            }
            using (SolidBrush t = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
            {
                g.FillRectangle(t, x + 22.5f, 15f, 2.5f, 4f);
            }
            if (pct >= 0)
            {
                float fillW = 18f * pct / 100f;
                if (fillW >= 1f)
                {
                    using (SolidBrush b = new SolidBrush(LevelColor(pct, snap.Battery.NoBattery)))
                    {
                        g.FillRectangle(b, x + 2f, 13f, fillW, 8f);
                    }
                }
            }
            if (snap.Battery.Charging)
            {
                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    g.FillPolygon(b, new PointF[] {
                        new PointF(x + 13f, 12.5f), new PointF(x + 9.5f, 17.5f), new PointF(x + 11.5f, 17.5f),
                        new PointF(x + 10.5f, 21.5f), new PointF(x + 14.5f, 16f), new PointF(x + 12.5f, 16f)
                    });
                }
            }
        }

        // Top bar: battery icon, percent, separator, time remaining, (when FPS is on) FPS, then CPU temperature.
        // Returns only the total width if draw=false.
        float DrawBar(Graphics g, bool draw, float x)
        {
            int pct = snap.Battery.Percent;
            // Pet on the far left: first the animal, then the battery icon
            if (Settings.PetEnabled)
            {
                if (draw)
                {
                    petRect = new RectangleF(x, 4f, PetW, PetH);
                    Pet.Draw(g, Settings.PetKind, petRect, PetReact());
                }
                x += PetW + 8f;
            }
            if (draw) DrawBattery(g, x, pct);
            x += 24f;
            string pctText = pct >= 0 ? (pct.ToString() + "%") : "--";
            x = Put(g, draw, x, pctText, fMid, PilUi.TextMain, 6f);
            x = Put(g, draw, x, "·", fSmall, PilUi.Muted, 5f);
            x = Put(g, draw, x, snap.Battery.Short, fSmall, PilUi.Muted, 4f);
            if (Settings.ShowFps)
            {
                int fps = FpsMonitor.Fps();
                x = Put(g, draw, x, (fps >= 0 ? fps.ToString() : "--") + " FPS", fMid, PilUi.TextMain, 12f);
            }
            x = Put(g, draw, x, TempInfo.TempValue(snap.CpuTemp), fMid, TempInfo.TempColor(snap.CpuTemp), 12f);
            if (Settings.ShowGpuTemp)
                x = Put(g, draw, x, TempInfo.TempValue(snap.GpuTemp), fMid, TempInfo.TempColor(snap.GpuTemp), 9f);
            return x;
        }

        // On hover: power mode name (with a dot), amount of expansion
        float HoverExtra(Graphics g)
        {
            float labelW = g.MeasureString(Power.ModeName(snap.PowerMode), fMid).Width;
            return 12f + 6f + 6f + labelW;
        }

        void DrawHoverLabel(Graphics g, float x, float a)
        {
            Color c = PowerColorOf(snap.PowerMode);
            using (SolidBrush b = new SolidBrush(WithAlpha(c, a))) g.FillEllipse(b, x, 14f, 6f, 6f);
            string modeName = Power.ModeName(snap.PowerMode);
            Txt(g, modeName, fMid, WithAlpha(PilUi.TextMain, a), x + 12f, CenterY(g, modeName, fMid));
        }

        // a: 0..1 opacity (buttons fade in while opening)
        static void DrawChip(Graphics g, RectangleF r, string label, Color fill, Color textColor, Font f, float a)
        {
            fill = WithAlpha(fill, a);
            textColor = WithAlpha(textColor, a);
            using (GraphicsPath cp = PilUi.Rounded(r, r.Height / 2f))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, cp);
            }
            SizeF sz = g.MeasureString(label, f);
            using (SolidBrush t = new SolidBrush(textColor))
            {
                g.DrawString(label, f, t, r.X + (r.Width - sz.Width) / 2f, r.Y + (r.Height - sz.Height) / 2f);
            }
        }

        // Power mode buttons (5th button: Open = main window)
        void DrawPowerRow(Graphics g, float a)
        {
            for (int i = 0; i < 5; i++)
            {
                RectangleF r = PowerChip(i);
                if (i == 4)
                {
                    DrawChip(g, r, Loc.T("chip_ayarlar"), PilUi.CardHover, PilUi.TextMain, fChip, a);
                    continue;
                }
                bool active = snap.PowerMode == PowerKeys[i];
                Color fill = active ? PowerColorOf(PowerKeys[i]) : PilUi.Card;
                Color text = active ? PilUi.Bg : PilUi.Muted;
                DrawChip(g, r, PowerLabels[i], fill, text, fChip, a);
            }
        }


        // Shape is computed from hoverT (sideways expansion) and openT (opening); content is clipped to the shape
        void DrawNotch(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            float contentW = DrawBar(g, false, 0f);
            float closedW = contentW + 2f * Pad + HoverExtra(g) * hoverT * (1f - openT);
            // Bounce: width and height swell together briefly
            float pulse = PulseAmount();
            float dw = Lerp(closedW, OpenW, openT) * (1f + 0.035f * pulse);
            float h = Lerp(BarH, OpenH, openT) * (1f + 0.14f * pulse);
            float x0 = (WinW - dw) / 2f;

            using (GraphicsPath p = NotchPath(x0, dw, h, NotchRadius))
            {
                using (SolidBrush black = new SolidBrush(Color.Black))
                {
                    g.FillPath(black, p);
                }

                g.SetClip(p);
                // The top bar slides left when collapsed, to the center when open
                float barX = Lerp(x0 + Pad, x0 + (dw - contentW) / 2f, openT);
                DrawBar(g, true, barX);

                float labelA = Clamp01((hoverT - 0.5f) * 2f) * (1f - openT);
                if (labelA > 0f) DrawHoverLabel(g, barX + contentW + 12f, labelA);

                // Buttons appear in the second half of the opening and slide out of the notch
                float chipA = Clamp01((openT - 0.35f) / 0.65f);
                if (chipA > 0f) DrawPowerRow(g, chipA);
                g.ResetClip();
            }
        }
    }

    // Dark-themed language picker: drawn by hand because Windows' white ComboBox breaks the theme
    public class LangPicker : Control
    {
        public event EventHandler SelectedIndexChanged;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        string[] items = new string[0];
        int selected = -1;

        public LangPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            BackColor = PilUi.CardHover;
            ForeColor = PilUi.TextMain;
            Font = new Font(Loc.Face("Segoe UI"), 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            menu.BackColor = PilUi.Card;
            menu.ForeColor = PilUi.TextMain;
            menu.ShowImageMargin = false;
        }

        public void SetItems(string[] names)
        {
            items = names;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            string text = selected >= 0 && selected < items.Length ? items[selected] : "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(10, 0, Width - 34, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            int cx = Width - 16, cy = Height / 2;
            Point[] arrow = { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) };
            using (SolidBrush mark = new SolidBrush(PilUi.Muted)) g.FillPolygon(mark, arrow);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            menu.Items.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(items[i]);
                mi.Tag = i;
                mi.Checked = i == selected;
                mi.ForeColor = PilUi.TextMain;
                mi.BackColor = PilUi.Card;
                mi.Click += MenuItemClick;
                menu.Items.Add(mi);
            }
            menu.Show(this, new Point(0, Height));
        }

        void MenuItemClick(object sender, EventArgs e)
        {
            int index = (int)((ToolStripItem)sender).Tag;
            if (index == selected) return;
            selected = index;
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
    }

    // Dropdown list colors: the app's dark theme
    class DarkColors : ProfessionalColorTable
    {
        public DarkColors() { UseSystemColors = false; }
        public override Color MenuItemSelected { get { return PilUi.CardHover; } }
        public override Color MenuItemBorder { get { return PilUi.CardHover; } }
        public override Color MenuBorder { get { return PilUi.CardHover; } }
        public override Color ToolStripDropDownBackground { get { return PilUi.Card; } }
        public override Color ImageMarginGradientBegin { get { return PilUi.Card; } }
        public override Color ImageMarginGradientMiddle { get { return PilUi.Card; } }
        public override Color ImageMarginGradientEnd { get { return PilUi.Card; } }
        public override Color MenuItemSelectedGradientBegin { get { return PilUi.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return PilUi.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return PilUi.CardHover; } }
        public override Color MenuItemPressedGradientEnd { get { return PilUi.CardHover; } }
    }

    // Dark-themed number box: drawn by hand instead of Windows' white arrow buttons
    public class DarkNumeric : Panel
    {
        public event EventHandler ValueChanged;
        readonly TextBox text = new TextBox();
        decimal val;
        decimal lo;
        decimal hi = 100;
        decimal step = 1;
        bool hoverUp;
        bool hoverDown;

        public DarkNumeric()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = PilUi.CardHover;
            ForeColor = PilUi.TextMain;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = PilUi.CardHover;
            text.ForeColor = PilUi.TextMain;
            text.TextAlign = HorizontalAlignment.Right;
            text.Font = new Font(Loc.Face("Segoe UI"), 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            text.KeyDown += TextKeyDown;
            text.Leave += delegate { Commit(); };
            Controls.Add(text);
            Resize += delegate { PlaceText(); };
        }

        public decimal Minimum { get { return lo; } set { lo = value; SetValue(val, false); } }
        public decimal Maximum { get { return hi; } set { hi = value; SetValue(val, false); } }
        public decimal Increment { get { return step; } set { step = value; } }
        public decimal Value { get { return val; } set { SetValue(value, true); } }

        void PlaceText()
        {
            text.Width = Math.Max(10, Width - 34);
            text.Location = new Point(8, (Height - text.PreferredHeight) / 2);
        }

        void SetValue(decimal v, bool raise)
        {
            decimal next = Math.Max(lo, Math.Min(hi, v));
            bool changed = next != val;
            val = next;
            text.Text = ((int)next).ToString();
            if (changed && raise && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        // A typed value is applied on Enter or when focus leaves
        void Commit()
        {
            int typed;
            if (int.TryParse(text.Text, out typed)) SetValue(typed, true);
            else SetValue(val, false);
        }

        void TextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { SetValue(val + step, true); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { SetValue(val - step, true); e.SuppressKeyPress = true; }
        }

        Rectangle UpZone { get { return new Rectangle(Width - 22, 0, 22, Height / 2); } }
        Rectangle DownZone { get { return new Rectangle(Width - 22, Height / 2, 22, Height - Height / 2); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            DrawArrow(g, UpZone, true, hoverUp);
            DrawArrow(g, DownZone, false, hoverDown);
        }

        void DrawArrow(Graphics g, Rectangle zone, bool up, bool hover)
        {
            Color c = hover && Enabled ? PilUi.Cyan : PilUi.Muted;
            int cx = zone.Left + zone.Width / 2;
            int cy = zone.Top + zone.Height / 2;
            Point[] tri = up
                ? new[] { new Point(cx - 4, cy + 2), new Point(cx + 4, cy + 2), new Point(cx, cy - 2) }
                : new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 2) };
            using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, tri);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool up = UpZone.Contains(e.Location);
            bool down = DownZone.Contains(e.Location);
            if (up != hoverUp || down != hoverDown)
            {
                hoverUp = up;
                hoverDown = down;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverUp = false;
            hoverDown = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (UpZone.Contains(e.Location)) SetValue(val + step, true);
            else if (DownZone.Contains(e.Location)) SetValue(val - step, true);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }

    // Dark-themed checkbox: drawn by hand instead of Windows' white CheckBox
    public class DarkCheck : Control
    {
        public event EventHandler CheckedChanged;
        const int Box = 14;
        bool isChecked;
        bool hover;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = PilUi.Card;
            ForeColor = PilUi.TextMain;
            Font = new Font(Loc.Face("Segoe UI"), 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            Rectangle box = new Rectangle(1, (Height - Box) / 2, Box, Box);
            Color fill = isChecked && Enabled ? PilUi.Cyan : PilUi.CardHover;
            Color edge = !Enabled ? PilUi.Muted : (isChecked || hover ? PilUi.Cyan : PilUi.Muted);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush f = new SolidBrush(fill)) g.FillRectangle(f, box);
            using (Pen p = new Pen(edge)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
            if (isChecked)
            {
                Point[] tick = { new Point(box.X + 3, box.Y + 7), new Point(box.X + 6, box.Y + 10), new Point(box.X + 11, box.Y + 4) };
                using (Pen p = new Pen(Enabled ? PilUi.Bg : PilUi.Muted, 2f)) g.DrawLines(p, tick);
            }
            g.SmoothingMode = SmoothingMode.Default;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Box + 9, 0, Width - Box - 9, Height), Enabled ? ForeColor : PilUi.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }
    }

    // Dark-themed message box: our own window so Windows' MessageBox does not break the theme
    public static class DarkBox
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Show(IWin32Window owner, string text, string caption)
        {
            using (Form f = new Form())
            {
                f.Text = caption;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.BackColor = PilUi.Card;
                f.ForeColor = PilUi.TextMain;
                f.Font = new Font(Loc.Face("Segoe UI"), 9.5f, FontStyle.Regular, GraphicsUnit.Point);

                Label body = new Label();
                body.Text = text;
                body.AutoSize = true;
                body.MaximumSize = new Size(360, 0);
                body.Location = new Point(20, 20);
                body.ForeColor = PilUi.TextMain;
                f.Controls.Add(body);

                Button ok = new Button();
                ok.Text = "OK";
                ok.Size = new Size(90, 30);
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderSize = 0;
                ok.FlatAppearance.BorderColor = PilUi.Cyan;
                ok.FlatAppearance.MouseOverBackColor = PilUi.Cyan;
                ok.FlatAppearance.MouseDownBackColor = PilUi.Cyan;
                ok.BackColor = PilUi.Cyan;
                ok.ForeColor = PilUi.Bg;
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                f.AcceptButton = ok;

                int top = body.Bottom + 20;
                f.ClientSize = new Size(400, top + 30 + 20);
                ok.Location = new Point(f.ClientSize.Width - 110, top);
                f.HandleCreated += delegate { ApplyDark(f.Handle); };

                if (owner != null) f.ShowDialog(owner);
                else f.ShowDialog();
            }
        }

        // Dark title bar (Win10 20H1+ / Win11); older versions keep the default
        static void ApplyDark(IntPtr handle)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                int caption = PilUi.Card.R | (PilUi.Card.G << 8) | (PilUi.Card.B << 16);
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
                int text = PilUi.TextMain.R | (PilUi.TextMain.G << 8) | (PilUi.TextMain.B << 16);
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows versions do not have these attributes
            }
        }
    }

    public class SettingsForm : Form
    {
        readonly DarkNumeric pilCpu = new DarkNumeric();
        readonly DarkNumeric pilScreen = new DarkNumeric();
        readonly DarkNumeric maksCpu = new DarkNumeric();
        readonly DarkNumeric maksScreen = new DarkNumeric();
        readonly DarkCheck maksRefresh = new DarkCheck();
        readonly DarkCheck notchBox = new DarkCheck();
        readonly DarkCheck petBox = new DarkCheck();
        readonly PetPicker petPicker = new PetPicker();
        readonly DarkCheck fpsBox = new DarkCheck();
        readonly DarkCheck gpuBox = new DarkCheck();
        readonly DarkCheck autoBox = new DarkCheck();
        readonly Label adminLabel = new Label();
        readonly Button adminBtn = new Button();
        readonly Button closeBtn = new Button();
        readonly LangPicker langBox = new LangPicker();
        // All language-dependent texts: reapplied from this list when the language changes
        readonly List<Action> texts = new List<Action>();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        readonly Action onChanged;
        int y = 16;
        bool ready = false;
        bool closingAnimated = false;

        // On close the window returns into the notch (NotchRect)
        public Rectangle NotchRect;

        // onChanged: called after a short delay when a setting changes. Settings apply without waiting for a save button.
        public SettingsForm(Action onChanged)
        {
            this.onChanged = onChanged;
            Bind(delegate { Text = Loc.F("settings_title", "PowerNotch"); });
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = PilUi.Card;
            ForeColor = PilUi.TextMain;
            Font = Fnt("Segoe UI", 9.5f, FontStyle.Regular);

            // Language picker: at the very top; when selected, all texts switch to the new language instantly
            AddLanguageRow();
            y += 8;

            AddHeader("group_general");
            AddCheck(notchBox, () => Loc.T("show_notch"), Settings.NotchEnabled);
            AddCheck(petBox, () => Loc.T("pet_show"), Settings.PetEnabled);
            AddPetRow();
            AddCenterButton();
            AddCheck(fpsBox, () => Loc.T("show_fps"), Settings.ShowFps);
            AddCheck(gpuBox, () => Loc.T("show_gpu"), Settings.ShowGpuTemp);
            AddCheck(autoBox, () => Loc.F("autostart", "PowerNotch"), Settings.AutoStart);
            y += 8;

            AddHeader("group_power");
            AddRow("row_bat_cpu", pilCpu, Power.BatteryCpuMax, 5, 100);
            AddRow("row_bat_scr", pilScreen, Power.BatteryBrightness, 0, 100);
            AddRow("row_max_cpu", maksCpu, Power.MaksCpuMax, 5, 100);
            AddRow("row_max_scr", maksScreen, Power.MaksBrightness, 0, 100);
            AddCheck(maksRefresh, () => Loc.T("max_refresh"), Settings.MaksRefresh);
            y += 8;

            // Admin mode: installed once; afterwards PowerNotch opens as admin without a UAC prompt (needed for FPS)
            AddHeader("group_admin");
            adminLabel.AutoSize = true;
            adminLabel.ForeColor = PilUi.Muted;
            adminLabel.Location = new Point(28, y + 9);
            Controls.Add(adminLabel);
            adminBtn.Location = new Point(200, y + 2);
            adminBtn.Size = new Size(170, 30);
            StyleButton(adminBtn, PilUi.CardHover, PilUi.TextMain);
            adminBtn.Click += (s, e) => InstallAdmin();
            Controls.Add(adminBtn);
            RefreshAdminLabel();
            y += 50;

            Button close = closeBtn;
            Bind(delegate { close.Text = Loc.T("close"); });
            close.Location = new Point(280, y);
            close.Size = new Size(80, 30);
            StyleButton(close, PilUi.Cyan, PilUi.Bg);
            close.Click += (s, e) => AnimatedClose();
            Controls.Add(close);
            // Signature: 'developed by' muted, 'klashain' in the accent color
            Label signBy = new Label();
            signBy.Text = "developed by ";
            signBy.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            signBy.ForeColor = PilUi.Muted;
            signBy.AutoSize = true;
            signBy.Location = new Point(20, y + 13);
            Controls.Add(signBy);
            Label signName = new Label();
            signName.Text = "klashain";
            signName.Font = new Font("Segoe UI Semibold", 8.5f);
            signName.ForeColor = PilUi.Cyan;
            signName.AutoSize = true;
            signName.Location = new Point(20 + TextRenderer.MeasureText(signBy.Text, signBy.Font, Size.Empty, TextFormatFlags.NoPadding).Width, y + 13);
            Controls.Add(signName);
            // Version label: a muted "beta" next to the signature
            Label signBeta = new Label();
            signBeta.Text = "beta";
            signBeta.Font = new Font("Segoe UI Semibold", 7.5f);
            signBeta.ForeColor = PilUi.Muted;
            signBeta.AutoSize = true;
            signBeta.Location = new Point(signName.Right + 6, y + 14);
            Controls.Add(signBeta);
            CancelButton = close;

            debounce.Interval = 250;
            debounce.Tick += delegate { debounce.Stop(); ApplyAll(); };
            pilCpu.ValueChanged += Changed;
            pilScreen.ValueChanged += Changed;
            maksCpu.ValueChanged += Changed;
            maksScreen.ValueChanged += Changed;
            maksRefresh.CheckedChanged += Changed;
            notchBox.CheckedChanged += Changed;
            petBox.CheckedChanged += Changed;
            fpsBox.CheckedChanged += Changed;
            gpuBox.CheckedChanged += Changed;
            autoBox.CheckedChanged += Changed;
            ready = true;
            ClientSize = new Size(380, y + 52);
        }

        // Font: when Chinese is selected, a CJK-capable font is used instead of Segoe
        static Font Fnt(string face, float size, FontStyle style)
        {
            return new Font(Loc.Face(face), size, style, GraphicsUnit.Point);
        }

        // Applies the text and font now, and again when the language changes
        void Bind(Action apply)
        {
            texts.Add(apply);
            apply();
        }

        // Language-dependent label: takes its text from a key
        Label TextLabel(string key, string face, float size, FontStyle style, Color color, Point at)
        {
            Label l = new Label();
            l.UseMnemonic = false;     // Do not treat '&' as a mnemonic (e.g. "Akku & Energie")
            l.ForeColor = color;
            l.AutoSize = true;
            l.Location = at;
            Controls.Add(l);
            Bind(delegate
            {
                l.Text = Loc.T(key);
                l.Font = Fnt(face, size, style);
            });
            return l;
        }

        void AddHeader(string key)
        {
            TextLabel(key, "Segoe UI Semibold", 10.5f, FontStyle.Regular, PilUi.TextMain, new Point(20, y));
            y += 30;
        }

        // Language picker row: the list shows native names (Türkçe, English...)
        void AddLanguageRow()
        {
            TextLabel("lang_label", "Segoe UI", 9.5f, FontStyle.Regular, PilUi.Muted, new Point(28, y + 4));
            langBox.Location = new Point(250, y);
            langBox.Size = new Size(110, 24);
            langBox.SetItems(Loc.Names);
            langBox.SelectedIndex = Math.Max(0, Array.IndexOf(Loc.Codes, Loc.Current));
            langBox.SelectedIndexChanged += LanguageChanged;
            Controls.Add(langBox);
            y += 34;
        }

        void AddRow(string key, DarkNumeric box, int value, int min, int max)
        {
            TextLabel(key, "Segoe UI", 9.5f, FontStyle.Regular, PilUi.Muted, new Point(28, y + 4));

            box.Minimum = min;
            box.Maximum = max;
            box.Increment = 1;
            box.Value = Math.Max(min, Math.Min(max, value));
            box.BackColor = PilUi.CardHover;
            box.ForeColor = PilUi.TextMain;
            box.Location = new Point(250, y);
            box.Size = new Size(110, 24);
            Controls.Add(box);
            y += 34;
        }

        // Pet picker row: the selected pet is written to Settings via ApplyAll
        // Moves the notch back to the top center of the screen (one button instead of dragging)
        void AddCenterButton()
        {
            Button b = new Button();
            b.Location = new Point(20, y);
            b.Size = new Size(170, 30);
            StyleButton(b, PilUi.CardHover, PilUi.TextMain);
            Bind(delegate { b.Text = Loc.T("notch_center"); });
            b.Click += (s, e) =>
            {
                Settings.NotchX = -1;
                Settings.Save();
                if (onChanged != null) onChanged();
            };
            Controls.Add(b);
            y += 40;
        }

        void AddPetRow()
        {
            petPicker.SelectedIndex = Settings.PetKind;
            petPicker.Location = new Point(20, y);
            petPicker.SelectionChanged += Changed;
            Controls.Add(petPicker);
            y += 48;
        }

        void AddCheck(DarkCheck cb, Func<string> text, bool value)
        {
            cb.ForeColor = PilUi.TextMain;
            cb.Location = new Point(20, y);
            cb.Size = new Size(340, 24);
            cb.Checked = value;
            Controls.Add(cb);
            Bind(delegate
            {
                cb.Text = text();
                cb.Font = Fnt("Segoe UI", 9.5f, FontStyle.Regular);
            });
            y += 30;
        }

        // Language selected: saved, reflected in the main window, and all texts in this window are refreshed
        void LanguageChanged(object sender, EventArgs e)
        {
            if (!ready || langBox.SelectedIndex < 0) return;
            Loc.Set(Loc.Codes[langBox.SelectedIndex]);
            debounce.Stop();
            ApplyAll();
            RefreshTexts();
        }

        void RefreshTexts()
        {
            foreach (Action apply in texts) apply();
            RefreshAdminLabel();
        }

        // A setting changed: applied after a short delay (once for consecutive changes)
        void Changed(object sender, EventArgs e)
        {
            if (!ready) return;
            debounce.Stop();
            debounce.Start();
        }

        // Applies and saves the values immediately
        void ApplyAll()
        {
            Power.BatteryCpuMax = (int)pilCpu.Value;
            Power.BatteryBrightness = (int)pilScreen.Value;
            Power.MaksCpuMax = (int)maksCpu.Value;
            Power.MaksBrightness = (int)maksScreen.Value;
            Settings.MaksRefresh = maksRefresh.Checked;
            Settings.NotchEnabled = notchBox.Checked;
            Settings.PetEnabled = petBox.Checked;
            Settings.PetKind = petPicker.SelectedIndex;
            Settings.ShowFps = fpsBox.Checked;
            Settings.ShowGpuTemp = gpuBox.Checked;
            Settings.AutoStart = autoBox.Checked;
            Settings.Language = Loc.Current;
            Autostart.Apply(Settings.AutoStart);
            Settings.Save();
            if (onChanged != null) onChanged();
        }

        void RefreshAdminLabel()
        {
            bool task = Admin.TaskExists();
            bool layer = Admin.LayerSet();
            bool done = task && !layer;
            adminLabel.Text = done ? Loc.T("admin_installed") : (layer ? Loc.T("admin_needs") : Loc.T("admin_not"));
            adminBtn.Text = done ? Loc.T("admin_reinstall") : Loc.T("admin_install");
        }

        // Setup may ask for UAC approval; runs in the background, the result is reported with a message
        void InstallAdmin()
        {
            adminBtn.Enabled = false;
            adminLabel.Text = Loc.T("admin_installing");
            string error = Loc.T("admin_fail");
            Actions.Run(delegate
            {
                error = Admin.Install();
                return error == null;
            }, delegate(bool ok)
            {
                adminBtn.Enabled = true;
                RefreshAdminLabel();
                if (ok) DarkBox.Show(this, Loc.F("admin_ok", "PowerNotch"), "PowerNotch");
                else DarkBox.Show(this, error, "PowerNotch");
            });
        }

        // Title bar (the area Windows draws) in the same dark color as the body: DWM attributes
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarColors();
        }

        void ApplyTitleBarColors()
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));           // dark mode (Win10 20H1+ / Win11)
                int caption = ColorRef(PilUi.Card);
                DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));        // title bar color (Win11)
                int text = ColorRef(PilUi.TextMain);
                DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));           // title text color (Win11)
            }
            catch (Exception)
            {
                // Older Windows versions do not have these attributes; the window opens with the default color
            }
        }

        // COLORREF: 0x00BBGGRR
        static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        // On close: pending settings are applied immediately, then the window returns to the notch
        void AnimatedClose()
        {
            if (closingAnimated) return;
            closingAnimated = true;
            if (debounce.Enabled)
            {
                debounce.Stop();
                ApplyAll();
            }
            Morph.Close(this, Bounds, NotchRect, true, delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing with the X button also plays the animation first; the animation starts outside the closing event
            if (!closingAnimated && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                BeginInvoke((Action)delegate { AnimatedClose(); });
                return;
            }
            base.OnFormClosing(e);
        }

        static void StyleButton(Button b, Color back, Color fore)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            // No Windows light color on hover/press
            b.FlatAppearance.BorderColor = back;
            b.FlatAppearance.MouseOverBackColor = back;
            b.FlatAppearance.MouseDownBackColor = back;
            b.BackColor = back;
            b.ForeColor = fore;
        }
    }

    public class MainForm : Form
    {
        const int W = 420;
        const int H = 600;

        readonly BatteryPanel battery = new BatteryPanel();
        readonly ModeCard[] powerCards = new ModeCard[4];
        readonly Label status = new Label();
        readonly System.Windows.Forms.Timer telemetryTimer = new System.Windows.Forms.Timer();
        // The FPS value refreshes every half second (1 s measurement window)
        readonly System.Windows.Forms.Timer fpsTimer = new System.Windows.Forms.Timer();
        // CPU and GPU temperature: every 3 s
        readonly System.Windows.Forms.Timer cpuTimer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolStripMenuItem[] powerItems = new ToolStripMenuItem[4];
        readonly ToolStripMenuItem notchItem = new ToolStripMenuItem(Loc.T("menu_notch"));
        readonly string[] powerKeys = { "maks", "pil", "normal", "performans" };
        readonly Label titleLabel = new Label();
        readonly Label subLabel = new Label();
        readonly ToolStripMenuItem openItem = new ToolStripMenuItem();
        readonly ToolStripMenuItem exitItem = new ToolStripMenuItem();
        // Power mode names follow the selected language; re-read on every call
        string[] PowerTitles { get { return new[] { Loc.T("mode_maks"), Loc.T("mode_pil"), Loc.T("mode_normal"), Loc.T("mode_perf") }; } }
        readonly string[] powerIcons = { "leaf", "battery", "gauge", "bolt" };
        readonly Color[] powerAccents = { PilUi.Violet, PilUi.Green, PilUi.Blue, PilUi.Orange };
        IslandForm island;
        HpSnapshot lastSnap = new HpSnapshot();
        bool exiting = false;
        bool hideHintShown = false;

        public MainForm()
        {
            Text = "PowerNotch";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = PilUi.Bg;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(W, H);
            Region = PilUi.RoundedRegion(W, H, 18f);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) HideToTray(); };

            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // Title
            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(W, 72);
            header.BackColor = PilUi.Bg;
            header.MouseDown += StartDrag;
            Controls.Add(header);
            titleLabel = MakeLabel("", "Segoe UI Semibold", 16f, PilUi.TextMain, new Point(20, 14));
            subLabel = MakeLabel("", "Segoe UI", 9f, PilUi.Muted, new Point(22, 44));
            header.Controls.Add(titleLabel);
            header.Controls.Add(subLabel);

            Label gear = new Label();
            gear.Text = "⚙";
            gear.Font = new Font("Segoe UI Symbol", 14f);
            gear.ForeColor = PilUi.Muted;
            gear.BackColor = PilUi.Bg;
            gear.Size = new Size(34, 34);
            gear.TextAlign = ContentAlignment.MiddleCenter;
            gear.Location = new Point(W - 88, 14);
            gear.Cursor = Cursors.Hand;
            gear.Click += (s, e) => OpenSettings();
            gear.MouseEnter += (s, e) => gear.ForeColor = PilUi.TextMain;
            gear.MouseLeave += (s, e) => gear.ForeColor = PilUi.Muted;
            header.Controls.Add(gear);

            Label close = new Label();
            close.Text = "×";
            close.Font = new Font("Segoe UI", 16f);
            close.ForeColor = PilUi.Muted;
            close.BackColor = PilUi.Bg;
            close.Size = new Size(34, 34);
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.Location = new Point(W - 50, 14);
            close.Cursor = Cursors.Hand;
            close.Click += (s, e) => HideToTray();
            close.MouseEnter += (s, e) => close.ForeColor = PilUi.TextMain;
            close.MouseLeave += (s, e) => close.ForeColor = PilUi.Muted;
            header.Controls.Add(close);

            battery.Location = new Point(20, 80);
            battery.Size = new Size(380, 130);
            battery.MouseDown += StartDrag;
            Controls.Add(battery);

            // Power mode cards
            for (int i = 0; i < 4; i++)
            {
                ModeCard pc = new ModeCard();
                pc.Tag = powerKeys[i];
                pc.IconKind = powerIcons[i];
                pc.Accent = powerAccents[i];
                pc.Size = new Size(380, 76);
                pc.Location = new Point(20, 226 + i * 84);
                pc.Click += OnPowerCardClick;
                Controls.Add(pc);
                powerCards[i] = pc;
            }

            status.Location = new Point(20, 566);
            status.Size = new Size(380, 24);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = PilUi.Muted;
            status.BackColor = PilUi.Bg;
            Controls.Add(status);

            // Notch
            island = new IslandForm(ApplyPower, ToggleFromNotch);

            // Tray menu
            for (int i = 0; i < 4; i++)
            {
                ToolStripMenuItem pi = new ToolStripMenuItem();
                pi.Tag = powerKeys[i];
                pi.Click += OnPowerMenuClick;
                menu.Items.Add(pi);
                powerItems[i] = pi;
            }
            menu.Items.Add(new ToolStripSeparator());

            openItem.Click += (s, e) => ShowFromTray();
            menu.Items.Add(openItem);

            notchItem.Click += (s, e) => SetNotch(!notchItem.Checked);
            menu.Items.Add(notchItem);

            menu.Items.Add(new ToolStripSeparator());
            exitItem.Click += (s, e) => ExitApp();
            menu.Items.Add(exitItem);

            // The tray menu also uses the dark theme
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            menu.BackColor = PilUi.Card;
            menu.ForeColor = PilUi.TextMain;
            foreach (ToolStripItem item in menu.Items) item.ForeColor = PilUi.TextMain;

            tray.Icon = MakeTrayIcon();
            tray.Text = "PowerNotch";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowFromTray();
            tray.Visible = true;

            FormClosing += (s, e) =>
            {
                if (!exiting && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };
            FormClosed += (s, e) =>
            {
                telemetryTimer.Stop();
                fpsTimer.Stop();
                cpuTimer.Stop();
                island.Close();
                tray.Visible = false;
                tray.Dispose();
            };

            telemetryTimer.Interval = 3000;
            telemetryTimer.Tick += (s, e) => Telemetry.Poll(OnSnapshot);
            fpsTimer.Interval = 500;
            fpsTimer.Tick += (s, e) => { if (Settings.ShowFps && Settings.NotchEnabled) island.Redraw(); };
            cpuTimer.Interval = 3000;
            cpuTimer.Tick += (s, e) => Telemetry.PollCpu(OnCpuTemp);

            ApplyLanguage();
            ApplySnapshot(lastSnap);
        }

        // Main window, cards and tray menu are rewritten for the selected language
        void ApplyLanguage()
        {
            titleLabel.Text = Loc.T("app_title");
            subLabel.Text = Loc.T("app_subtitle");
            string[] titles = PowerTitles;
            for (int i = 0; i < 4; i++)
            {
                powerCards[i].Title = titles[i];
                powerItems[i].Text = titles[i];
            }
            notchItem.Text = Loc.T("menu_notch");
            openItem.Text = Loc.T("menu_open");
            exitItem.Text = Loc.T("menu_exit");
            RefreshSubtitles();
        }

        // Telemetry and the notch start only while the program runs
        public void StartTelemetry()
        {
            if (Settings.NotchEnabled) island.Show();
            FpsMonitor.Apply(Settings.ShowFps);
            telemetryTimer.Start();
            fpsTimer.Start();
            cpuTimer.Start();
            Telemetry.Poll(OnSnapshot);
        }

        Label MakeLabel(string text, string face, float size, Color color, Point at)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font(Loc.Face(face), size, GraphicsUnit.Point);
            l.ForeColor = color;
            l.BackColor = PilUi.Bg;
            l.AutoSize = true;
            l.Location = at;
            l.MouseDown += StartDrag;
            return l;
        }

        void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) WinNative.Drag(Handle);
        }

        static string PowerSubtitle(string key)
        {
            switch (key)
            {
                case "maks": return Loc.F("sub_maks", Power.MaksCpuMax, Power.MaksBrightness, Settings.MaksRefresh ? Loc.T("hz60") : "");
                case "pil": return Loc.F("sub_pil", Power.BatteryCpuMax, Power.BatteryBrightness);
                case "normal": return Loc.T("sub_normal");
                default: return Loc.T("sub_perf");
            }
        }

        static Color PowerColor(string key)
        {
            switch (key)
            {
                case "maks": return PilUi.Violet;
                case "pil": return PilUi.Green;
                case "normal": return PilUi.Blue;
                case "performans": return PilUi.Orange;
                default: return PilUi.Muted;
            }
        }

        void RefreshSubtitles()
        {
            for (int i = 0; i < 4; i++)
            {
                powerCards[i].Subtitle = PowerSubtitle(powerKeys[i]);
                powerCards[i].Invalidate();
            }
        }

        // Live CPU temperature: only this value is updated, the rest of the data comes in its own cycle
        void OnCpuTemp(double celsius)
        {
            lastSnap.CpuTemp = celsius;
            ApplySnapshot(lastSnap);
            island.Apply(lastSnap);
        }

        void OnSnapshot(HpSnapshot s)
        {
            lastSnap = s;
            ApplySnapshot(s);
            island.Apply(s);
        }

        void ApplySnapshot(HpSnapshot s)
        {
            BatteryInfo b = s.Battery;
            battery.Percent = b.Percent;
            battery.NoBattery = b.NoBattery;
            battery.Charging = b.Charging;
            battery.StateText = b.State;
            battery.ActiveText = Loc.T("active_prefix") + Power.ModeName(s.PowerMode);
            battery.ActiveColor = PowerColor(s.PowerMode);
            battery.Invalidate();

            for (int i = 0; i < 4; i++)
            {
                powerCards[i].IsCurrent = s.PowerMode == powerKeys[i];
                powerCards[i].Badge = powerCards[i].IsCurrent ? Loc.T("badge_active") : "";
                powerCards[i].Invalidate();
                powerItems[i].Checked = s.PowerMode == powerKeys[i];
            }

            notchItem.Checked = Settings.NotchEnabled;
        }

        // Reflect the live state immediately when the user does something
        void RefreshLocal()
        {
            lastSnap.Battery = BatteryInfo.Read();
            lastSnap.PowerMode = Power.ActiveKey();
            ApplySnapshot(lastSnap);
            island.Apply(lastSnap);
        }

        void OnPowerCardClick(object sender, EventArgs e)
        {
            ApplyPower((string)((ModeCard)sender).Tag);
        }

        void OnPowerMenuClick(object sender, EventArgs e)
        {
            ApplyPower((string)((ToolStripItem)sender).Tag);
        }

        // Power mode: powercfg and screen refresh rate are applied in the background
        void ApplyPower(string key)
        {
            status.ForeColor = PilUi.Muted;
            status.Text = Loc.T("status_applying");
            string[] msg = new string[1];
            Actions.Run(delegate
            {
                try
                {
                    msg[0] = Power.Apply(key);
                    string displayErr = Display.Apply(key);
                    if (displayErr != null) msg[0] += " (" + displayErr + ")";
                    string gpuErr = GpuSwitch.Apply(key);
                    if (gpuErr != null) msg[0] += " (" + gpuErr + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    msg[0] = Loc.T("error_prefix") + ex.Message;
                    return false;
                }
            }, delegate(bool ok)
            {
                status.ForeColor = ok ? PilUi.Green : PilUi.Red;
                status.Text = msg[0];
                RefreshLocal();
                if (!Visible) tray.ShowBalloonTip(3000, "PowerNotch", msg[0], ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            });
        }

        void SetNotch(bool on)
        {
            Settings.NotchEnabled = on;
            Settings.Save();
            if (on) island.Show(); else island.Hide();
            notchItem.Checked = on;
        }

        // Settings: the settings window comes out of the notch; when closed it returns to the notch and saved values are applied
        void OpenSettings()
        {
            SettingsForm f = new SettingsForm(OnSettingsChanged);
            Rectangle notch = island.VisualRect;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Rectangle target = new Rectangle(area.Left + (area.Width - f.Width) / 2, area.Top + (area.Height - f.Height) / 2, f.Width, f.Height);
            f.NotchRect = notch;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = target;
            f.Opacity = 0;
            // First the layer flying out of the notch; the settings window appears in its place when ready
            MorphOverlay flight = Morph.Open(notch, target, 620);
            f.Shown += delegate { Morph.Reveal(f, flight, null); };
            try
            {
                f.ShowDialog(this);
            }
            finally
            {
                f.Dispose();
            }
            // The settings window returned to the notch: the notch bounces
            island.Pulse();
            RefreshLocal();
        }

        // Applied immediately when a setting changes (no save button)
        void OnSettingsChanged()
        {
            ApplyLanguage();
            FpsMonitor.Apply(Settings.ShowFps);
            if (Settings.NotchEnabled) island.Show(); else island.Hide();
            notchItem.Checked = Settings.NotchEnabled;
            island.Redraw();
            island.PlaceAtCenter();   // the notch position may have changed (e.g. the center button)
            string pk = lastSnap.PowerMode;
            if (pk == "pil" || pk == "maks") ApplyPower(pk);
            Telemetry.Poll(OnSnapshot);
        }

        // On notch click: the main window opens below the notch; closes if already open
        void ToggleFromNotch()
        {
            if (Morph.Busy) return;
            if (Visible)
            {
                HideAnimated();
                return;
            }
            Rectangle b = Screen.PrimaryScreen.Bounds;
            int cx = island.ResolvedCenterX();
            StartPosition = FormStartPosition.Manual;
            Point at = new Point(Math.Max(b.Left, Math.Min(b.Right - W, cx - W / 2)), b.Top + 40);
            Rectangle target = new Rectangle(at, new Size(W, H));
            Opacity = 0;
            Bounds = target;
            MorphOverlay flight = Morph.Open(island.VisualRect, target, 620);
            Show();
            Activate();
            Morph.Reveal(this, flight, null);
        }

        // The main window returns into the notch, then hides
        void HideAnimated()
        {
            if (!Visible || Morph.Busy) return;
            Morph.Close(this, Bounds, island.VisualRect, false, delegate
            {
                Opacity = 1.0;
                island.Pulse();
            });
        }

        void HideToTray()
        {
            HideAnimated();
            if (!hideHintShown)
            {
                hideHintShown = true;
                tray.ShowBalloonTip(3000, "PowerNotch", Loc.T("balloon_hidden"), ToolTipIcon.Info);
            }
        }

        void ShowFromTray()
        {
            Show();
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            FpsMonitor.Apply(false);
            GpuSwitch.Restore();   // a disabled discrete graphics card is re-enabled on exit
            Close();
        }

        static System.Drawing.Icon MakeTrayIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath path = PilUi.Rounded(new RectangleF(0f, 0f, 31f, 31f), 7f))
                using (SolidBrush b = new SolidBrush(PilUi.Bg))
                {
                    g.FillPath(b, path);
                }
                PilUi.Icon(g, "bolt", new RectangleF(0f, 0f, 32f, 32f), PilUi.Green);

                IntPtr handle = bmp.GetHicon();
                System.Drawing.Icon copy = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
                WinNative.DestroyIcon(handle);
                return copy;
            }
        }
    }

    static class WinNative
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int processId);

        // To allow dragging the window by its title (empty area)
        public static void Drag(IntPtr handle)
        {
            ReleaseCapture();
            SendMessage(handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
        }
    }
}
