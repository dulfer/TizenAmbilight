using System;
using System.Collections.Generic;
using System.Threading;

namespace TizenAmbilight
{
    static partial class Ambilight
    {
        // ================================================================ on-TV settings app link
        //
        // The settings app (TizenAmbilight.Settings, same package) talks to this service only through
        // files in the shared data directory; see UserSettings for the protocol.

        static ZoneLayout _testLayout;

        /// <summary>Send one frame of the 4-edge colour test. Needs no capture, so it works while the
        /// settings app itself fills the screen. Flat curve (gain 1, gamma 1, no black crush): the
        /// pattern is for finding edges and direction, not for picture fidelity.</summary>
        static void SendTestPattern(Config cfg, Stats st)
        {
            if (_testLayout == null || _testLayout.Total != cfg.ZoneCount || _testLayout.IsStale)
            {
                // the plane's aspect, so the proportional split matches the one the picture uses
                _testLayout = new ZoneLayout(VideoPlaneCapture.PlaneW, VideoPlaneCapture.PlaneH, cfg.ZoneCount, cfg.BandFrac);
                L("test pattern layout: " + _testLayout.Describe());
            }
            byte[] payload = WledOutput.BuildDrgbMapped(_testLayout.TestPattern(), (byte)cfg.WledTimeout,
                                                        1.0, 1.0, 0.0, cfg.MapOffset, cfg.MapReverse, cfg.LedCount);
            string err = WledOutput.Send(cfg.WledIp, cfg.WledPort, payload);
            if (err == null) { st.SendOk++; st.LastSendErr = null; }
            else { st.SendFail++; st.LastSendErr = err; }
        }

        const int StatusWriteMs = 2000;
        static DateTime _lastStatusUtc = DateTime.MinValue;
        static long _statusSendOk;
        static bool _statusErrLogged;

        /// <summary>Write status.json for the settings app, every 2 s and ONLY while the app is open
        /// (its heartbeat is fresh), so flash is not rewritten for the life of the TV.</summary>
        static void MaybeWriteStatus(Config cfg, Stats st)
        {
            if (_uiHeartbeat == null) { _lastStatusUtc = DateTime.MinValue; return; }
            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastStatusUtc).TotalSeconds;
            if (_lastStatusUtc != DateTime.MinValue && dt * 1000.0 < StatusWriteMs) return;
            double fps = (_lastStatusUtc == DateTime.MinValue || dt <= 0) ? 0 : (st.SendOk - _statusSendOk) / dt;
            _lastStatusUtc = now;
            _statusSendOk = st.SendOk;
            var d = new Dictionary<string, object>
            {
                { "version", Version },
                { "ts_utc", now.ToString("o") },
                { "status", st.LastStatus ?? "" },
                { "power", st.PowerState ?? "" },
                { "backend", st.BackendActive ?? "" },
                { "output_enabled", cfg.OutputEnabled },
                { "wled_configured", cfg.HasWled },
                { "send_fps", Math.Round(fps, 1) },
                { "send_error", st.LastSendErr ?? "" },
                { "zones", (long)cfg.ZoneCount },
                { "leds", (long)cfg.LedCount },
                { "paused", _capturePaused },
            };
            try { UserSettings.Write(StatusPath, d); _statusErrLogged = false; }
            catch (Exception e)
            {
                if (!_statusErrLogged) L("status.json write failed: " + e.GetType().Name + ": " + e.Message);
                _statusErrLogged = true;
            }
        }
    }
}
