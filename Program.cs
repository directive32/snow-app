using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SnowApp
{
    internal static class Program
    {
        private const string MutexName = "SnowApp_SingleInstance_Mutex_Dir";

        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Single-instance handling: gracefully signal any existing instance to exit
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                try
                {
                    // Request old instance to exit cleanly and remove its tray icon
                    Native.PostMessage(Native.HWND_BROADCAST, Native.WM_SNOWAPP_EXIT, IntPtr.Zero, IntPtr.Zero);

                    // Wait up to 1.2s for old instance to release mutex
                    if (!mutex.WaitOne(1200))
                    {
                        var cur = System.Diagnostics.Process.GetCurrentProcess();
                        foreach (var p in System.Diagnostics.Process.GetProcessesByName("snow_app"))
                        {
                            if (p.Id != cur.Id)
                            {
                                p.Kill();
                                p.WaitForExit(500);
                            }
                        }
                    }
                }
                catch { }
            }

            // Immediately sweep away any dead ghost tray icons left from previous abrupt terminations
            Native.RefreshTrayArea();

            var overlay = new SnowOverlay();
            overlay.Show();

            var trayIcon = new NotifyIcon
            {
                Text = "Snow App (Сніг на Windows)",
                Icon = CreateSnowflakeIcon(),
                Visible = true
            };

            void CleanExit()
            {
                try
                {
                    if (trayIcon != null)
                    {
                        trayIcon.Visible = false;
                        trayIcon.Dispose();
                    }
                }
                catch { }
                Native.RefreshTrayArea();
            }

            Application.ApplicationExit += (s, e) => CleanExit();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanExit();

            var contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("❄️ Сніг на Windows") { Enabled = false };
            contextMenu.Items.Add(titleItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            var pauseItem = new ToolStripMenuItem("Пауза");
            pauseItem.Click += (s, e) =>
            {
                overlay.IsPaused = !overlay.IsPaused;
                pauseItem.Checked = overlay.IsPaused;
                pauseItem.Text = overlay.IsPaused ? "Відновити снігопад" : "Пауза";
            };
            contextMenu.Items.Add(pauseItem);

            var intensityMenu = new ToolStripMenuItem("Інтенсивність");
            var lightItem = new ToolStripMenuItem("Легкий сніг (150)");
            var mediumItem = new ToolStripMenuItem("Помірний сніг (300)") { Checked = true };
            var heavyItem = new ToolStripMenuItem("Завірюха (600)");

            lightItem.Click += (s, e) =>
            {
                overlay.FlakeCount = 150;
                lightItem.Checked = true;
                mediumItem.Checked = false;
                heavyItem.Checked = false;
            };
            mediumItem.Click += (s, e) =>
            {
                overlay.FlakeCount = 300;
                lightItem.Checked = false;
                mediumItem.Checked = true;
                heavyItem.Checked = false;
            };
            heavyItem.Click += (s, e) =>
            {
                overlay.FlakeCount = 600;
                lightItem.Checked = false;
                mediumItem.Checked = false;
                heavyItem.Checked = true;
            };

            intensityMenu.DropDownItems.Add(lightItem);
            intensityMenu.DropDownItems.Add(mediumItem);
            intensityMenu.DropDownItems.Add(heavyItem);
            contextMenu.Items.Add(intensityMenu);

            var clearItem = new ToolStripMenuItem("Струсити весь сніг");
            clearItem.Click += (s, e) => overlay.ClearAccumulatedSnow();
            contextMenu.Items.Add(clearItem);

            var restoreItem = new ToolStripMenuItem("Відновити замети на вікнах");
            restoreItem.Click += (s, e) => overlay.RestoreAccumulatedSnow();
            contextMenu.Items.Add(restoreItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Вихід");
            exitItem.Click += (s, e) =>
            {
                CleanExit();
                overlay.Close();
                Application.Exit();
            };
            contextMenu.Items.Add(exitItem);

            trayIcon.ContextMenuStrip = contextMenu;

            Application.Run();

            CleanExit();
        }

        private static Icon CreateSnowflakeIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var pen = new Pen(Color.FromArgb(235, 245, 255), 2.2f);
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;

                float cx = 16f, cy = 16f, r = 11f;

                // 6-arm snowflake
                for (int i = 0; i < 6; i++)
                {
                    double angle = i * Math.PI / 3.0;
                    float x2 = cx + (float)(Math.Cos(angle) * r);
                    float y2 = cy + (float)(Math.Sin(angle) * r);
                    g.DrawLine(pen, cx, cy, x2, y2);

                    // Branches
                    float bx = cx + (float)(Math.Cos(angle) * (r * 0.65f));
                    float by = cy + (float)(Math.Sin(angle) * (r * 0.65f));

                    double bAngle1 = angle + Math.PI / 4.0;
                    double bAngle2 = angle - Math.PI / 4.0;
                    float blen = 3.5f;

                    g.DrawLine(pen, bx, by, bx + (float)(Math.Cos(bAngle1) * blen), by + (float)(Math.Sin(bAngle1) * blen));
                    g.DrawLine(pen, bx, by, bx + (float)(Math.Cos(bAngle2) * blen), by + (float)(Math.Sin(bAngle2) * blen));
                }

                using var brush = new SolidBrush(Color.White);
                g.FillEllipse(brush, cx - 2, cy - 2, 4, 4);
            }

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
    }
}
