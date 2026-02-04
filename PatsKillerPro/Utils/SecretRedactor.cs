using System;
using System.Text.RegularExpressions;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Security utility to redact sensitive PATS codes from logs and telemetry.
    /// CRITICAL: InCodes and OutCodes must NEVER be persisted to disk or transmitted.
    /// </summary>
    public static class SecretRedactor
    {
        // Patterns that match incode/outcode formats
        // Incodes: 4-8 hex digits (e.g., "1A2B", "1A2B3C4D")
        // Outcodes: 6-12 hex digits (e.g., "A1B2C3D4E5F6")
        
        private static readonly Regex IncodePattern = new(
            @"(?i)(?<=incode[:\s=]+)[0-9A-Fa-f]{4,8}(?!\w)",
            RegexOptions.Compiled);
        
        private static readonly Regex OutcodePattern = new(
            @"(?i)(?<=outcode[:\s=]+)[0-9A-Fa-f]{6,12}(?!\w)",
            RegexOptions.Compiled);

        // Generic hex code patterns (for standalone codes)
        private static readonly Regex StandaloneHexPattern = new(
            @"\b[0-9A-Fa-f]{4,12}\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Redacts incodes and outcodes from a message for safe logging.
        /// Replaces sensitive codes with [REDACTED].
        /// </summary>
        public static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var result = message;

            // Redact patterns like "incode: 1A2B3C4D" or "incode=1234"
            result = IncodePattern.Replace(result, "[REDACTED]");
            
            // Redact patterns like "outcode: A1B2C3D4E5F6"
            result = OutcodePattern.Replace(result, "[REDACTED]");

            return result;
        }

        /// <summary>
        /// Aggressively redacts any hex codes that could be incodes/outcodes.
        /// Use for maximum security in error messages.
        /// </summary>
        public static string RedactAggressive(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            // First apply standard redaction
            var result = Redact(message);

            // Then redact any standalone 4-12 character hex codes
            // This may over-redact but ensures security
            result = StandaloneHexPattern.Replace(result, match =>
            {
                // Keep short common hex values like addresses
                if (match.Value.Length <= 4 && 
                    (match.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                     match.Value.All(c => c >= '0' && c <= '9')))
                {
                    return match.Value;
                }
                return "[REDACTED]";
            });

            return result;
        }

        /// <summary>
        /// Checks if a string contains what appears to be an incode or outcode.
        /// </summary>
        public static bool ContainsSensitiveCode(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            
            return IncodePattern.IsMatch(message) || OutcodePattern.IsMatch(message);
        }

        /// <summary>
        /// Returns a masked version of an incode for display purposes.
        /// Shows first and last character only: "1***4"
        /// </summary>
        public static string MaskIncode(string? incode)
        {
            if (string.IsNullOrEmpty(incode) || incode.Length < 2)
                return "****";

            return $"{incode[0]}***{incode[^1]}";
        }

        /// <summary>
        /// Returns a masked version of an outcode for display purposes.
        /// Shows first 2 and last 2 characters: "A1****F6"
        /// </summary>
        public static string MaskOutcode(string? outcode)
        {
            if (string.IsNullOrEmpty(outcode) || outcode.Length < 4)
                return "******";

            var len = outcode.Length;
            return $"{outcode[..2]}****{outcode[(len-2)..]}";
        }
    }
}
