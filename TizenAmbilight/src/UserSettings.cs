using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TizenAmbilight
{
    /// <summary>
    /// Files shared between the ambilight service and the on-TV settings app. The settings app is a
    /// separate package signed with the same author certificate, so it can use the service package's
    /// <c>shared/trusted</c> directory (<c>apps_rw/&lt;pkgid&gt;/shared/trusted/</c>); that is the
    /// whole IPC:
    ///
    /// <list type="bullet">
    /// <item><c>settings.json</c>: the user layer of <see cref="ConfigFile"/>. Written by the settings
    /// app on every change, polled by the service, which applies it live.</item>
    /// <item><c>ui-active</c>: a heartbeat the settings app rewrites every 2 s while it is open. Its
    /// body is <c>test</c> while the LED layout page wants the edge colour test, else <c>open</c>.
    /// The service acts on it only while it is fresh, so a crashed or closed settings app can never
    /// leave the test pattern on.</item>
    /// <item><c>status.json</c>: a flat status snapshot for the settings app. The service writes it
    /// only while the heartbeat is fresh, so flash is not rewritten every few seconds forever.</item>
    /// <item><c>service-error.txt</c>: written by the service when its startup throws, deleted once a
    /// later start reaches the main loop. Telemetry is optional and the service has no screen, so this
    /// is the only place the TV owner can see why it is not running.</item>
    /// <item><c>defaults.json</c>: a copy of the service's bundled config.json, written by the service
    /// when it differs. The settings app shows these values when no user value is set and returns to
    /// them on Reset.</item>
    /// </list>
    ///
    /// Why a file rather than the Tizen Preference store or a message port: Preference is scoped to
    /// one application id, and a file needs no extra privilege, survives either app being stopped,
    /// and is testable on the host. No Tizen dependency; the harness compiles this file.
    /// </summary>
    public static class UserSettings
    {
        public const string FileName = "settings.json";
        public const string HeartbeatFileName = "ui-active";
        public const string StatusFileName = "status.json";
        public const string ErrorFileName = "service-error.txt";
        public const string DefaultsFileName = "defaults.json";
        public const string HeartbeatOpen = "open";
        public const string HeartbeatTest = "test";

        /// <summary>The settings app rewrites the heartbeat every 2 s; allow for a slow tick.</summary>
        public const int HeartbeatFreshMs = 6000;

        /// <summary>The heartbeat body if the file is fresh, else null.</summary>
        public static string ReadHeartbeat(string path, DateTime nowUtc)
        {
            if (!IsFresh(path, nowUtc, HeartbeatFreshMs)) return null;
            try { return File.ReadAllText(path, Encoding.UTF8).Trim(); }
            catch { return null; }
        }

        /// <summary>Read a flat JSON object. Missing or malformed = empty (never throws).</summary>
        public static Dictionary<string, object> Read(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return ConfigFile.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { }
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>One flat object, keys sorted, one per line, so the file diffs and reads well.</summary>
        public static string Serialize(IDictionary<string, object> values)
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n");
            for (int i = 0; i < keys.Count; i++)
            {
                object v = values[keys[i]];
                if (v is double)
                {
                    double d = (double)v;
                    if (double.IsNaN(d) || double.IsInfinity(d)) v = null;
                }
                sb.Append("  ").Append(Json.ToJson(keys[i])).Append(": ").Append(Json.ToJson(v));
                sb.Append(i + 1 < keys.Count ? ",\n" : "\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>Write atomically: a temp file, flushed to disk, then renamed over the target.
        /// The TV can lose power at any moment (that is how most people turn it off), and a
        /// half-written settings file would be rejected as a whole and silently drop every value.</summary>
        public static void Write(string path, IDictionary<string, object> values)
        {
            WriteText(path, Serialize(values));
        }

        public static void WriteText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        /// <summary>True when <paramref name="path"/> exists and was written within
        /// <paramref name="maxAgeMs"/> of <paramref name="nowUtc"/>.</summary>
        public static bool IsFresh(string path, DateTime nowUtc, int maxAgeMs)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                double age = (nowUtc - File.GetLastWriteTimeUtc(path)).TotalMilliseconds;
                return age >= -maxAgeMs && age <= maxAgeMs;
            }
            catch { return false; }
        }

        /// <summary>Strict dotted-quad IPv4 (no leading zeros, no hostnames): the WLED target is a
        /// raw UDP address, and a typo must be refused rather than stored.</summary>
        public static bool IsIPv4(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split('.');
            if (parts.Length != 4) return false;
            foreach (string p in parts)
            {
                if (p.Length == 0 || p.Length > 3) return false;
                if (p.Length > 1 && p[0] == '0') return false;
                foreach (char c in p) if (c < '0' || c > '9') return false;
                if (int.Parse(p, CultureInfo.InvariantCulture) > 255) return false;
            }
            return true;
        }
    }
}
