using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Windows Credential Manager wrapper for storing sensitive secrets (tokens) securely.
    ///
    /// Why: settings.json is readable by any process running as the current user.
    /// Credential Manager stores secrets using OS protections and avoids plaintext files.
    /// </summary>
    public static class SecureStorage
    {
        private const string TARGET_PREFIX = "PatsKillerPro_";

        // https://learn.microsoft.com/windows/win32/api/wincred/ns-wincred-credentialw
        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        // Fallback storage (DPAPI encrypted) for environments where Credential Manager is blocked by policy.
        // IMPORTANT: still not plaintext, but less ideal than Credential Manager.
        private static readonly object _fallbackLock = new();
        private static readonly Dictionary<string, string> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

        private static string FallbackFile
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "PatsKillerPro");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "secrets_dpapi.json");
            }
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree([In] IntPtr buffer);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        private static string Target(string key) => TARGET_PREFIX + key;

        public static void SaveSecret(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) value = string.Empty;

            // Always keep in-memory for the running session.
            lock (_fallbackLock)
            {
                _memoryCache[key] = value;
            }

            var target = Target(key);
            var bytes = Encoding.Unicode.GetBytes(value);
            var blob = Marshal.AllocHGlobal(bytes.Length);

            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);

                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = Environment.UserName,
                    Comment = "PatsKiller Pro secure storage"
                };

                if (CredWrite(ref cred, 0))
                    return;

                // Credential Manager may be blocked by policy. Fall back to DPAPI-encrypted file.
                var lastErr = Marshal.GetLastWin32Error();
                TrySaveDpapi(key, value, lastErr);
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }

        public static string? LoadSecret(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            // 1) Credential Manager
            var target = Target(key);
            if (CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr) && credPtr != IntPtr.Zero)
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                        return string.Empty;

                    var bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                    var v = Encoding.Unicode.GetString(bytes);

                    lock (_fallbackLock)
                    {
                        _memoryCache[key] = v;
                    }

                    return v;
                }
                finally
                {
                    CredFree(credPtr);
                }
            }

            // 2) DPAPI fallback file
            var fromFile = TryLoadDpapi(key);
            if (fromFile != null)
            {
                lock (_fallbackLock)
                {
                    _memoryCache[key] = fromFile;
                }
                return fromFile;
            }

            // 3) Memory cache
            lock (_fallbackLock)
            {
                return _memoryCache.TryGetValue(key, out var v) ? v : null;
            }
        }

        public static void DeleteSecret(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var target = Target(key);

            lock (_fallbackLock)
            {
                _memoryCache.Remove(key);
            }

            // If it's missing, that's fine.
            try { CredDelete(target, CRED_TYPE_GENERIC, 0); } catch { /* best effort */ }
            TryDeleteDpapi(key);
        }

        private static void TrySaveDpapi(string key, string value, int credWriteErr)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                var b64 = Convert.ToBase64String(protectedBytes);

                lock (_fallbackLock)
                {
                    var dict = ReadFallbackDict_NoThrow();
                    dict[key] = b64;
                    File.WriteAllText(FallbackFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex)
            {
                // Last resort: keep only in memory for this run.
                // Do NOT throw: we prefer "login again next time" over crashing the app.
                try
                {
                    // Avoid hard dependency on Logger from utils (keep SecureStorage standalone)
                    System.Diagnostics.Debug.WriteLine($"SecureStorage: CredWrite failed ({credWriteErr}) and DPAPI fallback failed: {ex.Message}");
                }
                catch { /* ignore */ }
            }
        }

        private static string? TryLoadDpapi(string key)
        {
            try
            {
                lock (_fallbackLock)
                {
                    var dict = ReadFallbackDict_NoThrow();
                    if (!dict.TryGetValue(key, out var b64) || string.IsNullOrWhiteSpace(b64))
                        return null;

                    var bytes = Convert.FromBase64String(b64);
                    var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
            }
            catch
            {
                return null;
            }
        }

        private static void TryDeleteDpapi(string key)
        {
            try
            {
                lock (_fallbackLock)
                {
                    var dict = ReadFallbackDict_NoThrow();
                    if (dict.Remove(key))
                    {
                        File.WriteAllText(FallbackFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }
            catch { /* best effort */ }
        }

        private static Dictionary<string, string> ReadFallbackDict_NoThrow()
        {
            try
            {
                if (!File.Exists(FallbackFile)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var json = File.ReadAllText(FallbackFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
