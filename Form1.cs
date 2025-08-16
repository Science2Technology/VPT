using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VPT
{
    public partial class Form1 : Form
    {
        // --- Theme -----------------------------------------------------------
        private static readonly Color Bg          = Color.FromArgb(24, 24, 28);
        private static readonly Color PanelBg     = Color.FromArgb(30, 30, 36);
        private static readonly Color CardBg      = Color.FromArgb(45, 45, 52);
        private static readonly Color CardBgHover = Color.FromArgb(55, 55, 64);
        private static readonly Color Fg          = Color.Gainsboro;
        private static readonly Color Muted       = Color.Silver;
        private static readonly Color Accent      = Color.FromArgb(26, 137, 23);

        // --- WinUI dark chrome ----------------------------------------------
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // 19 on some builds
        private const int DWMWA_BORDER_COLOR            = 34; // Win11+
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static void EnableDarkTitleBar(IntPtr hwnd)
        {
            try { int on = 1; _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
            catch { try { int on = 1; _ = DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int)); } catch { } }
        }
        private static void SetBorderColor(IntPtr hwnd, Color c)
        {
            try { int bgr = c.R | (c.G << 8) | (c.B << 16); _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bgr, sizeof(int)); }
            catch { }
        }

        // --- UI fields -------------------------------------------------------
        private TabControl tabs = null!;
        private TabPage tabSingleClicks = null!;
        private TabPage tabCropTrim = null!;
        private TabPage tabTranscode = null!;

        private Panel leftDropArea = null!;
        private TableLayoutPanel grid = null!;
        private Button renderBtn = null!;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 8000, InitialDelay = 300, ReshowDelay = 100 };

        // --- State -----------------------------------------------------------
        private float _customRotateDeg = 0f;
        private string? _pendingInputFile;

        // --- PATHS FOR PNG ICONS (covers both layouts) ----------------------
        internal static readonly string[] IconSearchDirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets"),
        };

        public Form1()
        {
            InitializeComponent();

            Text = "VPT";
            Width = 960; Height = 720;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Bg; ForeColor = Fg;

            EnableDarkTitleBar(this.Handle);
            SetBorderColor(this.Handle, PanelBg);

            BuildTabs();
            BuildSingleClicksTab();
            BuildPlaceholders();

            DebugIconProbe();   // one-time helper in Debug builds
        }

        [Conditional("DEBUG")]
        private void DebugIconProbe()
        {
            try
            {
                var found = IconSearchDirs.Select(d => new
                {
                    Dir = d,
                    Exists = Directory.Exists(d),
                    Count = Directory.Exists(d) ? Directory.GetFiles(d, "*.png", SearchOption.TopDirectoryOnly).Length : 0
                }).ToArray();

                if (found.All(f => !f.Exists || f.Count == 0))
                {
                    MessageBox.Show(
                        "No PNG icons were found next to the EXE.\r\n\r\n" +
                        string.Join("\r\n", found.Select(f => $"{(f.Exists ? "✓" : "✗")} {f.Dir}  (pngs: {f.Count})")),
                        "Icon probe (DEBUG)",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch { /* ignore */ }
        }

        // ===================== TABS =====================
        private void BuildTabs()
        {
            tabs = new DarkTabControl(PanelBg, Bg, Fg)
            {
                Dock = DockStyle.Fill,
                SizeMode = TabSizeMode.Fixed,
                Padding = new Point(18, 12),
                ItemSize = new Size(160, 40)
            };

            tabSingleClicks = new TabPage("Single Clicks") { BackColor = Bg, ForeColor = Fg };
            tabCropTrim     = new TabPage("Crop & Trim")  { BackColor = Bg, ForeColor = Fg };
            tabTranscode    = new TabPage("Transcode")    { BackColor = Bg, ForeColor = Fg };

            tabs.TabPages.Add(tabSingleClicks);
            tabs.TabPages.Add(tabCropTrim);
            tabs.TabPages.Add(tabTranscode);
            Controls.Add(tabs);
        }

        // ===================== SINGLE CLICKS TAB =====================
        private void BuildSingleClicksTab()
        {
            // Root (content + render bar)
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            tabSingleClicks.Controls.Add(root);

            // Content split
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Bg, Padding = new Padding(10) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            root.Controls.Add(content, 0, 0);

            // Left: drop area
            leftDropArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(12) };
            ApplyRounded(leftDropArea, 12);
            leftDropArea.Resize += (s, e) => ApplyRounded(leftDropArea, 12);
            var dropLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                BackColor = Color.Black,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16),
                Text = "Drag and Drop\nor click"
            };
            leftDropArea.Controls.Add(dropLabel);
            leftDropArea.AllowDrop = true;
            leftDropArea.DragEnter += (s, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
            leftDropArea.DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    _pendingInputFile = files[0];
                    MessageBox.Show("File loaded. Now select actions and click Render.", "VPT");
                }
            };
            content.Controls.Add(leftDropArea, 0, 0);

            // Right: 4x6 grid
            grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6, BackColor = Bg, Padding = new Padding(8) };
            for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int r = 0; r < 6; r++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
            grid.Resize += (_, __) => UpdateSquareGrid();
            content.Controls.Add(grid, 1, 0);

            // --- SingleClicks: BUTTON GRID START ---
            // Row 0: rotate (4 small)
            AddBtn("VPU_Icon_Rotation_90.png",     "Rotate 90°",     col: 0, row: 0);
            AddBtn("VPU_Icon_Rotation_180.png",    "Rotate 180°",    col: 1, row: 0);
            AddBtn("VPU_Icon_Rotation_270.png",    "Rotate 270°",    col: 2, row: 0);  // you confirmed this PNG exists
            AddBtn("VPU_Icon_Rotation_Custom.png", "Rotate custom",  col: 3, row: 0);

            // Rows 1–2: flips (2×2 big)
            AddBtn("VPU_Icon_Flip_Horizontal.png", "Flip horizontal", col: 0, row: 1, colSpan: 2, rowSpan: 2, big: true);
            AddBtn("VPU_Icon_Flip_Vertical.png",   "Flip vertical",   col: 2, row: 1, colSpan: 2, rowSpan: 2, big: true);

            // Row 3: volume (4 small)
            AddBtn("VPU_Icon_Volume_50_Up.png",   "Volume +50%", col: 0, row: 3);
            AddBtn("VPU_Icon_Volume_25_Up.png",   "Volume +25%", col: 1, row: 3);
            AddBtn("VPU_Icon_Volume_25_Down.png", "Volume −25%", col: 2, row: 3);
            AddBtn("VPU_Icon_Volume_50_Down.png", "Volume −50%", col: 3, row: 3);

            // Rows 4–5: big actions
            AddBtn("01_VPU_Icon_Stereot2mono.png", "Stereo → Mono", col: 0, row: 4, colSpan: 2, rowSpan: 2, big: true);
            AddBtn("VPU_Icon_Volume_Mute.png",     "Mute",          col: 2, row: 4, colSpan: 2, rowSpan: 2, big: true);
            // --- SingleClicks: BUTTON GRID END ---

            // Render bar
            renderBtn = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Render",
                Height = 52,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = CardBg,
                ForeColor = Fg,
                Margin = new Padding(10, 6, 10, 10)
            };
            renderBtn.FlatAppearance.BorderSize = 1;
            renderBtn.FlatAppearance.BorderColor = CardBgHover;
            renderBtn.FlatAppearance.MouseOverBackColor = CardBgHover;
            renderBtn.Click += async (s, e) =>
            {
                string? inputFile = _pendingInputFile;
                if (inputFile == null)
                {
                    var ofd = new OpenFileDialog
                    {
                        Title = "Select video file",
                        Filter = "Video Files|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.wmv|All Files|*.*"
                    };
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                        inputFile = ofd.FileName;
                }
                if (inputFile != null)
                {
                    await ProcessAllActionsAsync(inputFile);
                    MessageBox.Show("Render complete. See logs folder for details.", "VPT");
                    _pendingInputFile = null; // reset after processing
                }
            };
            renderBtn.Resize += (s, e) => ApplyRounded(renderBtn, 12);
            root.Controls.Add(renderBtn, 0, 1);
            ApplyRounded(renderBtn, 12);

            UpdateSquareGrid();
        }

        private void UpdateSquareGrid()
        {
            if (grid.Width <= 0) return;
            int colCount = 4;

            int pad = grid.Padding.Left + grid.Padding.Right;
            float unit = (grid.ClientSize.Width - pad) / (float)colCount;
            unit = Math.Max(unit, 32f);

            for (int r = 0; r < grid.RowCount; r++)
            {
                grid.RowStyles[r].Height = unit;
                grid.RowStyles[r].SizeType = SizeType.Absolute;
            }

            foreach (Control ctl in grid.Controls)
            {
                if (ctl is Button b && b.Tag is PngIconTag t)
                {
                    int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                    if (target < 24) target = 24;
                    b.Image = PngIconService.Render(t.FileName, t.Active ? Color.White : Color.Gainsboro, target, target, padding: 8);
                    ApplyRounded(b, 14);
                }
            }
        }

        private List<Button> rotationButtons = new();
        private List<Button> volumeButtons = new();
        private List<Button> bigAudioButtons = new();

        private void AddBtn(string fileName, string help, int col, int row, int colSpan = 1, int rowSpan = 1, bool big = false)
        {
            var btn = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8),
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBg,
                ForeColor = Fg,
                Text = string.Empty,
                ImageAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0),
                Tag = new PngIconTag { FileName = fileName, Big = big, Active = false }
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = CardBgHover;

            btn.MouseEnter += (s, e) => { if (!((PngIconTag)btn.Tag!).Active) btn.BackColor = CardBgHover; };
            btn.MouseLeave += (s, e) => { if (!((PngIconTag)btn.Tag!).Active) btn.BackColor = CardBg; };
            btn.Resize += (s, e) => ApplyRounded(btn, 14);

            // Register rotation buttons
            if (fileName.StartsWith("VPU_Icon_Rotation_", StringComparison.OrdinalIgnoreCase))
                rotationButtons.Add(btn);

            // Register volume buttons
            if (fileName.StartsWith("VPU_Icon_Volume_", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Contains("Mute", StringComparison.OrdinalIgnoreCase))
                volumeButtons.Add(btn);

            // Register big audio buttons (Stereo to Mono & Mute)
            if (fileName.Contains("Stereot2mono", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Mute", StringComparison.OrdinalIgnoreCase))
                bigAudioButtons.Add(btn);

            btn.Click += (s, e) =>
            {
                var tag = (PngIconTag)btn.Tag!;

                // --- Custom rotation dialog: handle FIRST ---
                if (tag.FileName.Equals("VPU_Icon_Rotation_Custom.png", StringComparison.OrdinalIgnoreCase))
                {
                    using (var dlg = new CustomRotationDialog(this))
                    {
                        dlg.StartPosition = FormStartPosition.CenterParent;
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                        {
                            tag.Active = false;
                        }
                        else if (float.TryParse(dlg.AngleDeg, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg))
                        {
                            _customRotateDeg = deg;
                            tips.SetToolTip(btn, $"Rotate custom ({_customRotateDeg:0.#}°)");
                            tag.Active = true;
                        }
                        else
                        {
                            MessageBox.Show(this, "Please enter a valid number (degrees).", "Invalid input",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            tag.Active = false;
                        }
                    }
                }
                // --- Rotation buttons: only one active, toggle off if already active ---
                else if (rotationButtons.Contains(btn))
                {
                    if (tag.Active)
                    {
                        tag.Active = false;
                    }
                    else
                    {
                        foreach (var b in rotationButtons)
                        {
                            var t = (PngIconTag)b.Tag!;
                            t.Active = (b == btn);
                            b.Tag = t;
                            b.BackColor = t.Active ? Accent : CardBg;
                            b.FlatAppearance.BorderColor = t.Active ? Accent : CardBgHover;
                            float unit = grid.RowStyles[0].Height;
                            int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                            if (target < 24) target = 24;
                            b.Image = PngIconService.Render(t.FileName, t.Active ? Color.White : Color.Gainsboro, target, target, padding: 8);
                        }
                        tag.Active = true;
                    }
                }
                // --- Flip buttons: toggle on/off ---
                else if (fileName.Contains("Flip", StringComparison.OrdinalIgnoreCase))
                {
                    tag.Active = !tag.Active;
                }
                // --- Mute and big audio buttons: only one active, toggle off if already active ---
                else if (bigAudioButtons.Contains(btn))
                {
                    if (tag.Active)
                    {
                        tag.Active = false;
                    }
                    else
                    {
                        foreach (var b in bigAudioButtons)
                        {
                            var t = (PngIconTag)b.Tag!;
                            t.Active = (b == btn);
                            b.Tag = t;
                            b.BackColor = t.Active ? Accent : CardBg;
                            b.FlatAppearance.BorderColor = t.Active ? Accent : CardBgHover;
                            float unit = grid.RowStyles[0].Height;
                            int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                            if (target < 24) target = 24;
                            b.Image = PngIconService.Render(t.FileName, t.Active ? Color.White : Color.Gainsboro, target, target, padding: 8);
                        }
                        tag.Active = true;
                    }
                    // If mute is activated, deactivate all volume buttons
                    if (fileName.Contains("Mute", StringComparison.OrdinalIgnoreCase) && tag.Active)
                    {
                        foreach (var b in volumeButtons)
                        {
                            var t = (PngIconTag)b.Tag!;
                            t.Active = false;
                            b.Tag = t;
                            b.BackColor = CardBg;
                            b.FlatAppearance.BorderColor = CardBgHover;
                            float unit = grid.RowStyles[0].Height;
                            int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                            if (target < 24) target = 24;
                            b.Image = PngIconService.Render(t.FileName, Color.Gainsboro, target, target, padding: 8);
                        }
                    }
                }
                // --- Volume buttons: stackable by direction, toggle off if already active ---
                else if (volumeButtons.Contains(btn))
                {
                    // If mute is active, ignore volume button clicks
                    bool muteActive = bigAudioButtons.Any(b =>
                        ((PngIconTag)b.Tag!).Active &&
                        ((PngIconTag)b.Tag!).FileName.Contains("Mute", StringComparison.OrdinalIgnoreCase));
                    if (muteActive)
                        return;

                    bool isUp = fileName.Contains("Up", StringComparison.OrdinalIgnoreCase);
                    bool isDown = fileName.Contains("Down", StringComparison.OrdinalIgnoreCase);

                    // If already active, toggle off
                    if (tag.Active)
                    {
                        tag.Active = false;
                    }
                    else
                    {
                        // Check if any opposite vector is active
                        bool oppositeActive = volumeButtons.Any(b =>
                            ((PngIconTag)b.Tag!).Active &&
                            ((PngIconTag)b.Tag!).FileName.Contains(isUp ? "Down" : "Up", StringComparison.OrdinalIgnoreCase));

                        if (oppositeActive)
                        {
                            // Deactivate all volume buttons, then activate only the clicked one
                            foreach (var b in volumeButtons)
                            {
                                var t = (PngIconTag)b.Tag!;
                                t.Active = false;
                                b.Tag = t;
                                b.BackColor = CardBg;
                                b.FlatAppearance.BorderColor = CardBgHover;
                                float unit = grid.RowStyles[0].Height;
                                int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                                if (target < 24) target = 24;
                                b.Image = PngIconService.Render(t.FileName, Color.Gainsboro, target, target, padding: 8);
                            }
                            tag.Active = true;
                        }
                        else
                        {
                            // Stack same direction
                            tag.Active = true;
                        }
                    }
                }
                else
                {
                    tag.Active = !tag.Active;
                }

                btn.Tag = tag;
                btn.BackColor = tag.Active ? Accent : CardBg;
                btn.FlatAppearance.BorderColor = tag.Active ? Accent : CardBgHover;

                float unit2 = grid.RowStyles[0].Height;
                int target2 = tag.Big ? (int)(unit2 * 2) - 28 : (int)unit2 - 18;
                if (target2 < 24) target2 = 24;
                btn.Image = PngIconService.Render(tag.FileName, tag.Active ? Color.White : Color.Gainsboro, target2, target2, padding: 8);
            };

            tips.SetToolTip(btn, help);
            grid.Controls.Add(btn, col, row);
            if (colSpan > 1) grid.SetColumnSpan(btn, colSpan);
            if (rowSpan > 1) grid.SetRowSpan(btn, rowSpan);
        }

        private struct PngIconTag
        {
            public string FileName;
            public bool Big;
            public bool Active;
        }

        private static void ApplyRounded(Control c, int radius)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            using var path = new GraphicsPath();
            int r = radius * 2;
            Rectangle rect = new Rectangle(0, 0, c.Width, c.Height);
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            c.Region = new Region(path);
        }

        private void BuildPlaceholders()
        {
            var wip1 = new Label
            {
                Text = "WIP 1",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted,
                BackColor = Bg,
                Font = new Font("Segoe UI", 36, FontStyle.Bold)
            };
            tabCropTrim.Controls.Add(wip1);

            var wip2 = new Label
            {
                Text = "WIP 2",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted,
                BackColor = Bg,
                Font = new Font("Segoe UI", 36, FontStyle.Bold)
            };
            tabTranscode.Controls.Add(wip2);
        }

        

        private async Task ProcessAllActionsAsync(string inputPath)
        {
            string logFile = NewLogFilePath();
            try
            {
                File.AppendAllText(logFile, $"Log file: {logFile}{Environment.NewLine}");

                if (!File.Exists(inputPath))
                {
                    File.AppendAllText(logFile, $"File not found: {inputPath}{Environment.NewLine}");
                    return;
                }

                string ffmpegPath = ExtractFfmpegTool("ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    File.AppendAllText(logFile, @"ERROR: ffmpeg.exe not found (embedded resource missing)." + Environment.NewLine);
                    return;
                }

                string dir = Path.GetDirectoryName(inputPath)!;
                string name = Path.GetFileNameWithoutExtension(inputPath);
                string ext = Path.GetExtension(inputPath);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
                string outPath = Path.Combine(dir, $"{name}_processed_{timestamp}{ext}");

                // Build FFmpeg filters based on active buttons
                var vfFilters = new List<string>();
                var afFilters = new List<string>();
                string extraArgs = "";

                // Rotation
                if (rotationButtons.Any(b => ((PngIconTag)b.Tag!).Active))
                {
                    var active = rotationButtons.First(b => ((PngIconTag)b.Tag!).Active);
                    var tag = (PngIconTag)active.Tag!;
                    if (tag.FileName.Contains("90"))
                    {
                        vfFilters.Add("transpose=clock");
                        File.AppendAllText(logFile, "Rotate 90°\r\n");
                    }
                    else if (tag.FileName.Contains("180"))
                    {
                        vfFilters.Add("transpose=clock,transpose=clock");
                        File.AppendAllText(logFile, "Rotate 180°\r\n");
                    }
                    else if (tag.FileName.Contains("270"))
                    {
                        vfFilters.Add("transpose=cclock");
                        File.AppendAllText(logFile, "Rotate 270°\r\n");
                    }
                    else if (tag.FileName.Contains("Custom"))
                    {
                        double radians = _customRotateDeg * Math.PI / 180.0;
                        vfFilters.Add($"rotate={radians:F4}:c=none");
                        File.AppendAllText(logFile, $"Custom rotate: {_customRotateDeg}° ({radians:F4} rad)\r\n");
                    }
                }

                // Flip
                foreach (var b in grid.Controls.OfType<Button>().Where(b => b.Tag is PngIconTag t && t.Active))
                {
                    var tag = (PngIconTag)b.Tag!;
                    if (tag.FileName.Contains("Flip_Horizontal"))
                    {
                        vfFilters.Add("hflip");
                        File.AppendAllText(logFile, "Flip horizontal\r\n");
                    }
                    else if (tag.FileName.Contains("Flip_Vertical"))
                    {
                        vfFilters.Add("vflip");
                        File.AppendAllText(logFile, "Flip vertical\r\n");
                    }
                }

                // Volume & Mute logic
                bool muteActive = bigAudioButtons.Any(b => ((PngIconTag)b.Tag!).Active && ((PngIconTag)b.Tag!).FileName.Contains("Mute"));
                string audioCodec = muteActive ? "" : "-c:a aac -b:a 192k ";
                string af = ""; // audio filter string

                if (muteActive)
                {
                    // Only strip audio, do not add any audio filters or codecs
                    extraArgs += " -an";
                    af = ""; // clear audio filters
                    File.AppendAllText(logFile, "Audio stripped (mute button active)\r\n");
                }
                else
                {
                    float gainDb = 0f;
                    foreach (var b in volumeButtons.Where(b => ((PngIconTag)b.Tag!).Active))
                    {
                        var tag = (PngIconTag)b.Tag!;
                        if (tag.FileName.Contains("50_Up")) { gainDb += 12f; File.AppendAllText(logFile, "Volume +50% (+12dB)\r\n"); }
                        if (tag.FileName.Contains("25_Up")) { gainDb += 6f;  File.AppendAllText(logFile, "Volume +25% (+6dB)\r\n"); }
                        if (tag.FileName.Contains("25_Down")) { gainDb -= 6f; File.AppendAllText(logFile, "Volume −25% (−6dB)\r\n"); }
                        if (tag.FileName.Contains("50_Down")) { gainDb -= 12f; File.AppendAllText(logFile, "Volume −50% (−12dB)\r\n"); }
                    }
                    if (gainDb != 0f)
                    {
                        af = $"-af \"volume={gainDb}dB\" ";
                    }
                }

                // Stereo to Mono
                bool monoActive = bigAudioButtons.Any(b => ((PngIconTag)b.Tag!).Active && ((PngIconTag)b.Tag!).FileName.Contains("Stereot2mono"));
                if (monoActive)
                {
                    afFilters.Add("pan=mono|c0=.5*c0+.5*c1");
                    File.AppendAllText(logFile, "Stereo to mono\r\n");
                }

                // Build filter strings
                string vf = vfFilters.Count > 0 ? $"-vf \"{string.Join(",", vfFilters)}\" " : "";
                af = afFilters.Count > 0 ? $"-af \"{string.Join(",", afFilters)}\" " : "";

                string args =
                    $"-y -i \"{inputPath}\" " +
                    "-map 0:v? -map 0:a? " +
                    vf + af +
                    "-c:v libx264 -preset veryfast -crf 20 " +
                    audioCodec +
                    extraArgs +
                    $"\"{outPath}\"";

                File.AppendAllText(logFile, $"> ffmpeg {args}{Environment.NewLine}");

                int exitCode = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var p = new Process { StartInfo = psi };
                    p.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) File.AppendAllText(logFile, e.Data + Environment.NewLine); };
                    p.ErrorDataReceived  += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) File.AppendAllText(logFile, e.Data + Environment.NewLine); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    return p.ExitCode;
                });

                File.AppendAllText(logFile, (exitCode == 0 && File.Exists(outPath))
                    ? $"Done ✅  Output: {outPath}{Environment.NewLine}"
                    : "Failed ❌  (see log above)\r\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFile, $"Exception: {ex}{Environment.NewLine}");
            }
        }

        private string ExtractFfmpegTool(string toolName)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "VPT_FFMPEG");
            Directory.CreateDirectory(tempDir);
            string outPath = Path.Combine(tempDir, toolName);

            if (!File.Exists(outPath))
            {
                var asm = Assembly.GetExecutingAssembly();
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(toolName, StringComparison.OrdinalIgnoreCase));
                if (resourceName == null)
                    throw new FileNotFoundException($"Embedded resource {toolName} not found.");

                using var s = asm.GetManifestResourceStream(resourceName);
                if (s == null)
                    throw new FileNotFoundException($"Embedded resource stream for {toolName} not found.");
                using var f = File.Create(outPath);
                s.CopyTo(f);
            }
            return outPath;
        }

        private string NewLogFilePath()
        {
            string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
            return Path.Combine(logDir, $"VPT_{timestamp}.log");
        }
    }

    // ===================== PNG Icon Service (with DISK FALLBACK) =============
    static class PngIconService
    {
        private static readonly Dictionary<string, Bitmap> OriginalCache = new();

        private static string? FindResource(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            return asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith($".Assets.{fileName}", StringComparison.OrdinalIgnoreCase));
        }

        public static Bitmap Render(string fileName, Color tint, int width, int height, int padding = 8)
        {
            if (!OriginalCache.TryGetValue(fileName, out var original))
            {
                // 1) Try EMBEDDED resource
                var asm = Assembly.GetExecutingAssembly();
                string? res = FindResource(fileName);
                if (res != null)
                {
                    using var s = asm.GetManifestResourceStream(res)!;
                    original = new Bitmap(s);
                }
                else
                {
                    // 2) Try DISK fallback (VPT\Assets\IconsPng OR Assets\IconsPng)
                    string? onDisk = Form1.IconSearchDirs
                        .Where(Directory.Exists)
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(File.Exists);

                    original = onDisk != null
                        ? new Bitmap(onDisk)
                        : new Bitmap(Math.Max(1, width), Math.Max(1, height)); // last ditch blank
                }

                OriginalCache[fileName] = original;
            }

            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                float availW = Math.Max(1, width - 2f * padding);
                float availH = Math.Max(1, height - 2f * padding);
                float scale = Math.Min(availW / original.Width, availH / original.Height);

                float drawW = original.Width * scale;
                float drawH = original.Height * scale;
                float x = (width - drawW) / 2f;
                float y = (height - drawH) / 2f;

                using var ia = MakeTintAttributes(tint);
                var dest = new RectangleF(x, y, drawW, drawH);
                g.DrawImage(original, Rectangle.Round(dest), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, ia);
            }
            return bmp;
        }

        private static ImageAttributes MakeTintAttributes(Color tint)
        {
            float r = tint.R / 255f, g = tint.G / 255f, b = tint.B / 255f;
            var matrix = new ColorMatrix(new float[][]
            {
                new float[] { r, 0, 0, 0, 0 },
                new float[] { 0, g, 0, 0, 0 },
                new float[] { 0, 0, b, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 0 }
            });
            var ia = new ImageAttributes();
            ia.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return ia;
        }
    }

    // ===================== Dark TabControl (no white gutter) ==================
    internal class DarkTabControl : TabControl
    {
        private readonly Color _strip;
        private readonly Color _bg;
        private readonly Color _fg;
        private readonly Color _border = Color.FromArgb(45, 45, 52);

        public DarkTabControl(Color strip, Color bg, Color fg)
        {
            _strip = strip; _bg = bg; _fg = fg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            BackColor = _bg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            int stripH = ItemSize.Height + 6;
            using var sb = new SolidBrush(_strip);
            e.Graphics.FillRectangle(sb, new Rectangle(0, 0, Width, stripH));
            using var pb = new SolidBrush(_bg);
            e.Graphics.FillRectangle(pb, new Rectangle(0, stripH, Width, Height - stripH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            for (int i = 0; i < TabPages.Count; i++)
            {
                Rectangle rect = GetTabRect(i);
                bool selected = (i == SelectedIndex);
                using var bg = new SolidBrush(selected ? _strip : _bg);
                using var pen = new Pen(_border, 1);
                e.Graphics.FillRectangle(bg, rect);
                e.Graphics.DrawRectangle(pen, rect);

                TextRenderer.DrawText(
                    e.Graphics,
                    TabPages[i].Text,
                    new Font("Segoe UI", selected ? 10.5f : 10f, selected ? FontStyle.Bold : FontStyle.Regular),
                    rect,
                    _fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            int sh = ItemSize.Height + 6;
            using var line = new Pen(Color.FromArgb(50, 50, 58), 1);
            e.Graphics.DrawLine(line, 0, sh - 1, Width, sh - 1);
        }
    }
}

// This is an integration test: verifying Copilot and IDE
