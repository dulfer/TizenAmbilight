using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace TizenAmbilight.Settings
{
    /// <summary>
    /// Look of the Samsung TV settings menu (One UI for TV): near-black background, a category column on
    /// the left, the selected page on the right, focus shown as a white rounded pill with dark text, the
    /// current value in grey under each title, and a short explanation at the bottom. Every size is in
    /// 1920x1080 design pixels and scaled to the real window.
    /// </summary>
    static class Theme
    {
        public static float K = 1f;
        public static float S(float v) { return v * K; }

        public static Color Hex(int rgb, float a = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
        }

        public static readonly int Background = 0x0E0F12;
        public static readonly int Panel = 0x18191D;
        public static readonly int Popup = 0x26272C;
        public static readonly int Focus = 0xF2F2F2;
        public static readonly int Selected = 0x34363C;
        public static readonly int TextPrimary = 0xF2F2F2;
        public static readonly int TextSecondary = 0x9EA1A8;
        public static readonly int TextOnFocus = 0x111111;
        public static readonly int TextOnFocusSecondary = 0x4A4A4A;
        public static readonly int Accent = 0x2F7BF5;      // Samsung blue
        public static readonly int Track = 0x4A4C52;
        public static readonly int Positive = 0x3DD68C;
        public static readonly int Negative = 0xFF6B6B;

        public static View Box(float x, float y, float w, float h, int color, float radius = 0, float alpha = 1f)
        {
            var v = new View
            {
                Position = new Position(S(x), S(y)),
                Size = new Size(S(w), S(h)),
                BackgroundColor = Hex(color, alpha),
            };
            if (radius > 0) v.CornerRadius = S(radius);
            return v;
        }

        public static TextLabel Label(string text, float px, int color, float x, float y, float w, float h,
                                      HorizontalAlignment ha = HorizontalAlignment.Begin)
        {
            return new TextLabel(text ?? "")
            {
                Position = new Position(S(x), S(y)),
                Size = new Size(S(w), S(h)),
                PixelSize = S(px),
                TextColor = Hex(color),
                HorizontalAlignment = ha,
                VerticalAlignment = VerticalAlignment.Center,
                Ellipsis = true,
            };
        }

        public static void Move(View v, float y, int ms = 160)
        {
            var a = new Animation(ms);
            a.AnimateTo(v, "PositionY", S(y), new AlphaFunction(AlphaFunction.BuiltinFunctions.EaseOut));
            a.Play();
        }
    }

    /// <summary>One menu row: title, value underneath, and a switch for toggles.</summary>
    sealed class Row
    {
        public const float Height = 112, Width = 1160;
        public readonly SettingItem Item;
        public readonly View View;
        readonly View _bg;
        readonly TextLabel _title, _value;
        readonly View _track, _knob;
        bool _focused;

        public Row(SettingItem item, float y)
        {
            Item = item;
            View = new View { Position = new Position(0, Theme.S(y)), Size = new Size(Theme.S(Width), Theme.S(Height)) };
            _bg = Theme.Box(0, 4, Width, Height - 8, Theme.Focus, 14);
            _bg.Hide();
            View.Add(_bg);
            bool oneLine = item.Kind == ItemKind.Action;
            _title = Theme.Label(item.Title, 32, Theme.TextPrimary, 40, oneLine ? 4 : 14, Width - 200, oneLine ? Height - 8 : 46);
            View.Add(_title);
            _value = Theme.Label("", 26, Theme.TextSecondary, 40, 60, Width - (item.Kind == ItemKind.Toggle ? 200 : 80), 38);
            if (!oneLine) View.Add(_value);
            if (item.Kind == ItemKind.Toggle)
            {
                _track = Theme.Box(Width - 130, (Height - 40) / 2, 76, 40, Theme.Track, 20);
                _knob = Theme.Box(4, 4, 32, 32, 0xE6E6E6, 16);
                _track.Add(_knob);
                View.Add(_track);
            }
        }

        public void Update(SettingsModel m, bool focused)
        {
            _focused = focused;
            if (focused) _bg.Show(); else _bg.Hide();
            _title.TextColor = Theme.Hex(focused ? Theme.TextOnFocus : Theme.TextPrimary);
            _value.TextColor = Theme.Hex(focused ? Theme.TextOnFocusSecondary : Theme.TextSecondary);
            _value.Text = m.Format(Item);
            View.Opacity = (Item.Dimmed != null && Item.Dimmed(m)) ? 0.45f : 1f;
            if (_track != null)
            {
                bool on = m.GetToggle(Item);
                _track.BackgroundColor = Theme.Hex(on ? Theme.Accent : (focused ? 0x9A9DA5 : 0x6B6E76));
                _knob.PositionX = Theme.S(on ? 40 : 4);
                _knob.BackgroundColor = Theme.Hex(on ? 0xFFFFFF : 0xE6E6E6);
            }
        }
    }

    /// <summary>A popup over the page. Returns true from <see cref="Key"/> while it stays open.</summary>
    abstract class Popup
    {
        public const float X = 700, Y = 290, W = 1080, H = 470;
        public readonly View Root;
        protected readonly View Panel;

        protected Popup(string title)
        {
            Root = Theme.Box(0, 0, 1920, 1080, 0x000000, 0, 0.55f);
            Panel = Theme.Box(X, Y, W, H, Theme.Popup, 20);
            Root.Add(Panel);
            Panel.Add(Theme.Label(title, 34, Theme.TextPrimary, 60, 30, W - 120, 56));
        }

        protected void Hint(string text)
        {
            Panel.Add(Theme.Label(text, 24, Theme.TextSecondary, 60, H - 76, W - 120, 44, HorizontalAlignment.Center));
        }

        /// <summary>Handle a key; return false to close.</summary>
        public abstract bool Key(string key);
        /// <summary>Called once when the popup closes for any reason.</summary>
        public virtual void Close() { }
    }

    /// <summary>Samsung-style slider: Left/Right step, Up/Down big step, number keys type a value.
    /// Changes apply (and are stored) while sliding, so the strip can be judged live.</summary>
    sealed class NumberPopup : Popup
    {
        readonly SettingsModel _m;
        readonly SettingItem _it;
        readonly Action _changed;
        readonly TextLabel _value;
        readonly View _fill, _knob;
        readonly Tizen.NUI.Timer _save;
        double _v;
        string _typed = "";
        const float TrackX = 80, TrackY = 270, TrackW = W - 160;

        public NumberPopup(SettingsModel m, SettingItem it, Action changed) : base(it.Title)
        {
            _m = m; _it = it; _changed = changed;
            _v = m.Clamp(it, m.GetNumber(it));
            _value = Theme.Label("", 64, Theme.TextPrimary, 60, 120, W - 120, 100, HorizontalAlignment.Center);
            Panel.Add(_value);
            Panel.Add(Theme.Box(TrackX, TrackY, TrackW, 8, Theme.Track, 4));
            _fill = Theme.Box(TrackX, TrackY, 0, 8, Theme.Accent, 4);
            Panel.Add(_fill);
            _knob = Theme.Box(TrackX - 16, TrackY - 12, 32, 32, 0xFFFFFF, 16);
            Panel.Add(_knob);
            Panel.Add(Theme.Label(Label(it.Min), 24, Theme.TextSecondary, TrackX - 40, TrackY + 30, 200, 40));
            Panel.Add(Theme.Label(Label(m.MaxOf(it)), 24, Theme.TextSecondary, TrackX + TrackW - 160, TrackY + 30, 200, 40,
                                  HorizontalAlignment.End));
            Hint("Left/Right: adjust     Up/Down: faster     0-9: type     OK: done");
            _save = new Tizen.NUI.Timer(300);
            _save.Tick += (s, e) => { Commit(); return false; };
            Render();
        }

        string Label(double v)
        {
            if (v == 0 && _it.ZeroLabel != null) return _it.ZeroLabel;
            if (v == 0 && (_it.Key == "ledCount" || _it.Key.StartsWith("leds", StringComparison.Ordinal))) return "Auto";
            return SettingsModel.FormatNumber(_it, v);
        }

        void Render()
        {
            _value.Text = _typed.Length > 0 ? _typed : Label(_v);
            double max = _m.MaxOf(_it);
            double f = max > _it.Min ? (_v - _it.Min) / (max - _it.Min) : 0;
            f = Math.Max(0, Math.Min(1, f));
            _fill.SizeWidth = Theme.S((float)(TrackW * f));
            _knob.PositionX = Theme.S((float)(TrackX - 16 + TrackW * f));
        }

        void Set(double v)
        {
            _v = _m.Clamp(_it, v);
            Render();
            _save.Stop();
            _save.Start();
        }

        void Commit()
        {
            _save.Stop();
            if (_m.Clamp(_it, _m.GetNumber(_it)) != _v) _m.SetNumber(_it, _v);
            _changed();
        }

        public override bool Key(string key)
        {
            int digit = KeyNames.Digit(key);
            if (digit >= 0 && _it.Decimals == 0)
            {
                _typed = (_typed + digit).TrimStart('0');
                if (_typed.Length > 5) _typed = _typed.Substring(_typed.Length - 5);
                double t = _typed.Length == 0 ? 0 : double.Parse(_typed);
                Set(t);
                _value.Text = _typed.Length == 0 ? "0" : _typed;
                return true;
            }
            _typed = "";
            switch (key)
            {
                case KeyNames.Left: Set(_v - _it.Step); return true;
                case KeyNames.Right: Set(_v + _it.Step); return true;
                case KeyNames.Down: Set(_v - _it.BigStep); return true;
                case KeyNames.Up: Set(_v + _it.BigStep); return true;
                case KeyNames.Ok:
                case KeyNames.Back: return false;
            }
            return true;
        }

        public override void Close()
        {
            Commit();
            _save.Dispose();
        }
    }

    /// <summary>IPv4 entry with the remote: four octet boxes, Up/Down change, Left/Right move,
    /// number keys type. OK stores, Back cancels. No on-screen keyboard is needed.</summary>
    sealed class IpPopup : Popup
    {
        readonly SettingsModel _m;
        readonly SettingItem _it;
        readonly Action _changed;
        readonly int[] _oct = { 192, 168, 1, 0 };
        readonly View[] _boxes = new View[4];
        readonly TextLabel[] _labels = new TextLabel[4];
        readonly TextLabel _error;
        int _pos;
        string _typed = "";
        const float BoxW = 170, BoxH = 110, Gap = 50, BoxY = 140;

        public IpPopup(SettingsModel m, SettingItem it, Action changed) : base(it.Title)
        {
            _m = m; _it = it; _changed = changed;
            string cur = m.GetString(it);
            if (UserSettings.IsIPv4(cur))
            {
                string[] p = cur.Split('.');
                for (int i = 0; i < 4; i++) _oct[i] = int.Parse(p[i]);
            }
            float x0 = (W - (4 * BoxW + 3 * Gap)) / 2;
            for (int i = 0; i < 4; i++)
            {
                float x = x0 + i * (BoxW + Gap);
                _boxes[i] = Theme.Box(x, BoxY, BoxW, BoxH, 0x3A3C42, 14);
                Panel.Add(_boxes[i]);
                _labels[i] = Theme.Label("", 52, Theme.TextPrimary, 0, 0, BoxW, BoxH, HorizontalAlignment.Center);
                _boxes[i].Add(_labels[i]);
                if (i < 3) Panel.Add(Theme.Label(".", 52, Theme.TextSecondary, x + BoxW, BoxY, Gap, BoxH, HorizontalAlignment.Center));
            }
            _error = Theme.Label("", 26, Theme.Negative, 60, BoxY + BoxH + 40, W - 120, 44, HorizontalAlignment.Center);
            Panel.Add(_error);
            Hint("Up/Down: change     Left/Right: move     0-9: type     OK: save     Back: cancel");
            Render();
        }

        void Render()
        {
            for (int i = 0; i < 4; i++)
            {
                bool f = i == _pos;
                _boxes[i].BackgroundColor = Theme.Hex(f ? Theme.Focus : 0x3A3C42);
                _labels[i].TextColor = Theme.Hex(f ? Theme.TextOnFocus : Theme.TextPrimary);
                _labels[i].Text = (f && _typed.Length > 0) ? _typed : _oct[i].ToString();
            }
        }

        void Move(int d)
        {
            _typed = "";
            _pos = Math.Max(0, Math.Min(3, _pos + d));
        }

        public override bool Key(string key)
        {
            _error.Text = "";
            int digit = KeyNames.Digit(key);
            if (digit >= 0)
            {
                string t = _typed + digit;
                if (int.Parse(t) > 255) t = digit.ToString();
                _typed = t;
                _oct[_pos] = int.Parse(t);
                // a third digit, or a value that cannot take another, completes the octet
                if (_typed.Length == 3 || _oct[_pos] * 10 > 255) { if (_pos < 3) Move(1); else _typed = ""; }
                Render();
                return true;
            }
            switch (key)
            {
                case KeyNames.Left: Move(-1); break;
                case KeyNames.Right: Move(1); break;
                case KeyNames.Up: _typed = ""; _oct[_pos] = (_oct[_pos] + 1) % 256; break;
                case KeyNames.Down: _typed = ""; _oct[_pos] = (_oct[_pos] + 255) % 256; break;
                case KeyNames.Back: return false;
                case KeyNames.Ok:
                {
                    string ip = _oct[0] + "." + _oct[1] + "." + _oct[2] + "." + _oct[3];
                    if (_oct[0] == 0 || _oct[0] == 127 || _oct[0] >= 224)
                    {
                        _error.Text = ip + " is not a device address";
                        return true;
                    }
                    if (!_m.SetString(_it, ip))
                    {
                        _error.Text = "Could not save: " + (_m.SaveError ?? "invalid address");
                        return true;
                    }
                    _changed();
                    return false;
                }
            }
            Render();
            return true;
        }
    }

    /// <summary>Option list, like the TV's own drop-down: Up/Down, OK selects, Back cancels.</summary>
    sealed class ChoicePopup : Popup
    {
        readonly SettingsModel _m;
        readonly SettingItem _it;
        readonly Action _changed;
        readonly View _hl;
        readonly TextLabel[] _rows;
        int _sel;
        const float RowH = 84, RowY = 110;

        public ChoicePopup(SettingsModel m, SettingItem it, Action changed) : base(it.Title)
        {
            _m = m; _it = it; _changed = changed;
            _sel = Math.Max(0, Array.IndexOf(it.Choices, m.GetString(it)));
            _hl = Theme.Box(40, RowY + _sel * RowH, W - 80, RowH - 8, Theme.Focus, 14);
            Panel.Add(_hl);
            _rows = new TextLabel[it.Choices.Length];
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = Theme.Label(it.ChoiceLabels[i], 30, Theme.TextPrimary, 80, RowY + i * RowH, W - 160, RowH - 8);
                Panel.Add(_rows[i]);
            }
            Render();
        }

        void Render()
        {
            Theme.Move(_hl, RowY + _sel * RowH, 120);
            for (int i = 0; i < _rows.Length; i++)
                _rows[i].TextColor = Theme.Hex(i == _sel ? Theme.TextOnFocus : Theme.TextPrimary);
        }

        public override bool Key(string key)
        {
            switch (key)
            {
                case KeyNames.Up: if (_sel > 0) { _sel--; Render(); } return true;
                case KeyNames.Down: if (_sel < _rows.Length - 1) { _sel++; Render(); } return true;
                case KeyNames.Back: return false;
                case KeyNames.Ok:
                    _m.SetString(_it, _it.Choices[_sel]);
                    _changed();
                    return false;
            }
            return true;
        }
    }

    /// <summary>Confirmation dialog with two buttons, the destructive one first as on the TV.</summary>
    sealed class ConfirmPopup : Popup
    {
        readonly Action _confirm;
        readonly View[] _btn = new View[2];
        readonly TextLabel[] _txt = new TextLabel[2];
        int _sel = 1;   // focus starts on Cancel

        public ConfirmPopup(string title, string message, string action, Action confirm) : base(title)
        {
            _confirm = confirm;
            var msg = Theme.Label(message, 28, Theme.TextSecondary, 60, 110, W - 120, 150);
            msg.MultiLine = true;
            msg.Ellipsis = false;
            msg.VerticalAlignment = VerticalAlignment.Top;
            Panel.Add(msg);
            string[] names = { action, "Cancel" };
            for (int i = 0; i < 2; i++)
            {
                _btn[i] = Theme.Box(W / 2 - 330 + i * 340, H - 150, 320, 84, 0x3A3C42, 42);
                _txt[i] = Theme.Label(names[i], 30, Theme.TextPrimary, 0, 0, 320, 84, HorizontalAlignment.Center);
                _btn[i].Add(_txt[i]);
                Panel.Add(_btn[i]);
            }
            Render();
        }

        void Render()
        {
            for (int i = 0; i < 2; i++)
            {
                _btn[i].BackgroundColor = Theme.Hex(i == _sel ? Theme.Focus : 0x3A3C42);
                _txt[i].TextColor = Theme.Hex(i == _sel ? Theme.TextOnFocus : Theme.TextPrimary);
            }
        }

        public override bool Key(string key)
        {
            switch (key)
            {
                case KeyNames.Left: _sel = 0; Render(); return true;
                case KeyNames.Right: _sel = 1; Render(); return true;
                case KeyNames.Back: return false;
                case KeyNames.Ok:
                    if (_sel == 0) _confirm();
                    return false;
            }
            return true;
        }
    }

    /// <summary>Remote key names, normalised. The Samsung remote sends the X11 names below; the
    /// keyboard names make the emulator and a USB keyboard work too.</summary>
    static class KeyNames
    {
        public const string Up = "Up", Down = "Down", Left = "Left", Right = "Right", Ok = "OK", Back = "Back";

        public static string Normalise(string k)
        {
            switch (k)
            {
                case "Return": case "KP_Enter": case "Select": case "Enter": return Ok;
                case "XF86Back": case "Escape": case "BackSpace": return Back;
                case "KP_Up": return Up;
                case "KP_Down": return Down;
                case "KP_Left": return Left;
                case "KP_Right": return Right;
            }
            if (k != null && k.StartsWith("KP_", StringComparison.Ordinal) && k.Length == 4 && char.IsDigit(k[3]))
                return k.Substring(3);
            return k ?? "";
        }

        public static int Digit(string k)
        {
            return (k != null && k.Length == 1 && k[0] >= '0' && k[0] <= '9') ? k[0] - '0' : -1;
        }
    }
}
