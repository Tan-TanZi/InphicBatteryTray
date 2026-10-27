// InphicBatteryTray.cs  —  INPHIC / ATK 鼠标电量托盘显示器
// 接收或查询电量，不修改设备配置、固件或原厂软件。
//
// 图标：由 鼠标.svg 渲染的剪影，按电量从下往上填充，颜色随电量变化。
//
// 编译：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 ^
//     /resource:MouseIcon.png,MouseIcon.png ^
//     /out:InphicBatteryTray.exe /reference:System.dll /reference:System.Windows.Forms.dll ^
//     /reference:System.Drawing.dll InphicBatteryTray.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InphicBatteryTray
{
    static class Native
    {
        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const int FILE_SHARE_READ = 1;
        public const int FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public const int ERROR_IO_PENDING = 997;
        public const uint WAIT_OBJECT_0 = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct GUID { public uint Data1; public ushort Data2; public ushort Data3; public byte b0, b1, b2, b3, b4, b5, b6, b7; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public GUID InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(ref GUID ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref GUID interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out GUID guid);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(IntPtr device, IntPtr buffer, int bufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, int shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
        public static extern bool ReadFileBuffer(IntPtr hFile, IntPtr buffer, int numberOfBytesToRead, out int numberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
        public static extern bool WriteFileBuffer(IntPtr hFile, IntPtr buffer, int numberOfBytesToWrite, out int numberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, byte[] capabilities);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    class HidInterface
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public string Product;
        public string Transport;   // USB / 蓝牙
        public bool IsIn9;         // IN9 系列：需要主动查询
        public bool IsAtk;
        public ushort UsagePage;
        public ushort Usage;
        public int InLen;          // 输入报告长度
        public int OutLen;         // 输出报告长度
        public int FeatLen;        // Feature 报告长度
    }

    class DeviceState
    {
        public string Label;
        public int Battery = -1;
        public double LastIntervalMs;
        public DateTime LastSeen = DateTime.MinValue;
        public int ChargeState = -1;   // 1 正常 / 2 已满 / 3 充电
        public string Protocol;        // "IN10 push" / "IN9 query" / "ATK query"
        public string Product;
        public ushort Vid;
        public ushort Pid;
        public string Transport;
        public int TypeCode = -1;
    }

    class Reader
    {
        // 从 HID 接口路径推断连接方式（蓝牙设备走 BTHENUM / bth 路径）
        public static string DetectTransport(string path)
        {
            if (string.IsNullOrEmpty(path)) return "未知";
            string p = path.ToLowerInvariant();
            if (p.Contains("bth") || p.Contains("bluetooth")) return "蓝牙 (BLE)";
            return "USB";
        }

        public static List<HidInterface> Enumerate()
        {
            var list = new List<HidInterface>();
            Native.GUID guid;
            Native.HidD_GetHidGuid(out guid);
            IntPtr set = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == Native.INVALID_HANDLE_VALUE) return list;

            try
            {
                int idx = 0;
                while (true)
                {
                    var did = new Native.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, idx, ref did))
                        break;
                    idx++;

                    int need;
                    Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, need, out need, IntPtr.Zero))
                            continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        var dev = new HidInterface();
                        dev.Path = path;
                        IntPtr h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (h == Native.INVALID_HANDLE_VALUE) continue;
                        try
                        {
                            var attr = new Native.HIDD_ATTRIBUTES();
                            attr.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                            if (Native.HidD_GetAttributes(h, ref attr))
                            {
                                dev.Vid = attr.VendorID;
                                dev.Pid = attr.ProductID;
                            }
                            IntPtr sbuf = Marshal.AllocHGlobal(512);
                            try
                            {
                                if (Native.HidD_GetProductString(h, sbuf, 512))
                                    dev.Product = Marshal.PtrToStringUni(sbuf);
                            }
                            finally { Marshal.FreeHGlobal(sbuf); }

                            // 报告长度：WriteFile 必须严格匹配，否则会 ERROR_INVALID_PARAMETER
                            IntPtr pp;
                            if (Native.HidD_GetPreparsedData(h, out pp) && pp != IntPtr.Zero)
                            {
                                try
                                {
                                    byte[] caps = new byte[256];
                                    if (Native.HidP_GetCaps(pp, caps) == 0x00110000)
                                    {
                                        dev.Usage = BitConverter.ToUInt16(caps, 0);
                                        dev.UsagePage = BitConverter.ToUInt16(caps, 2);
                                        dev.InLen = BitConverter.ToUInt16(caps, 4);
                                        dev.OutLen = BitConverter.ToUInt16(caps, 6);
                                        dev.FeatLen = BitConverter.ToUInt16(caps, 8);
                                    }
                                }
                                finally { Native.HidD_FreePreparsedData(pp); }
                            }
                        }
                        finally { Native.CloseHandle(h); }

                        if (dev.Vid != 0 || dev.Pid != 0)
                        {
                            dev.Transport = DetectTransport(path);
                            list.Add(dev);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }

            return list;
        }
    }

    static class Program
    {
        // 已从 INPHIC.exe 逆向出的候选 VID/PID
        static readonly ushort[][] Candidates = new ushort[][]
        {
            new ushort[]{0x1D57,0xFA60}, new ushort[]{0x1D57,0xFA65},
            new ushort[]{0x045E,0x0095}, new ushort[]{0x045E,0x0090},
            new ushort[]{0x045E,0x0093}, new ushort[]{0x045E,0x0099},
            new ushort[]{0x045E,0x0098}, new ushort[]{0x045E,0x0273},
            new ushort[]{0x045E,0x0271},
        };

        static readonly HashSet<int> TypeCodes = new HashSet<int> { 0x95, 0x90, 0x93, 0x99, 0x98, 0x73 };

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RUN_NAME = "InphicBatteryTray";

        static NotifyIcon tray;
        static Icon currentIcon;
        static string logPath = null;
        static string calibrationPath;
        static readonly object logSync = new object();
        static Bitmap srcIcon = null;
        static MenuItem autoItem;
        static int lastDrawnLevel = -999;
        static bool lastDrawnOffline = true;

        // 超过该时长没收到电量报告即视为离线（设备上报周期约 2s）
        const int OfflineAfterMs = 8000;
        static System.Windows.Forms.Timer statusTimer;

        static readonly object sync = new object();
        static readonly Dictionary<string, DeviceState> states = new Dictionary<string, DeviceState>();
        static volatile bool running = true;
        static Thread supervisor;

        static void Log(string s)
        {
            if (logPath == null) return;
            lock (logSync)
            {
                try
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > 512 * 1024)
                        File.WriteAllText(logPath, string.Empty);
                    File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n");
                }
                catch { }
            }
        }

        [STAThread]
        internal static void Main(string[] args)
        {
            bool first;
            using (var instance = new Mutex(true, @"Local\InphicBatteryTray", out first))
            {
                if (first) Run(args);
            }
        }

        static void Run(string[] args)
        {
            logPath = Environment.GetEnvironmentVariable("INPHIC_TRAY_LOG");
            foreach (string a in args)
            {
                if (a.StartsWith("--log=")) logPath = a.Substring(6);
                if (a.StartsWith("--calibration=")) calibrationPath = a.Substring(14);
            }
            if (logPath == null)
            {
                // 默认写日志到程序目录，方便排查（尤其 IN9 支持）
                try
                {
                    string dir = Path.GetDirectoryName(Application.ExecutablePath);
                    logPath = Path.Combine(dir, "tray.log");
                }
                catch { logPath = null; }
            }
            Log("=== start ===");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LoadIconSource();

            tray = new NotifyIcon();
            tray.Text = "鼠标电量：搜索设备中…";
            currentIcon = DrawIcon(-1, true);
            tray.Icon = currentIcon;
            tray.Visible = true;

            var menu = new ContextMenu();

            autoItem = new MenuItem("开机自动启动");
            autoItem.Checked = IsAutoStartEnabled();
            autoItem.Click += delegate { ToggleAutoStart(); };

            menu.MenuItems.Add("显示详情", delegate { ShowDetails(); });
            menu.MenuItems.Add("重新扫描设备", delegate { Rescan(); });
            menu.MenuItems.Add(autoItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("退出", delegate { Quit(); });

            tray.ContextMenu = menu;
            tray.DoubleClick += delegate { ShowDetails(); };

            supervisor = new Thread(Supervise);
            supervisor.IsBackground = true;
            supervisor.Start();

            // 每秒检查一次：设备是否已经长时间没有上报（关机/断连/休眠）
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 1000;
            statusTimer.Tick += delegate { TickStatus(); };
            statusTimer.Start();

            // 自测：设置 INPHIC_TEST_POPUP=1 时启动 2.5 秒后自动弹一次浮层
            if (Environment.GetEnvironmentVariable("INPHIC_TEST_POPUP") == "1")
            {
                var testTimer = new System.Windows.Forms.Timer();
                testTimer.Interval = 2500;
                testTimer.Tick += delegate
                {
                    testTimer.Dispose();
                    try { ShowDetails(); Log("test popup shown"); }
                    catch (Exception ex) { Log("test popup FAILED: " + ex); }
                };
                testTimer.Start();
            }

            try { Application.Run(); }
            finally
            {
                running = false;
                Rescan();
                statusTimer.Dispose();
                if (popup != null) popup.Dispose();
                tray.Dispose();
                if (currentIcon != null) currentIcon.Dispose();
                if (srcIcon != null) srcIcon.Dispose();
            }
        }

        // ---------------------------------------------------------- 开机自启

        static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RUN_NAME);
                    if (v == null) return false;
                    string cur = v.ToString().Trim('"');
                    return string.Equals(cur, Application.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        static void ToggleAutoStart()
        {
            try
            {
                bool want = !IsAutoStartEnabled();
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k == null)
                    {
                        MessageBox.Show("无法打开启动项注册表键。", "鼠标电量",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (want)
                        k.SetValue(RUN_NAME, "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue(RUN_NAME, false);
                }
                if (autoItem != null) autoItem.Checked = IsAutoStartEnabled();
                Log("autostart -> " + want);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启动失败：" + ex.Message, "鼠标电量",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------------------------------------------------- 图标

        static void LoadIconSource()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("MouseIcon.png"))
                {
                    if (s != null) srcIcon = new Bitmap(s);
                }
                Log("icon source: " + (srcIcon == null ? "MISSING" : srcIcon.Width + "x" + srcIcon.Height));
            }
            catch (Exception ex) { Log("icon load error: " + ex.Message); }
        }

        static Color StatusColor(int lv)
        {
            if (lv <= 15) return Color.FromArgb(255, 70, 70);
            if (lv <= 40) return Color.FromArgb(255, 190, 60);
            return Color.FromArgb(0x19, 0xFA, 0xC6);   // #19fac6
        }

        // 原图主体是 #1afa29 高饱和绿；缩放混色后仍保持 g 明显优势
        static bool IsGreenBody(Color c)
        {
            return c.G > 110 && (c.G - c.R) > 45 && (c.G - c.B) > 45;
        }

        static Color OutlineColor { get { return Color.FromArgb(38, 32, 62); } }

        // 最外一圈：alpha 有效且四邻域里有背景（保证 16px 下轮廓实心不虚）
        static bool[,] BuildRing(int[,] alpha, int w, int h, int thr)
        {
            bool[,] ring = new bool[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (alpha[y, x] < thr) continue;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                    {
                        ring[y, x] = true;
                        continue;
                    }
                    if (alpha[y, x + 1] < thr || alpha[y, x - 1] < thr ||
                        alpha[y + 1, x] < thr || alpha[y - 1, x] < thr)
                        ring[y, x] = true;
                }
            }
            return ring;
        }

        // 按电量从下往上填充鼠标主体；外圈实心描边，滚轮/顶部保持原色
        // offline=true 时整体灰显（设备关机 / 断连 / 长时间未上报）
        static Icon DrawIcon(int level, bool offline)
        {
            Size sz = SystemInformation.SmallIconSize;
            if (sz.Width < 16) sz = new Size(16, 16);
            int w = sz.Width, h = sz.Height;

            using (Bitmap scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.Clear(Color.Transparent);
                    if (srcIcon != null)
                        g.DrawImage(srcIcon, new Rectangle(0, 0, w, h));
                }

                using (Bitmap outp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    double fillTop = h * (1.0 - Math.Max(0, Math.Min(100, level)) / 100.0);
                    Color st = StatusColor(level);
                    Color empty = Color.FromArgb(100, 100, 110);
                    Color outline = OutlineColor;
                    Color offOutline = Color.FromArgb(52, 52, 62);
                    bool grey = offline || level < 0;

                    int[,] alpha = new int[h, w];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            alpha[y, x] = scaled.GetPixel(x, y).A;

                    bool[,] ring = BuildRing(alpha, w, h, 40);

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            Color c = scaled.GetPixel(x, y);
                            if (c.A < 40) continue;   // 丢弃极淡边缘，避免 16px 下发虚

                            Color o;
                            if (grey)
                            {
                                if (ring[y, x])
                                {
                                    o = offOutline;
                                }
                                else
                                {
                                    // 灰度化，但保留内部明暗（滚轮仍可辨）
                                    int L = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                                    int v = 74 + L * 108 / 255;
                                    if (v > 255) v = 255;
                                    o = Color.FromArgb(v, v, Math.Min(255, v + 8));
                                }
                            }
                            else if (ring[y, x])
                            {
                                o = outline;
                            }
                            else if (IsGreenBody(c))
                            {
                                o = (y >= fillTop) ? st : empty;
                            }
                            else
                            {
                                o = c;   // 滚轮 / 顶部 / 高光 原样保留
                            }

                            outp.SetPixel(x, y, Color.FromArgb(c.A, o));
                        }
                    }

                    IntPtr hIcon = outp.GetHicon();
                    try
                    {
                        using (Icon tmp = Icon.FromHandle(hIcon))
                            return (Icon)tmp.Clone();
                    }
                    finally
                    {
                        Native.DestroyIcon(hIcon);
                    }
                }
            }
        }

        // ---------------------------------------------------------- 菜单动作

        static void Quit()
        {
            Application.Exit();
        }

        internal static void Rescan()
        {
            Interlocked.Increment(ref readerGeneration);
            scanRequested.Set();
        }

        static readonly Dictionary<string, Thread> readerThreads =
            new Dictionary<string, Thread>(StringComparer.OrdinalIgnoreCase);
        static readonly AutoResetEvent scanRequested = new AutoResetEvent(false);
        static int readerGeneration;

        internal static void StartReader(HidInterface dev)
        {
            lock (readerThreads)
            {
                if (!running || readerThreads.ContainsKey(dev.Path)) return;
                int generation = readerGeneration;
                var thread = new Thread(delegate ()
                {
                    try
                    {
                        if (dev.IsAtk) AtkQueryLoop(dev, generation);
                        else if (dev.IsIn9) In9QueryLoop(dev, generation);
                        else ReadLoop(dev, generation);
                    }
                    finally
                    {
                        lock (readerThreads) readerThreads.Remove(dev.Path);
                        if (generation != Thread.VolatileRead(ref readerGeneration)) scanRequested.Set();
                    }
                });
                thread.IsBackground = true;
                readerThreads.Add(dev.Path, thread);
                try { thread.Start(); }
                catch { readerThreads.Remove(dev.Path); throw; }
            }
        }

        static string ChargeText(int c)
        {
            switch (c)
            {
                case 1: return "正常（未充电）";
                case 2: return "已充满";
                case 3: return "充电中";
                default: return "未知";
            }
        }

        static void ShowDetails()
        {
            string msg;
            lock (sync)
            {
                if (states.Count == 0)
                {
                    msg = "尚未读取到电量。\n\n请确认：\n· 鼠标已连接并唤醒\n· 必要时关闭原厂驱动界面后重新扫描";
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in states)
                    {
                        var d = kv.Value;
                        bool off = (DateTime.Now - d.LastSeen).TotalMilliseconds > OfflineAfterMs;

                        sb.AppendLine(string.IsNullOrEmpty(d.Product) ? d.Label : d.Product);
                        sb.AppendLine("  连接方式  : " + (string.IsNullOrEmpty(d.Transport) ? "未知" : d.Transport));
                        sb.AppendLine(string.Format("  硬件 ID   : {0:X4}:{1:X4}", d.Vid, d.Pid));
                        if (d.TypeCode >= 0)
                            sb.AppendLine(string.Format("  协议族    : type 0x{0:X2}", d.TypeCode));
                        sb.AppendLine("  协议      : " + (string.IsNullOrEmpty(d.Protocol) ? "未知" : d.Protocol));
                        sb.AppendLine("  电量      : " + d.Battery + "%");
                        sb.AppendLine("  充电状态  : " + ChargeText(d.ChargeState) + (off ? "（最后记录）" : ""));
                        sb.AppendLine("  刷新间隔  : " + (d.LastIntervalMs > 0
                            ? string.Format("{0:F0} ms", d.LastIntervalMs) : "等待下一次读取"));
                        sb.AppendLine(string.Format("  最后上报  : {0:HH:mm:ss}", d.LastSeen));
                        sb.AppendLine("  当前状态  : " + (off ? "暂无回应" : "在线"));
                        sb.AppendLine();
                    }
                    msg = sb.ToString();
                }
            }
            ShowInfoPopup(msg);
        }

        static InfoPopup popup;

        // 非模态信息浮层：不抢焦点、不阻塞、鼠标移开自动消失
        static void ShowInfoPopup(string text)
        {
            if (popup != null)
            {
                popup.Close();
                popup = null;
            }
            popup = new InfoPopup(text);
            popup.FormClosed += delegate { popup = null; };
            popup.ShowNear(Cursor.Position);
        }

        // ---------------------------------------------------------- 设备读取

        static void Supervise()
        {
            while (running)
            {
                try
                {
                    List<HidInterface> all = Reader.Enumerate();
                    var targets = new List<HidInterface>();
                    foreach (HidInterface d in all)
                    {
                        if (IsAtk(d)) { d.IsAtk = true; targets.Add(d); continue; }
                        if (IsIn10(d)) { targets.Add(d); continue; }

                        if (IsIn9Vid(d.Vid))
                        {
                            bool featOk = IsIn9(d);
                            Log(string.Format("IN9 iface: in={0} out={1} feat={2} {3}{4}",
                                d.InLen, d.OutLen, d.FeatLen, d.Product,
                                featOk ? "  <- candidate" : ""));
                            // IN9 走 Feature Report（报告 ID 0x0C，33 字节）
                            if (featOk) { d.IsIn9 = true; targets.Add(d); }
                        }
                    }
                    Log(string.Format("enumerate: {0} hid interfaces, {1} candidates ({2} IN9)",
                        all.Count, targets.Count, CountIn9(targets)));

                    foreach (HidInterface d in targets) StartReader(d);

                    scanRequested.WaitOne(10000);
                }
                catch (Exception)
                {
                    scanRequested.WaitOne(5000);
                }
            }
        }

        // ---------------------------------------------------------- ATK（主动查询）

        internal static bool IsAtk(HidInterface dev)
        {
            return dev.Vid == 0x373B && dev.Pid == 0x101A &&
                dev.UsagePage == 0xFF02 && dev.Usage == 0x0002 &&
                dev.InLen == 17 && dev.OutLen == 17;
        }

        internal static int ParseAtkBattery(byte[] response, int count)
        {
            if (response == null || count != 17 || response.Length < count ||
                response[0] != 0x08 || response[1] != 0x04 || response[6] > 100)
                return -1;
            int sum = 0;
            for (int i = 0; i < count; i++) sum += response[i];
            if ((sum & 0xFF) != 0x55) return -1;
            return response[6];
        }

        internal static int ParseAtkCharge(byte[] response, int count)
        {
            if (ParseAtkBattery(response, count) < 0) return -1;
            // 实机拔线/充电器供电对照：byte 7 为 0 / 1；不根据百分比推断充满。
            if (response[7] == 0) return 1;
            if (response[7] == 1) return 3;
            return -1;
        }

        internal static void RecordAtkCalibration(HidInterface dev, byte[] response, int count)
        {
            if (string.IsNullOrEmpty(calibrationPath) || !IsAtk(dev) || ParseAtkBattery(response, count) < 0) return;
            lock (logSync)
            {
                try
                {
                    bool header = !File.Exists(calibrationPath) || new FileInfo(calibrationPath).Length == 0;
                    string row = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:o},\"{1}\",{2},{3},{4},{5}\r\n", DateTime.UtcNow,
                        (dev.Path ?? "").Replace("\"", "\"\""), response[6], response[7],
                        response[8] * 256 + response[9], BitConverter.ToString(response, 0, count));
                    File.AppendAllText(calibrationPath, (header
                        ? "utc,device_path,battery_percent,charging_flag,voltage_mv,packet\r\n" : "") + row);
                }
                catch (Exception ex) { Log("ATK calibration write failed: " + ex.Message); }
            }
        }

        internal static int QueryAtkBattery(IntPtr handle, HidInterface dev, out int charge)
        {
            charge = -1;
            if (!IsAtk(dev)) return -1;
            // ATK/Compx Nordic52 电量查询；17 字节之和为 0x55。
            byte[] request = new byte[17];
            request[0] = 0x08;
            request[1] = 0x04;
            request[16] = 0x49;
            if (HidTransfer(handle, request, 1000, true) != request.Length)
            {
                Log("ATK query write failed or timed out");
                return -1;
            }

            byte[] response = new byte[dev.InLen];
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 1500)
            {
                int count = HidTransfer(handle, response, (int)Math.Max(0, 1500 - elapsed.ElapsedMilliseconds), false);
                if (count <= 0) break;
                Log("ATK rx: " + Hex(response, count));
                int level = ParseAtkBattery(response, count);
                if (level >= 0)
                {
                    charge = ParseAtkCharge(response, count);
                    RecordAtkCalibration(dev, response, count);
                    return level;
                }
            }
            Log("ATK no valid battery reply");
            return -1;
        }

        static void AtkQueryLoop(HidInterface dev, int generation)
        {
            Log("ATK try open: " + dev.Path);
            IntPtr handle = Native.CreateFileW(dev.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == Native.INVALID_HANDLE_VALUE)
            {
                Log("ATK open FAILED err=" + Marshal.GetLastWin32Error());
                return;
            }
            try
            {
                int failures = 0;
                while (running && generation == Thread.VolatileRead(ref readerGeneration))
                {
                    int charge;
                    int level = QueryAtkBattery(handle, dev, out charge);
                    if (!running || generation != Thread.VolatileRead(ref readerGeneration)) break;
                    if (level < 0)
                    {
                        if (++failures >= 3) break;
                    }
                    else
                    {
                        failures = 0;
                        lock (sync)
                        {
                            DeviceState state;
                            if (!states.TryGetValue(dev.Path, out state))
                            {
                                state = new DeviceState();
                                state.Label = "ATK X1 Pro (1K)";
                                state.Product = "ATK X1 Pro / " + dev.Product;
                                state.Protocol = "ATK query";
                                state.Vid = dev.Vid;
                                state.Pid = dev.Pid;
                                state.Transport = "2.4G (USB 接收器)";
                                states[dev.Path] = state;
                            }
                            DateTime now = DateTime.Now;
                            if (state.LastSeen != DateTime.MinValue)
                                state.LastIntervalMs = (now - state.LastSeen).TotalMilliseconds;
                            state.Battery = level;
                            state.ChargeState = charge;
                            state.LastSeen = now;
                        }
                        Log("ATK battery " + level + "% charge=" + charge);
                    }
                    Thread.Sleep(2500);
                }
            }
            catch (Exception ex) { Log("ATK loop exception: " + ex.Message); }
            finally { Native.CloseHandle(handle); }
        }

        // ---------------------------------------------------------- IN9（主动查询）

        // IN9 系列：USB=VID_248A，2.4G=VID_249A（PID 不固定）
        static readonly ushort[] In9Vids = new ushort[] { 0x248A, 0x249A };

        static bool IsIn9Vid(ushort vid)
        {
            for (int i = 0; i < In9Vids.Length; i++)
                if (In9Vids[i] == vid) return true;
            return false;
        }

        internal static bool IsIn10(HidInterface dev)
        {
            if (dev.UsagePage != 0x000A || dev.InLen < 5) return false;
            foreach (ushort[] candidate in Candidates)
                if (dev.Vid == candidate[0] && dev.Pid == candidate[1]) return true;
            return false;
        }

        internal static bool IsIn9(HidInterface dev)
        {
            return IsIn9Vid(dev.Vid) && dev.UsagePage != 0x0001 &&
                dev.UsagePage != 0x0007 && dev.FeatLen >= 33;
        }

        internal static int ParseIn10Battery(byte[] response, int count)
        {
            if (response == null || count < 5 || count > response.Length ||
                response[0] != 3 || !TypeCodes.Contains(response[1]) ||
                (response[2] != 0x40 && response[2] != 0x41) || response[4] > 100)
                return -1;
            return response[4];
        }

        internal static int ParseIn9Battery(byte[] response)
        {
            if (response == null || response.Length < 33 || response[0] != 0x0C ||
                response[1] != 1 || response[2] != 0x20 || response[6] == 0 || response[0x12] > 100)
                return -1;
            return response[0x12];
        }

        static int CountIn9(List<HidInterface> list)
        {
            int n = 0;
            foreach (HidInterface d in list) if (d.IsIn9) n++;
            return n;
        }

        static string Hex(byte[] b, int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n && i < b.Length; i++)
                sb.Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }

        // IN9 命令帧（33 字节 Feature Report，实测自 IN9 升级版）
        //   0C 01 <cmd> 00 <seq> <payload...>
        //   握手 cmd=0xF0 payload=01 01 FF
        //   查电量 cmd=0x20 payload=00 00
        static byte[] BuildIn9Frame(byte cmd, byte seq, byte[] payload)
        {
            byte[] f = new byte[33];
            f[0] = 0x0C;      // 报告 ID
            f[1] = 0x01;
            f[2] = cmd;
            f[3] = 0x00;
            f[4] = seq;
            if (payload != null)
                Array.Copy(payload, 0, f, 5, Math.Min(payload.Length, 28));
            return f;
        }

        internal static int HidTransfer(IntPtr h, byte[] buf, int ms, bool write)
        {
            IntPtr ev = Native.CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
            if (ev == IntPtr.Zero) return -1;
            IntPtr ovPtr = IntPtr.Zero;
            GCHandle pinned = new GCHandle();
            bool pending = false;
            try
            {
                ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.OVERLAPPED)));
                pinned = GCHandle.Alloc(buf, GCHandleType.Pinned);
                Native.OVERLAPPED ov = new Native.OVERLAPPED();
                ov.hEvent = ev;
                Marshal.StructureToPtr(ov, ovPtr, false);

                int read = 0;
                bool ok = write
                    ? Native.WriteFileBuffer(h, pinned.AddrOfPinnedObject(), buf.Length, out read, ovPtr)
                    : Native.ReadFileBuffer(h, pinned.AddrOfPinnedObject(), buf.Length, out read, ovPtr);
                if (ok) return read;
                if (Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING) return -1;
                pending = true;

                if (Native.WaitForSingleObject(ev, (uint)ms) != Native.WAIT_OBJECT_0)
                {
                    return 0;
                }
                int got = 0;
                if (!Native.GetOverlappedResult(h, ovPtr, out got, false)) return -1;
                return got;
            }
            finally
            {
                // 取消完成前，内核仍可能访问缓冲区和 OVERLAPPED。
                if (pending)
                {
                    Native.CancelIoEx(h, ovPtr);
                    int ignored;
                    Native.GetOverlappedResult(h, ovPtr, out ignored, true);
                }
                if (pinned.IsAllocated) pinned.Free();
                if (ovPtr != IntPtr.Zero) Marshal.FreeHGlobal(ovPtr);
                Native.CloseHandle(ev);
            }
        }

        static void In9QueryLoop(HidInterface dev, int generation)
        {
            string label = string.Format("{0:X4}:{1:X4}", dev.Vid, dev.Pid);
            Log("IN9 try open: " + dev.Path);
            IntPtr h = Native.CreateFileW(dev.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE)
            {
                Log("IN9 open FAILED err=" + Marshal.GetLastWin32Error());
                return;
            }
            Log("IN9 opened OK: " + dev.Product + "  feat=" + dev.FeatLen);

            byte seq = 0x01;
            byte[] resp = new byte[33];
            int failStreak = 0;
            int polls = 0;

            try
            {
                while (running && generation == Thread.VolatileRead(ref readerGeneration))
                {
                    // 握手（只发不收）：0C 01 F0 00 <seq> 01 01 FF
                    byte[] hs = BuildIn9Frame(0xF0, seq, new byte[] { 0x01, 0x01, 0xFF });
                    Native.HidD_SetFeature(h, hs, hs.Length);

                    // 查电量：0C 01 20 00 <seq> 00 00
                    byte[] q = BuildIn9Frame(0x20, seq, new byte[] { 0x00, 0x00 });
                    polls++;

                    if (!Native.HidD_SetFeature(h, q, q.Length))
                    {
                        Log("IN9 SetFeature FAILED err=" + Marshal.GetLastWin32Error());
                        if (++failStreak >= 3) { Log("IN9 give up this interface"); break; }
                    }
                    else
                    {
                        failStreak = 0;
                        Thread.Sleep(10);
                        Array.Clear(resp, 0, resp.Length);
                        resp[0] = 0x0C;
                        if (!Native.HidD_GetFeature(h, resp, resp.Length))
                        {
                            Log("IN9 GetFeature FAILED err=" + Marshal.GetLastWin32Error());
                        }
                        else
                        {
                            if (polls <= 5) Log("IN9 rx: " + Hex(resp, 33));
                            AcceptIn9(dev, label, resp, polls <= 5);
                        }
                    }

                    seq++;
                    if (seq == 0) seq = 1;
                    Thread.Sleep(2500);
                }
            }
            catch (Exception ex) { Log("IN9 loop exception: " + ex.Message); }
            finally { Native.CloseHandle(h); }
        }

        static void AcceptIn9(HidInterface dev, string label, byte[] resp, bool dump)
        {
            // 实测布局：(IN9 升级版)
            //   [0]=0x0C [1]=0x01 [2]=0x20(命令回显) [6]!=0 有效
            //   [8..0x0B]="M909" [0x0F]=IC [0x12]=电量 [0x13]==2 充电中 [0x15]=在线
            int lv = ParseIn9Battery(resp);

            if (lv < 0)
            {
                Log("IN9 invalid battery response: " + Hex(resp, resp == null ? 0 : resp.Length));
                return;
            }

            int charge = (resp[0x13] == 2) ? 3 : 1;   // 3=充电中，1=正常

            DateTime now = DateTime.Now;
            lock (sync)
            {
                DeviceState st;
                if (!states.TryGetValue(dev.Path, out st))
                {
                    st = new DeviceState();
                    st.Label = label;
                    states[dev.Path] = st;
                }
                if (st.LastSeen != DateTime.MinValue)
                    st.LastIntervalMs = (now - st.LastSeen).TotalMilliseconds;

                bool changed = (st.Battery != lv || st.ChargeState != charge);
                st.Battery = lv;
                st.ChargeState = charge;
                st.Protocol = "IN9 query";
                st.LastSeen = now;
                st.Product = dev.Product;
                st.Vid = dev.Vid;
                st.Pid = dev.Pid;
                st.Transport = dev.Transport;

                if (changed || dump)
                    Log("IN9 battery " + lv + "%  charge=" + charge + "  [" + dev.Product + "]");
            }
        }

        static void ReadLoop(HidInterface dev, int generation)
        {
            string label = string.Format("{0:X4}:{1:X4}", dev.Vid, dev.Pid);
            if (!string.IsNullOrEmpty(dev.Product)) label = dev.Product + " [" + label + "]";

            IntPtr h = Native.CreateFileW(dev.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE)
            {
                h = Native.CreateFileW(dev.Path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            }
            if (h == Native.INVALID_HANDLE_VALUE) return;
            Log("opened: " + label);

            var buf = new byte[dev.InLen];

            DateTime lastReport = DateTime.MinValue;

            try
            {
                while (running && generation == Thread.VolatileRead(ref readerGeneration))
                {
                    int got = HidTransfer(h, buf, 3000, false);
                    if (got < 0) break;
                    if (got == 0) continue;

                    // 电量事件：0x40 / 0x41，第 4 字节是充电状态（不是设备号）
                    int level = ParseIn10Battery(buf, got);
                    if (level >= 0)
                    {
                        int charge = buf[3];

                        DateTime now = DateTime.Now;
                        double interval = 0;
                        if (lastReport != DateTime.MinValue)
                            interval = (now - lastReport).TotalMilliseconds;
                        lastReport = now;

                        lock (sync)
                        {
                            DeviceState st;
                            if (!states.TryGetValue(dev.Path, out st))
                            {
                                st = new DeviceState();
                                st.Label = label;
                                states[dev.Path] = st;
                            }
                            st.Battery = level;
                            st.ChargeState = charge;
                            st.Protocol = "IN10 push";
                            st.LastSeen = now;
                            st.Product = dev.Product;
                            st.Vid = dev.Vid;
                            st.Pid = dev.Pid;
                            st.Transport = dev.Transport;
                            st.TypeCode = buf[1];
                            if (interval > 0) st.LastIntervalMs = interval;
                        }
                        Log(string.Format("battery {0}%  charge={1}  {2:F0}ms  transport={3}  type=0x{4:X2}  [{5}]",
                            level, charge, interval, dev.Transport, buf[1], label));
                    }
                }
            }
            catch (Exception ex) { Log("IN10 loop exception: " + ex.Message); }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        static void TickStatus()
        {
            int level = -1;
            int charge = -1;
            DateTime newest = DateTime.MinValue;
            double interval = 0;

            lock (sync)
            {
                foreach (var kv in states)
                {
                    if (kv.Value.LastSeen > newest)
                    {
                        newest = kv.Value.LastSeen;
                        level = kv.Value.Battery;
                        charge = kv.Value.ChargeState;
                        interval = kv.Value.LastIntervalMs;
                    }
                }
            }

            bool offline = (newest == DateTime.MinValue) ||
                           ((DateTime.Now - newest).TotalMilliseconds > OfflineAfterMs);

            RefreshTray(level, interval, offline, newest, charge);
        }

        static void RefreshTray(int level, double interval, bool offline, DateTime lastSeen, int charge)
        {
            if (level != lastDrawnLevel || offline != lastDrawnOffline)
            {
                Log(string.Format("tray state: level={0} offline={1}", level, offline));
                Icon old = currentIcon;
                currentIcon = DrawIcon(level, offline);
                tray.Icon = currentIcon;
                lastDrawnLevel = level;
                lastDrawnOffline = offline;
                if (old != null) { try { old.Dispose(); } catch { } }
            }

            string tip;
            if (level < 0)
                tip = "鼠标电量：未检测到设备";
            else if (offline)
                tip = string.Format("鼠标：暂无回应（最后 {0}%，{1:HH:mm:ss}）",
                    level, lastSeen);
            else if (interval > 0)
                tip = string.Format("鼠标电量 {0}%  ({1:F0}ms 刷新)", level, interval);
            else
                tip = string.Format("鼠标电量 {0}%", level);

            if (!offline && level >= 0 && charge == 3) tip += "  充电中";
            if (tip.Length > 127) tip = tip.Substring(0, 127);
            tray.Text = tip;
        }

    }

    // 轻量信息浮层：不抢焦点、不阻塞，鼠标移开自动消失
    class InfoPopup : Form
    {
        System.Windows.Forms.Timer lifeTimer;
        readonly Font detailFont = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        int countdown;

        protected override void Dispose(bool disposing)
        {
            if (disposing && lifeTimer != null) lifeTimer.Dispose();
            base.Dispose(disposing);
            if (disposing) detailFont.Dispose();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不进 Alt+Tab
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE：不抢焦点
                return cp;
            }
        }

        public InfoPopup(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 28, 32);

            Label lbl = new Label();
            lbl.Dock = DockStyle.Fill;
            lbl.Text = text;
            lbl.ForeColor = Color.FromArgb(236, 236, 242);
            lbl.Font = detailFont;
            lbl.TextAlign = ContentAlignment.TopLeft;
            lbl.BackColor = Color.Transparent;
            Controls.Add(lbl);

            Padding = new Padding(18, 13, 18, 13);
            Size ts = TextRenderer.MeasureText(text, lbl.Font,
                new Size(560, 2000), TextFormatFlags.Left | TextFormatFlags.Top);
            ClientSize = new Size(ts.Width + 36, ts.Height + 26);

            lifeTimer = new System.Windows.Forms.Timer();
            lifeTimer.Interval = 1000;
            lifeTimer.Tick += delegate
            {
                countdown--;
                if (countdown <= 0) Close();
            };

            EventHandler enter = delegate { countdown = 9999; };
            EventHandler leave = delegate { countdown = 2; };
            MouseEnter += enter;
            MouseLeave += leave;
            lbl.MouseEnter += enter;
            lbl.MouseLeave += leave;
            Click += delegate { Close(); };
            lbl.Click += delegate { Close(); };
        }

        public void ShowNear(Point anchor)
        {
            Show();
            Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X - Width / 2;
            int y = anchor.Y - Height - 16;
            if (y < wa.Top) y = anchor.Y + 20;      // 上方放不下就翻到下方
            if (x < wa.Left) x = wa.Left + 4;
            if (x + Width > wa.Right) x = wa.Right - Width - 4;
            Location = new Point(x, y);
            countdown = 4;                          // 4 秒后自动消失；鼠标停留则保持
            lifeTimer.Start();
        }
    }
}
