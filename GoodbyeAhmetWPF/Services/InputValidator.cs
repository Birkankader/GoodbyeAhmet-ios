using System;
using System.Net;
using System.Text.RegularExpressions;

namespace GoodbyeAhmetWPF.Services
{
    /// <summary>
    /// Centralized validators for user-supplied network parameters.
    /// Used by both the UI (pre-save validation, numeric filters) and the
    /// process launcher (defense-in-depth against argument injection).
    /// </summary>
    public static class InputValidator
    {
        // Modeset is a goodbyedpi switch token like "-1", "-2", ..., "-9", "-10".
        private static readonly Regex _modeset = new("^-[0-9]{1,2}$", RegexOptions.Compiled);

        public static bool IsModeset(string? v) =>
            !string.IsNullOrWhiteSpace(v) && _modeset.IsMatch(v);

        public static bool IsTtl(string? v) =>
            !string.IsNullOrWhiteSpace(v) && byte.TryParse(v, out var b) && b > 0;

        public static bool IsPort(string? v) =>
            !string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var p) && p > 0 && p <= 65535;

        public static bool IsIp(string? v) =>
            !string.IsNullOrWhiteSpace(v) && IPAddress.TryParse(v, out _);

        /// <summary>Used by UI PreviewTextInput on TTL/Port fields.</summary>
        public static bool IsDigitsOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }

        /// <summary>
        /// Validates a blocklist URL: requires HTTPS, parseable absolute URI.
        /// </summary>
        public static bool IsHttpsUrl(string? v, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(v))
            {
                error = "URL is empty.";
                return false;
            }
            if (!Uri.TryCreate(v.Trim(), UriKind.Absolute, out var uri))
            {
                error = "URL is not a valid absolute URI.";
                return false;
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Only HTTPS URLs are allowed for blocklists.";
                return false;
            }
            return true;
        }
    }
}
