using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;


namespace VPT
{
    public partial class Form1 : Form
    {
        // Tabs
        private TabControl tabs = null!;
        private TabPage tabSingleClicks = null!;
        private TabPage tabCropTrim = null!;
        private TabPage tabTranscode = null!;

        // Tab 1 layout
        private Panel leftDropArea = null!;
        private TableLayoutPanel grid = null!;
        private Button renderBtn = null!;
        private ToolTip tips = new ToolTip { AutoPopDelay = 8000, InitialDelay = 300, ReshowDelay = 100 };

        // Theme
        private static readonly Color Bg          = Color.FromArgb(24, 24, 28);
        private static readonly Color PanelBg     = Color.FromArgb(30, 30, 36);
        private static readonly Color CardBg      = Color.FromArgb(45, 45, 52);
        private static readonly Color CardBgHover = Color.FromArgb(55, 55, 64);
        private static readonly Color Fg          = Color.Gainsboro;
        private static readonly Color Muted       = Color.Silver;
        private static readonly Color Accent      = Color.FromArgb(26, 137, 23); // green armed

        // State for custom rotation
        private float _customRotateDeg = 0f;  // updated when user sets it

        public Form1()
        {
            InitializeComponent();
            
        

            Text = "VPT";
            Width = 960; Height = 720;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Bg;
            ForeColor = Fg;

            // System dark chrome
            EnableDarkTitleBar(this.Handle);
            SetBorderColor(this.Handle, PanelBg); // remove white border on Win11+

            BuildTabs();
            BuildSingleClicksTab();
            BuildPlaceholders();
        }

        // ---- Dark title bar & border (Windows 10/11) ----
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // 19 on older, 20 on newer
        private const int DWMWA_BORDER_COLOR            = 34; // Win11 22H2+
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static void EnableDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                int older = 19; // fallback for some builds
                _ = DwmSetWindowAttribute(hwnd, older, ref on, sizeof(int));
            }
            catch { /* ignore on unsupported OS */ }
        }
        private static void SetBorderColor(IntPtr hwnd, Color color)
        {
            try
            {
                // COLORREF (BGR, no alpha)
                int bgr = color.R | (color.G << 8) | (color.B << 16);
                _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bgr, sizeof(int));
            }
            catch { /* ignore on unsupported OS */ }
        }

        private void BuildTabs()
        {
            // Use custom dark tab control (owns all painting so no white gutter)
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

        private void BuildSingleClicksTab()
        {
            // Root: content + render button
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            tabSingleClicks.Controls.Add(root);

            // Content: left drop, right grid
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Padding = new Padding(10) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            root.Controls.Add(content, 0, 0);

            // Left: drop area (rounded)
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
            leftDropArea.DragEnter += (s, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
            };
            leftDropArea.DragDrop += async (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    await Rotate90Async(files[0]); // placeholder action
            };
            content.Controls.Add(leftDropArea, 0, 0);

            // Right grid: 4 columns x 6 rows (unit rows for perfect squares)
            grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 6,
                BackColor = Bg,
                Padding = new Padding(8)
            };
            for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int r = 0; r < 6; r++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
            grid.Resize += (_, __) => UpdateSquareGrid();
            content.Controls.Add(grid, 1, 0);

            // Row 0: 4 small squares
            AddBtn("VPU_Icon_Rotation_90.png",     "Rotate 90°",     col: 0, row: 0);
            AddBtn("VPU_Icon_Rotation_180.png",    "Rotate 180°",    col: 1, row: 0);
            AddBtn("VPU_Icon_Rotation_270.png",    "Rotate 270°",    col: 2, row: 0);
            AddBtn("VPU_Icon_Rotation_Custom.png", "Rotate custom",  col: 3, row: 0);

            // Rows 1–2: two big flip buttons (2×2)
            AddBtn("VPU_Icon_Flip_Horizontal.png", "Flip H", col: 0, row: 1, colSpan: 2, rowSpan: 2, big: true);
            AddBtn("VPU_Icon_Flip_Vertical.png",   "Flip V", col: 2, row: 1, colSpan: 2, rowSpan: 2, big: true);

            // Row 3: 4 small squares (volumes)
            AddBtn("VPU_Icon_Volume_50_Up.png",   "Vol +50%", col: 0, row: 3);
            AddBtn("VPU_Icon_Volume_25_Up.png",   "Vol +25%", col: 1, row: 3);
            AddBtn("VPU_Icon_Volume_25_Down.png", "Vol −25%", col: 2, row: 3);
            AddBtn("VPU_Icon_Volume_50_Down.png", "Vol −50%", col: 3, row: 3);

            // Rows 4–5: 2 big squares (Stereo→Mono, Mute)
            AddBtn("01_VPU_Icon_Stereot2mono.png", "Stereo→Mono", col: 0, row: 4, colSpan: 2, rowSpan: 2, big: true);
            AddBtn("VPU_Icon_Volume_Mute.png",     "Mute",        col: 2, row: 4, colSpan: 2, rowSpan: 2, big: true);

            // Bottom: render (rounded)
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
            renderBtn.Click += (s, e) => MessageBox.Show("Render queued (placeholder).", "VPT");
            renderBtn.Resize += (s, e) => ApplyRounded(renderBtn, 12);
            root.Controls.Add(renderBtn, 0, 1);
            ApplyRounded(renderBtn, 12);

            UpdateSquareGrid(); // initial pass
        }

        private void UpdateSquareGrid()
        {
            if (grid.Width <= 0) return;
            int colCount = 4;

            // one "unit" square from available width
            int pad = grid.Padding.Left + grid.Padding.Right;
            float unit = (grid.ClientSize.Width - pad) / (float)colCount;
            unit = Math.Max(unit, 32f);

            for (int r = 0; r < grid.RowCount; r++)
            {
                grid.RowStyles[r].Height = unit;
                grid.RowStyles[r].SizeType = SizeType.Absolute;
            }

            // update icons + rounded corners
            foreach (Control ctl in grid.Controls)
            {
                if (ctl is Button b && b.Tag is PngIconTag t)
                {
                    int target = t.Big ? (int)(unit * 2) - 28 : (int)unit - 18; // icon size inside button
                    if (target < 24) target = 24;

                    b.Image = PngIconService.Render(t.FileName, t.Active ? Color.White : Color.Gainsboro, target, target, padding: 8);
                    ApplyRounded(b, 14);
                }
            }
        }

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

            btn.Click += (s, e) =>
            {
                var tag = (PngIconTag)btn.Tag!;

                // Special handling for the custom rotation button:
               if (tag.FileName.Equals("VPU_Icon_Rotation_Custom.png", StringComparison.OrdinalIgnoreCase))
{
    if (!tag.Active)
    {
        // Only ask for an angle when arming it
        using (var dlg = new CustomRotationDialog(this))
        {
            dlg.StartPosition = FormStartPosition.CenterParent;
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            // Parse the string to a float (invariant culture, allows decimals and sign)
            if (float.TryParse(dlg.AngleDeg, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg))
            {
                _customRotateDeg = deg;
                tips.SetToolTip(btn, $"Rotate custom ({_customRotateDeg:0.#}°)");
            }
            else
            {
                MessageBox.Show(this, "Please enter a valid number (degrees).", "Invalid input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        tag.Active = true;
    }
    else
    {
        // disarm without showing dialog
        tag.Active = false;
    }
}


                btn.Tag = tag;
                btn.BackColor = tag.Active ? Accent : CardBg;
                btn.FlatAppearance.BorderColor = tag.Active ? Accent : CardBgHover;

                float unit = grid.RowStyles[0].Height;
                int target = tag.Big ? (int)(unit * 2) - 28 : (int)unit - 18;
                if (target < 24) target = 24;
                btn.Image = PngIconService.Render(tag.FileName, tag.Active ? Color.White : Color.Gainsboro, target, target, padding: 8);
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

        // Rounded-corner helper
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

        // ===== Minimal FFmpeg runner for the drop test =====
        private string NewLogFilePath()
        {
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
            string logsDir = Path.Combine(exeDir, "logs");
            Directory.CreateDirectory(logsDir);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
            return Path.Combine(logsDir, $"{stamp}_log.txt");
        }

        private async Task Rotate90Async(string inputPath)
        {
            string logFile = NewLogFilePath();
            File.AppendAllText(logFile, $"Log file: {logFile}{Environment.NewLine}");

            if (!File.Exists(inputPath))
            {
                File.AppendAllText(logFile, $"File not found: {inputPath}{Environment.NewLine}");
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string ffmpegPath = Path.Combine(baseDir, "ThirdParty", "bin", "ffmpeg.exe");
            if (!File.Exists(ffmpegPath)) ffmpegPath = Path.Combine(baseDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                File.AppendAllText(logFile, @"ERROR: ffmpeg.exe not found. Expected at: .\ThirdParty\bin\ffmpeg.exe" + Environment.NewLine);
                return;
            }

            string dir = Path.GetDirectoryName(inputPath)!;
            string name = Path.GetFileNameWithoutExtension(inputPath);
            string ext = Path.GetExtension(inputPath);
            string outPath = Path.Combine(dir, $"{name}_rot90{ext}");

            string args =
                $"-y -i \"{inputPath}\" " +
                "-map 0:v:0 -map 0:a? " +
                "-vf \"transpose=clock\" " +
                "-c:v libx264 -preset veryfast -crf 20 " +
                "-c:a copy -movflags +faststart " +
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
    }

    // ===== Embedded PNG loader/renderer (centered, scaled, tinted) =====
    static class PngIconService
    {
        private static readonly Dictionary<string, Bitmap> OriginalCache = new();

        private static string? FindResource(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            return asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith($".Assets.IconsPng.{fileName}", StringComparison.OrdinalIgnoreCase));
        }

        public static Bitmap Render(string fileName, Color tint, int width, int height, int padding = 8)
        {
            var asm = Assembly.GetExecutingAssembly();

            if (!OriginalCache.TryGetValue(fileName, out var original))
            {
                string? res = FindResource(fileName);
                if (res == null) return new Bitmap(width, height);
                using var s = asm.GetManifestResourceStream(res)!;
                original = new Bitmap(s);
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
            float r = tint.R / 255f;
            float g = tint.G / 255f;
            float b = tint.B / 255f;

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

    // ===== Custom dark TabControl (eliminates white gutter) =====
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
            // Fill header strip fully
            int stripH = ItemSize.Height + 6;
            using var sb = new SolidBrush(_strip);
            e.Graphics.FillRectangle(sb, new Rectangle(0, 0, Width, stripH));

            // Fill remaining area with page bg
            using var pb = new SolidBrush(_bg);
            e.Graphics.FillRectangle(pb, new Rectangle(0, stripH, Width, Height - stripH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // draw tabs
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

            // subtle bottom divider under strip
            int stripH = ItemSize.Height + 6;
            using var line = new Pen(Color.FromArgb(50, 50, 58), 1);
            e.Graphics.DrawLine(line, 0, stripH - 1, Width, stripH - 1);
        }
    }
}
