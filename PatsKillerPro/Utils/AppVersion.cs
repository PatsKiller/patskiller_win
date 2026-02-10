using System;
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
                try
                {
                    // Common pattern: "X.Y.Z+N" or "X.Y.Z+build.N"
                    var parts = info.Split('+', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var build = parts[1]
                            .Replace("build.", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();
                        if (!string.IsNullOrWhiteSpace(build) && build != "0")
                            return $"{parts[0]} (build {build})";

                        return parts[0];
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
