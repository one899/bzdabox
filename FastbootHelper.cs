using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastbootFlasher
{
    public static class FastbootHelper
    {
        private static string? _toolsDir;
        private static bool _toolsExtracted;

        static void EnsureTools()
        {
            if (_toolsExtracted) return;
            _toolsExtracted = true;
            _toolsDir = Path.Combine(Path.GetTempPath(), "FlashTool_platform-tools");

            try
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm == null) return;

                var prefix = "FastbootFlasherGUI.Tools.adb.";
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.StartsWith(prefix)) continue;
                    var rel = name.Substring(prefix.Length);
                    var dest = Path.Combine(_toolsDir, rel);
                    if (File.Exists(dest) && new FileInfo(dest).Length > 0) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var src = asm.GetManifestResourceStream(name);
                    if (src == null) continue;
                    using var fs = File.Create(dest);
                    src.CopyTo(fs);
                }
                ConsoleWriter.LogRaw($"platform-tools 已释放到 {_toolsDir}");
            }
            catch { _toolsDir = null; }
        }

        public static string FindFastbootExecutable(string baseDir)
        {
            EnsureTools();

            if (!string.IsNullOrEmpty(_toolsDir))
            {
                var t = Path.Combine(_toolsDir, "fastboot.exe");
                if (File.Exists(t)) { ConsoleWriter.LogRaw($"使用内置 fastboot: {t}"); return t; }
            }

            var candidates = new List<string>
            {
                Path.Combine(baseDir, "fastboot.exe"),
                Path.Combine(baseDir, "platform-tools", "windows", "fastboot.exe"),
                Path.Combine(baseDir, "platform-tools", "fastboot.exe"),
            };

            foreach (var envName in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT", "ANDROID_SDK_HOME" })
            {
                var envVal = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrEmpty(envVal))
                    candidates.Add(Path.Combine(envVal, "platform-tools", "fastboot.exe"));
            }

            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "fastboot.exe"));

            // Program Files
            foreach (var progDir in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (!string.IsNullOrEmpty(progDir))
                    candidates.Add(Path.Combine(progDir, "platform-tools", "fastboot.exe"));
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    ConsoleWriter.LogRaw($"找到 fastboot: {candidate}");
                    return candidate;
                }
            }

            // PATH
            try
            {
                using var p = Process.Start(new ProcessStartInfo("where", "fastboot.exe")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                });
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && File.Exists(lines[0]))
                {
                    ConsoleWriter.LogRaw($"从 PATH 找到 fastboot: {lines[0]}");
                    return lines[0];
                }
            }
            catch { }

            return null;
        }

        // ═══ Fastboot 执行 ══════════════════════════════

        public static async Task<(bool Success, string Output)> RunFastboot(
            string fastbootPath, string args, bool showOutput = false, int timeoutMs = 300000)
        {
            return await RunProcess(fastbootPath, args, showOutput, timeoutMs);
        }

        public static async Task<string> RunFastbootOutput(
            string fastbootPath, string args, int timeoutMs = 30000)
        {
            return await RunProcessOutput(fastbootPath, args, timeoutMs);
        }

        public static string ExtractVarValue(string output, string varName)
        {
            if (string.IsNullOrEmpty(output)) return null;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("(bootloader) ", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(13).Trim();
                var prefix = $"{varName}:";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(prefix.Length).Trim();
            }
            return null;
        }

        // ═══ ADB ════════════════════════════════════════

        public static string FindAdbExecutable(string baseDir)
        {
            EnsureTools();

            if (!string.IsNullOrEmpty(_toolsDir))
            {
                var t = Path.Combine(_toolsDir, "adb.exe");
                if (File.Exists(t)) { ConsoleWriter.LogRaw($"使用内置 adb: {t}"); return t; }
            }

            var candidates = new List<string>
            {
                Path.Combine(baseDir, "adb.exe"),
                Path.Combine(baseDir, "platform-tools", "windows", "adb.exe"),
                Path.Combine(baseDir, "platform-tools", "adb.exe"),
            };

            foreach (var envName in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT", "ANDROID_SDK_HOME" })
            {
                var v = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrEmpty(v))
                    candidates.Add(Path.Combine(v, "platform-tools", "adb.exe"));
            }

            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(local))
                candidates.Add(Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"));

            foreach (var c in candidates)
                if (File.Exists(c)) { ConsoleWriter.LogRaw($"找到 adb: {c}"); return c; }

            try
            {
                using var p = Process.Start(new ProcessStartInfo("where", "adb.exe")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                });
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && File.Exists(lines[0]))
                {
                    ConsoleWriter.LogRaw($"从 PATH 找到 adb: {lines[0]}");
                    return lines[0];
                }
            }
            catch { }

            return null;
        }

        public static async Task<string> RunAdbOutput(string adbPath, string args, int timeoutMs = 30000)
            => await RunProcessOutput(adbPath, args, timeoutMs);

        public static async Task<(bool Success, string Output)> RunAdb(
            string adbPath, string args, int timeoutMs = 30000)
            => await RunProcess(adbPath, args, false, timeoutMs);

        // ═══ 内核：真正异步的进程执行 ════════════════════

        static async Task<(bool Success, string Output)> RunProcess(
            string exePath, string args, bool showOutput, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        SafeWriteLine(e.Data, showOutput);
                        ConsoleWriter.LogRaw(e.Data, "CMD");
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        SafeWriteLine(e.Data, showOutput);
                        ConsoleWriter.LogRaw(e.Data, "CMD_ERR");
                    }
                };

                var exeName = Path.GetFileNameWithoutExtension(exePath);
                ConsoleWriter.LogRaw($"执行: {exeName} {args}", "CMD");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 真正异步等待，不阻塞 UI 线程
                var exited = await Task.Run(() => process.WaitForExit(timeoutMs));
                if (exited)
                {
                    await Task.Delay(30);
                    var output = outputBuilder.ToString().Trim();
                    ConsoleWriter.LogRaw($"{exeName} 返回码: {process.ExitCode}", "CMD");
                    return (process.ExitCode == 0, output);
                }
                else
                {
                    try { process.Kill(); } catch { }
                    ConsoleWriter.LogRaw($"{exeName} 命令超时", "ERROR");
                    return (false, "命令执行超时");
                }
            }
            catch (Exception ex)
            {
                ConsoleWriter.LogRaw($"进程错误: {ex.Message}", "ERROR");
                return (false, ex.Message);
            }
        }

        static async Task<string> RunProcessOutput(
            string exePath, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                var sb = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = await Task.Run(() => process.WaitForExit(timeoutMs));
                if (exited)
                {
                    await Task.Delay(20);
                    return sb.ToString().Trim();
                }
                else
                    try { process.Kill(); } catch { }

                return null;
            }
            catch { return null; }
        }

        static void SafeWriteLine(string text, bool enabled)
        {
            if (!enabled) return;
            try { Console.WriteLine(text); } catch { }
        }
    }
}
