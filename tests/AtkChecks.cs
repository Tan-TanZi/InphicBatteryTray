using System;

namespace InphicBatteryTray
{
    static class AtkChecks
    {
        static int checks;

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }

        static HidInterface Receiver()
        {
            return new HidInterface { Vid = 0x373B, Pid = 0x101A,
                UsagePage = 0xFF02, Usage = 2, InLen = 17, OutLen = 17 };
        }

        static void SetLevel(byte[] reply, byte level)
        {
            reply[16] = unchecked((byte)(reply[16] + reply[6] - level));
            reply[6] = level;
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                HidInterface dev = Receiver();
                Check(Program.IsAtk(dev), "Match the X1 Pro battery collection");
                dev.Vid = 0x3554;
                Check(!Program.IsAtk(dev), "Reject another vendor");
                dev = Receiver(); dev.Pid = 0x101B;
                Check(!Program.IsAtk(dev), "Do not probe unverified 8K receivers");
                dev = Receiver(); dev.UsagePage = 1;
                Check(!Program.IsAtk(dev), "Never read keyboard/mouse input");
                dev = Receiver(); dev.Usage = 1;
                Check(!Program.IsAtk(dev), "Reject another vendor collection");
                dev = Receiver(); dev.InLen = 16;
                Check(!Program.IsAtk(dev), "Reject a short input report");
                dev = Receiver(); dev.OutLen = 16;
                Check(!Program.IsAtk(dev), "Reject a short output report");

                byte[] reply = new byte[] { 8, 4, 0, 0, 0, 2, 35, 0, 14, 148, 0, 0, 0, 0, 0, 0, 130 };
                Check(Program.ParseAtkBattery(reply, reply.Length) == 35, "Decode a battery response");
                SetLevel(reply, 0);
                Check(Program.ParseAtkBattery(reply, 17) == 0, "Zero is a valid battery level");
                SetLevel(reply, 100);
                Check(Program.ParseAtkBattery(reply, 17) == 100, "Accept a full battery");
                SetLevel(reply, 101);
                Check(Program.ParseAtkBattery(reply, 17) == -1, "Reject an invalid percentage");
                SetLevel(reply, 35);
                reply[16]++;
                Check(Program.ParseAtkBattery(reply, 17) == -1, "Reject a corrupt checksum");
                reply[16]--;
                for (int n = 0; n < 17; n++)
                    Check(Program.ParseAtkBattery(reply, n) == -1, "Reject truncated report length " + n);
                Check(Program.ParseAtkBattery(new byte[7], 17) == -1, "Check buffer capacity");
                Check(Program.ParseAtkBattery(reply, 18) == -1, "Check received length");
                Check(Program.ParseAtkBattery(null, 17) == -1, "Reject a missing response");
                reply[0] = 3; reply[16] += 5;
                Check(Program.ParseAtkBattery(reply, 17) == -1, "Reject another report ID");
                reply[0] = 8; reply[1] = 5; reply[16] -= 6;
                Check(Program.ParseAtkBattery(reply, 17) == -1, "Reject another command");
                int charge;
                Check(Program.QueryAtkBattery(Native.INVALID_HANDLE_VALUE, Receiver(), out charge) == -1 && charge == -1,
                    "A failed write must not produce a reading");
                CheckAtkCharging();
                CheckChargingTooltip();
                CheckInphic();
                CheckReadCancellation();
                CheckReaderLifecycle();
                CheckPopupCleanup();
                CheckLogLimit();
                CheckSingleInstance();
                CheckDeviceIdentity();
                Console.WriteLine("PASS: " + checks + " ATK checks");

                if (args.Length > 0 && args[0] == "--probe") Probe();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        static void CheckSingleInstance()
        {
            bool first;
            using (var instance = new System.Threading.Mutex(true, @"Local\InphicBatteryTray", out first))
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Program.Main(new string[0]);
                Check(elapsed.ElapsedMilliseconds < 1000, "A second instance must exit promptly without opening a tray");
            }
        }

        static void CheckAtkCharging()
        {
            byte[] battery = new byte[] { 8, 4, 0, 0, 0, 2, 30, 0, 14, 90, 0, 0, 0, 0, 0, 0, 193 };
            byte[] charging = new byte[] { 8, 4, 0, 0, 0, 2, 50, 1, 15, 31, 0, 0, 0, 0, 0, 0, 230 };
            Check(Program.ParseAtkCharge(battery, 17) == 1, "Real battery-powered capture is not charging");
            Check(Program.ParseAtkBattery(charging, 17) == 50 && Program.ParseAtkCharge(charging, 17) == 3,
                "Real charging capture retains percentage and exposes the charging flag");
            Check(Program.ParseAtkCharge(charging, 7) == -1, "Never parse charging from partial data");
            Check(Program.ParseAtkCharge(null, 17) == -1, "Never parse charging from missing data");
            charging[16]++;
            Check(Program.ParseAtkCharge(charging, 17) == -1, "A corrupt report must not update charging state");
            charging[16] -= 2; charging[7] = 2;
            Check(Program.ParseAtkBattery(charging, 17) == 50 && Program.ParseAtkCharge(charging, 17) == -1,
                "An unknown charging flag must stay unknown without discarding valid battery data");
            charging[7] = 1; charging[16]++;
            SetLevel(charging, 100);
            Check(Program.ParseAtkCharge(charging, 17) == 3,
                "A 100 percent reading alone must not invent an unverified charge-complete flag");
        }

        static void CheckChargingTooltip()
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var trayField = typeof(Program).GetField("tray", flags);
            var iconField = typeof(Program).GetField("currentIcon", flags);
            var states = (System.Collections.Generic.Dictionary<string, DeviceState>)
                typeof(Program).GetField("states", flags).GetValue(null);
            var tick = typeof(Program).GetMethod("TickStatus", flags);
            using (var tray = new System.Windows.Forms.NotifyIcon())
            {
                try
                {
                    trayField.SetValue(null, tray);
                    var state = new DeviceState { Battery = 50, ChargeState = 3, LastSeen = DateTime.Now };
                    states.Add("test-ATK", state);
                    tick.Invoke(null, null);
                    Check(tray.Text.Contains("50%") && tray.Text.Contains("充电中"),
                        "The actual tray tooltip must display charging");
                    state.ChargeState = 1;
                    tick.Invoke(null, null);
                    Check(!tray.Text.Contains("充电中"), "Unplugging must clear charging even at the same percentage");
                    state.ChargeState = -1;
                    tick.Invoke(null, null);
                    Check(!tray.Text.Contains("充电中"), "An unknown flag must not claim charging");
                    state.ChargeState = 3; state.LastSeen = DateTime.Now.AddSeconds(-9);
                    tick.Invoke(null, null);
                    Check(tray.Text.Contains("暂无回应") && !tray.Text.Contains("充电中"),
                        "Stale charging data must not be presented as a current charge");
                }
                finally
                {
                    states.Clear();
                    trayField.SetValue(null, null);
                    var icon = iconField.GetValue(null) as System.Drawing.Icon;
                    if (icon != null) icon.Dispose();
                    iconField.SetValue(null, null);
                }
            }
        }

        static void CheckDeviceIdentity()
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var accept = typeof(Program).GetMethod("AcceptIn9", flags);
            var states = (System.Collections.Generic.Dictionary<string, DeviceState>)
                typeof(Program).GetField("states", flags).GetValue(null);
            var first = new HidInterface { Path = "test-IN9-first", Vid = 0x248A, Pid = 0x5B2E };
            var second = new HidInterface { Path = "test-IN9-second", Vid = first.Vid, Pid = first.Pid };
            byte[] reply = new byte[33];
            reply[0] = 0x0C; reply[1] = 1; reply[2] = 0x20; reply[6] = 1; reply[0x12] = 20;
            try
            {
                accept.Invoke(null, new object[] { first, "same model", reply, false });
                reply[0x12] = 80;
                accept.Invoke(null, new object[] { second, "same model", reply, false });
                Check(states.Count == 2, "Two receivers with the same model must retain separate battery states");
                Check(states[first.Path].Battery == 20 && states[second.Path].Battery == 80,
                    "One receiver must not overwrite the other battery");
                accept.Invoke(null, new object[] { first, "same model", new byte[1], false });
                Check(states[first.Path].Battery == 20, "Invalid data must preserve the last valid reading");
            }
            finally { states.Clear(); }
        }

        static void CheckPopupCleanup()
        {
            var popup = new InfoPopup("Battery check");
            var field = typeof(InfoPopup).GetField("lifeTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var timer = (System.Windows.Forms.Timer)field.GetValue(popup);
            timer.Start();
            popup.Dispose();
            Check(!timer.Enabled, "Closing the popup must stop its timer");
        }

        static void CheckLogLimit()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".log");
            var field = typeof(Program).GetField("logPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object previous = field.GetValue(null);
            try
            {
                System.IO.File.WriteAllBytes(path, new byte[512 * 1024 + 1]);
                field.SetValue(null, path);
                typeof(Program).GetMethod("Log", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static).Invoke(null, new object[] { "bounded-log-check" });
                Check(new System.IO.FileInfo(path).Length < 1024, "Runtime logs must stay bounded without a restart");
                Check(System.IO.File.ReadAllText(path).Contains("bounded-log-check"), "Keep the newest log entry");
            }
            finally
            {
                field.SetValue(null, previous);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        static void CheckReaderLifecycle()
        {
            var dev = Receiver();
            dev.IsAtk = true;
            dev.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".missing-hid");
            for (int i = 0; i < 50; i++)
            {
                Program.StartReader(dev);
                if (i % 5 == 0) Program.Rescan();
            }
            var field = typeof(Program).GetField("readerThreads",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var readers = (System.Collections.Generic.Dictionary<string, System.Threading.Thread>)field.GetValue(null);
            bool finished = System.Threading.SpinWait.SpinUntil(delegate
            {
                lock (readers) return readers.Count == 0;
            }, 2000);
            Check(finished, "Failed opens and rescans must not leave thread records or path reservations");
        }

        static void CheckInphic()
        {
            var dev = new HidInterface { Vid = 0x1D57, Pid = 0xFA60, UsagePage = 0x000A, InLen = 5 };
            Check(Program.IsIn10(dev), "Match the IN10 status interface");
            dev.UsagePage = 1;
            Check(!Program.IsIn10(dev), "Reject the IN10 mouse interface");
            dev.UsagePage = 7;
            Check(!Program.IsIn10(dev), "Reject the IN10 keyboard interface");
            dev = new HidInterface { Vid = 0x248A, UsagePage = 0xFF00, FeatLen = 33 };
            Check(Program.IsIn9(dev), "Match the IN9 feature interface");
            dev.UsagePage = 1;
            Check(!Program.IsIn9(dev), "Never query an IN9 generic input interface");
            dev.UsagePage = 7;
            Check(!Program.IsIn9(dev), "Never query an IN9 keyboard interface");

            byte[] push = new byte[] { 3, 0x98, 0x40, 1, 87 };
            Check(Program.ParseIn10Battery(push, 5) == 87, "Decode IN10 battery");
            for (int n = 0; n < 5; n++)
                Check(Program.ParseIn10Battery(push, n) == -1, "Reject partial IN10 data");
            push[4] = 101;
            Check(Program.ParseIn10Battery(push, 5) == -1, "Reject invalid IN10 percentage");
            push[4] = 0; push[2] = 0x41;
            Check(Program.ParseIn10Battery(push, 5) == 0, "Accept IN10 zero and alternate battery event");
            push[1] = 0;
            Check(Program.ParseIn10Battery(push, 5) == -1, "Reject unknown IN10 types");

            byte[] feature = new byte[33];
            feature[0] = 0x0C; feature[1] = 1; feature[2] = 0x20; feature[6] = 1; feature[0x12] = 87;
            Check(Program.ParseIn9Battery(feature) == 87, "Decode IN9 battery");
            Check(Program.ParseIn9Battery(new byte[20]) == -1, "Reject short IN9 data");
            feature[0] = 0;
            Check(Program.ParseIn9Battery(feature) == -1, "Reject incorrect IN9 report ID");
            feature[0] = 0x0C; feature[6] = 0;
            Check(Program.ParseIn9Battery(feature) == -1, "Reject invalid IN9 response flag");
        }

        static void CheckReadCancellation()
        {
            string name = "AtkReadCheck-" + Guid.NewGuid();
            using (var server = new System.IO.Pipes.NamedPipeServerStream(name,
                System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous))
            using (var client = new System.IO.Pipes.NamedPipeClientStream(".", name,
                System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.Asynchronous))
            {
                IAsyncResult waiting = server.BeginWaitForConnection(null, null);
                client.Connect(1000);
                server.EndWaitForConnection(waiting);
                byte[] buffer = new byte[17];
                IntPtr handle = server.SafePipeHandle.DangerousGetHandle();
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Check(Program.HidTransfer(handle, buffer, 50, false) == 0, "Cancel a pending read on timeout");
                Check(elapsed.ElapsedMilliseconds < 1000, "A read timeout must return promptly");
                IAsyncResult writing = client.BeginWrite(new byte[] { 42 }, 0, 1, null, null);
                Check(Program.HidTransfer(handle, buffer, 1000, false) == 1 && buffer[0] == 42,
                    "Read again successfully after cancellation");
                client.EndWrite(writing);
                client.Dispose();
                Check(Program.HidTransfer(handle, buffer, 1000, false) == -1,
                    "A disconnected peer must release the read");
            }
        }

        static void Probe()
        {
            int found = 0;
            foreach (HidInterface dev in Reader.Enumerate())
            {
                if (!Program.IsAtk(dev)) continue;
                found++;
                Console.WriteLine("ATK collection: " + dev.Product + " " + dev.Path);
                IntPtr handle = Native.CreateFileW(dev.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                    Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                Check(handle != Native.INVALID_HANDLE_VALUE, "Open the battery collection");
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int charge;
                        int level = Program.QueryAtkBattery(handle, dev, out charge);
                        Check(level >= 0, "No valid ATK battery reply; wake the mouse and close exclusive vendor tools");
                        Console.WriteLine("ATK battery: " + level + "% charge=" + charge);
                        if (i < 2) System.Threading.Thread.Sleep(2500);
                    }
                }
                finally { Native.CloseHandle(handle); }
            }
            Check(found > 0, "No matching ATK 1K battery collection connected");
            Console.WriteLine("PASS: hardware query");
        }
    }
}
