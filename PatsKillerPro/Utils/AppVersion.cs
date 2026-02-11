using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Centralized version helper.
    /// Uses AssemblyInformationalVersion (e.g., 2.8.25+123) so builds can show n+1.
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// Full informational version string from assembly.
        /// Example: "2.8.25+123" or "2.8.25".
        /// </summary>
        public static string Informational
        {
            get
            {
                try
                {
                    var attr = Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (!string.IsNullOrWhiteSpace(attr?.InformationalVersion))
                        return attr!.InformationalVersion;

                    // Fallback to assembly version (may be 4-part)
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v != null ? v.ToString() : "2.8.25";
                }
                catch
                {
                    return "2.8.25";
                }
            }
        }

        /// <summary>
        /// Friendly display string for UI headers.
        /// Example: "2.8.25 (build 123)".
        /// </summary>
        public static string Display
        {
            get
            {
                var info = Informational;

                // Common pattern: "X.Y.Z+N" or "X.Y.Z+build.N".
                // If we don't have a +build suffix, fall back to FileVersion 4th part.
                try
                {
                    var parts = info.Split('+', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var build = parts[1]
                            .Replace("build.", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();

                        if (!string.IsNullOrWhiteSpace(build))
                            return $"{parts[0]} (build {build})";

                        return parts[0];
                    }

                    // FileVersion is typically X.Y.Z.B
                    var asm = Assembly.GetExecutingAssembly();
                    var fv = FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fv))
                    {
                        var vparts = fv.Split('.', StringSplitOptions.RemoveEmptyEntries);
                        if (vparts.Length >= 4)
                        {
                            var ver = string.Join('.', vparts.Take(3));
                            var build = vparts[3];
                            return $"{ver} (build {build})";
                        }
                    }

                    return info;
                }
                catch
                {
                    return info;
                }
            }
        }
    }
}
