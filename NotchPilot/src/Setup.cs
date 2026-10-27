// NotchPilot installer: installs the embedded NotchPilot.exe into the user's folder.
// No admin needed. If "Install admin mode" is checked, Windows asks for UAC approval once and creates the NotchPilot task;
// after that NotchPilot opens as admin without a UAC prompt.
// Build: build\build.ps1

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace NotchPilotSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Installer and uninstaller follow the Windows display language
            Loc.Set(Loc.Detect());
            // Uninstall in the "Apps" list (or NotchPilot-Kaldir.exe /kaldir) enters this mode
            if (Array.IndexOf(args, "/kaldir") >= 0)
            {
                Uninstaller.Run();
                return;
            }
            // Update into a locked (admin) folder: the elevated copy only writes the files
            if (args.Length == 2 && args[0] == "/kopyala")
            {
                try
                {
                    Payload.CopyTo(args[1]);
                    Installer.CopyUninstaller(args[1]);
                    Environment.Exit(0);
                }
                catch (Exception) { Environment.Exit(1); }
            }
            Application.Run(new SetupForm());
        }
    }

    // Writes the embedded NotchPilot.exe to the target folder
    static class Payload
    {
        public const string ResourceName = "NotchPilotPayload";

        public static string CopyTo(string folder)
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "NotchPilot.exe");
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (src == null) throw new InvalidOperationException(Loc.F("setup_no_payload", "NotchPilot"));
                using (FileStream dst = File.Create(target))
                {
                    src.CopyTo(dst);
                }
            }
            return target;
        }
    }

    // Shortcut creation (WScript.Shell; no admin needed)
    static class Shortcut
    {
        public static void Create(string lnkPath, string target, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type scType = sc.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    static class Installer
    {
        // Uninstaller: the installer copies itself into the folder as NotchPilot-Kaldir.exe
        // Relaunches the installer as admin with /kopyala
        public static string CopyElevated(string folder)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/kopyala \"" + folder.TrimEnd('\\') + "\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException(Loc.F("setup_copy_fail", p.ExitCode));
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException(Loc.T("setup_copy_denied"));
            }
            return Path.Combine(folder, "NotchPilot.exe");
        }

        public static void CopyUninstaller(string folder)
        {
            File.Copy(Application.ExecutablePath, Path.Combine(folder, Uninstaller.FileName), true);
        }

        // Registration in the Windows "Apps" list (per user; no admin needed)
        public static void Register(string folder, string exe)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Uninstaller.KeyPath))
            {
                key.SetValue("DisplayName", "NotchPilot");
                key.SetValue("DisplayVersion", "1.1.0");
                key.SetValue("Publisher", "NotchPilot");
                key.SetValue("InstallLocation", folder);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("UninstallString", "\"" + Path.Combine(folder, Uninstaller.FileName) + "\" /kaldir");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        // The exe cannot be overwritten while NotchPilot is running
        public static bool IsRunning()
        {
            return Process.GetProcessesByName("NotchPilot").Length > 0;
        }

        // The installed NotchPilot.exe creates the elevated task with --gorev (this step asks for UAC approval once).
        // Success: null; error or cancel: message to show the user.
        // PawnIO driver (for CPU temperature): the installer embedded in the exe runs silently (-install -silent).
        // Asks for admin approval. Skipped if already installed. Returns a message on problems; setup does not stop.
        public static string InstallPawnIO()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
            {
                if (k != null) return null;
            }
            string tmp = Path.Combine(Path.GetTempPath(), "PawnIO_setup_" + Guid.NewGuid().ToString("N") + ".exe");
            FileStream guard = null;
            try
            {
                using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("PawnIOSetup"))
                {
                    if (src == null) return Loc.T("setup_pawn_missing");
                    using (FileStream dst = File.Create(tmp))
                    {
                        src.CopyTo(dst);
                    }
                }
                // The file that will run as admin is kept locked against writes until it runs
                guard = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
                ProcessStartInfo psi = new ProcessStartInfo(tmp, "-install -silent");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) return Loc.F("setup_pawn_fail", p.ExitCode);
                }
                return null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Loc.T("setup_pawn_denied");
            }
            finally
            {
                if (guard != null) guard.Dispose();
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }

        public static string InstallAdminTask(string exe)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--gorev \"" + user + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process p = Process.Start(psi);
                if (p == null) return Loc.F("setup_task_start_fail", "NotchPilot");
                using (p)
                {
                    p.WaitForExit();
                    return p.ExitCode == 0 ? null : Loc.F("setup_task_fail", "NotchPilot", p.ExitCode);
                }
            }
            catch (Exception)
            {
                return Loc.F("setup_task_denied", "NotchPilot");
            }
        }
    }

    // Uninstall: deletes the program folder, shortcuts, admin task, settings and registry entry
    static class Uninstaller
    {
        public const string FileName = "NotchPilot-Kaldir.exe";
        public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NotchPilot";

        public static void Run()
        {
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            bool answer = DarkBox.Ask(null, Loc.F("uninst_ask", "NotchPilot"), Loc.F("uninst_title", "NotchPilot"));
            if (!answer) return;

            if (Installer.IsRunning())
            {
                DarkBox.Show(null, Loc.F("uninst_running", "NotchPilot"), Loc.F("uninst_title", "NotchPilot"));
                return;
            }

            DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NotchPilot.lnk"));
            DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "NotchPilot.lnk"));
            DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NotchPilot.lnk"));
            RemoveTask();
            DeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchPilot"));
            try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false); }
            catch (Exception) { }

            // This uninstaller is also in the folder; since the running file is locked, the folder is deleted a few seconds after exit.
            // Deleted only if it really is the NotchPilot install folder (named NotchPilot and containing NotchPilot.exe).
            if (Path.GetFileName(folder) == "NotchPilot" && File.Exists(Path.Combine(folder, "NotchPilot.exe")))
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + folder + "\"");
                if (CanWrite(folder))
                {
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                }
                else
                {
                    // If admin mode locked the folder, deleting it requires Windows approval once
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                }
                try { Process.Start(psi); }
                catch (Exception) { }
            }
            DarkBox.Show(null, Loc.F("uninst_done", "NotchPilot"), Loc.F("uninst_title", "NotchPilot"));
        }

        // Deletes the task if present. If normal rights are not enough, does one elevated delete (Windows approval).
        static void RemoveTask()
        {
            string schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            if (!RunSilent(schtasks, "/Query /TN NotchPilot")) return;
            if (RunSilent(schtasks, "/Delete /TN NotchPilot /F")) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(schtasks, "/Delete /TN NotchPilot /F");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process p = Process.Start(psi);
                if (p != null) { using (p) { p.WaitForExit(); } }
            }
            catch (Exception) { }
        }

        static bool CanWrite(string folder)
        {
            string probe = Path.Combine(folder, "yazma_testi_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool RunSilent(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }

        static void DeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        static void DeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception) { }
        }
    }

    // Dark-themed checkbox: drawn by hand instead of Windows' white CheckBox
    public class DarkCheck : Control
    {
        public event EventHandler CheckedChanged;
        const int Box = 14;
        static readonly Color Fill = Color.FromArgb(50, 54, 62);
        static readonly Color Edge = Color.FromArgb(150, 156, 166);
        static readonly Color Accent = Color.FromArgb(0, 200, 255);
        static readonly Color Dark = Color.FromArgb(24, 26, 30);
        bool isChecked;
        bool hover;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = Dark;
            ForeColor = Color.FromArgb(235, 238, 242);
            Font = new Font(Loc.Face("Segoe UI"), 9.5f);
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            Rectangle box = new Rectangle(1, (Height - Box) / 2, Box, Box);
            Color fill = isChecked && Enabled ? Accent : Fill;
            Color edge = !Enabled ? Edge : (isChecked || hover ? Accent : Edge);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush f = new SolidBrush(fill)) g.FillRectangle(f, box);
            using (Pen p = new Pen(edge)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
            if (isChecked)
            {
                Point[] tick = { new Point(box.X + 3, box.Y + 7), new Point(box.X + 6, box.Y + 10), new Point(box.X + 11, box.Y + 4) };
                using (Pen p = new Pen(Enabled ? Dark : Edge, 2f)) g.DrawLines(p, tick);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Box + 9, 0, Width - Box - 9, Height), Enabled ? ForeColor : Edge, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }
    }

    // Dark-themed message box: our own window instead of Windows' MessageBox (OK / Yes-No)
    public static class DarkBox
    {
        static readonly Color Card = Color.FromArgb(36, 39, 45);
        static readonly Color CardHover = Color.FromArgb(52, 56, 64);
        static readonly Color TextMain = Color.FromArgb(235, 238, 242);
        static readonly Color Accent = Color.FromArgb(0, 200, 255);
        static readonly Color Bg = Color.FromArgb(24, 26, 30);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Show(IWin32Window owner, string text, string caption)
        {
            Run(owner, text, caption, false);
        }

        public static bool Ask(IWin32Window owner, string text, string caption)
        {
            return Run(owner, text, caption, true);
        }

        static bool Run(IWin32Window owner, string text, string caption, bool ask)
        {
            using (Form f = new Form())
            {
                f.Text = caption;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.BackColor = Card;
                f.ForeColor = TextMain;
                f.Font = new Font(Loc.Face("Segoe UI"), 9.5f);

                Label body = new Label();
                body.Text = text;
                body.AutoSize = true;
                body.MaximumSize = new Size(360, 0);
                body.Location = new Point(20, 20);
                body.ForeColor = TextMain;
                f.Controls.Add(body);

                int top = body.Bottom + 20;
                f.ClientSize = new Size(400, top + 30 + 20);

                if (ask)
                {
                    Button yes = Btn(Loc.T("dlg_yes"), Accent, Bg, new Point(f.ClientSize.Width - 200, top), DialogResult.Yes);
                    Button no = Btn(Loc.T("dlg_no"), CardHover, TextMain, new Point(f.ClientSize.Width - 102, top), DialogResult.No);
                    f.Controls.Add(yes);
                    f.Controls.Add(no);
                    f.AcceptButton = yes;
                    f.CancelButton = no;
                }
                else
                {
                    Button ok = Btn(Loc.T("dlg_ok"), Accent, Bg, new Point(f.ClientSize.Width - 110, top), DialogResult.OK);
                    f.Controls.Add(ok);
                    f.AcceptButton = ok;
                }

                f.HandleCreated += delegate { ApplyDark(f.Handle); };
                DialogResult result = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                return result == DialogResult.Yes;
            }
        }

        static Button Btn(string text, Color back, Color fore, Point at, DialogResult result)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(90, 30);
            b.Location = at;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.BorderColor = back;
            b.FlatAppearance.MouseOverBackColor = back;
            b.FlatAppearance.MouseDownBackColor = back;
            b.BackColor = back;
            b.ForeColor = fore;
            b.DialogResult = result;
            return b;
        }

        // Dark title bar (Win10 20H1+ / Win11); older versions keep the default
        static void ApplyDark(IntPtr handle)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                int caption = Card.R | (Card.G << 8) | (Card.B << 16);
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
                int text = TextMain.R | (TextMain.G << 8) | (TextMain.B << 16);
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows versions do not have these attributes
            }
        }
    }

    public class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 30);
        static readonly Color Card = Color.FromArgb(36, 39, 45);
        static readonly Color TextMain = Color.FromArgb(235, 238, 242);
        static readonly Color Muted = Color.FromArgb(150, 156, 166);
        static readonly Color Accent = Color.FromArgb(0, 200, 255);
        static readonly Color Red = Color.FromArgb(255, 96, 96);

        readonly TextBox folderBox = new TextBox();
        readonly DarkCheck desktopBox = new DarkCheck();
        readonly DarkCheck adminBox = new DarkCheck();
        readonly DarkCheck runBox = new DarkCheck();
        readonly Label status = new Label();
        readonly Button installBtn = new Button();

        public SetupForm()
        {
            Text = Loc.F("setup_title", "NotchPilot");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font(Loc.Face("Segoe UI"), 9.5f);
            ClientSize = new Size(460, 390);

            Label title = MakeLabel("NotchPilot", new Point(24, 16), Accent);
            title.Font = new Font(Loc.Face("Segoe UI Semibold"), 20f);
            Controls.Add(title);
            Controls.Add(MakeLabel(Loc.T("setup_tag_np"), new Point(26, 66), Muted));

            Controls.Add(MakeLabel(Loc.T("setup_folder"), new Point(24, 104), TextMain));
            folderBox.Location = new Point(24, 128);
            folderBox.Size = new Size(328, 24);
            folderBox.BackColor = Card;
            folderBox.ForeColor = TextMain;
            folderBox.BorderStyle = BorderStyle.FixedSingle;
            folderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NotchPilot");
            Controls.Add(folderBox);

            Button browse = new Button();
            browse.Text = Loc.T("setup_browse");
            browse.Location = new Point(362, 127);
            browse.Size = new Size(74, 26);
            StyleButton(browse, Card, TextMain);
            browse.Click += delegate { Browse(); };
            Controls.Add(browse);

            StyleCheck(desktopBox, Loc.T("setup_desktop"), 24, 170, true);
            StyleCheck(adminBox, Loc.T("setup_admin"), 24, 200, true);
            StyleCheck(runBox, Loc.F("setup_run", "NotchPilot"), 24, 230, true);

            Label info = MakeLabel(Loc.T("setup_info"), new Point(24, 264), Muted);
            info.Size = new Size(412, 36);
            info.AutoSize = false;
            Controls.Add(info);

            status.Location = new Point(24, 302);
            status.Size = new Size(412, 40);
            status.ForeColor = Muted;
            Controls.Add(status);

            installBtn.Text = Loc.T("setup_install");
            installBtn.Location = new Point(264, 344);
            installBtn.Size = new Size(84, 32);
            StyleButton(installBtn, Accent, Bg);
            installBtn.Click += delegate { Install(); };
            Controls.Add(installBtn);

            Button close = new Button();
            close.Text = Loc.T("close");
            close.Location = new Point(354, 344);
            close.Size = new Size(84, 32);
            StyleButton(close, Card, TextMain);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            AcceptButton = installBtn;
            CancelButton = close;
        }

        // Title bar in the same dark color as the body (Windows 11 DWM attributes)
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));                 // dark mode
                int caption = ColorRef(Bg);
                DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));              // title color (Win11)
                int text = ColorRef(TextMain);
                DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));                 // title text (Win11)
            }
            catch (Exception)
            {
                // Older Windows versions open with the default color
            }
        }

        // COLORREF: 0x00BBGGRR
        static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        Label MakeLabel(string text, Point at, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.AutoSize = true;
            l.Location = at;
            return l;
        }

        void StyleCheck(DarkCheck cb, string text, int x, int y, bool value)
        {
            cb.Text = text;
            cb.ForeColor = TextMain;
            cb.Location = new Point(x, y);
            cb.Size = new Size(412, 24);
            cb.Checked = value;
            Controls.Add(cb);
        }

        static void StyleButton(Button b, Color back, Color fore)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = fore;
        }

        void Show(string text, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Red : Muted;
            status.Refresh();
        }

        void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = Loc.F("setup_pick_folder", "NotchPilot");
                dlg.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    folderBox.Text = Path.Combine(dlg.SelectedPath, "NotchPilot");
            }
        }

        void Install()
        {
            string folder = folderBox.Text.Trim();
            if (folder.Length == 0) { Show(Loc.T("setup_need_folder"), true); return; }
            if (Installer.IsRunning()) { Show(Loc.F("setup_running", "NotchPilot"), true); return; }

            bool ok = false;
            installBtn.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                Show(Loc.T("setup_copying"), false);
                string exe;
                try
                {
                    exe = Payload.CopyTo(folder);
                    Installer.CopyUninstaller(folder);
                }
                catch (UnauthorizedAccessException)
                {
                    // Admin mode locked the folder: the files are written once with Windows approval
                    Show(Loc.T("setup_locked"), false);
                    exe = Installer.CopyElevated(folder);
                }
                Installer.Register(folder, exe);

                Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NotchPilot.lnk"), exe, "NotchPilot");
                Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "NotchPilot.lnk"), exe, "NotchPilot");
                if (desktopBox.Checked)
                    Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NotchPilot.lnk"), exe, "NotchPilot");

                string adminMsg = null;
                if (adminBox.Checked)
                {
                    Show(Loc.T("setup_admin_busy"), false);
                    adminMsg = Installer.InstallAdminTask(exe);
                }

                Show(Loc.T("setup_driver_busy"), false);
                string pawnMsg = Installer.InstallPawnIO();

                if (runBox.Checked) Process.Start(exe);

                string done = Loc.F("setup_done", folder);
                if (adminMsg != null) done += "\n\n" + adminMsg;
                if (pawnMsg != null) done += "\n\n" + pawnMsg;
                DarkBox.Show(this, done, Loc.F("setup_title", "NotchPilot"));
                ok = true;
            }
            catch (Exception ex)
            {
                Show(Loc.F("setup_failed", ex.Message), true);
            }
            finally
            {
                installBtn.Enabled = true;
                Cursor = Cursors.Default;
            }
            if (ok) Close();
        }
    }
}
