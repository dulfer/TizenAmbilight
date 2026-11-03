using System;
using System.Collections.Generic;
using System.IO;
using Tizen.Applications;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

namespace TizenAmbilight.Settings
{
    /// <summary>
    /// The on-TV settings screen. It only edits <c>settings.json</c> in the package's data directory (saved
    /// on every change); the Ambilight service, which shares that directory, notices the file within a
    /// second and applies it live. While open it also writes a heartbeat so the service reports its status
    /// and, on the LED Layout page, shows the edge colour test.
    /// </summary>
    sealed class SettingsApp : NUIApplication
    {
        const string ServiceAppId = "dev.ritwickverma.TizenAmbilight";
        const float PageX = 680, ListY = 230, ListH = 672, HelpY = 930;
        const float CatY = 250, CatH = 84;

        sealed class Page
        {
            public View Root, Content;
            public readonly List<Row> Rows = new List<Row>();
            public float Scroll;
        }

        SettingsModel _model;
        string _dataDir;
        bool _defaultsSeen;
        Window _win;
        readonly List<Page> _pages = new List<Page>();
        readonly List<TextLabel> _catLabels = new List<TextLabel>();
        readonly List<int> _itemIndex = new List<int>();
        View _catHl, _statusDot;
        TextLabel _pageTitle, _pageDesc, _help, _statusText;
        Popup _popup;
        Tizen.NUI.Timer _timer;
        int _cat;
        bool _inItems;

        static void Main(string[] args)
        {
            new SettingsApp().Run(args);
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _dataDir = LinkDir();
            _model = new SettingsModel(_dataDir, Path.Combine(_dataDir, UserSettings.DefaultsFileName));
            _defaultsSeen = File.Exists(Path.Combine(_dataDir, UserSettings.DefaultsFileName));
            _win = Window.Instance;
            Theme.K = _win.WindowSize.Width / 1920f;
            _win.BackgroundColor = Theme.Hex(Theme.Background);
            Build();
            _win.KeyEvent += OnKey;
            _timer = new Tizen.NUI.Timer(2000);
            _timer.Tick += (s, e) => { Tick(); return true; };
            LaunchService();
            Tick();
            _timer.Start();
        }

        protected override void OnPause()
        {
            base.OnPause();
            _timer?.Stop();
            StopHeartbeat();
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (_model == null) return;
            _model.Reload();
            Tick();
            _timer?.Start();
        }

        protected override void OnTerminate()
        {
            _timer?.Stop();
            CloseTopPopup();
            StopHeartbeat();
            base.OnTerminate();
        }

        // ================================================================ layout

        void Build()
        {
            var root = _win.GetDefaultLayer();

            var left = Theme.Box(0, 0, 600, 1080, Theme.Panel);
            root.Add(left);
            left.Add(Theme.Label("Ambilight", 52, Theme.TextPrimary, 80, 80, 480, 76));
            left.Add(Theme.Label("Settings", 28, Theme.TextSecondary, 80, 152, 480, 44));
            _catHl = Theme.Box(40, CatY, 520, CatH - 8, Theme.Focus, 14);
            left.Add(_catHl);
            for (int i = 0; i < _model.Categories.Count; i++)
            {
                var l = Theme.Label(_model.Categories[i].Title, 32, Theme.TextPrimary, 80, CatY + i * CatH, 440, CatH - 8);
                _catLabels.Add(l);
                left.Add(l);
            }
            _statusDot = Theme.Box(80, 994, 16, 16, Theme.TextSecondary, 8);
            left.Add(_statusDot);
            _statusText = Theme.Label("", 24, Theme.TextSecondary, 110, 980, 460, 44);
            left.Add(_statusText);

            _pageTitle = Theme.Label("", 44, Theme.TextPrimary, PageX + 40, 80, 1160, 64);
            root.Add(_pageTitle);
            _pageDesc = Theme.Label("", 26, Theme.TextSecondary, PageX + 40, 150, 1160, 44);
            root.Add(_pageDesc);

            foreach (var c in _model.Categories)
            {
                var p = new Page
                {
                    Root = new View
                    {
                        Position = new Position(Theme.S(PageX), Theme.S(ListY)),
                        Size = new Size(Theme.S(Row.Width), Theme.S(ListH)),
                        ClippingMode = ClippingModeType.ClipChildren,
                    },
                    Content = new View(),
                };
                p.Root.Add(p.Content);
                for (int i = 0; i < c.Items.Count; i++)
                {
                    var r = new Row(c.Items[i], i * Row.Height);
                    p.Rows.Add(r);
                    p.Content.Add(r.View);
                }
                p.Root.Hide();
                root.Add(p.Root);
                _pages.Add(p);
                _itemIndex.Add(0);
            }

            root.Add(Theme.Box(PageX + 40, HelpY - 24, Row.Width - 40, 2, Theme.Track));
            _help = Theme.Label("", 26, Theme.TextSecondary, PageX + 40, HelpY, Row.Width - 40, 120);
            _help.MultiLine = true;
            _help.Ellipsis = false;
            _help.VerticalAlignment = VerticalAlignment.Top;
            root.Add(_help);

            ShowCategory(0);
        }

        void ShowCategory(int i)
        {
            _pages[_cat].Root.Hide();
            _cat = i;
            _pages[_cat].Root.Show();
            _pageTitle.Text = _model.Categories[_cat].Title;
            _pageDesc.Text = _model.Categories[_cat].Description;
            Theme.Move(_catHl, CatY + _cat * CatH);
            Refresh();
        }

        /// <summary>Repaint everything that depends on the values: other rows can change with one edit
        /// (edge counts change the layout summary, Auto Brightness dims Gamma, ...).</summary>
        void Refresh()
        {
            _catHl.BackgroundColor = Theme.Hex(_inItems ? Theme.Selected : Theme.Focus);
            for (int i = 0; i < _catLabels.Count; i++)
                _catLabels[i].TextColor = Theme.Hex(i == _cat && !_inItems ? Theme.TextOnFocus : Theme.TextPrimary);
            var page = _pages[_cat];
            for (int i = 0; i < page.Rows.Count; i++)
                page.Rows[i].Update(_model, _inItems && i == _itemIndex[_cat]);
            if (_model.SaveError != null)
            {
                _help.TextColor = Theme.Hex(Theme.Negative);
                _help.Text = "Could not save the setting: " + _model.SaveError;
            }
            else
            {
                _help.TextColor = Theme.Hex(Theme.TextSecondary);
                _help.Text = _inItems ? (page.Rows[_itemIndex[_cat]].Item.Help ?? "") : _model.Categories[_cat].Description;
            }
            bool fresh = _model.StatusFresh;
            _statusDot.BackgroundColor = Theme.Hex(fresh ? Theme.Positive : Theme.Negative);
            if (!fresh && (DateTime.UtcNow - _model.OpenedUtc).TotalSeconds < 10) _statusDot.BackgroundColor = Theme.Hex(Theme.TextSecondary);
            _statusText.Text = fresh ? "Service running" : "Service not connected";
        }

        void FocusItem(int i)
        {
            var page = _pages[_cat];
            i = Math.Max(0, Math.Min(page.Rows.Count - 1, i));
            _itemIndex[_cat] = i;
            float top = i * Row.Height, bottom = top + Row.Height;
            if (top < page.Scroll) page.Scroll = top;
            else if (bottom > page.Scroll + ListH) page.Scroll = bottom - ListH;
            Theme.Move(page.Content, -page.Scroll);
            Refresh();
        }

        // ================================================================ keys

        void OnKey(object sender, Window.KeyEventArgs e)
        {
            if (e.Key.State != Key.StateType.Down) return;
            string k = KeyNames.Normalise(e.Key.KeyPressedName);
            if (_popup != null)
            {
                if (!_popup.Key(k)) CloseTopPopup();
                return;
            }
            if (!_inItems)
            {
                switch (k)
                {
                    case KeyNames.Up: if (_cat > 0) ShowCategory(_cat - 1); break;
                    case KeyNames.Down: if (_cat < _pages.Count - 1) ShowCategory(_cat + 1); break;
                    case KeyNames.Right:
                    case KeyNames.Ok: _inItems = true; FocusItem(_itemIndex[_cat]); break;
                    case KeyNames.Back: Exit(); break;
                }
                return;
            }
            switch (k)
            {
                case KeyNames.Up: FocusItem(_itemIndex[_cat] - 1); break;
                case KeyNames.Down: FocusItem(_itemIndex[_cat] + 1); break;
                case KeyNames.Left:
                case KeyNames.Back: _inItems = false; Refresh(); break;
                case KeyNames.Ok: Activate(_pages[_cat].Rows[_itemIndex[_cat]].Item); break;
                case KeyNames.Right:
                {
                    var it = _pages[_cat].Rows[_itemIndex[_cat]].Item;
                    if (it.Kind == ItemKind.Number) Activate(it);
                    break;
                }
            }
        }

        void Activate(SettingItem it)
        {
            switch (it.Kind)
            {
                case ItemKind.Toggle:
                    _model.SetToggle(it, !_model.GetToggle(it));
                    Changed();
                    break;
                case ItemKind.Number: Open(new NumberPopup(_model, it, Changed)); break;
                case ItemKind.Ip: Open(new IpPopup(_model, it, Changed)); break;
                case ItemKind.Choice: Open(new ChoicePopup(_model, it, Changed)); break;
                case ItemKind.Action:
                    if (it.Key == "_reset")
                        Open(new ConfirmPopup("Reset Settings",
                            "Every setting changed here goes back to the value installed with the app. " +
                            "The LED strip updates straight away.", "Reset", () => { _model.Reset(); Changed(); }));
                    break;
            }
        }

        void Changed()
        {
            Refresh();
            Heartbeat();
        }

        void Open(Popup p)
        {
            _popup = p;
            _win.GetDefaultLayer().Add(p.Root);
        }

        void CloseTopPopup()
        {
            if (_popup == null) return;
            var p = _popup;
            _popup = null;
            p.Close();
            p.Root.Unparent();
            p.Root.Dispose();
            Refresh();
        }

        // ================================================================ service link

        void Tick()
        {
            Heartbeat();
            // the service publishes defaults.json when it starts; pick it up if it was not there yet
            if (!_defaultsSeen && File.Exists(Path.Combine(_dataDir, UserSettings.DefaultsFileName)))
            {
                _defaultsSeen = true;
                _model.Reload();
            }
            string status = Path.Combine(_dataDir, UserSettings.StatusFileName);
            _model.StatusFresh = UserSettings.IsFresh(status, DateTime.UtcNow, UserSettings.HeartbeatFreshMs);
            if (_model.StatusFresh) _model.Status = UserSettings.Read(status);
            _model.ServiceError = _model.StatusFresh ? null : ReadServiceError();
            Refresh();
        }

        /// <summary>The exception line of service-error.txt (its third line), or null.</summary>
        string ReadServiceError()
        {
            try
            {
                string p = Path.Combine(_dataDir, UserSettings.ErrorFileName);
                if (!File.Exists(p)) return null;
                string[] lines = File.ReadAllLines(p);
                string msg = lines.Length > 2 ? lines[2] : lines.Length > 0 ? lines[lines.Length - 1] : "";
                string stage = lines.Length > 1 ? lines[1].Replace("stage: ", "") : "";
                return (msg.Length > 140 ? msg.Substring(0, 140) + "..." : msg) +
                       (stage.Length > 0 ? " (during " + stage + ")" : "");
            }
            catch { return null; }
        }

        /// <summary>"test" only while the LED Layout page is open with the colour test on, so leaving
        /// the page (or the app) always gives the strip back to the picture.</summary>
        void Heartbeat()
        {
            var test = _model.Find(SettingsModel.TestKey);
            bool onLayout = test != null && _model.Categories[_cat].Items.Contains(test);
            string beat = onLayout && _model.GetToggle(test) ? UserSettings.HeartbeatTest : UserSettings.HeartbeatOpen;
            try
            {
                UserSettings.WriteText(Path.Combine(_dataDir, UserSettings.HeartbeatFileName), beat);
            }
            catch (Exception e)
            {
                Tizen.Log.Warn("AMBSET", "heartbeat: " + e.Message);
            }
        }

        void StopHeartbeat()
        {
            try { File.Delete(Path.Combine(_dataDir, UserSettings.HeartbeatFileName)); }
            catch { }
        }

        /// <summary>The service starts on boot; start it here too, so a fresh install or a crash does not
        /// leave the settings with nothing to apply them.</summary>
        static void LaunchService()
        {
            try
            {
                var ac = new AppControl { ApplicationId = ServiceAppId, Operation = AppControlOperations.Default };
                AppControl.SendLaunchRequest(ac);
            }
            catch (Exception e)
            {
                Tizen.Log.Warn("AMBSET", "launch service: " + e.Message);
            }
        }

        /// <summary>The service package's shared/trusted directory, where it reads settings.json. This
        /// app is a separate package (see UserSettings); both are signed with the same author
        /// certificate, which is what lets this app write there.</summary>
        static string LinkDir()
        {
            try
            {
                string d = ApplicationManager.GetInstalledApplication(ServiceAppId)?.SharedTrustedPath;
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch (Exception e) { Tizen.Log.Warn("AMBSET", "service app info: " + e.Message); }
            // same layout as our own: apps_rw/<pkgid>/shared/trusted
            try
            {
                string own = Application.Current.DirectoryInfo.SharedTrusted;
                string pkgDir = Path.GetDirectoryName(Path.GetDirectoryName(own.TrimEnd('/')));
                return Path.Combine(Path.GetDirectoryName(pkgDir), ServiceAppId, "shared", "trusted");
            }
            catch { }
            return Path.Combine("/opt/usr/home/owner/apps_rw", ServiceAppId, "shared", "trusted");
        }
    }
}
