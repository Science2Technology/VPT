using System;
using System.Drawing;
using System.Windows.Forms;

namespace VPT
{
    /// <summary>
    /// Simple dark-theme dialog to collect a custom rotation in degrees.
    /// Matches Form1 usage: new CustomRotationDialog(this) and dlg.AngleDeg.
    /// </summary>
    public class CustomRotationDialog : Form
    {
        private readonly TextBox angleTextBox;
        private readonly Button setButton;

        /// <summary>
        /// Returns the raw text the user typed (Form1 will parse/validate).
        /// </summary>
        public string AngleDeg => angleTextBox.Text.Trim();

        /// <summary>
        /// Parameterless ctor (handy for designers/tests) — chains to themed ctor.
        /// </summary>
        public CustomRotationDialog() : this(null) { }

        /// <summary>
        /// Preferred ctor used by Form1: allows setting Owner and centers on parent.
        /// </summary>
        public CustomRotationDialog(Form1? parent)
        {
            // Basic window setup (dark theme)
            Text = "Custom Rotation";
            StartPosition = parent is null ? FormStartPosition.CenterScreen
                                           : FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(340, 150);

            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;

            if (parent != null) Owner = parent;

            // Label
            var label = new Label
            {
                AutoSize = true,
                Text = "Please input custom rotation value in degrees:",
                Location = new Point(14, 14)
            };
            Controls.Add(label);

            // Input
            angleTextBox = new TextBox
            {
                Location = new Point(18, 48),
                Width = ClientSize.Width - 36,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(angleTextBox);

            // SET button
            setButton = new Button
            {
                Text = "SET",
                Width = 90,
                Height = 30,
                Location = new Point(ClientSize.Width - 18 - 90, 96),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            setButton.FlatAppearance.BorderSize = 0;
            setButton.Click += SetButton_Click;
            Controls.Add(setButton);

            // Resize handler to keep button aligned if dialog size changes
            Resize += (s, e) =>
            {
                setButton.Left = ClientSize.Width - 18 - setButton.Width;
                angleTextBox.Width = ClientSize.Width - 36;
            };
        }

        private void SetButton_Click(object? sender, EventArgs e)
        {
            // Allow empty/any text; Form1 will parse and show errors as needed.
            // If you want lightweight validation here, uncomment below:

            // if (string.IsNullOrWhiteSpace(AngleDeg) ||
            //     !float.TryParse(AngleDeg, System.Globalization.NumberStyles.Float,
            //                     System.Globalization.CultureInfo.InvariantCulture,
            //                     out _))
            // {
            //     MessageBox.Show(this, "Please enter a valid number (degrees).",
            //         "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //     angleTextBox.Focus();
            //     angleTextBox.SelectAll();
            //     return;
            // }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}