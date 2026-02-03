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

        // -------- Sensitive keys (never persist to settings.json) --------

        private static bool IsSensitiveKey(string key)
        {
            // Tokens are secrets and must not live on disk in plaintext.
            return string.Equals(key, "auth_token", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "refresh_token", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSensitiveString(string key)
        {
            // 1) Try Credential Manager first
            try
            {
                var v = SecureStorage.LoadSecret(key);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch (Exception ex)
            {
                Logger.Warning($"SecureStorage read failed for '{key}': {ex.Message}");
            }

            // 2) If an old plaintext value exists in settings.json, migrate it to Credential Manager.
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value))
                {
                    var raw = value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : value.ToString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        try
                        {
                            SecureStorage.SaveSecret(key, raw);
                            _settings.Remove(key);
                            Save(); // remove plaintext token from disk
                            Logger.Info($"Migrated '{key}' from settings.json to Windows Credential Manager.");
                        }
                        catch (Exception ex)
                        {
                            // If migration fails, return the value so the app still works, but DO NOT write it back.
                            Logger.Warning($"SecureStorage migration failed for '{key}': {ex.Message}");
                        }
                    }
                    return raw;
                }
            }

            return string.Empty;
        }

        private static void SetSensitiveString(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                try { SecureStorage.DeleteSecret(key); } catch { /* best effort */ }
                lock (_lock) { _settings.Remove(key); }
                Save();
                return;
            }

            try
            {
                SecureStorage.SaveSecret(key, value);
            }
            catch (Exception ex)
            {
                // Hard-fail is safer than silently dropping back to plaintext.
                Logger.Error($"SecureStorage write failed for '{key}': {ex.Message}", ex);
                throw;
            }

            lock (_lock)
            {
                // Ensure we never persist it to settings.json
                _settings.Remove(key);
            }
            Save();
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

            if (IsSensitiveKey(key))
            {
                var secret = GetSensitiveString(key);
                return string.IsNullOrEmpty(secret) ? defaultValue : secret;
            }

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

            if (IsSensitiveKey(key))
            {
                SetSensitiveString(key, value);
                return;
            }

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

            if (IsSensitiveKey(key))
            {
                try { SecureStorage.DeleteSecret(key); } catch { /* best effort */ }
            }

            lock (_lock)
            {
                _settings.Remove(key);
            }
        }

        public static bool Contains(string key)
        {
            Load();

            if (IsSensitiveKey(key))
            {
                try { return !string.IsNullOrEmpty(SecureStorage.LoadSecret(key)); }
                catch { return false; }
            }

            lock (_lock)
            {
                return _settings.ContainsKey(key);
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                try
                {
                    SecureStorage.DeleteSecret("auth_token");
                    SecureStorage.DeleteSecret("refresh_token");
                }
                catch { /* best effort */ }

                _settings.Clear();
                Save();
            }
        }
    }
}
