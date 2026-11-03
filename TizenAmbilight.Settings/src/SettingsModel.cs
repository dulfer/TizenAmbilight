using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TizenAmbilight.Settings
{
    public enum ItemKind { Toggle, Number, Choice, Ip, Action, Info }

    /// <summary>One row of the settings menu. Numbers are shown in display units; <see cref="Scale"/>
    /// converts to the stored unit (staleMs is shown in seconds and stored in milliseconds).</summary>
    public sealed class SettingItem
    {
        public string Key, Title, Help;
        public ItemKind Kind;
        public double Min, Max, Step = 1, BigStep = 10, Default, Scale = 1;
        public int Decimals;
        public string Unit = "";
        public string ZeroLabel;                    // shown instead of 0 ("Auto", "Off", ...)
        public string[] Choices, ChoiceLabels;
        public string DefaultChoice;
        public bool Transient;                      // never written to settings.json
        public Func<SettingsModel, string> Info;    // Info rows: computed text
        public Func<SettingsModel, double> DynamicMax;
        public Func<SettingsModel, bool> Dimmed;    // shown greyed out (setting currently has no effect)
    }

    public sealed class Category
    {
        public string Title, Description;
        public List<SettingItem> Items = new List<SettingItem>();
    }

    /// <summary>
    /// The settings menu and its values. Reads the same layers the service reads (bundled
    /// <c>res/config.json</c>, then the user layer <c>settings.json</c>) through the shared
    /// <see cref="ConfigFile"/>, and writes every change straight to <c>settings.json</c> with an atomic
    /// replace, so a value is stored the moment it is changed, as on the TV's own settings menu.
    ///
    /// Defaults here MUST match <c>Ambilight.ReadConfig</c>; they are only shown when neither file sets
    /// the key, and the harness checks the important ones.
    /// </summary>
    public sealed class SettingsModel
    {
        public const string TestKey = "_edgeTest";
        public const int MaxLeds = 490;             // WledOutput.MaxDrgbLeds
        public const int DefaultZones = 73;         // Ambilight.DefaultZoneCount

        public readonly string SettingsPath, ConfigPath;
        public readonly List<Category> Categories;
        Dictionary<string, object> _user;
        readonly Dictionary<string, double> _transient = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>Last save error, or null. Shown to the user; a failed save must not be silent.</summary>
        public string SaveError;

        /// <summary>Status snapshot from the service (status.json), refreshed by the app.</summary>
        public Dictionary<string, object> Status = new Dictionary<string, object>(StringComparer.Ordinal);
        public bool StatusFresh;
        /// <summary>The service's last startup error (service-error.txt) while it is not reporting.</summary>
        public string ServiceError;
        public DateTime OpenedUtc = DateTime.UtcNow;

        /// <param name="dataDir">Directory holding settings.json (shared with the service).</param>
        /// <param name="defaultsPath">The service's bundled config (defaults.json, published by the
        /// service): the values shown when the user has not set one, and what Reset returns to.</param>
        public SettingsModel(string dataDir, string defaultsPath)
        {
            SettingsPath = Path.Combine(dataDir ?? "", UserSettings.FileName);
            ConfigPath = defaultsPath;
            Reload();
            Categories = Build();
        }

        public void Reload()
        {
            ConfigFile.Load(ConfigPath);
            _user = UserSettings.Read(SettingsPath);
            ConfigFile.SetUser(_user);
        }

        public bool HasUserValues { get { return _user.Count > 0; } }

        // ================================================================ values

        public double GetNumber(SettingItem it)
        {
            double v;
            if (it.Transient) return _transient.TryGetValue(it.Key, out v) ? v : it.Default;
            return ConfigFile.TryGetDouble(it.Key, out v) ? v / it.Scale : it.Default;
        }

        public bool GetToggle(SettingItem it) { return GetNumber(it) != 0; }

        public string GetString(SettingItem it)
        {
            string s;
            if (ConfigFile.TryGetString(it.Key, out s)) return s.Trim();
            return it.Kind == ItemKind.Choice ? it.DefaultChoice : "";
        }

        public double MaxOf(SettingItem it)
        {
            // Toggles declare no range; their Max field defaults to 0, which once clamped "on" back to off.
            if (it.Kind == ItemKind.Toggle) return 1;
            return it.DynamicMax != null ? it.DynamicMax(this) : it.Max;
        }

        /// <summary>Snap to the item's step and range, as the editor shows it.</summary>
        public double Clamp(SettingItem it, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = it.Default;
            if (it.Step > 0) v = Math.Round(v / it.Step) * it.Step;
            v = Math.Round(v, it.Decimals);
            double max = MaxOf(it);
            if (v < it.Min) v = it.Min;
            if (v > max) v = max;
            return v;
        }

        public bool SetNumber(SettingItem it, double shown)
        {
            double v = Clamp(it, shown);
            if (it.Transient) { _transient[it.Key] = v; return true; }
            _user[it.Key] = Math.Round(v * it.Scale, Math.Max(it.Decimals, 0) + 3);
            return Save();
        }

        public bool SetToggle(SettingItem it, bool on) { return SetNumber(it, on ? 1 : 0); }

        public bool SetString(SettingItem it, string s)
        {
            if (it.Kind == ItemKind.Ip && !(s.Length == 0 || UserSettings.IsIPv4(s))) return false;
            if (it.Kind == ItemKind.Choice && Array.IndexOf(it.Choices, s) < 0) return false;
            _user[it.Key] = s;
            return Save();
        }

        bool Save()
        {
            ConfigFile.SetUser(_user);
            try
            {
                UserSettings.Write(SettingsPath, _user);
                SaveError = null;
                return true;
            }
            catch (Exception e)
            {
                SaveError = e.Message;
                return false;
            }
        }

        /// <summary>Forget every value changed on the TV; the bundled config.json applies again.</summary>
        public bool Reset()
        {
            try
            {
                if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                _user.Clear();
                _transient.Clear();
                ConfigFile.SetUser(_user);
                SaveError = null;
                return true;
            }
            catch (Exception e)
            {
                SaveError = e.Message;
                return false;
            }
        }

        public SettingItem Find(string key)
        {
            foreach (var c in Categories)
                foreach (var it in c.Items)
                    if (it.Key == key) return it;
            return null;
        }

        // ================================================================ derived values

        int Edge(string key) { double v; return ConfigFile.TryGetDouble(key, out v) ? (int)Math.Round(v) : 0; }

        /// <summary>{top, right, bottom, left} when all four are set, else null.</summary>
        public int[] ExplicitEdges()
        {
            int t = Edge("ledsTop"), r = Edge("ledsRight"), b = Edge("ledsBottom"), l = Edge("ledsLeft");
            if (t < 1 || r < 1 || b < 1 || l < 1 || t + r + b + l > MaxLeds) return null;
            return new[] { t, r, b, l };
        }

        /// <summary>Zone total exactly as ReadConfig resolves it.</summary>
        public int ZoneTotal()
        {
            int[] e = ExplicitEdges();
            if (e != null) return e[0] + e[1] + e[2] + e[3];
            int leds = Edge("ledCount"), zones = Edge("zoneCount");
            if (leds <= 0 && zones <= 0) return DefaultZones;
            if (zones <= 0) zones = leds;
            return Math.Max(4, Math.Min(MaxLeds, zones));
        }

        /// <summary>Effective {top, right, bottom, left}: the explicit split or the proportional one.</summary>
        public int[] EffectiveEdges()
        {
            int[] e = ExplicitEdges();
            if (e != null) return e;
            var l = new ZoneLayout(960, 540, ZoneTotal(), 0.08);
            return new[] { l.TopCount, l.RightCount, l.BottomCount, l.LeftCount };
        }

        static readonly string[] EdgeKeys = { "ledsTop", "ledsRight", "ledsBottom", "ledsLeft" };

        public string Format(SettingItem it)
        {
            switch (it.Kind)
            {
                case ItemKind.Toggle: return GetToggle(it) ? "On" : "Off";
                case ItemKind.Info: return it.Info != null ? it.Info(this) : "";
                case ItemKind.Action: return "";
                case ItemKind.Ip:
                {
                    string s = GetString(it);
                    return s.Length == 0 ? "Not set" : s;
                }
                case ItemKind.Choice:
                {
                    string s = GetString(it);
                    int i = Array.IndexOf(it.Choices, s);
                    return i >= 0 ? it.ChoiceLabels[i] : s;
                }
            }
            double v = GetNumber(it);
            int edge = Array.IndexOf(EdgeKeys, it.Key);
            if (v == 0 && edge >= 0) return "Auto (" + EffectiveEdges()[edge] + ")";
            if (v == 0 && it.Key == "ledCount") return "Auto (" + ZoneTotal() + ")";
            if (v == 0 && it.ZeroLabel != null) return it.ZeroLabel;
            return FormatNumber(it, v);
        }

        public static string FormatNumber(SettingItem it, double v)
        {
            string s = v.ToString("F" + it.Decimals, CultureInfo.InvariantCulture);
            return it.Unit.Length > 0 ? s + " " + it.Unit : s;
        }

        string LayoutSummary()
        {
            int[] e = EffectiveEdges();
            return (ExplicitEdges() == null ? "Automatic: " : "") + "top " + e[0] + ", right " + e[1] +
                   ", bottom " + e[2] + ", left " + e[3] + " = " + (e[0] + e[1] + e[2] + e[3]) + " zones";
        }

        string StatusText()
        {
            if (!StatusFresh && !string.IsNullOrEmpty(ServiceError))
                return "The Ambilight service failed to start: " + ServiceError +
                       ". Try Reset Settings, then turn the TV off and on.";
            if (!StatusFresh)
                return (DateTime.UtcNow - OpenedUtc).TotalSeconds < 10
                    ? "Connecting to the Ambilight service..."
                    : "The Ambilight service is not running. Turn the TV off and on to start it.";
            object o;
            bool wled = Status.TryGetValue("wled_configured", out o) && o is bool && (bool)o;
            bool on = Status.TryGetValue("output_enabled", out o) && o is bool && (bool)o;
            string power = Status.TryGetValue("power", out o) ? o as string ?? "" : "";
            if (!wled) return "Running. Set the WLED IP address to start.";
            if (!on) return "Running. LEDs turned off.";
            if (power == "standby") return "Running. TV screen off.";
            string err = Status.TryGetValue("send_error", out o) ? o as string ?? "" : "";
            if (err.Length > 0) return "Cannot reach WLED: " + err;
            double fps = Status.TryGetValue("send_fps", out o) && o is double ? (double)o : 0;
            return "Running, " + fps.ToString("0", CultureInfo.InvariantCulture) + " updates per second";
        }

        // ================================================================ the menu

        List<Category> Build()
        {
            var cats = new List<Category>();

            var general = new Category { Title = "General", Description = "Ambilight Controls." };
            general.Items.Add(new SettingItem
            {
                Key = "outputEnabled", Title = "Ambilight", Kind = ItemKind.Toggle, Default = 1,
                Help = "Turn the LED strip on or off. When off, WLED goes back to its own lighting after a second."
            });
            general.Items.Add(new SettingItem
            {
                Key = "_status", Title = "Status", Kind = ItemKind.Info, Info = m => m.StatusText(),
                Help = "What the Ambilight service is doing right now."
            });
            general.Items.Add(new SettingItem
            {
                Key = "captureBackend", Title = "Picture Source", Kind = ItemKind.Choice,
                Choices = new[] { "video_plane", "ppi" , "file" },
                ChoiceLabels = new[] { "Video (recommended)", "Hardware Sampler", "Screenshot (legacy)" },
                DefaultChoice = "video_plane",
                Help = "Video reads the decoded picture and switches to the hardware sampler by itself for protected " +
                       "content. Choose Hardware Sampler on older TVs where Video stays dark."
            });
            cats.Add(general);

            var strip = new Category { Title = "LED Strip", Description = "Where to send the colours: your WLED controller and its LEDs." };
            strip.Items.Add(new SettingItem
            {
                Key = "wledIp", Title = "WLED IP Address", Kind = ItemKind.Ip,
                Help = "The IPv4 address of your WLED controller, as shown in the WLED app (for example 192.168.1.50)."
            });
            strip.Items.Add(new SettingItem
            {
                Key = "ledCount", Title = "Number of LEDs", Kind = ItemKind.Number, Min = 0, Max = MaxLeds,
                Step = 1, BigStep = 10,
                Help = "LEDs on the strip. Auto uses the edge counts, or 73. LEDs beyond the picture zones stay black."
            });
            strip.Items.Add(new SettingItem
            {
                Key = "wledPort", Title = "WLED Port", Kind = ItemKind.Number, Min = 1, Max = 65535,
                Default = 21324, Step = 1, BigStep = 100,
                Help = "UDP realtime port of WLED. Leave at 21324 unless you changed it in WLED's Sync settings."
            });
            cats.Add(strip);

            var layout = new Category { Title = "LED Layout", Description = "How the LEDs run round the screen. Use the Edge Colour Test to line them up." };
            layout.Items.Add(new SettingItem
            {
                Key = TestKey, Title = "Edge Colour Test", Kind = ItemKind.Toggle, Transient = true,
                Help = "Shows red on the top edge, green right, blue bottom and yellow left while this page is open. " +
                       "Adjust the settings below until each colour sits on its edge."
            });
            string[] names = { "Top", "Right", "Bottom", "Left" };
            for (int i = 0; i < 4; i++)
            {
                layout.Items.Add(new SettingItem
                {
                    Key = EdgeKeys[i], Title = names[i] + " LEDs", Kind = ItemKind.Number, Min = 0, Max = MaxLeds,
                    Step = 1, BigStep = 10,
                    Help = "LEDs along the " + names[i].ToLowerInvariant() + " edge. Set all four to match your strip " +
                           "(for example a gap for the stand); any edge on Auto spreads the LEDs evenly by screen shape."
                });
            }
            layout.Items.Add(new SettingItem
            {
                Key = "mapOffset", Title = "Start Position", Kind = ItemKind.Number, Min = 0, Step = 1, BigStep = 10,
                DynamicMax = m => m.ZoneTotal() - 1,
                Help = "Moves the picture along the strip. Change it until the red top edge starts at the top-left corner."
            });
            layout.Items.Add(new SettingItem
            {
                Key = "mapReverse", Title = "Direction", Kind = ItemKind.Choice,
                Choices = new[] { "0", "1" }, ChoiceLabels = new[] { "Normal", "Reversed" }, DefaultChoice = "0",
                Help = "Reverse it if the colours run the wrong way round the screen."
            });
            layout.Items.Add(new SettingItem
            {
                Key = "_layout", Title = "Layout", Kind = ItemKind.Info, Info = m => m.LayoutSummary(),
                Help = "Zones per edge, clockwise from the top-left corner."
            });
            cats.Add(layout);

            var picture = new Category { Title = "Picture", Description = "Brightness, colour and how the LEDs follow the picture." };
            picture.Items.Add(new SettingItem
            {
                Key = "adaptiveGamma", Title = "Auto Brightness", Kind = ItemKind.Toggle, Default = 1,
                Help = "Follows the brightness of the scene: dark films stay subtle, bright scenes glow. Turn off to use Gamma."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "gamma", Title = "Gamma", Kind = ItemKind.Number, Min = 1.0, Max = 4.0, Default = 1.5,
                Step = 0.05, BigStep = 0.5, Decimals = 2,
                Dimmed = m => m.GetToggle(m.Find("adaptiveGamma")),
                Help = "Brightness curve when Auto Brightness is off. Lower is brighter in dark scenes."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "saturation", Title = "Colour", Kind = ItemKind.Number, Min = 0, Max = 2.0, Default = 1.0,
                Step = 0.05, BigStep = 0.25, Decimals = 2,
                Help = "Colour intensity of the LEDs. 1.00 matches the picture; up to about 1.40 for a more vivid look."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "alpha", Title = "Response Speed", Kind = ItemKind.Number, Min = 0.02, Max = 1.0, Default = 0.25,
                Step = 0.01, BigStep = 0.1, Decimals = 2,
                Help = "How quickly the LEDs follow the picture. Lower is smoother; scene cuts always react fast."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "hdr", Title = "HDR Picture", Kind = ItemKind.Toggle, Default = 1,
                Help = "Keep on: Samsung TVs deliver the picture HDR-encoded. Turn off only if colours look washed out."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "staleMs", Title = "Dim Paused Picture", Kind = ItemKind.Number, Min = 0, Max = 120, Default = 6,
                Scale = 1000, Step = 1, BigStep = 10, Unit = "s", ZeroLabel = "Off",
                Help = "Dims the LEDs when the picture has not moved for this long, for example a paused video."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "bars", Title = "Ignore Black Bars", Kind = ItemKind.Toggle, Default = 1,
                Help = "Skips the black bars beside 4:3 content so the edges take their colour from the picture."
            });
            picture.Items.Add(new SettingItem
            {
                Key = "targetFps", Title = "Update Rate", Kind = ItemKind.Number, Min = 0, Max = 60, Default = 25,
                Step = 1, BigStep = 5, Unit = "per second", ZeroLabel = "Unlimited",
                Help = "Higher is more fluid but uses more of the TV's processor. 25 looks continuous."
            });
            cats.Add(picture);

            var support = new Category { Title = "Support", Description = "Advanced options, reset and version information." };
            support.Items.Add(new SettingItem
            {
                Key = "ppiZones", Title = "Sampler Points",
                Dimmed = m => m.GetString(m.Find("captureBackend")) == "file", Kind = ItemKind.Number, Min = 4, Max = 73, Default = 24,
                Step = 1, BigStep = 4,
                Help = "Points the hardware sampler measures round the screen. More is finer but each updates more slowly."
            });
            support.Items.Add(new SettingItem
            {
                Key = "_reset", Title = "Reset Settings", Kind = ItemKind.Action,
                Help = "Forget everything changed here and go back to the values from the installation."
            });
            support.Items.Add(new SettingItem
            {
                Key = "_about", Title = "About", Kind = ItemKind.Info,
                Info = m =>
                {
                    object v;
                    string ver = m.Status.TryGetValue("version", out v) ? v as string : null;
                    return "TizenAmbilight" + (ver != null ? " " + ver : "") + " - github.com/dulfer/TizenAmbilight (fork of excellent work by ritwickverma)";
                },
                Help = "Open source, MIT licensed."
            });
            cats.Add(support);
            return cats;
        }
    }
}
