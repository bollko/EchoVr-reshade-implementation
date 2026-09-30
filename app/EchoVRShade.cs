// Echo VR headset ReShade: installs the custom LibOVR ReShade build and edits the headset preset.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace EchoVRShade
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}
	}

	// Minimal INI editor that keeps unknown keys, sections and ordering intact (ReShade and the user edit these files too)
	class IniFile
	{
		readonly string _path;
		readonly List<string> _lines;

		public IniFile(string path)
		{
			_path = path;
			_lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
		}

		static string SectionOf(string line)
		{
			string t = line.Trim();
			return t.StartsWith("[") && t.EndsWith("]") ? t.Substring(1, t.Length - 2) : null;
		}

		int Find(string section, string key)
		{
			string current = "";
			for (int i = 0; i < _lines.Count; i++)
			{
				string s = SectionOf(_lines[i]);
				if (s != null) { current = s; continue; }
				if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
				int eq = _lines[i].IndexOf('=');
				if (eq > 0 && string.Equals(_lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
					return i;
			}
			return -1;
		}

		public string Get(string section, string key)
		{
			int i = Find(section, key);
			return i < 0 ? null : _lines[i].Substring(_lines[i].IndexOf('=') + 1).Trim();
		}

		public void Set(string section, string key, string value)
		{
			string line = key + "=" + value;
			int i = Find(section, key);
			if (i >= 0) { _lines[i] = line; return; }

			int sectionStart = -1;
			if (section.Length == 0)
				sectionStart = 0;
			else
				for (int k = 0; k < _lines.Count; k++)
					if (string.Equals(SectionOf(_lines[k]), section, StringComparison.OrdinalIgnoreCase)) { sectionStart = k + 1; break; }

			if (sectionStart < 0)
			{
				if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length != 0) _lines.Add("");
				_lines.Add("[" + section + "]");
				_lines.Add(line);
				return;
			}

			int sectionEnd = _lines.Count;
			for (int k = sectionStart; k < _lines.Count; k++)
				if (SectionOf(_lines[k]) != null) { sectionEnd = k; break; }
			int insertAt = sectionEnd;
			while (insertAt > sectionStart && _lines[insertAt - 1].Trim().Length == 0) insertAt--;
			_lines.Insert(insertAt, line);
		}

		public void SetDefault(string section, string key, string value)
		{
			if (Get(section, key) == null) Set(section, key, value);
		}

		public static void WriteAllTextAtomic(string path, string text)
		{
			string temp = path + ".tmp";
			File.WriteAllText(temp, text);
			if (File.Exists(path))
				File.Replace(temp, path, null);
			else
				File.Move(temp, path);
		}

		public void Save()
		{
			// Write to a temporary file and swap it in, so ReShade never reads a half-written file while live reloading
			string temp = _path + ".tmp";
			File.WriteAllLines(temp, _lines);
			if (File.Exists(_path))
				File.Replace(temp, _path, null);
			else
				File.Move(temp, _path);
		}
	}

	static class Theme
	{
		public static readonly Color Back = Color.FromArgb(22, 24, 29);
		public static readonly Color Card = Color.FromArgb(32, 35, 42);
		public static readonly Color Border = Color.FromArgb(48, 52, 62);
		public static readonly Color Track = Color.FromArgb(58, 63, 74);
		public static readonly Color Text = Color.FromArgb(232, 234, 237);
		public static readonly Color Muted = Color.FromArgb(150, 156, 166);
		public static readonly Color Disabled = Color.FromArgb(95, 100, 110);
		public static readonly Color Accent = Color.FromArgb(255, 138, 61);
		public static readonly Color AccentHover = Color.FromArgb(255, 164, 104);
		public static readonly Color Good = Color.FromArgb(80, 200, 120);
		public static readonly Color Warn = Color.FromArgb(240, 185, 60);

		public static readonly Font Body = new Font("Segoe UI", 9.5f);
		public static readonly Font Small = new Font("Segoe UI", 8.5f);
		public static readonly Font Semibold = new Font("Segoe UI Semibold", 10f);
		public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11.5f);
		public static readonly Font Title = new Font("Segoe UI Semibold", 17f);

		public static GraphicsPath Round(RectangleF r, float radius)
		{
			var p = new GraphicsPath();
			float d = radius * 2;
			p.AddArc(r.X, r.Y, d, d, 180, 90);
			p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			p.CloseFigure();
			return p;
		}
	}

	abstract class ThemedControl : Control
	{
		protected ThemedControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = Theme.Card;
			ForeColor = Theme.Text;
			Font = Theme.Body;
		}
		protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
		protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
	}

	class Card : Panel
	{
		public string Title;
		public Card()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = Theme.Back;
		}
		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Back);
			using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
			using (var fill = new SolidBrush(Theme.Card))
			using (var pen = new Pen(Theme.Border))
			{
				g.FillPath(fill, path);
				g.DrawPath(pen, path);
			}
			TextRenderer.DrawText(g, Title, Theme.CardTitle, new Point(16, 14), Theme.Text);
		}
	}

	class FlatButton : ThemedControl
	{
		public bool Primary;
		bool _hover;
		public FlatButton() { Cursor = Cursors.Hand; }
		protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
			base.OnKeyDown(e);
		}
		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Card);
			Color fill, text, border;
			if (!Enabled) { fill = Theme.Card; text = Theme.Disabled; border = Theme.Border; }
			else if (Primary) { fill = _hover ? Theme.AccentHover : Theme.Accent; text = Color.FromArgb(28, 20, 14); border = fill; }
			else { fill = _hover ? Theme.Track : Theme.Card; text = Theme.Text; border = Theme.Track; }
			using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7))
			using (var b = new SolidBrush(fill))
			using (var p = new Pen(border))
			{
				g.FillPath(b, path);
				g.DrawPath(p, path);
			}
			TextRenderer.DrawText(g, Text, Primary ? Theme.Semibold : Theme.Body, ClientRectangle, text,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		}
	}

	class ToggleSwitch : ThemedControl
	{
		bool _checked;
		public string Description, Badge;
		public event EventHandler CheckedChanged;
		public ToggleSwitch() { Cursor = Cursors.Hand; }
		public bool Checked
		{
			get { return _checked; }
			set
			{
				if (_checked == value) return;
				_checked = value;
				Invalidate();
				if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
			}
		}
		protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space) Checked = !Checked;
			base.OnKeyDown(e);
		}
		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Card);
			var track = new RectangleF(0.5f, 2.5f, 38, 20);
			Color trackColor = !Enabled ? Theme.Border : (_checked ? Theme.Accent : Theme.Track);
			using (var path = Theme.Round(track, 10))
			using (var b = new SolidBrush(trackColor))
				g.FillPath(b, path);
			float knobX = _checked ? track.Right - 18 : track.X + 2;
			using (var b = new SolidBrush(Enabled ? Color.White : Theme.Muted))
				g.FillEllipse(b, knobX, track.Y + 2, 16, 16);

			Color text = Enabled ? Theme.Text : Theme.Disabled;
			if (string.IsNullOrEmpty(Description))
			{
				int badgeWidth = 0;
				if (!string.IsNullOrEmpty(Badge))
				{
					badgeWidth = TextRenderer.MeasureText(Badge, Theme.Small).Width + 8;
					TextRenderer.DrawText(g, Badge, Theme.Small, new Rectangle(Width - badgeWidth, 0, badgeWidth, 25), Enabled ? Theme.Muted : Theme.Disabled,
						TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
				}
				TextRenderer.DrawText(g, Text, Theme.Semibold, new Rectangle(50, 0, Width - 50 - badgeWidth, 25), text,
					TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			}
			else
			{
				TextRenderer.DrawText(g, Text, Theme.Semibold, new Point(50, 3), text);
				TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(50, 24, Width - 50, Height - 24),
					Enabled ? Theme.Muted : Theme.Disabled, TextFormatFlags.WordBreak);
			}
		}
	}

	class Slider : ThemedControl
	{
		decimal _value;
		public decimal Minimum = 0, Maximum = 1, Step = 0.01m;
		public int Decimals = 2;
		public event EventHandler ValueChanged;
		const int Pad = 8;

		public Slider() { Cursor = Cursors.Hand; }

		public decimal Value
		{
			get { return _value; }
			set
			{
				value = Math.Max(Minimum, Math.Min(Maximum, value));
				if (_value == value) return;
				_value = value;
				Invalidate();
				if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
			}
		}

		void SetFromMouse(int x)
		{
			float frac = Math.Max(0f, Math.Min(1f, (x - Pad) / (float)(Width - 2 * Pad)));
			decimal raw = Minimum + (decimal)frac * (Maximum - Minimum);
			Value = Minimum + Math.Round((raw - Minimum) / Step) * Step;
		}
		protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) SetFromMouse(e.X); base.OnMouseDown(e); }
		protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetFromMouse(e.X); base.OnMouseMove(e); }
		protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? Step : -Step; base.OnMouseWheel(e); }
		protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Left) Value -= Step;
			if (e.KeyCode == Keys.Right) Value += Step;
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Card);

			TextRenderer.DrawText(g, Text, Theme.Body, new Point(Pad - 3, 0), Enabled ? Theme.Muted : Theme.Disabled);
			string valueText = _value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
			TextRenderer.DrawText(g, valueText, Theme.Semibold, new Rectangle(0, 0, Width - Pad + 3, 20), Enabled ? Theme.Text : Theme.Disabled,
				TextFormatFlags.Right);

			float frac = Maximum > Minimum ? (float)((_value - Minimum) / (Maximum - Minimum)) : 0f;
			float y = 30, left = Pad, right = Width - Pad;
			float thumbX = left + frac * (right - left);
			using (var path = Theme.Round(new RectangleF(left, y - 2, right - left, 4), 2))
			using (var b = new SolidBrush(Theme.Track))
				g.FillPath(b, path);
			if (thumbX - left > 1)
				using (var path = Theme.Round(new RectangleF(left, y - 2, thumbX - left, 4), 2))
				using (var b = new SolidBrush(Enabled ? Theme.Accent : Theme.Disabled))
					g.FillPath(b, path);
			using (var b = new SolidBrush(Enabled ? Color.White : Theme.Muted))
				g.FillEllipse(b, thumbX - 7, y - 7, 14, 14);
			if (Focused)
				using (var p = new Pen(Theme.Accent, 2))
					g.DrawEllipse(p, thumbX - 8, y - 8, 16, 16);
		}
		protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
		protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
	}

	class StatusLine : ThemedControl
	{
		Color _dot = Theme.Muted;
		public void Set(string text, Color dot) { _dot = dot; Text = text; }
		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Card);
			using (var b = new SolidBrush(_dot))
				g.FillEllipse(b, 2, 6, 9, 9);
			TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(18, 0, Width - 18, Height), Theme.Text, TextFormatFlags.WordBreak);
		}
	}

	class LookParam
	{
		public string Key, Label, Group;
		public int Component = -1; // -1 for a single value, otherwise the index into a float3 value
		public decimal Min, Max, Step, Default;
		public int Decimals = 2;
		public Slider Slider;
	}

	class LookEffect
	{
		public string Technique, Shader, Title, Tab;
		public bool DefaultOn;
		public LookParam[] Params;
		public ToggleSwitch Toggle;
		public string Id { get { return Technique + "@" + Shader; } }
	}

	class ExtraEffect
	{
		public string Id, Label, File;
		public bool UsesDepth;
		public ToggleSwitch Toggle;
	}

	class TabStrip : ThemedControl
	{
		public string[] Tabs = new string[0];
		public const int TabWidth = 92;
		int _selected;
		public event EventHandler SelectedChanged;
		public TabStrip() { Cursor = Cursors.Hand; }
		public int Selected
		{
			get { return _selected; }
			set
			{
				if (_selected == value) return;
				_selected = value;
				Invalidate();
				if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
			}
		}
		protected override void OnMouseDown(MouseEventArgs e)
		{
			int i = (e.X - 3) / TabWidth;
			if (i >= 0 && i < Tabs.Length) Selected = i;
			base.OnMouseDown(e);
		}
		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Theme.Card);
			using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9))
			using (var b = new SolidBrush(Theme.Back))
				g.FillPath(b, path);
			for (int i = 0; i < Tabs.Length; i++)
			{
				var r = new Rectangle(3 + i * TabWidth, 3, TabWidth, Height - 6);
				if (i == _selected)
					using (var path = Theme.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), 7))
					using (var b = new SolidBrush(Theme.Track))
						g.FillPath(b, path);
				Color text = !Enabled ? Theme.Disabled : (i == _selected ? Theme.Text : Theme.Muted);
				TextRenderer.DrawText(g, Tabs[i], i == _selected ? Theme.Semibold : Theme.Body, r, text,
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			}
		}
	}

	class MainForm : Form
	{
		const string VrIni = "ReShadeVR.ini";
		const string VrPreset = "ReShadeVRPreset.ini";
		const string DesktopIni = "ReShade.ini";
		const string StateIni = "EchoVRShade.ini";
		const string EmbeddedDll = "ReShade64-libovr.dll";

		static LookParam Num(string key, string label, decimal min, decimal max, decimal step, decimal def, int decimals = 2)
		{
			return new LookParam { Key = key, Label = label, Min = min, Max = max, Step = step, Default = def, Decimals = decimals };
		}
		static LookParam Rgb(string key, int component, string group, decimal def)
		{
			return new LookParam { Key = key, Component = component, Label = new[] { "Red", "Green", "Blue" }[component], Group = group, Min = 0.5m, Max = 1.5m, Step = 0.01m, Default = def };
		}
		static LookParam InGroup(string group, LookParam p) { p.Group = group; return p; }

		static readonly string[] TabNames = { "Color", "Tone", "Bloom", "Sharpen", "All effects" };
		const string AllEffectsTab = "All effects";
		static readonly string[] DepthDefines = { "RESHADE_DEPTH_INPUT_IS_REVERSED", "RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN", "RESHADE_DEPTH_INPUT_IS_LOGARITHMIC" };
		static readonly Regex DepthRegex = new Regex(@"LinearizedDepth|DepthBuffer|linear_?depth|get_?depth|DepthTex", RegexOptions.IgnoreCase);
		static readonly Regex TechniqueRegex = new Regex(@"^[ \t]*technique[ \t]+(\w+)\s*(<[^>]*>)?", RegexOptions.Multiline);

		// Listed in the order ReShade renders them (TechniqueSorting)
		readonly LookEffect[] Effects =
		{
			new LookEffect { Technique = "Tonemap", Shader = "Tonemap.fx", Title = "Tone", Tab = "Tone", Params = new[] {
				Num("Exposure", "Exposure", -1m, 1m, 0.01m, 0m),
				Num("Gamma", "Gamma", 0m, 2m, 0.01m, 1m),
				Num("Saturation", "Saturation", -1m, 1m, 0.01m, 0m),
				Num("Bleach", "Bleach bypass", 0m, 1m, 0.01m, 0m) } },
			new LookEffect { Technique = "LiftGammaGain", Shader = "LiftGammaGain.fx", Title = "Color balance", Tab = "Color", DefaultOn = true, Params = new[] {
				Rgb("RGB_Lift", 0, "Shadows", 1m), Rgb("RGB_Lift", 1, "Shadows", 1m), Rgb("RGB_Lift", 2, "Shadows", 1m),
				Rgb("RGB_Gamma", 0, "Midtones", 1m), Rgb("RGB_Gamma", 1, "Midtones", 1m), Rgb("RGB_Gamma", 2, "Midtones", 1m),
				Rgb("RGB_Gain", 0, "Highlights", 1.06m), Rgb("RGB_Gain", 1, "Highlights", 1.02m), Rgb("RGB_Gain", 2, "Highlights", 0.94m) } },
			new LookEffect { Technique = "Curves", Shader = "Curves.fx", Title = "Contrast", Tab = "Tone", Params = new[] {
				Num("Contrast", "Amount", -1m, 1m, 0.01m, 0.15m) } },
			new LookEffect { Technique = "Vibrance", Shader = "Vibrance.fx", Title = "Vibrance", Tab = "Tone", DefaultOn = true, Params = new[] {
				Num("Vibrance", "Amount", -1m, 1m, 0.01m, 0.15m) } },
			new LookEffect { Technique = "MagicBloom", Shader = "MagicBloom.fx", Title = "Bloom", Tab = "Bloom", DefaultOn = true, Params = new[] {
				InGroup("Glow", Num("fBloom_Intensity", "Intensity", 0m, 2m, 0.05m, 0.35m)),
				InGroup("Glow", Num("fBloom_Threshold", "Threshold", 1m, 10m, 0.1m, 3.5m, 1)),
				InGroup("Auto-exposure", Num("fExposure", "Target exposure", 0m, 1m, 0.05m, 0.3m)),
				InGroup("Auto-exposure", Num("fAdapt_Speed", "Adapt speed", 0.01m, 1m, 0.01m, 0.05m)),
				InGroup("Auto-exposure", Num("fAdapt_Sensitivity", "Sensitivity", 0m, 3m, 0.05m, 1m)) } },
			new LookEffect { Technique = "ContrastAdaptiveSharpen", Shader = "CAS.fx", Title = "Sharpening (CAS)", Tab = "Sharpen", Params = new[] {
				Num("Sharpening", "Strength", 0m, 1m, 0.05m, 0.4m),
				Num("Contrast", "Contrast adaptation", 0m, 1m, 0.05m, 0m) } },
		};

		string _folder;
		bool _loading;
		readonly Timer _saveTimer = new Timer { Interval = 300 };

		readonly Label lblFolder = new Label();
		readonly StatusLine status = new StatusLine();
		readonly FlatButton btnInstall = new FlatButton(), btnUninstall = new FlatButton(), btnLaunch = new FlatButton();
		readonly Card lookCard = new Card();
		readonly ToggleSwitch chkDesktopMenu = new ToggleSwitch();

		readonly ToggleSwitch[] depthToggles = { new ToggleSwitch { Text = "Reversed" }, new ToggleSwitch { Text = "Flipped" }, new ToggleSwitch { Text = "Log" } };
		readonly List<ExtraEffect> _extras = new List<ExtraEffect>();
		readonly Panel _extrasList = new Panel();
		readonly TextBox _search = new TextBox();
		string _scannedFolder;

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

		[DllImport("dwmapi.dll")]
		static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			int dark = 1;
			DwmSetWindowAttribute(Handle, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
			int caption = Theme.Back.R | (Theme.Back.G << 8) | (Theme.Back.B << 16);
			DwmSetWindowAttribute(Handle, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
		}

		public MainForm()
		{
			Text = "Echo VR Headset ReShade";
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(640, 728);
			BackColor = Theme.Back;
			ForeColor = Theme.Text;
			Font = Theme.Body;
			DoubleBuffered = true;

			var lblTitle = new Label { Text = "Echo VR Headset ReShade", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Left = 22, Top = 16 };

			// Install card
			var installCard = new Card { Title = "Install", Left = 20, Top = 60, Width = 600, Height = 176 };
			lblFolder.SetBounds(18, 50, 466, 20);
			lblFolder.ForeColor = Theme.Muted; lblFolder.BackColor = Theme.Card; lblFolder.AutoEllipsis = true;
			var btnBrowse = new FlatButton { Text = "Browse" };
			btnBrowse.SetBounds(492, 44, 90, 32);
			btnBrowse.Click += delegate { Browse(); };
			status.SetBounds(18, 84, 564, 40);
			btnInstall.Primary = true; btnInstall.Text = "Install / Update"; btnInstall.SetBounds(18, 128, 150, 34); btnInstall.Click += delegate { Install(); };
			btnUninstall.Text = "Uninstall"; btnUninstall.SetBounds(178, 128, 110, 34); btnUninstall.Click += delegate { Uninstall(); };
			btnLaunch.Text = "Launch Echo"; btnLaunch.SetBounds(298, 128, 130, 34); btnLaunch.Click += delegate { Launch(); };
			installCard.Controls.AddRange(new Control[] { lblFolder, btnBrowse, status, btnInstall, btnUninstall, btnLaunch });

			// Headset look card
			lookCard.Title = "Headset look";
			lookCard.SetBounds(20, 252, 600, 456);
			var lblHint = new Label { Text = "Saves instantly and applies in the headset while you play.", ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Left = 18, Top = 42 };
			lookCard.Controls.Add(lblHint);

			var tabs = new TabStrip { Tabs = TabNames };
			tabs.SetBounds(18, 70, TabNames.Length * TabStrip.TabWidth + 6, 34);
			lookCard.Controls.Add(tabs);
			var pages = new List<Panel>();
			foreach (string tab in TabNames)
			{
				var page = new Panel { BackColor = Theme.Card, Visible = pages.Count == 0 };
				page.SetBounds(18, 116, 564, 276);
				string name = tab;
				if (name == AllEffectsTab)
					BuildAllEffectsPage(page);
				else
					BuildPage(page, Effects.Where(e => e.Tab == name).ToList());
				pages.Add(page);
				lookCard.Controls.Add(page);
			}
			tabs.SelectedChanged += delegate { for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == tabs.Selected; };

			chkDesktopMenu.Text = "Link Home-key menu";
			chkDesktopMenu.Description = "Desktop ReShade menu edits this preset";
			chkDesktopMenu.SetBounds(18, 400, 330, 44);
			chkDesktopMenu.CheckedChanged += delegate { DesktopMenuChanged(); };
			var btnDefaults = new FlatButton { Text = "Reset to defaults" };
			btnDefaults.SetBounds(442, 406, 140, 34);
			btnDefaults.Click += delegate { ResetDefaults(); };
			lookCard.Controls.AddRange(new Control[] { chkDesktopMenu, btnDefaults });

			Controls.AddRange(new Control[] { lblTitle, installCard, lookCard });

			_saveTimer.Tick += delegate { _saveTimer.Stop(); SaveLook(); };
			Activated += delegate { if (!_saveTimer.Enabled) RefreshAll(); };

			string saved = LoadSetting();
			SetFolder(saved);
		}

		// Lays out effects in two columns, or an effect with slider groups (e.g. Shadows / Midtones / Highlights) across the full width
		void BuildPage(Panel page, List<LookEffect> effects)
		{
			const int width = 564, gap = 18, sliderHeight = 42;
			int half = (width - gap) / 2;
			int[] columnY = { 0, 0 };
			foreach (var effect in effects)
			{
				var groups = effect.Params.Select(p => p.Group).Distinct().ToList();
				bool grouped = groups[0] != null;
				int col = grouped || columnY[0] <= columnY[1] ? 0 : 1;
				int x = col * (half + gap);
				int y = grouped ? Math.Max(columnY[0], columnY[1]) : columnY[col];

				effect.Toggle = new ToggleSwitch { Text = effect.Title };
				effect.Toggle.SetBounds(x, y, grouped ? width : half, 26);
				var captured = effect;
				effect.Toggle.CheckedChanged += delegate { UpdateEffectEnabled(captured); LookChanged(); };
				page.Controls.Add(effect.Toggle);
				y += 34;

				if (grouped)
				{
					int groupWidth = (width - gap * (groups.Count - 1)) / groups.Count;
					int bottom = y;
					for (int g = 0; g < groups.Count; g++)
					{
						int gx = g * (groupWidth + gap), gy = y;
						page.Controls.Add(new Label { Text = groups[g], Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Left = gx + 5, Top = gy });
						gy += 22;
						string group = groups[g];
						foreach (var p in effect.Params.Where(q => q.Group == group))
						{
							AddSlider(page, p, gx, gy, groupWidth);
							gy += sliderHeight;
						}
						bottom = Math.Max(bottom, gy);
					}
					columnY[0] = columnY[1] = bottom + 14;
				}
				else
				{
					foreach (var p in effect.Params)
					{
						AddSlider(page, p, x, y, half);
						y += sliderHeight;
					}
					columnY[col] = y + 14;
				}
			}
		}

		void BuildAllEffectsPage(Panel page)
		{
			page.Controls.Add(new Label { Text = "Every effect in reshade-shaders. Depth-based and heavy effects can cost a lot of frame time in VR.",
				Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Left = 5, Top = 0 });

			_search.BackColor = Theme.Back; _search.ForeColor = Theme.Text; _search.BorderStyle = BorderStyle.FixedSingle;
			_search.SetBounds(5, 24, 150, 26);
			_search.HandleCreated += delegate { SendMessage(_search.Handle, 0x1501, (IntPtr)1, "Search effects"); }; // EM_SETCUEBANNER
			_search.TextChanged += delegate { LayoutExtras(); };
			page.Controls.Add(_search);

			page.Controls.Add(new Label { Text = "Depth:", ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Left = 166, Top = 28 });
			int[] x = { 212, 336, 448 }, w = { 120, 108, 100 };
			for (int i = 0; i < depthToggles.Length; i++)
			{
				depthToggles[i].SetBounds(x[i], 25, w[i], 26);
				depthToggles[i].CheckedChanged += delegate { LookChanged(); };
				page.Controls.Add(depthToggles[i]);
			}

			_extrasList.BackColor = Theme.Card;
			_extrasList.AutoScroll = true;
			_extrasList.SetBounds(0, 60, 564, 216);
			_extrasList.HandleCreated += delegate { SetWindowTheme(_extrasList.Handle, "DarkMode_Explorer", null); };
			page.Controls.Add(_extrasList);
		}

		// Finds every technique in the folder's shaders, except the ones managed on the other tabs
		void RebuildExtras()
		{
			_scannedFolder = _folder;
			_extrasList.SuspendLayout();
			foreach (Control c in _extrasList.Controls.Cast<Control>().ToList()) c.Dispose();
			_extras.Clear();

			string dir = P(@"reshade-shaders\Shaders");
			if (Directory.Exists(dir))
			{
				var managed = Effects.Select(e => e.Id).ToList();
				foreach (string file in Directory.GetFiles(dir, "*.fx", SearchOption.AllDirectories))
				{
					string text;
					try { text = File.ReadAllText(file); } catch { continue; }
					bool usesDepth = DepthRegex.IsMatch(text);
					foreach (Match m in TechniqueRegex.Matches(text))
					{
						string annotations = m.Groups[2].Value;
						if (Regex.IsMatch(annotations, @"hidden\s*=\s*(true|1)")) continue;
						string id = m.Groups[1].Value + "@" + Path.GetFileName(file);
						if (managed.Contains(id) || _extras.Any(e => e.Id == id)) continue;
						Match label = Regex.Match(annotations, "ui_label\\s*=\\s*\"([^\"]*)\"");
						_extras.Add(new ExtraEffect { Id = id, Label = label.Success ? label.Groups[1].Value : m.Groups[1].Value, File = Path.GetFileName(file), UsesDepth = usesDepth });
					}
				}
			}
			_extras.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

			foreach (var extra in _extras)
			{
				extra.Toggle = new ToggleSwitch { Text = extra.Label, Badge = (extra.UsesDepth ? "depth  \u00B7  " : "") + extra.File };
				extra.Toggle.CheckedChanged += delegate { LookChanged(); };
				_extrasList.Controls.Add(extra.Toggle);
			}
			LayoutExtras();
			_extrasList.ResumeLayout();
		}

		void LayoutExtras()
		{
			string query = _search.Text.Trim();
			_extrasList.AutoScrollPosition = new Point(0, 0);
			int y = 0;
			foreach (var extra in _extras)
			{
				bool show = query.Length == 0 || extra.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || extra.File.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
				extra.Toggle.Visible = show;
				if (!show) continue;
				extra.Toggle.SetBounds(4, y, 536, 26);
				y += 36;
			}
		}

		void AddSlider(Panel page, LookParam p, int x, int y, int width)
		{
			p.Slider = new Slider { Text = p.Label, Minimum = p.Min, Maximum = p.Max, Step = p.Step, Decimals = p.Decimals };
			p.Slider.SetBounds(x, y, width, 40);
			p.Slider.ValueChanged += delegate { LookChanged(); };
			page.Controls.Add(p.Slider);
		}

		static void UpdateEffectEnabled(LookEffect effect)
		{
			foreach (var p in effect.Params) p.Slider.Enabled = effect.Toggle.Checked;
		}

		string BuildDefaultPreset()
		{
			var sb = new System.Text.StringBuilder();
			sb.Append("PreprocessorDefinitions=MAGICBLOOM_NODIRT=1\r\n");
			sb.Append("Techniques=" + string.Join(",", Effects.Where(e => e.DefaultOn).Select(e => e.Id)) + "\r\n");
			sb.Append("TechniqueSorting=" + string.Join(",", Effects.Select(e => e.Id)) + "\r\n");
			foreach (var effect in Effects)
			{
				sb.Append("\r\n[" + effect.Shader + "]\r\n");
				foreach (string key in effect.Params.Select(p => p.Key).Distinct())
				{
					var parts = effect.Params.Where(p => p.Key == key).OrderBy(p => p.Component).ToList();
					string value = parts[0].Component < 0 ? F(parts[0].Default) : string.Join(",", parts.Select(p => F(p.Default)));
					sb.Append(key + "=" + value + "\r\n");
				}
			}
			return sb.ToString();
		}

		string P(string name) { return Path.Combine(_folder, name); }

		// ---- Folder and status ----

		static string SettingsPath
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EchoVRShade", "folder.txt"); }
		}
		static string LoadSetting()
		{
			try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null; } catch { return null; }
		}

		static string NormalizeFolder(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (File.Exists(Path.Combine(path, "echovr.exe"))) return path;
			string bin = Path.Combine(path, "bin", "win10");
			return File.Exists(Path.Combine(bin, "echovr.exe")) ? bin : null;
		}

		void Browse()
		{
			using (var dlg = new FolderBrowserDialog { Description = "Select the Echo VR folder (the one containing bin\\win10\\echovr.exe)" })
			{
				if (dlg.ShowDialog(this) != DialogResult.OK) return;
				if (NormalizeFolder(dlg.SelectedPath) == null) { Msg("echovr.exe was not found in that folder or in its bin\\win10 subfolder."); return; }
				SetFolder(dlg.SelectedPath);
			}
		}

		void SetFolder(string path)
		{
			_folder = NormalizeFolder(path);
			lblFolder.Text = _folder ?? "No folder selected";
			if (_folder != null)
			{
				try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); File.WriteAllText(SettingsPath, _folder); } catch { }
			}
			RefreshAll();
		}

		bool IsGameRunning()
		{
			foreach (Process p in Process.GetProcessesByName("echovr"))
			{
				try
				{
					if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), _folder, StringComparison.OrdinalIgnoreCase)) return true;
				}
				catch { return true; } // Cannot tell which install it is, so assume this one
			}
			return false;
		}

		static bool IsReShade(string path)
		{
			try { return FileVersionInfo.GetVersionInfo(path).ProductName == "ReShade"; } catch { return false; }
		}

		static byte[] _embedded;
		static byte[] EmbeddedDllBytes()
		{
			if (_embedded == null)
				using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDll))
				using (var m = new MemoryStream()) { s.CopyTo(m); _embedded = m.ToArray(); }
			return _embedded;
		}
		static bool SameAsEmbedded(string path)
		{
			using (var sha = SHA256.Create())
				return sha.ComputeHash(File.ReadAllBytes(path)).SequenceEqual(sha.ComputeHash(EmbeddedDllBytes()));
		}

		void RefreshAll()
		{
			bool valid = _folder != null;
			btnInstall.Enabled = btnLaunch.Enabled = valid;
			btnUninstall.Enabled = false;
			lookCard.Enabled = valid && File.Exists(P(VrPreset));
			if (!valid) { status.Set("Pick your Echo VR folder.", Theme.Muted); return; }

			string dll = P("dinput8.dll");
			bool installed = File.Exists(dll) && IsReShade(dll);
			btnUninstall.Enabled = installed;

			string text;
			Color dot = Theme.Muted;
			if (installed && SameAsEmbedded(dll)) { text = "Installed."; dot = Theme.Good; }
			else if (installed) { text = "Installed, but an older build. Click Install / Update."; dot = Theme.Warn; }
			else if (File.Exists(dll)) { text = "Not installed: a different dinput8.dll (another mod) is in this folder."; dot = Theme.Warn; }
			else text = "Not installed.";

			string shaders = P(@"reshade-shaders\Shaders");
			var enabledIds = File.Exists(P(VrPreset)) ? SplitList(new IniFile(P(VrPreset)).Get("", "Techniques")) : Effects.Where(e => e.DefaultOn).Select(e => e.Id).ToList();
			var missing = Effects.Where(e => enabledIds.Contains(e.Id)).Select(e => e.Shader).Distinct().Where(n => !Directory.Exists(shaders) || Directory.GetFiles(shaders, n, SearchOption.AllDirectories).Length == 0).ToList();
			if (missing.Count > 0) dot = Theme.Warn;
			if (missing.Count > 0) text += " Missing shaders in reshade-shaders\\Shaders: " + string.Join(", ", missing) + ".";
			if (IsGameRunning()) text += " Echo is running.";
			status.Set(text, dot);

			if (lookCard.Enabled && _scannedFolder != _folder) RebuildExtras();
			if (lookCard.Enabled) LoadLook();
		}

		// ---- Install / uninstall / launch ----

		static string FreeName(string path)
		{
			string candidate = path;
			for (int i = 2; File.Exists(candidate); i++) candidate = path + i;
			return candidate;
		}

		void Install()
		{
			if (IsGameRunning()) { Msg("Close Echo first."); return; }
			try
			{
				string dinput = P("dinput8.dll");
				if (File.Exists(dinput) && !IsReShade(dinput)) { Msg("This folder already has a dinput8.dll from another mod, so it was not overwritten."); return; }

				// A ReShade dxgi.dll never loads in Echo (the Oculus runtime loads the system dxgi.dll first), so move it out of the way
				string dxgi = P("dxgi.dll");
				if (File.Exists(dxgi) && IsReShade(dxgi)) File.Move(dxgi, FreeName(P("dxgi.dll.bak")));

				File.WriteAllBytes(dinput, EmbeddedDllBytes());
				CopyBundledShaders();

				var vr = new IniFile(P(VrIni));
				vr.SetDefault("GENERAL", "EffectSearchPaths", @".\reshade-shaders\Shaders\**");
				vr.SetDefault("GENERAL", "TextureSearchPaths", @".\reshade-shaders\Textures\**");
				vr.SetDefault("GENERAL", "PresetPath", @".\" + VrPreset);
				vr.SetDefault("GENERAL", "SkipLoadingDisabledEffects", "1");
				vr.SetDefault("GENERAL", "PerformanceMode", "0");
				vr.Save();

				if (!File.Exists(P(VrPreset))) File.WriteAllText(P(VrPreset), BuildDefaultPreset());

				RefreshAll();
				Msg("Installed. Start Echo with \"Launch Echo\" (the Oculus app always starts its own install).");
			}
			catch (Exception ex) { Msg("Install failed: " + ex.Message); }
		}

		// Copies the reshade-shaders folder shipped next to this app into the game folder, keeping any files already there
		void CopyBundledShaders()
		{
			string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reshade-shaders");
			if (!Directory.Exists(source)) return;
			foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
			{
				string target = P(Path.Combine("reshade-shaders", file.Substring(source.Length).TrimStart('\\')));
				if (File.Exists(target)) continue;
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.Copy(file, target);
			}
		}

		void Uninstall()
		{
			if (IsGameRunning()) { Msg("Close Echo first."); return; }
			try
			{
				if (IsDesktopUsingVrPreset()) SetDesktopMenu(false);

				string dinput = P("dinput8.dll");
				if (File.Exists(dinput) && IsReShade(dinput)) File.Delete(dinput);

				string dxgi = P("dxgi.dll"), bak = P("dxgi.dll.bak");
				if (!File.Exists(dxgi) && File.Exists(bak)) File.Move(bak, dxgi);

				RefreshAll();
				Msg("Uninstalled. Your headset preset files were kept.");
			}
			catch (Exception ex) { Msg("Uninstall failed: " + ex.Message); }
		}

		void Launch()
		{
			if (IsGameRunning()) { Msg("Echo is already running from this folder."); return; }
			try { Process.Start(new ProcessStartInfo(P("echovr.exe")) { WorkingDirectory = _folder, UseShellExecute = true }); }
			catch (Exception ex) { Msg("Could not start Echo: " + ex.Message); }
		}

		// ---- Desktop menu link ----

		bool IsDesktopUsingVrPreset()
		{
			string v = new IniFile(P(DesktopIni)).Get("GENERAL", "PresetPath") ?? "";
			return string.Equals(Path.GetFileName(v), VrPreset, StringComparison.OrdinalIgnoreCase);
		}

		void SetDesktopMenu(bool on)
		{
			var desktop = new IniFile(P(DesktopIni));
			var state = new IniFile(P(StateIni));
			string current = desktop.Get("GENERAL", "PresetPath") ?? "";
			if (on)
			{
				if (!string.Equals(Path.GetFileName(current), VrPreset, StringComparison.OrdinalIgnoreCase))
				{
					state.Set("ECHOVRSHADE", "DesktopPresetPath", current);
					state.Save();
				}
				desktop.Set("GENERAL", "PresetPath", @".\" + VrPreset);
			}
			else
			{
				string previous = state.Get("ECHOVRSHADE", "DesktopPresetPath");
				desktop.Set("GENERAL", "PresetPath", string.IsNullOrEmpty(previous) ? @".\ReShadePreset.ini" : previous);
			}
			desktop.Save();
		}

		void DesktopMenuChanged()
		{
			if (_loading) return;
			if (IsGameRunning())
			{
				Msg("Close Echo first. ReShade rewrites ReShade.ini when the game exits, which would undo this.");
				_loading = true; chkDesktopMenu.Checked = !chkDesktopMenu.Checked; _loading = false;
				return;
			}
			try { SetDesktopMenu(chkDesktopMenu.Checked); }
			catch (Exception ex) { Msg("Could not update ReShade.ini: " + ex.Message); }
		}

		// ---- Headset preset ----

		static List<string> SplitList(string value)
		{
			return (value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
		}
		static double ParseNumber(string value, double fallback)
		{
			double d;
			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
		}
		static string F(decimal value) { return value.ToString("0.000000", CultureInfo.InvariantCulture); }
		static void SetNumber(Slider n, double value)
		{
			n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)value));
		}

		void LoadLook()
		{
			_loading = true;
			try
			{
				var p = new IniFile(P(VrPreset));
				var techniques = SplitList(p.Get("", "Techniques"));
				foreach (var effect in Effects)
				{
					effect.Toggle.Checked = techniques.Contains(effect.Id);
					foreach (var param in effect.Params)
					{
						string raw = p.Get(effect.Shader, param.Key);
						double fallback = (double)param.Default;
						SetNumber(param.Slider, param.Component < 0
							? ParseNumber(raw, fallback)
							: ParseNumber(SplitList(raw).ElementAtOrDefault(param.Component), fallback));
					}
					UpdateEffectEnabled(effect);
				}

				foreach (var extra in _extras)
					extra.Toggle.Checked = techniques.Contains(extra.Id);

				var defines = SplitList(p.Get("", "PreprocessorDefinitions"));
				for (int i = 0; i < DepthDefines.Length; i++)
					depthToggles[i].Checked = defines.Any(d => d.StartsWith(DepthDefines[i] + "=") && d.Substring(DepthDefines[i].Length + 1).Trim() == "1");

				chkDesktopMenu.Checked = IsDesktopUsingVrPreset();
			}
			catch (Exception ex) { status.Set("Could not read " + VrPreset + ": " + ex.Message, Theme.Warn); }
			finally { _loading = false; }
		}

		void LookChanged()
		{
			if (_loading) return;
			_saveTimer.Stop();
			_saveTimer.Start();
		}

		void SaveLook()
		{
			try
			{
				var p = new IniFile(P(VrPreset));
				var ids = Effects.Select(e => e.Id).ToList();

				var extraIds = _extras.Select(e => e.Id).ToList();

				var techniques = SplitList(p.Get("", "Techniques"));
				techniques.RemoveAll(t => ids.Contains(t) || extraIds.Contains(t));
				techniques.AddRange(Effects.Where(e => e.Toggle.Checked).Select(e => e.Id));
				techniques.AddRange(_extras.Where(e => e.Toggle.Checked).Select(e => e.Id));
				p.Set("", "Techniques", string.Join(",", techniques));

				// Depth settings live in the preset so they apply live (preset definitions override ReShadeVR.ini)
				var defines = SplitList(p.Get("", "PreprocessorDefinitions"));
				for (int i = 0; i < DepthDefines.Length; i++)
				{
					string name = DepthDefines[i];
					string define = name + "=" + (depthToggles[i].Checked ? "1" : "0");
					int index = defines.FindIndex(d => d.StartsWith(name + "="));
					if (index >= 0) defines[index] = define; else defines.Add(define);
				}
				p.Set("", "PreprocessorDefinitions", string.Join(",", defines));

				// Render the managed effects in their intended order, followed by anything else the preset has
				var sorting = SplitList(p.Get("", "TechniqueSorting"));
				if (!ids.All(sorting.Contains))
				{
					sorting.RemoveAll(t => ids.Contains(t));
					sorting.InsertRange(0, ids);
				}
				p.Set("", "TechniqueSorting", string.Join(",", sorting));

				foreach (var effect in Effects)
				{
					foreach (string key in effect.Params.Select(q => q.Key).Distinct())
					{
						var parts = effect.Params.Where(q => q.Key == key).ToList();
						if (parts[0].Component < 0)
						{
							p.Set(effect.Shader, key, F(parts[0].Slider.Value));
							continue;
						}
						var values = SplitList(p.Get(effect.Shader, key));
						while (values.Count < 3) values.Add(F(1m));
						foreach (var q in parts) values[q.Component] = F(q.Slider.Value);
						p.Set(effect.Shader, key, string.Join(",", values));
					}
				}
				p.Save();
			}
			catch (IOException) { _saveTimer.Start(); } // ReShade may be reading the file right now, try again shortly
			catch (Exception ex) { Msg("Could not save " + VrPreset + ": " + ex.Message); }
		}

		void ResetDefaults()
		{
			_loading = true;
			foreach (var effect in Effects)
			{
				effect.Toggle.Checked = effect.DefaultOn;
				foreach (var param in effect.Params) SetNumber(param.Slider, (double)param.Default);
				UpdateEffectEnabled(effect);
			}
			foreach (var extra in _extras) extra.Toggle.Checked = false;
			_loading = false;
			LookChanged();
		}

		void Msg(string text) { MessageBox.Show(this, text, Text); }
	}
}
