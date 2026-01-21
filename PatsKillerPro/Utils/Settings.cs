using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// User settings management
    /// Settings are stored in %APPDATA%\PatsKiller Pro\settings.json
    /// </summary>
    public static class Settings
    {
        private static readonly object _lock = new();
        private static Dictionary<string, JsonElement> _settings = new();
        private static string _settingsFile = "";
        private static bool _loaded = false;

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
                        var doc = JsonDocument.Parse(json);
                        _settings = new Dictionary<string, JsonElement>();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            _settings[prop.Name] = prop.Value.Clone();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to load settings: {ex.Message}");
                    _settings = new Dictionary<string, JsonElement>();
                }

                _loaded = true;
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var toSave = new Dictionary<string, object?>();
                    foreach (var kvp in _settings)
                    {
                        toSave[kvp.Key] = JsonElementToObject(kvp.Value);
                    }
                    var json = JsonSerializer.Serialize(toSave, options);
                    File.WriteAllText(SettingsFile, json);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to save settings: {ex.Message}", ex);
                }
            }
        }

        private static object? JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        public static string GetString(string key, string defaultValue = "")
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    return value.ValueKind == JsonValueKind.String ? value.GetString() ?? defaultValue : value.ToString();
                }
                return defaultValue;
            }
        }

        public static void SetString(string key, string value)
        {
            Load();
            lock (_lock)
            {
                using var doc = JsonDocument.Parse($"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
                _settings[key] = doc.RootElement.Clone();
            }
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    if (value.ValueKind == JsonValueKind.True) return true;
                    if (value.ValueKind == JsonValueKind.False) return false;
                    if (bool.TryParse(value.ToString(), out bool parsed))
                    {
                        return parsed;
                    }
                }
                return defaultValue;
            }
        }

        public static void SetBool(string key, bool value)
        {
            Load();
            lock (_lock)
            {
                using var doc = JsonDocument.Parse(value ? "true" : "false");
                _settings[key] = doc.RootElement.Clone();
            }
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            Load();
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
                    {
                        return result;
                    }
                    if (int.TryParse(value.ToString(), out int parsed))
                    {
                        return parsed;
                    }
                }
                return defaultValue;
            }
        }

        public static void SetInt(string key, int value)
        {
            Load();
            lock (_lock)
            {
                using var doc = JsonDocument.Parse(value.ToString());
                _settings[key] = doc.RootElement.Clone();
            }
        }

        public static void Remove(string key)
        {
            Load();
            lock (_lock)
            {
                _settings.Remove(key);
            }
        }

        public static bool Contains(string key)
        {
            Load();
            lock (_lock)
            {
                return _settings.ContainsKey(key);
            }
        }

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
