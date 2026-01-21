using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// User settings management
    /// Settings are stored in %APPDATA%\PatsKiller Pro\settings.json
    /// </summary>
    public static class Settings
    {
        private static readonly object _lock = new();
        private static Dictionary<string, object> _settings = new();
        private static string _settingsFile = "";
        private static bool _loaded = false;

        /// <summary>
        /// Gets the settings file path
        /// </summary>
        private static string SettingsFile
        {
            get
            {
                if (string.IsNullOrEmpty(_settingsFile))
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var folder = Path.Combine(appData, "PatsKiller Pro");
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }
                    _settingsFile = Path.Combine(folder, "settings.json");
                }
                return _settingsFile;
            }
        }

        /// <summary>
        /// Loads settings from file
        /// </summary>
        private static void Load()
        {
            if (_loaded) return;

            lock (_lock)
            {
                if (_loaded) return;

                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        var json = File.ReadAllText(SettingsFile);
                        _settings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) 
                                    ?? new Dictionary<string, object>();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to load settings: {ex.Message}");
                    _settings = new Dictionary<string, object>();
                }

                _loaded = true;
            }
        }

        /// <summary>
        /// Saves settings to file
        /// </summary>
        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                    File.WriteAllText(SettingsFile, json);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to save settings: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Gets a string setting
        /// </summary>
        public static string GetString(string key, string defaultValue = "")
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    return value?.ToString() ?? defaultValue;
                }
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a string setting
        /// </summary>
        public static void SetString(string key, string value)
        {
            Load();
            lock (_lock)
            {
                _settings[key] = value;
            }
        }

        /// <summary>
        /// Gets a boolean setting
        /// </summary>
        public static bool GetBool(string key, bool defaultValue = false)
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                    if (bool.TryParse(value?.ToString(), out bool parsed))
                    {
                        return parsed;
                    }
                }
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a boolean setting
        /// </summary>
        public static void SetBool(string key, bool value)
        {
            Load();
            lock (_lock)
            {
                _settings[key] = value;
            }
        }

        /// <summary>
        /// Gets an integer setting
        /// </summary>
        public static int GetInt(string key, int defaultValue = 0)
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                    if (value is long longValue)
                    {
                        return (int)longValue;
                    }
                    if (int.TryParse(value?.ToString(), out int parsed))
                    {
                        return parsed;
                    }
                }
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets an integer setting
        /// </summary>
        public static void SetInt(string key, int value)
        {
            Load();
            lock (_lock)
            {
                _settings[key] = value;
            }
        }

        /// <summary>
        /// Removes a setting
        /// </summary>
        public static void Remove(string key)
        {
            Load();
            lock (_lock)
            {
                _settings.Remove(key);
            }
        }

        /// <summary>
        /// Checks if a setting exists
        /// </summary>
        public static bool Contains(string key)
        {
            Load();
            lock (_lock)
            {
                return _settings.ContainsKey(key);
            }
        }

        /// <summary>
        /// Resets all settings to defaults
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _settings.Clear();
                Save();
            }
        }
    }
}
