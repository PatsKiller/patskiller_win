using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// File-based logging utility
    /// Logs are stored in %APPDATA%\PatsKiller Pro\logs\
    /// SECURITY: All logs are automatically redacted via SecretRedactor
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static string _logFolder = "";
        private static string _currentLogFile = "";
        private static bool _initialized = false;

        // Log levels
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// Minimum log level to write to file
        /// </summary>
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Gets the application version from assembly
        /// </summary>
        private static string GetVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "2.0.0";
            }
            catch
            {
                return "2.0.0";
            }
        }

        /// <summary>
        /// Initializes the logger
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Create log folder: %APPDATA%\PatsKiller Pro\logs\
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _logFolder = Path.Combine(appData, "PatsKiller Pro", "logs");
                
                if (!Directory.Exists(_logFolder))
                {
                    Directory.CreateDirectory(_logFolder);
                }

                // Create log file with date
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
                _currentLogFile = Path.Combine(_logFolder, $"PatsKillerPro_{timestamp}.log");

                _initialized = true;

                // Write startup header
                WriteToFile($"\n{'=',-60}");
                WriteToFile($"PatsKiller Pro v{GetVersion()} - Session Started");
                WriteToFile($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteToFile($"OS: {Environment.OSVersion}");
                WriteToFile($"{'=',-60}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the log folder path
        /// </summary>
        public static string GetLogFolder()
        {
            if (string.IsNullOrEmpty(_logFolder))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _logFolder = Path.Combine(appData, "PatsKiller Pro", "logs");
            }
            return _logFolder;
        }

        /// <summary>
        /// Logs a debug message
        /// </summary>
        public static void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        /// <summary>
        /// Logs an info message
        /// </summary>
        public static void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        public static void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        /// <summary>
        /// Logs an error message
        /// </summary>
        public static void Error(string message, Exception? ex = null)
        {
            var fullMessage = message;
            if (ex != null)
            {
                fullMessage += $"\n  Exception: {ex.GetType().Name}: {ex.Message}";
                if (ex.StackTrace != null)
                {
                    fullMessage += $"\n  Stack: {ex.StackTrace}";
                }
                if (ex.InnerException != null)
                {
                    fullMessage += $"\n  Inner: {ex.InnerException.Message}";
                }
            }
            Log(LogLevel.Error, fullMessage);
        }

        /// <summary>
        /// Logs a message with specified level
        /// </summary>
        public static void Log(LogLevel level, string message)
        {
            if (level < MinimumLevel) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelStr = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "?    "
            };

            var logLine = $"[{timestamp}] [{levelStr}] {message}";
            WriteToFile(logLine);
        }

        /// <summary>
        /// Writes a line to the log file
        /// </summary>
        private static void WriteToFile(string line)
        {
            if (!_initialized)
            {
                Initialize();
            }

            // SECURITY: Redact any incodes/outcodes before persisting to disk
            var safeLine = SecretRedactor.Redact(line);

            lock (_lock)
            {
                try
                {
                    using var writer = new StreamWriter(_currentLogFile, true, Encoding.UTF8);
                    writer.WriteLine(safeLine);
                }
                catch
                {
                    // Silently fail if we can't write to log
                }
            }
        }

        /// <summary>
        /// Cleans up old log files (older than 30 days)
        /// </summary>
        public static void CleanupOldLogs(int daysToKeep = 30)
        {
            try
            {
                if (!Directory.Exists(_logFolder)) return;

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logFiles = Directory.GetFiles(_logFolder, "*.log");

                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try
                        {
                            File.Delete(file);
                            Info($"Deleted old log file: {Path.GetFileName(file)}");
                        }
                        catch
                        {
                            // Ignore if we can't delete
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Warning($"Error cleaning up old logs: {ex.Message}");
            }
        }
    }
}
