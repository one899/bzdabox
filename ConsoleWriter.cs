using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FastbootFlasher
{
    public static class ConsoleWriter
    {
        private static readonly Dictionary<string, ConsoleColor> _colorMap = new Dictionary<string, ConsoleColor>
        {
            ["{red}"] = ConsoleColor.Red,
            ["{green}"] = ConsoleColor.Green,
            ["{blue}"] = ConsoleColor.Blue,
            ["{cyan}"] = ConsoleColor.Cyan,
            ["{yellow}"] = ConsoleColor.Yellow,
            ["{magenta}"] = ConsoleColor.Magenta,
            ["{gray}"] = ConsoleColor.Gray,
            ["{white}"] = ConsoleColor.White,
            ["{darkred}"] = ConsoleColor.DarkRed,
            ["{darkgreen}"] = ConsoleColor.DarkGreen,
            ["{darkblue}"] = ConsoleColor.DarkBlue,
            ["{darkcyan}"] = ConsoleColor.DarkCyan,
            ["{darkyellow}"] = ConsoleColor.DarkYellow,
            ["{darkmagenta}"] = ConsoleColor.DarkMagenta,
            ["{darkgray}"] = ConsoleColor.DarkGray,
        };

        private static string _logFile;
        private static readonly object _logLock = new object();
        private static bool? _hasConsole;

        static bool HasConsole
        {
            get
            {
                if (_hasConsole == null)
                {
                    try { var _ = Console.Out; _hasConsole = true; }
                    catch { _hasConsole = false; }
                }
                return _hasConsole.Value;
            }
        }

        public static void InitLog(string logDir)
        {
            try
            {
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
            }
            catch { return; }

            _logFile = Path.Combine(logDir, $"flash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            LogRaw("========== 工具启动 ==========");
        }

        public static void LogRaw(string message, string level = "INFO")
        {
            if (_logFile == null) return;
            lock (_logLock)
            {
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        public static void Write(string text, ConsoleColor defaultColor = ConsoleColor.Gray)
        {
            if (!HasConsole) { LogRaw(StripTags(text)); return; }

            try
            {
                var parts = System.Text.RegularExpressions.Regex.Split(text, @"(\{[^}]+\})");
                var currentColor = defaultColor;
                var plainText = new StringBuilder();

                foreach (var part in parts)
                {
                    if (_colorMap.TryGetValue(part, out var color))
                        currentColor = color;
                    else if (part == "{end}")
                        currentColor = defaultColor;
                    else if (!string.IsNullOrEmpty(part))
                    {
                        plainText.Append(part);
                        Console.ForegroundColor = currentColor;
                        Console.Write(part);
                    }
                }
                Console.ResetColor();
                LogRaw(plainText.ToString());
            }
            catch { LogRaw(StripTags(text)); }
        }

        public static void WriteLine(string text, ConsoleColor defaultColor = ConsoleColor.Gray)
        {
            Write(text + Environment.NewLine, defaultColor);
        }

        public static void WriteLine() { if (HasConsole) try { Console.WriteLine(); } catch { } }

        public static void WriteRow(string prefix, string message, ConsoleColor prefixColor = ConsoleColor.Cyan, ConsoleColor msgColor = ConsoleColor.Gray)
        {
            if (!HasConsole) { LogRaw(prefix + message); return; }
            try
            {
                Console.ForegroundColor = prefixColor;
                Console.Write(prefix);
                Console.ForegroundColor = msgColor;
                Console.WriteLine(message);
                Console.ResetColor();
                LogRaw(prefix + message);
            }
            catch { LogRaw(prefix + message); }
        }

        public static void WriteLineLocalized(Dictionary<string, string> texts, string key,
            string[] variables = null, ConsoleColor defaultColor = ConsoleColor.Gray, string level = "INFO")
        {
            if (!texts.TryGetValue(key, out var text))
            {
                text = $"未知文本键: {key}";
            }

            if (variables != null)
            {
                for (int i = 0; i < variables.Length; i++)
                {
                    text = text.ReplaceFirst("{}", variables[i]);
                }
            }

            WriteLine(text, defaultColor);
        }

        public static string ReadLineLocalized(Dictionary<string, string> texts, string key,
            string[] variables = null, ConsoleColor defaultColor = ConsoleColor.Gray)
        {
            WriteLineLocalized(texts, key, variables, defaultColor);
            var input = Console.ReadLine();
            LogRaw($"用户输入: {input}", "INPUT");
            return input;
        }

        public static void ExitWithDelay(Dictionary<string, string> texts, string key = null,
            string[] variables = null, int delay = 5)
        {
            if (key != null)
                WriteLineLocalized(texts, key, variables, ConsoleColor.Red, "ERROR");

            try
            {
                WriteLine();
                for (int i = delay; i > 0; i--)
                {
                    Console.Write($"脚本将在 {i} 秒后退出... ");
                    StartSleep(1);
                    Console.Write(new string(' ', 20) + "\r");
                }
                WriteLine();
            }
            catch { }

            LogRaw("脚本异常退出", "ERROR");
            Environment.Exit(1);
        }

        static string StripTags(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"\{[^}]+\}", "");
        }

        public static bool Confirm(Dictionary<string, string> texts, string key)
        {
            while (true)
            {
                WriteLineLocalized(texts, key, defaultColor: ConsoleColor.DarkYellow);
                Console.Write(" (是[y]/否[n]): ");
                var response = Console.ReadLine()?.ToLower();
                LogRaw($"用户输入: {response}", "INPUT");

                if (response == "y" || response == "yes" || response == "是")
                    return true;
                if (response == "n" || response == "no" || response == "否")
                    return false;

                WriteLineLocalized(texts, "user_input_invalid", defaultColor: ConsoleColor.Yellow);
            }
        }

        public static void StartSleep(int seconds)
        {
            for (int i = 0; i < seconds * 10; i++)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    public static class StringExtensions
    {
        public static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0) return text;
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }
    }
}
