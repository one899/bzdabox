using System;
using System.IO;
using System.Threading.Tasks;

namespace FastbootFlasher
{
    public static class Simulator
    {
        public static bool Enabled { get; set; }
        public static string Mode { get; private set; } = "fastbootd"; // bootloader | fastbootd | adb | disconnected
        public static string CurrentSlot { get; private set; } = "a";

        public static void Toggle() => Enabled = !Enabled;

        public static string SimGetFastbootPath(string baseDir) => Path.Combine(baseDir, "simulator_fastboot.exe");
        public static string SimGetAdbPath(string baseDir) => Path.Combine(baseDir, "simulator_adb.exe");

        public static Task<string> SimFastbootOutput(string args)
        {
            if (Mode == "disconnected") return Task.FromResult("");
            if (args.StartsWith("devices"))
                return Task.FromResult($"SIM001\tfastboot");
            if (args.StartsWith("getvar is-userspace"))
                return Task.FromResult($"is-userspace: {(Mode == "fastbootd" ? "yes" : "no")}");
            if (args.StartsWith("getvar current-slot"))
                return Task.FromResult($"current-slot: {CurrentSlot}");
            if (args.StartsWith("getvar unlocked"))
                return Task.FromResult("unlocked: yes");
            if (args.StartsWith("getvar all"))
                return Task.FromResult("(bootloader) product: sim\n(bootloader) current-slot: " + CurrentSlot + "\n(bootloader) is-userspace: " + (Mode == "fastbootd" ? "yes" : "no"));
            return Task.FromResult("OKAY");
        }

        public static Task<(bool, string)> SimFastboot(string args)
        {
            if (args.StartsWith("reboot fastboot"))
            {
                Mode = "fastbootd";
                return Task.FromResult((true, "模拟: 已重启到 fastbootd"));
            }
            if (args.StartsWith("reboot bootloader"))
            {
                Mode = "bootloader";
                return Task.FromResult((true, "模拟: 已重启到 bootloader"));
            }
            if (args.StartsWith("reboot"))
            {
                Mode = "disconnected";
                return Task.FromResult((true, "模拟: 已重启"));
            }
            if (args.StartsWith("flash"))
            {
                System.Threading.Thread.Sleep(300); // 模拟写入延迟
                return Task.FromResult((true, "模拟: 刷写成功"));
            }
            if (args.StartsWith("--set-active="))
            {
                var slot = args.Substring("--set-active=".Length);
                if (slot == "a" || slot == "b") CurrentSlot = slot;
                return Task.FromResult((true, "OKAY"));
            }
            if (args == "-w" || args.StartsWith("erase"))
                return Task.FromResult((true, "模拟: 操作成功"));
            return Task.FromResult((true, "OKAY"));
        }

        public static Task<string> SimAdbOutput(string args)
        {
            if (Mode == "disconnected" || Mode == "bootloader" || Mode == "fastbootd") return Task.FromResult("");
            if (args.StartsWith("devices"))
                return Task.FromResult("SIM001\tdevice");
            return Task.FromResult("OKAY");
        }

        public static Task<(bool, string)> SimAdb(string args)
        {
            if (args.StartsWith("reboot bootloader"))
            {
                Mode = "bootloader";
                return Task.FromResult((true, "模拟: 已重启到 bootloader"));
            }
            return Task.FromResult((true, "OKAY"));
        }
    }
}
