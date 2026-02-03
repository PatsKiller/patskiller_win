using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

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

                if (!CredWrite(ref cred, 0))
                {
                    // If this fails, do NOT silently fall back to plaintext.
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CredWrite failed");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }

        public static string? LoadSecret(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var target = Target(key);
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr) || credPtr == IntPtr.Zero)
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return string.Empty;

                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public static void DeleteSecret(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var target = Target(key);
            // If it's missing, that's fine.
            CredDelete(target, CRED_TYPE_GENERIC, 0);
        }
    }
}
