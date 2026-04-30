using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ImagePaster
{
    public class MainForm : Form
    {
        // ---------------------------------------------------------
        // UI Elements - عناصر الواجهة
        // ---------------------------------------------------------
        private PictureBox pictureBox;
        private Label infoLabel;
        private Image currentImage;
        private string tempFilePath;
        private Panel titleBar;
        private Label titleLabel;
        private Button btnClose;
        private Button btnMin;
        private Panel contentPanel;
        
        // ---------------------------------------------------------
        // History & Grid Elements - عناصر التاريخ والشبكة
        // ---------------------------------------------------------
        private Panel historyPanel;
        private FlowLayoutPanel historyFlow;
        private Label historyTitle;
        private Button btnClear;
        private System.Windows.Forms.Timer clipboardTimer;
        private string lastClipboardHash = "";

        // ---------------------------------------------------------
        // System Tray & Hotkey - أيقونة النظام والاختصارات
        // ---------------------------------------------------------
        private NotifyIcon trayIcon;
        private const int HOTKEY_ID = 9000;
        private const int WM_HOTKEY = 0x0312;

        // ---------------------------------------------------------
        // Single Instance - لمنع فتح أكثر من نسخة
        // ---------------------------------------------------------
        private static Mutex mutex = new Mutex(true, "{ImagePaster-Unique-Mutex-ID}");

        // ---------------------------------------------------------
        // App Settings - إعدادات التطبيق
        // ---------------------------------------------------------
        private int appWidth = 460;
        private int appHeight = 500;

        // ---------------------------------------------------------
        // Win32 API Imports - استيراد دوال النظام
        // ---------------------------------------------------------
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr winHandle);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData 
        { 
            public WindowCompositionAttribute Attribute; 
            public IntPtr Data; 
            public int SizeOfData; 
        }

        internal enum WindowCompositionAttribute 
        { 
            WCA_ACCENT_POLICY = 19 
        }

        internal enum AccentState 
        { 
            ACCENT_ENABLE_BLURBEHIND = 3 
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy 
        { 
            public AccentState AccentState; 
            public int AccentFlags; 
            public int GradientColor; 
            public int AnimationId; 
        }

        public MainForm()
        {
            // إعدادات النافذة الرئيسية
            this.Size = new Size(appWidth, appHeight);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.ShowInTaskbar = true;

            // تحميل الأيقونة - استخدام المسار المطلق لضمان عملها عند التشغيل مع النظام
            try 
            { 
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath)) 
                {
                    this.Icon = new Icon(iconPath); 
                }
                else
                {
                    // محاولة استخراج الأيقونة المدمجة في ملف EXE
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            } 
            catch { }
            
            // تفعيل تأثير الزجاج
            EnableAcrylic(this.Handle);
            
            // التفعيل عند الإقلاع
            SetStartup(true);

            // -----------------------------------------------------
            // Title Bar - شريط العنوان
            // -----------------------------------------------------
            titleBar = new Panel();
            titleBar.Height = 40;
            titleBar.Dock = DockStyle.Top;
            titleBar.BackColor = Color.Transparent; 

            titleLabel = new Label();
            titleLabel.Text = "Image Paster Pro";
            titleLabel.ForeColor = Color.White;
            titleLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            titleLabel.Location = new Point(15, 10);
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;

            btnMin = CreateTitleButton("-", appWidth - 80);
            btnMin.Click += delegate(object sender, EventArgs e) 
            { 
                ToggleWindow();
            };

            btnClose = CreateTitleButton("✕", appWidth - 40);
            btnClose.Click += delegate(object sender, EventArgs e) 
            { 
                ToggleWindow();
            };
            btnClose.MouseEnter += delegate(object sender, EventArgs e) 
            { 
                btnClose.BackColor = Color.FromArgb(150, 255, 0, 0); 
            };
            btnClose.MouseLeave += delegate(object sender, EventArgs e) 
            { 
                btnClose.BackColor = Color.Transparent; 
            };

            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(btnMin);
            titleBar.Controls.Add(btnClose);
            titleBar.MouseDown += Window_MouseDown;
            titleLabel.MouseDown += Window_MouseDown;

            // -----------------------------------------------------
            // Content Area - منطقة العرض الرئيسية
            // -----------------------------------------------------
            contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.BackColor = Color.Transparent;

            infoLabel = new Label();
            infoLabel.Text = "DROP OR PASTE HERE";
            infoLabel.ForeColor = Color.White;
            infoLabel.TextAlign = ContentAlignment.MiddleCenter;
            infoLabel.Dock = DockStyle.Fill;
            infoLabel.Font = new Font("Segoe UI Semibold", 14);
            infoLabel.BackColor = Color.Transparent;

            pictureBox = new PictureBox();
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.Visible = false;
            pictureBox.Cursor = Cursors.Hand;
            pictureBox.BackColor = Color.Transparent;

            // لوحة التوهج (Glow Panel) خلف الصورة
            Panel glowPanel = new Panel();
            glowPanel.Dock = DockStyle.Fill;
            glowPanel.Padding = new Padding(5);
            glowPanel.BackColor = Color.Transparent;
            glowPanel.Controls.Add(pictureBox);

            contentPanel.Controls.Add(glowPanel);
            contentPanel.Controls.Add(infoLabel);

            pictureBox.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (isDraggingImage)
                {
                    using (Pen p = new Pen(Color.White, 3))
                    {
                        p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        e.Graphics.DrawRectangle(p, 2, 2, pictureBox.Width - 4, pictureBox.Height - 4);
                    }
                }
            };
            // تمت إزالة منطق التوهج الخلفي القديم واستبداله بالرسم المباشر

            // -----------------------------------------------------
            // History Panel - منطقة التاريخ والشبكة
            // -----------------------------------------------------
            historyPanel = new Panel();
            historyPanel.Height = 180;
            historyPanel.Dock = DockStyle.Bottom;
            historyPanel.BackColor = Color.Transparent; 

            historyTitle = new Label();
            historyTitle.Text = "IMAGE HISTORY";
            historyTitle.ForeColor = Color.FromArgb(150, 255, 255, 255);
            historyTitle.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            historyTitle.Location = new Point(15, 10);
            historyTitle.AutoSize = true;

            btnClear = new Button();
            btnClear.Text = "CLEAR";
            btnClear.Size = new Size(110, 25);
            btnClear.Location = new Point(appWidth - 125, 5);
            btnClear.FlatStyle = FlatStyle.Flat;
            btnClear.ForeColor = Color.OrangeRed;
            btnClear.Font = new Font("Segoe UI", 7, FontStyle.Bold);
            btnClear.Cursor = Cursors.Hand;
            btnClear.FlatAppearance.BorderSize = 1;
            btnClear.FlatAppearance.BorderColor = Color.FromArgb(100, 255, 69, 0);
            btnClear.Click += delegate(object sender, EventArgs e) 
            { 
                historyFlow.Controls.Clear(); 
            };

            // حاوية لإخفاء السكرول بار
            Panel historyContainer = new Panel();
            historyContainer.Dock = DockStyle.Fill;
            historyContainer.BackColor = Color.Transparent;

            historyFlow = new FlowLayoutPanel();
            historyFlow.AutoScroll = true;
            historyFlow.BackColor = Color.Transparent;
            historyFlow.Padding = new Padding(10, 35, 10, 10);
            historyFlow.Width = appWidth + 40; 
            historyFlow.Height = 180;
            historyFlow.Location = new Point(0, 0);
            historyFlow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;

            historyContainer.Controls.Add(historyFlow);
            historyPanel.Controls.Add(btnClear);
            historyPanel.Controls.Add(historyTitle);
            historyPanel.Controls.Add(historyContainer);

            // -----------------------------------------------------
            // إضافة العناصر للنموذج
            // -----------------------------------------------------
            this.Controls.Add(contentPanel);
            this.Controls.Add(historyPanel);
            this.Controls.Add(titleBar);

            // ربط السحب والإفلات
            SetupDragDrop(this);
            SetupDragDrop(contentPanel);
            SetupDragDrop(infoLabel);
            SetupDragDrop(pictureBox);

            pictureBox.MouseDown += PictureBox_MouseDown;
            infoLabel.Click += delegate(object sender, EventArgs e) 
            { 
                if (Clipboard.ContainsImage()) PasteFromClipboard(); 
            };

            // -----------------------------------------------------
            // Tray Icon & Monitor
            // -----------------------------------------------------
            trayIcon = new NotifyIcon();
            trayIcon.Text = "Image Paster Pro";
            try 
            { 
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath)) 
                    trayIcon.Icon = new Icon(iconPath); 
                else 
                    trayIcon.Icon = this.Icon ?? SystemIcons.Application; 
            } 
            catch 
            { 
                trayIcon.Icon = this.Icon ?? SystemIcons.Application; 
            }
            trayIcon.Visible = true;
            trayIcon.Click += delegate(object sender, EventArgs e) 
            { 
                ToggleWindow();
            };
            
            ContextMenu trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Show", delegate(object sender, EventArgs e) { ToggleWindow(); });
            trayMenu.MenuItems.Add("Exit", delegate(object sender, EventArgs e) { trayIcon.Visible = false; Application.Exit(); });
            trayIcon.ContextMenu = trayMenu;

            // تسجيل الاختصار العالمي F9
            RegisterHotKey(this.Handle, HOTKEY_ID, 0, (int)Keys.F9);

            // مؤقت مراقبة الحافظة
            clipboardTimer = new System.Windows.Forms.Timer();
            clipboardTimer.Interval = 1000;
            clipboardTimer.Tick += delegate(object sender, EventArgs e) 
            { 
                CheckClipboard(); 
            };
            clipboardTimer.Start();

            // رسم الحدود الخارجية
            this.Paint += delegate(object sender, PaintEventArgs e) 
            {
                using (Pen p = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            };
        }

        private void PlaySound()
        {
            try
            {
                if (File.Exists("hide_f9.mp3"))
                {
                    mciSendString("close mp3", null, 0, IntPtr.Zero);
                    mciSendString("open \"hide_f9.mp3\" type mpegvideo alias mp3", null, 0, IntPtr.Zero);
                    mciSendString("play mp3", null, 0, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void ToggleWindow()
        {
            if (this.Visible)
            {
                PlaySound();
                this.Hide();
            }
            else
            {
                PlaySound();
                this.Show();
                this.Activate();
            }
        }

        private void AddToHistory(Image img)
        {
            PictureBox thumb = new PictureBox();
            int normalSize = 85;
            int hoverSize = 89; 
            
            thumb.Size = new Size(normalSize, normalSize);
            thumb.Image = img;
            thumb.SizeMode = PictureBoxSizeMode.Zoom;
            thumb.Cursor = Cursors.Hand;
            thumb.Margin = new Padding(12); 
            thumb.BackColor = Color.FromArgb(10, 255, 255, 255);
            
            thumb.MouseEnter += delegate(object sender, EventArgs e) 
            { 
                thumb.Size = new Size(hoverSize, hoverSize); 
                thumb.BackColor = Color.FromArgb(25, 255, 255, 255); 
            };
            
            thumb.MouseLeave += delegate(object sender, EventArgs e) 
            { 
                thumb.Size = new Size(normalSize, normalSize); 
                thumb.BackColor = Color.FromArgb(10, 255, 255, 255); 
            };
            
            thumb.Click += delegate(object sender, EventArgs e) 
            { 
                DisplayImage(img); 
            };
            
            if (historyFlow.Controls.Count > 40) 
            {
                historyFlow.Controls.RemoveAt(0);
            }
            historyFlow.Controls.Add(thumb);
            historyFlow.ScrollControlIntoView(thumb);
        }

        private void DisplayImage(Image img)
        {
            currentImage = img;
            pictureBox.Image = currentImage;
            pictureBox.Visible = true;
            pictureBox.BringToFront();
            infoLabel.Visible = false;
            pictureBox.Update();
        }

        private void SetImage(Image img)
        {
            DisplayImage(img);
            lastClipboardHash = img.Width + "x" + img.Height;
            AddToHistory(img);
        }

        private void CheckClipboard()
        {
            try 
            {
                if (Clipboard.ContainsImage()) 
                {
                    Image img = Clipboard.GetImage();
                    string currentHash = img.Width + "x" + img.Height;
                    if (currentHash != lastClipboardHash) 
                    { 
                        lastClipboardHash = currentHash; 
                        AddToHistory(img); 
                    }
                }
            } 
            catch { }
        }

        private void SetStartup(bool enable)
        {
            try 
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (enable) 
                {
                    key.SetValue("ImagePaster", "\"" + Application.ExecutablePath + "\"");
                }
                else 
                {
                    key.DeleteValue("ImagePaster", false);
                }
            } 
            catch { }
        }

        private void SetupDragDrop(Control ctrl) 
        { 
            ctrl.AllowDrop = true; 
            ctrl.DragEnter += delegate(object sender, DragEventArgs e) 
            { 
                if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)) 
                    e.Effect = DragDropEffects.Copy; 
            }; 
            ctrl.DragDrop += MainForm_DragDrop; 
        }

        private void EnableAcrylic(IntPtr handle) 
        { 
            AccentPolicy accent = new AccentPolicy(); 
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND; 
            int accentStructSize = System.Runtime.InteropServices.Marshal.SizeOf(accent); 
            IntPtr accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentStructSize); 
            System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false); 
            WindowCompositionAttributeData data = new WindowCompositionAttributeData(); 
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY; 
            data.SizeOfData = accentStructSize; 
            data.Data = accentPtr; 
            SetWindowCompositionAttribute(handle, ref data); 
            System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr); 
        }

        private Button CreateTitleButton(string text, int x) 
        { 
            Button btn = new Button();
            btn.Text = text; 
            btn.ForeColor = Color.White; 
            btn.Size = new Size(40, 40); 
            btn.Location = new Point(x, 0); 
            btn.FlatStyle = FlatStyle.Flat; 
            btn.FlatAppearance.BorderSize = 0; 
            btn.Font = new Font("Arial", 10, FontStyle.Bold); 
            btn.BackColor = Color.Transparent;
            return btn; 
        }

        private void Window_MouseDown(object sender, MouseEventArgs e) 
        { 
            if (e.Button == MouseButtons.Left) 
            { 
                ReleaseCapture(); 
                SendMessage(Handle, 0xA1, 0x2, 0); 
            } 
        }

        private void PasteFromClipboard() 
        { 
            if (Clipboard.ContainsImage()) 
                SetImage(Clipboard.GetImage()); 
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e) 
        { 
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) 
            { 
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop); 
                if (files.Length > 0) 
                {
                    try { SetImage(Image.FromFile(files[0])); } catch { } 
                }
            } 
            else if (e.Data.GetDataPresent(DataFormats.Bitmap)) 
            {
                SetImage((Image)e.Data.GetData(DataFormats.Bitmap)); 
            }
        }

        private bool isDraggingImage = false;

        private void PictureBox_MouseDown(object sender, MouseEventArgs e) 
        { 
            if (currentImage != null && e.Button == MouseButtons.Left) 
            { 
                // تفعيل حالة السحب لتشغيل الرسم في حدث الـ Paint
                isDraggingImage = true;
                pictureBox.Invalidate(); // إجبار الصورة على إعادة الرسم لإظهار الإطار

                tempFilePath = Path.Combine(Path.GetTempPath(), "pasted_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png"); 
                currentImage.Save(tempFilePath, System.Drawing.Imaging.ImageFormat.Png); 
                DataObject data = new DataObject(DataFormats.FileDrop, new string[] { tempFilePath }); 
                
                // عملية السحب
                pictureBox.DoDragDrop(data, DragDropEffects.Copy); 

                // إطفاء حالة السحب وإخفاء الإطار
                isDraggingImage = false;
                pictureBox.Invalidate();
            } 
        }

        protected override void WndProc(ref Message m) 
        { 
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) 
            { 
                ToggleWindow();
            } 
            base.WndProc(ref m); 
        }

        protected override void OnFormClosing(FormClosingEventArgs e) 
        { 
            UnregisterHotKey(this.Handle, HOTKEY_ID); 
            base.OnFormClosing(e); 
        }

        [STAThread] 
        static void Main() 
        { 
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                return;
            }
            Application.EnableVisualStyles(); 
            Application.SetCompatibleTextRenderingDefault(false); 
            Application.Run(new MainForm()); 
        }
    }
}
