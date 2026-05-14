using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FastbootFlasherGUI
{
    public partial class MainWindow : Window
    {
        private string _fastbootPath = "";
        private string _baseDir;
        private string _imageDir;
        private string _currentSlot = "";
        private bool _isFlashing;
        private bool _adbMode;
        private bool _showDecrypted;

        public ObservableCollection<ImageItem> Images { get; } = new();

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;

        // ═══ 映 射 (无限制版) ═══

        public MainWindow()
        {
            InitializeComponent();
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _imageDir = Path.Combine(_baseDir, "images");
            try { ConsoleWriter.InitLog(Path.Combine(_baseDir, "logs")); } catch { }
            DgImages.ItemsSource = Images;
            IcImages.ItemsSource = Images;

            SourceInitialized += (s, e) =>
            {
                var val = DWMWCP_ROUND;
                DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,
                    DWMWA_WINDOW_CORNER_PREFERENCE, ref val, sizeof(int));
            };

            Loaded += (s, e) => BtnDetect_Click(null!, null!);
        }

        static Dictionary<string, string> GetBuiltinMapping() => new(StringComparer.OrdinalIgnoreCase);

        public class ImageItem : INotifyPropertyChanged
        {
            bool _c;
            public string FileName { get; set; } = "";
            public string Partition { get; set; } = "";
            public string PartitionDisplay => Partition;
            public string FullPath { get; set; } = "";
            public bool IsChecked { get => _c; set { _c = value; P(); } }
            public void SetShowDecrypted(bool show) { }
            public event PropertyChangedEventHandler? PropertyChanged;
            void P([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        // ═══ 窗 口 ═══
        void TitleBar_MouseDown(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        void BtnMinimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void BtnClose_Click(object s, RoutedEventArgs e) => Close();
        void BtnMaximize_Click(object s, RoutedEventArgs e) => ToggleMaximize();
        void BtnTheme_Click(object s, RoutedEventArgs e) => App.Toggle();
        void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void NavHome_Click(object s, MouseButtonEventArgs e) => SwitchPage(0);
        void NavFlash_Click(object s, MouseButtonEventArgs e) => SwitchPage(1);
        void NavTools_Click(object s, MouseButtonEventArgs e) => SwitchPage(2);
        void NavAbout_Click(object s, MouseButtonEventArgs e) => SwitchPage(3);

        void SwitchPage(int page)
        {
            PageHome.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageFlash.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageTools.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
            LogPanel.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;

            var active = TryFindResource("NavActive") as Brush ?? Brushes.Transparent;
            var text1 = TryFindResource("Text1") as Brush ?? Brushes.White;
            var text2 = TryFindResource("Text2") as Brush ?? Brushes.Gray;

            UpdateNav(NavHome, page == 0, active, text1, text2);
            UpdateNav(NavFlash, page == 1, active, text1, text2);
            UpdateNav(NavTools, page == 2, active, text1, text2);
            UpdateNav(NavAbout, page == 3, active, text1, text2);
        }

        static void UpdateNav(Border nav, bool sel, Brush active, Brush t1, Brush t2)
        {
            nav.Background = sel ? active : Brushes.Transparent;
            ((TextBlock)nav.Child).Foreground = sel ? t1 : t2;
        }

        // ═══ 路由 ═══
        Task<string> FbOut(string args) => Simulator.Enabled ? Simulator.SimFastbootOutput(args) : FastbootHelper.RunFastbootOutput(_fastbootPath, args);
        Task<(bool, string)> Fb(string args, bool show = false) => Simulator.Enabled ? Simulator.SimFastboot(args) : FastbootHelper.RunFastboot(_fastbootPath, args, show);
        Task<string> AdbOut(string args) => Simulator.Enabled ? Simulator.SimAdbOutput(args) : FastbootHelper.RunAdbOutput(FastbootHelper.FindAdbExecutable(_baseDir), args);
        Task<(bool, string)> Adb(string args) => Simulator.Enabled ? Simulator.SimAdb(args) : FastbootHelper.RunAdb(FastbootHelper.FindAdbExecutable(_baseDir), args);
        bool HasAdb() => Simulator.Enabled || !string.IsNullOrEmpty(FastbootHelper.FindAdbExecutable(_baseDir));

        // ═══ 设备 ═══
        async void BtnDetect_Click(object s, RoutedEventArgs e) { await Safe(Detect); }
        async void BtnRefresh_Click(object s, RoutedEventArgs e) { await Safe(() => RefreshDevice()); }
        async void BtnAdb_Click(object s, RoutedEventArgs e) { await Safe(AdbReboot); }

        async Task Detect()
        {
            Log("══════ fastboot 检测 ══════", "#FACC15");
            _fastbootPath = Simulator.Enabled ? Simulator.SimGetFastbootPath(_baseDir) : FastbootHelper.FindFastbootExecutable(_baseDir);
            if (string.IsNullOrEmpty(_fastbootPath))
            {
                LblFastboot.Text = "未找到";
                Log("未找到 fastboot.exe", "#F06055");
                HomeModel.Text = "—";
                HomeAndroid.Text = "";
                HomeKernel.Text = "";
                HomeSlot.Text = "";
                HomeStatus.Text = "未连接";
                return;
            }
            LblFastboot.Text = "OK";
            Log($"{_fastbootPath} 就绪", "#3CB878");
            await RefreshDevice();
            BtnScan_Click(null!, null!);
        }

        async Task RefreshDevice()
        {
            if (string.IsNullOrEmpty(_fastbootPath)) return;
            LblStatus.Text = "检测中…";
            GridDeviceInfo.Children.Clear();
            _adbMode = false;

            var out_ = await FbOut("devices");
            if (!string.IsNullOrEmpty(out_) && out_.Contains("fastboot"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(out_, @"(\S+)\s+fastboot");
                var sn = m.Success ? m.Groups[1].Value : "?";
                LblStatus.Text = $"已连接 ({sn})";
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x3C, 0xB8, 0x78));

                var fbd = await FbOut("getvar is-userspace");
                var isFbd = (fbd ?? "").Contains("yes");
                // 部分设备不支持 is-userspace，通过 partition-type 判断
                if (!isFbd)
                {
                    var ptCheck = await FbOut("getvar partition-type:system");
                    isFbd = (ptCheck ?? "").Contains("raw");
                }
                if (isFbd) Log("模式: Fastbootd", "#9B7EC4");

                _currentSlot = FastbootHelper.ExtractVarValue(await FbOut("getvar current-slot"), "current-slot") ?? "?";
                LblSlot.Text = $"Slot {_currentSlot}";
                if (_currentSlot == "a") CmbSlot.SelectedIndex = 0;
                else if (_currentSlot == "b") CmbSlot.SelectedIndex = 1;

                HomeStatus.Text = isFbd ? "Fastbootd" : "Bootloader";

                // 收集设备信息
                var vars = new Dictionary<string, string>();
                var all = await FbOut("getvar all");
                if (!string.IsNullOrEmpty(all))
                {
                    foreach (var line in all.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("(bootloader) ")) t = t.Substring(13);
                        var idx = t.IndexOf(':');
                        if (idx > 0) vars[t.Substring(0, idx).Trim()] = t.Substring(idx + 1).Trim();
                    }
                }

                vars.TryGetValue("product", out var product);
                HomeModel.Text = product ?? sn;
                HomeAndroid.Text = "";
                HomeKernel.Text = "";
                HomeSlot.Text = $"Slot {_currentSlot}";
                HomeDot.Visibility = HomeDot2.Visibility = Visibility.Collapsed;

                AddInfoRow("序列号", sn);
                AddInfoRow("产品代号", product ?? "—");
                if (vars.TryGetValue("variant", out var variant)) AddInfoRow("变体", variant);
                if (vars.TryGetValue("hw-revision", out var hw)) AddInfoRow("硬件版本", hw);
                if (vars.TryGetValue("secure", out var sec)) AddInfoRow("安全启动", sec);
                if (vars.TryGetValue("unlocked", out var unl)) AddInfoRow("解锁状态", unl == "yes" ? "已解锁" : "已锁定");
                if (vars.TryGetValue("slot-count", out var sc)) AddInfoRow("槽位数量", sc);
                if (vars.TryGetValue("max-download-size", out var mds)) AddInfoRow("最大传输", $"{long.Parse(mds) / 1024 / 1024} MB");
                if (vars.TryGetValue("battery-voltage", out var bv)) AddInfoRow("电池电压", $"{bv} mV");
                if (vars.TryGetValue("version-bootloader", out var vbl)) AddInfoRow("Bootloader", vbl);
                if (vars.TryGetValue("version-baseband", out var vbb)) AddInfoRow("基带", vbb);
                if (vars.TryGetValue("battery-soc-ok", out var bsoc)) AddInfoRow("电池状态", bsoc);
                if (vars.TryGetValue("has-slot", out var hs)) AddInfoRow("支持槽位", hs);
                if (vars.TryGetValue("partition-type", out var pt)) AddInfoRow("分区类型", pt);

                return;
            }

            if (HasAdb())
            {
                var adbOut = await AdbOut("devices");
                var adbMatch = System.Text.RegularExpressions.Regex.Match(adbOut ?? "", @"^(\S+)\s+device\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (adbMatch.Success)
                {
                    LblStatus.Text = $"ADB ({adbMatch.Groups[1].Value})";
                    DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x9B, 0x7E, 0xC4));
                    _adbMode = true;
                    HomeStatus.Text = "Android";
                    HomeDot.Visibility = HomeDot2.Visibility = Visibility.Visible;

                    var model = (await AdbOut("shell getprop ro.product.model"))?.Trim();
                    var brand = (await AdbOut("shell getprop ro.product.brand"))?.Trim();
                    var android = (await AdbOut("shell getprop ro.build.version.release"))?.Trim();
                    var kernel = (await AdbOut("shell uname -r"))?.Trim();

                    // 电池、内存、存储
                    var battLevel = ExtractAdbVal(await AdbOut("shell dumpsys battery") ?? "", "level:");
                    var memOut = await AdbOut("shell cat /proc/meminfo") ?? "";
                    var memTotal = ExtractMeminfo(memOut, "MemTotal");
                    var storageOut = await AdbOut("shell df -h /data") ?? "";
                    var storageSize = ExtractDfSize(storageOut);

                    HomeModel.Text = !string.IsNullOrEmpty(model) ? model : adbMatch.Groups[1].Value;
                    HomeAndroid.Text = !string.IsNullOrEmpty(android) ? $"Android {android}" : "";
                    HomeKernel.Text = !string.IsNullOrEmpty(kernel) ? kernel : "";
                    HomeSlot.Text = "";

                    AddInfoRow("品牌", brand ?? "—");
                    AddInfoRow("型号", model ?? "—");
                    AddInfoRow("系统版本", !string.IsNullOrEmpty(android) ? $"Android {android}" : "—");
                    AddInfoRow("内核版本", !string.IsNullOrEmpty(kernel) ? kernel : "—");
                    if (!string.IsNullOrEmpty(battLevel)) AddInfoRow("电池电量", battLevel);
                    if (!string.IsNullOrEmpty(memTotal)) AddInfoRow("RAM", memTotal);
                    if (!string.IsNullOrEmpty(storageSize)) AddInfoRow("储存", storageSize);

                    var sdk = (await AdbOut("shell getprop ro.build.version.sdk"))?.Trim();
                    var cpu = (await AdbOut("shell getprop ro.product.cpu.abi"))?.Trim();
                    var serial = (await AdbOut("shell getprop ro.serialno"))?.Trim();
                    var display = (await AdbOut("shell wm size"))?.Trim().Replace("Physical size: ", "");
                    var density = (await AdbOut("shell wm density"))?.Trim().Replace("Physical density: ", "");

                    if (!string.IsNullOrEmpty(sdk)) AddInfoRow("SDK 等级", sdk);
                    if (!string.IsNullOrEmpty(cpu)) AddInfoRow("CPU 架构", cpu);
                    if (!string.IsNullOrEmpty(serial)) AddInfoRow("序列号", serial);
                    if (!string.IsNullOrEmpty(display)) AddInfoRow("屏幕分辨率", display);
                    if (!string.IsNullOrEmpty(density)) AddInfoRow("屏幕密度", density + " dpi");
                    return;
                }
            }

            LblStatus.Text = "未连接";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x74));
            HomeModel.Text = "—";
            HomeAndroid.Text = "";
            HomeKernel.Text = "";
            HomeSlot.Text = "";
            HomeStatus.Text = "未连接";
            HomeDot.Visibility = HomeDot2.Visibility = Visibility.Collapsed;
            AddInfoRow("状态", "等待设备…");
        }

        async Task AdbReboot()
        {
            if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; }
            if (!HasAdb()) { Log("未找到 adb", "#F06055"); return; }
            var adbOut = await AdbOut("devices");
            var adbMatch = System.Text.RegularExpressions.Regex.Match(adbOut ?? "", @"^(\S+)\s+device\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!adbMatch.Success) { Log("无 ADB 设备", "#F0A020"); return; }
            if (!Ask("通过 ADB 重启到 fastboot？")) return;
            Log("adb reboot bootloader…", "#FACC15");
            await Adb("reboot bootloader");
            await Task.Delay(5000);
            for (int w = 0; w < 30; w++)
            {
                var fbOut = await FbOut("devices");
                if ((fbOut ?? "").Contains("fastboot")) { Log("已进入 fastboot", "#3CB878"); await RefreshDevice(); return; }
                await Task.Delay(2000);
            }
        }

        // ═══ 扫 描 ═══
        async void BtnScan_Click(object s, RoutedEventArgs e)
        {
            Images.Clear();
            if (!Directory.Exists(_imageDir)) { Log($"目录不存在: {_imageDir}", "#F0A020"); return; }

            IcImages.Visibility = Visibility.Collapsed;
            DgImages.Visibility = Visibility.Visible;

            foreach (var p in Directory.GetFiles(_imageDir, "*.img").OrderBy(p => p))
            {
                var fn = Path.GetFileName(p);
                var pt = Path.GetFileNameWithoutExtension(fn);
                Images.Add(new ImageItem { FileName = fn, Partition = pt, FullPath = p });
            }
            CardImgCount.Text = $"{Images.Count} files";
            Log($"{Images.Count} 个映像", "#3CB878");
        }

        async void BtnBrowse_Click(object s, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择固件文件夹（将自动扫描 images 子目录或 .img 文件）" };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var dir = dlg.SelectedPath;
            // 优先找子目录 images/
            var imgDir = Directory.Exists(Path.Combine(dir, "images")) ? Path.Combine(dir, "images") : dir;
            Log($"浏览: {imgDir}", "#FACC15");

            Images.Clear();
            IcImages.Visibility = Visibility.Visible;
            DgImages.Visibility = Visibility.Collapsed;

            var files = Directory.GetFiles(imgDir, "*.img", SearchOption.TopDirectoryOnly);
            foreach (var p in files.OrderBy(p => p))
            {
                var fn = Path.GetFileName(p);
                var pt = Path.GetFileNameWithoutExtension(fn);
                Images.Add(new ImageItem { FileName = fn, Partition = pt, FullPath = p });
            }
            CardImgCount.Text = $"{Images.Count} files";
            Log($"找到 {Images.Count} 个映像", "#3CB878");
        }

        // ═══ 刷 写 ═══
        async void BtnFlashSel_Click(object s, RoutedEventArgs e) { await Safe(() => FlashSel()); }
        async void BtnFlashAll_Click(object s, RoutedEventArgs e) { await Safe(() => FlashAll()); }

        async Task FlashSel()
        {
            var l = Images.Where(i => i.IsChecked).ToList();
            if (l.Count == 0) { Log("请先勾选分区", "#F0A020"); return; }
            if (!Ask($"刷写 {l.Count} 个分区？")) return;
            await Flash(l);
        }

        async Task FlashAll()
        {
            foreach (var img in Images) img.IsChecked = true;
            var l = Images.ToList();
            if (l.Count == 0) { Log("没有可刷写的映像", "#F0A020"); return; }
            if (!Ask($"全刷 {l.Count} 个分区？")) return;
            await Flash(l);
        }

        async Task Flash(List<ImageItem> items)
        {
            if (_isFlashing) { Log("任务进行中", "#F0A020"); return; }
            _isFlashing = true; Busy(true);
            try
            {
                int n = 0, err = 0;
                var md = items.Where(i => string.Equals(i.Partition, "modem", StringComparison.OrdinalIgnoreCase)).ToList();
                var logical = items.Where(i => IsLogicalPartition(i.Partition)).ToList();
                var regular = items.Except(md).Except(logical).ToList();

                // 1. 普通分区
                foreach (var it in regular)
                {
                    if (!File.Exists(it.FullPath)) { Log($"跳过 {it.FileName}", "#F0A020"); err++; continue; }
                    Log($"── [{++n}/{items.Count}] 刷写 {it.FileName}", "#FACC15");
                    var (ok, o) = await Fb($"flash {it.Partition} \"{it.FullPath}\"", true);
                    if (!ok) { err++; Log(o, "#F06055"); } else Log($"{it.FileName} ✓", "#3CB878");
                }

                // 2. 逻辑分区
                if (logical.Count > 0)
                {
                    Log("处理逻辑分区…", "#9B7EC4");
                    // 确保在 fastbootd 模式
                    var isFbd = await FbOut("getvar is-userspace");
                    if (!(isFbd ?? "").Contains("yes"))
                    {
                        Log("需要 fastbootd 模式", "#F0A020");
                        if (Ask("重启到 fastbootd 模式？"))
                        {
                            await Fb("reboot fastboot");
                            await Task.Delay(5000);
                            for (int w = 0; w < 30; w++) { var dv = await FbOut("devices"); if ((dv ?? "").Contains("fastboot")) break; await Task.Delay(2000); }
                        }
                        else { Log("跳过逻辑分区", "#F0A020"); err += logical.Count; goto modemPhase; }
                    }

                    // 删除旧逻辑分区
                    foreach (var it in logical)
                    {
                        foreach (var slot in new[] { "a", "b" })
                        {
                            var name = $"{it.Partition}_{slot}";
                            Log($"删除 {name}", "#6A6A74");
                            await Fb($"delete-logical-partition {name}");
                            await Fb($"delete-logical-partition {name}-cow");
                        }
                    }

                    // 创建新逻辑分区
                    foreach (var it in logical)
                    {
                        foreach (var slot in new[] { "a", "b" })
                        {
                            var name = $"{it.Partition}_{slot}";
                            Log($"创建 {name}", "#6A6A74");
                            var (cok, _) = await Fb($"create-logical-partition {name} 335872");
                            if (!cok) Log($"创建 {name} 失败", "#F06055");
                        }
                    }

                    // 刷写逻辑分区
                    foreach (var it in logical)
                    {
                        if (!File.Exists(it.FullPath)) { Log($"跳过 {it.FileName}", "#F0A020"); err++; continue; }
                        Log($"── [{++n}/{items.Count}] 刷写 {it.FileName}", "#FACC15");
                        var (ok, o) = await Fb($"flash {it.Partition} \"{it.FullPath}\"", true);
                        if (!ok) { err++; Log(o, "#F06055"); } else Log($"{it.FileName} ✓", "#3CB878");
                    }
                }

            modemPhase:
                // 3. Modem
                foreach (var it in md)
                {
                    if (!File.Exists(it.FullPath)) { err++; continue; }
                    var fbd = await FbOut("getvar is-userspace");
                    if ((fbd ?? "").Contains("yes"))
                    {
                        Log("modem 需 bootloader", "#F0A020");
                        if (Ask("重启到 bootloader 刷写 modem？"))
                        {
                            Log("→ bootloader…", "#FACC15");
                            await Fb("reboot bootloader");
                            await Task.Delay(5000);
                            for (int w = 0; w < 30; w++) { var dv = await FbOut("devices"); if ((dv ?? "").Contains("fastboot")) break; await Task.Delay(2000); }
                        }
                        else { Log("跳过 modem", "#F0A020"); continue; }
                    }
                    Log($"── [{++n}/{items.Count}] 刷写 {it.FileName}", "#FACC15");
                    var (mok, mo) = await Fb($"flash modem \"{it.FullPath}\"", true);
                    if (!mok) { err++; Log(mo, "#F06055"); } else Log($"{it.FileName} ✓", "#3CB878");
                }
                Log($"══════ {n - err}/{n} 成功, {err} 失败 ══════", err == 0 ? "#3CB878" : "#F06055");
                if (ChkWipeAfter.IsChecked == true && err == 0) BtnWipeData_Click(null!, null!);
            }
            catch (Exception ex) { Log($"异常: {ex.Message}", "#F06055"); }
            finally { Busy(false); _isFlashing = false; }
        }

        static bool IsLogicalPartition(string name)
        {
            foreach (var p in new[] { "^system$", "^system_ext$", "^vendor$", "^product$", "^odm$", "^my_" })
                if (System.Text.RegularExpressions.Regex.IsMatch(name, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            return false;
        }

        async void BtnWipeData_Click(object s, RoutedEventArgs e) { await Safe(WipeData); }
        async Task WipeData()
        {
            if (!Ask("确认清除？")) return;
            Busy(true);
            try { var (ok, o) = await Fb("-w", true); Log(ok ? "已清除" : o, ok ? "#3CB878" : "#F06055"); }
            catch { }
            finally { Busy(false); }
        }

        // ═══ 重启 / Lock / Erase ═══
        async void BtnRebootSys_Click(object s, RoutedEventArgs e) => await Safe(() => Reboot("", "系统"));
        async void BtnRebootBL_Click(object s, RoutedEventArgs e) => await Safe(() => Reboot("bootloader", "Bootloader"));
        async void BtnRebootRec_Click(object s, RoutedEventArgs e) => await Safe(() => Reboot("recovery", "Recovery"));
        async void BtnRebootFBD_Click(object s, RoutedEventArgs e) => await Safe(() => Reboot("fastboot", "Fastbootd"));
        async void BtnPowerOff_Click(object s, RoutedEventArgs e) => await Safe(() => Reboot("poweroff", "关机"));
        async Task Reboot(string t, string d)
        {
            // ADB 模式用 adb 命令
            if (_adbMode)
            {
                var target = string.IsNullOrEmpty(t) ? "" : t == "fastboot" ? "bootloader" : t;
                Log($"adb reboot {target}…", "#9B7EC4");
                var adbPath = FastbootHelper.FindAdbExecutable(_baseDir);
                if (!string.IsNullOrEmpty(adbPath))
                {
                    await FastbootHelper.RunAdb(adbPath, $"reboot {target}".Trim());
                    Log($"已发送: {d}", "#3CB878");
                    return;
                }
            }

            if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; }
            var (ok, o) = await Fb($"reboot {t}".Trim()); Log(ok ? $"重启 → {d}" : o, ok ? "#3CB878" : "#F06055");
        }
        async void BtnUnlock_Click(object s, RoutedEventArgs e) => await Safe(Unlock);
        async Task Unlock() { if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; } if (!Ask("解锁会清除数据！")) return; var (ok, o) = await Fb("flashing unlock", true); Log(ok ? "已发送解锁" : o, ok ? "#3CB878" : "#F06055"); }
        async void BtnLock_Click(object s, RoutedEventArgs e) => await Safe(Lock);
        async Task Lock() { if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; } if (!Ask("确认上锁？")) return; var (ok, o) = await Fb("flashing lock", true); Log(ok ? "已发送上锁" : o, ok ? "#3CB878" : "#F06055"); }
        async void BtnSlotA_Click(object s, RoutedEventArgs e) => await Safe(() => SetActive("a"));
        async void BtnSlotB_Click(object s, RoutedEventArgs e) => await Safe(() => SetActive("b"));
        async Task SetActive(string sl) { if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; } var (ok, o) = await Fb($"--set-active={sl}"); if (ok) { _currentSlot = sl; LblSlot.Text = $"Slot {sl}"; } Log(ok ? $"Slot → {sl.ToUpper()}" : o, ok ? "#3CB878" : "#F06055"); }
        async void BtnErase_Click(object s, RoutedEventArgs e) => await Safe(Erase);
        async Task Erase() { if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; } var p = TxtErase.Text.Trim(); if (p.Length == 0) return; if (!Ask($"擦除 {p}？")) return; var (ok, o) = await Fb($"erase {p}", true); Log(ok ? $"{p} 已擦除" : o, ok ? "#3CB878" : "#F06055"); }
        async void BtnWipeCache_Click(object s, RoutedEventArgs e) => await Safe(WipeCache);
        async Task WipeCache() { if (string.IsNullOrEmpty(_fastbootPath)) { Log("请先检测 fastboot", "#F0A020"); return; } if (!Ask("清除 Cache？")) return; var (ok, o) = await Fb("erase cache", true); Log(ok ? "Cache 已清除" : o, ok ? "#3CB878" : "#F06055"); }

        // ═══ 工具 ═══
        static string? _scrcpyDir;
        void BtnScrcpy_Click(object s, RoutedEventArgs e)
        {
            if (_scrcpyDir == null)
            {
                _scrcpyDir = Path.Combine(Path.GetTempPath(), "FlashTool_scrcpy");
                Directory.CreateDirectory(_scrcpyDir);

                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var prefix = "FastbootFlasherGUI.Tools.scrcpy.";

                // 调试: 列出自检
                var names = asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix)).ToList();
                Log($"scrcpy 嵌入 {names.Count} 个文件", "#A0A0A8");

                foreach (var name in names)
                {
                    if (!name.StartsWith(prefix)) continue;
                    var rel = name.Substring(prefix.Length);
                    var dest = Path.Combine(_scrcpyDir, rel);
                    // 已存在且大小>0则跳过
                    if (File.Exists(dest) && new FileInfo(dest).Length > 0) continue;
                    using var src = asm.GetManifestResourceStream(name);
                    if (src == null) continue;
                    using var fs = File.Create(dest);
                    src.CopyTo(fs);
                }

                // 验证关键文件
                var scrcpyExe = Path.Combine(_scrcpyDir, "scrcpy.exe");
                if (!File.Exists(scrcpyExe))
                {
                    // 重试一次
                    foreach (var name in asm.GetManifestResourceNames())
                    {
                        if (!name.StartsWith(prefix)) continue;
                        var rel = name.Substring(prefix.Length);
                        using var src = asm.GetManifestResourceStream(name);
                        if (src == null) continue;
                        using var fs = File.Create(Path.Combine(_scrcpyDir, rel));
                        src.CopyTo(fs);
                    }
                }
                Log("scrcpy 就绪", File.Exists(scrcpyExe) ? "#3CB878" : "#F06055");
            }

            var exe = Path.Combine(_scrcpyDir, "scrcpy.exe");
            if (!File.Exists(exe)) { Log("scrcpy.exe 缺失，请重试", "#F06055"); return; }
            Log("启动 scrcpy…", "#FACC15");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    WorkingDirectory = _scrcpyDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log($"启动失败: {ex.Message}", "#F06055"); }
        }

        static string? _payloadExe;
        void BtnPayload_Click(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择 payload.bin 或 zip 固件包", Filter = "Payload/固件|*.bin;*.zip|All|*.*" };
            if (dlg.ShowDialog() != true) return;

            // 选择输出目录
            var outDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择解包输出目录" };
            if (outDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var outDir = outDlg.SelectedPath;

            var payloadFile = dlg.FileName;

            // 如果是 zip，先解压到临时目录
            if (dlg.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tmpDir = Path.Combine(Path.GetTempPath(), "FlashTool_payload_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tmpDir);
                Log($"解压 zip: {Path.GetFileName(dlg.FileName)}", "#FACC15");
                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, tmpDir);
                    // 找 payload.bin
                    payloadFile = Directory.GetFiles(tmpDir, "payload.bin", SearchOption.AllDirectories).FirstOrDefault() ?? "";
                    if (string.IsNullOrEmpty(payloadFile))
                    {
                        Log("zip 中未找到 payload.bin", "#E0554A");
                        try { Directory.Delete(tmpDir, true); } catch { }
                        return;
                    }
                    Log($"找到 payload.bin: {Path.GetFileName(Path.GetDirectoryName(payloadFile))}/{Path.GetFileName(payloadFile)}", "#3CB878");
                }
                catch (Exception ex) { Log($"解压失败: {ex.Message}", "#E0554A"); return; }
            }

            if (_payloadExe == null)
            {
                _payloadExe = Path.Combine(Path.GetTempPath(), "payload-dumper-go.exe");
                if (!File.Exists(_payloadExe))
                {
                    using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FastbootFlasherGUI.Tools.payload-dumper-go.exe");
                    if (stream == null) { Log("payload-dumper-go 未嵌入", "#E0554A"); return; }
                    using var fs = File.Create(_payloadExe);
                    stream.CopyTo(fs);
                    Log("已释放 payload-dumper-go", "#3CB878");
                }
            }

            Log($"解包: {Path.GetFileName(payloadFile)} → {outDir}", "#FACC15");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_payloadExe, $"\"{payloadFile}\" -o \"{outDir}\"") { UseShellExecute = true });
                Log("解包已启动", "#3CB878");
            }
            catch (Exception ex) { Log($"失败: {ex.Message}", "#E0554A"); }
        }

        static string ExtractAdbVal(string output, string key)
        {
            if (string.IsNullOrEmpty(output)) return "";
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return t.Substring(key.Length).Trim();
            }
            return "";
        }

        static string ExtractMeminfo(string output, string key)
        {
            if (string.IsNullOrEmpty(output)) return "";
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = t.Split(new[] { ':' }, 2);
                    if (parts.Length > 1)
                    {
                        var val = parts[1].Trim();
                        if (val.EndsWith(" kB") && long.TryParse(val.Replace(" kB", ""), out var kb))
                        {
                            if (kb >= 1024 * 1024) return $"{kb / 1024 / 1024} GB";
                            return $"{kb / 1024} MB";
                        }
                        return val;
                    }
                }
            }
            return "";
        }

        static string ExtractDfSize(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            var lines = output.Split('\n');
            if (lines.Length > 1)
            {
                var parts = lines[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return parts[1];
            }
            return "";
        }

        // ═══ 日志 ═══
        void BtnClearLog_Click(object s, RoutedEventArgs e) => RtbLog.Document.Blocks.Clear();
        void Log(string msg, string hex)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                RtbLog.Document.Blocks.Add(new Paragraph(new Run(msg)) { Foreground = new SolidColorBrush(c), Margin = new Thickness(0), Padding = new Thickness(0), LineHeight = 2 });
                RtbLog.ScrollToEnd();
            });
            ConsoleWriter.LogRaw(msg);
        }
        void Busy(bool on)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Progress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                LblProgress.Text = on ? "操作中…" : "就绪";
                BtnFlashSel.IsEnabled = !on; BtnFlashAll.IsEnabled = !on; BtnWipeData.IsEnabled = !on;
            });
        }
        void AddInfoRow(string label, string value)
        {
            var bg = TryFindResource("SurfaceBg") as Brush ?? new SolidColorBrush(Colors.Transparent);
            var fg1 = TryFindResource("Text3") as Brush ?? new SolidColorBrush(Colors.Gray);
            var fg2 = TryFindResource("Text1") as Brush ?? new SolidColorBrush(Colors.Black);
            var item = new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(3)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Foreground = fg1, FontSize = 10.5 });
            sp.Children.Add(new TextBlock { Text = value, Foreground = fg2, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
            item.Child = sp;
            GridDeviceInfo.Children.Add(item);
        }

        bool Ask(string msg) => MessageBox.Show(msg, "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        async Task Safe(Func<Task> action) { try { await action(); } catch (Exception ex) { Log($"错误: {ex.Message}", "#F06055"); } }

        void Hyperlink_RequestNavigate(object s, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        void LinkGroup1_Click(object s, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://qun.qq.com/universal-share/share?ac=1&authKey=xJt7n4OfE6k8Li276URCkJZYcjEAKDfNAu10yXklUSUVhbH8qWgwxR3RylD8EiaC&busi_data=eyJncm91cENvZGUiOiI0Nzg1Mzg1MzkiLCJ0b2tlbiI6Ii9NVkpoUXpXOGp5ZTNoSEJYakFRelBKMG1WcEVWTXRhU0pNamJoTnY2ZXVOSWNGUk9qbHRYZ0NPYmhLditFNnAiLCJ1aW4iOiIzMTk5ODM0NjAifQ%3D%3D&data=iBeWjEr4StrLxquMnx2AJTlTc4EpEdBAzsZM0K1rXSUOJd4d0nmn7NKz8PABAcd0fCQ247NrgBblGvML3YHqNQ&svctype=4&tempid=h5_group_info") { UseShellExecute = true });
            e.Handled = true;
        }
        void LinkGroup2_Click(object s, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://qun.qq.com/universal-share/share?ac=1&authKey=KVZ6PpmFc7k32TUPClUBvOaAU5xb2VdHvLLE8SKEvbmBE7BUVI9O3XEh3ABXEhQe&busi_data=eyJncm91cENvZGUiOiIxMTAxMTk5NDA1IiwidG9rZW4iOiIzWjlmeXNiaFFQT2lmS01ESkMxSkFXSUd2MmRzTVF1U0d3VGRZZko4bnJNOXhyKzlBRyt5anVxSHUzT2J2d2NqIiwidWluIjoiMzE5OTgzNDYwIn0%3D&data=A0ozFlLYjhX0LMwUfYlMRNzXw1x-bsfSdykt57ZiAieju7xq4_JA6thrayFulOtqAPpphPNKKRIfpUCNMK7r0A&svctype=4&tempid=h5_group_info") { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
