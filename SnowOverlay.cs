using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SnowApp
{
    public class SnowOverlay : Form
    {
        private readonly int _width;
        private readonly int _height;

        private IntPtr _hMemDC;
        private IntPtr _hBitmap;
        private IntPtr _oldBitmap;
        private IntPtr _pBits;

        private Thread? _renderThread;
        private volatile bool _isRunning;
        private volatile bool _isPaused;
        private int _flakeCount = 200;

        private readonly List<Flake> _flakes = new();
        private readonly List<SpilledParticle> _spilledParticles = new();
        private ObstacleManager? _obstacleManager;

        private Native.POINT _lastMousePos;
        private IntPtr _lastForegroundHwnd;
        private readonly Stopwatch _sw = new();
        private static readonly Random Rand = new Random();

        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        public int FlakeCount
        {
            get => _flakeCount;
            set
            {
                _flakeCount = value;
                AdjustFlakeCount();
            }
        }

        public SnowOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            var screen = Screen.PrimaryScreen.Bounds;
            _width = screen.Width;
            _height = screen.Height;

            Location = new Point(0, 0);
            Size = new Size(_width, _height);

            InitDIB();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST
                           | Native.WS_EX_TRANSPARENT
                           | Native.WS_EX_LAYERED
                           | Native.WS_EX_TOOLWINDOW
                           | Native.WS_EX_NOACTIVATE;
                cp.Style = unchecked((int)Native.WS_POPUP);
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Native.WM_SNOWAPP_EXIT)
            {
                try
                {
                    Close();
                    Application.Exit();
                }
                catch { }
                return;
            }
            base.WndProc(ref m);
        }

        private void InitDIB()
        {
            IntPtr hScreenDC = Native.GetDC(IntPtr.Zero);
            _hMemDC = Native.CreateCompatibleDC(hScreenDC);

            var bmi = new Native.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = _width;
            bmi.bmiHeader.biHeight = -_height; // Top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = Native.BI_RGB;

            _hBitmap = Native.CreateDIBSection(_hMemDC, ref bmi, Native.DIB_RGB_COLORS, out _pBits, IntPtr.Zero, 0);
            _oldBitmap = Native.SelectObject(_hMemDC, _hBitmap);

            Native.ReleaseDC(IntPtr.Zero, hScreenDC);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _obstacleManager = new ObstacleManager(Handle, _width, _height);
            AdjustFlakeCount(fullInit: true);

            _isRunning = true;
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();
        }

        private void AdjustFlakeCount(bool fullInit = false)
        {
            lock (_flakes)
            {
                while (_flakes.Count < _flakeCount)
                {
                    var f = new Flake();
                    f.Reset(_width, _height, fullInit);
                    _flakes.Add(f);
                }
                if (_flakes.Count > _flakeCount)
                {
                    _flakes.RemoveRange(_flakeCount, _flakes.Count - _flakeCount);
                }
            }
        }

        public void ClearAccumulatedSnow()
        {
            if (_obstacleManager != null)
            {
                var spilled = new List<SpilledParticle>();
                _obstacleManager.ClearAllSnowBlankets(spilled);
                if (spilled.Count > 0)
                {
                    lock (_spilledParticles)
                    {
                        int maxParticles = 600;
                        int overflow = (_spilledParticles.Count + spilled.Count) - maxParticles;
                        if (overflow > 0)
                        {
                            _spilledParticles.RemoveRange(0, Math.Min(overflow, _spilledParticles.Count));
                        }
                        _spilledParticles.AddRange(spilled);
                    }
                }
            }
            else
            {
                lock (_spilledParticles)
                {
                    _spilledParticles.Clear();
                }
            }
        }

        public void RestoreAccumulatedSnow()
        {
            if (_obstacleManager != null)
            {
                _obstacleManager.RestoreAllSnowBlankets();
            }
        }

        private unsafe void RenderLoop()
        {
            _sw.Start();
            long lastMemoryTrim = 0;
            long lastFrameTicks = _sw.ElapsedTicks;
            double invFreq = 1.0 / Stopwatch.Frequency;
            var sbFgClass = new StringBuilder(128);

            Native.GetCursorPos(out _lastMousePos);

            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA
            };

            var ptDst = new Native.POINT(0, 0);
            var ptSrc = new Native.POINT(0, 0);
            var size = new Native.SIZE(_width, _height);

            int pixelCount = _width * _height;
            var frameSpilled = new List<SpilledParticle>();

            while (_isRunning)
            {
                long currentTicks = _sw.ElapsedTicks;
                float dt = Math.Clamp((float)((currentTicks - lastFrameTicks) * invFreq), 0.005f, 0.05f);
                lastFrameTicks = currentTicks;

                if (_isPaused)
                {
                    Thread.Sleep(100);
                    continue;
                }

                // Smooth time delta factor (normalized to 60 FPS)
                float frameFactor = dt * 60.0f;

                // Mouse state
                Native.GetCursorPos(out var curMouse);
                float mouseDx = curMouse.X - _lastMousePos.X;
                float mouseDy = curMouse.Y - _lastMousePos.Y;
                float mouseDist = (float)Math.Sqrt(mouseDx * mouseDx + mouseDy * mouseDy);
                bool isMouseMoving = mouseDist >= 1.5f;
                bool isLButtonDown = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
                _lastMousePos = curMouse;

                var obstacles = _obstacleManager?.GetSnapshot() ?? new List<WindowObstacle>();

                // 1. INSTANT ALT+TAB SYNC
                IntPtr fgHwnd = Native.GetForegroundWindow();
                if (fgHwnd != _lastForegroundHwnd && fgHwnd != IntPtr.Zero)
                {
                    sbFgClass.Clear();
                    Native.GetClassName(fgHwnd, sbFgClass, 128);
                    string fgCls = sbFgClass.ToString();

                    if (!ObstacleManager.IsAltTabOrSystemOverlay(fgCls))
                    {
                        _lastForegroundHwnd = fgHwnd;
                        foreach (var obs in obstacles)
                        {
                            if (obs.Hwnd == fgHwnd)
                            {
                                obs.ZIndex = 0;
                            }
                            else if (obs.ZIndex == 0)
                            {
                                obs.ZIndex = 1;
                            }
                        }
                    }
                }

                frameSpilled.Clear();
                foreach (var obs in obstacles)
                {
                    if (Native.GetExactWindowRect(obs.Hwnd, out var currentRect))
                    {
                        if (currentRect.Width <= 0 || currentRect.Height <= 0) continue;

                        int newW = Math.Max(1, currentRect.Width);
                        if (newW != obs.SnowMap.Length)
                        {
                            obs.EnsureWidth(newW);
                        }

                        int dx = currentRect.Left - obs.Rect.Left;
                        int dy = currentRect.Top - obs.Rect.Top;
                        obs.DeltaX = dx;
                        obs.DeltaY = dy;
                        obs.IsMoved = (dx != 0 || dy != 0);
                        obs.Rect = currentRect;

                        if (dx != 0 || dy != 0)
                        {
                            obs.HandleWindowMovement(dx, dy, frameSpilled);
                        }
                    }
                }

                if (frameSpilled.Count > 0)
                {
                    lock (_spilledParticles)
                    {
                        const int maxParticles = 5500;
                        int overflow = (_spilledParticles.Count + frameSpilled.Count) - maxParticles;
                        if (overflow > 0)
                        {
                            _spilledParticles.RemoveRange(0, Math.Min(overflow, _spilledParticles.Count));
                        }
                        _spilledParticles.AddRange(frameSpilled);
                    }
                }

                // Clear memory buffer
                uint* pixels = (uint*)_pBits.ToPointer();
                Unsafe.InitBlock(pixels, 0, (uint)(pixelCount * 4));

                // Melt snow layers very slowly
                foreach (var obs in obstacles)
                {
                    obs.Melt(dt);
                }

                // Mouse Tip interaction: ONLY occurs when mouse is actively moving!
                if (isMouseMoving)
                {
                    var carvedParticles = new List<SpilledParticle>();
                    foreach (var obs in obstacles)
                    {
                        if (curMouse.X >= obs.Rect.Left && curMouse.X < obs.Rect.Right)
                        {
                            int lx = curMouse.X - obs.Rect.Left;
                            float h = (lx >= 0 && lx < obs.SnowMap.Length) ? obs.SnowMap[lx] : 0f;
                            float snowTopY = obs.Rect.Top - h;

                            if (curMouse.Y >= snowTopY - 2 && curMouse.Y <= obs.Rect.Top + 2)
                            {
                                // Check if this point on obs or the cursor is occluded by any foreground window in front of obs
                                bool isOccluded = false;
                                for (int j = 0; j < obs.ZIndex && j < obstacles.Count; j++)
                                {
                                    var higher = obstacles[j];
                                    if (curMouse.X >= higher.Rect.Left && curMouse.X <= higher.Rect.Right)
                                    {
                                        // 1. Snow blanket itself on 'obs' is hidden behind 'higher' (submerged 120 px down)
                                        if (obs.Rect.Top > (higher.Rect.Top + 120) && obs.Rect.Top < (higher.Rect.Bottom - 4))
                                        {
                                            if (curMouse.X >= higher.Rect.Left && curMouse.X < higher.Rect.Right)
                                            {
                                                isOccluded = true;
                                                break;
                                            }
                                        }
                                        // 2. Cursor is inside 'higher' window surface
                                        if (curMouse.Y >= higher.Rect.Top && curMouse.Y <= higher.Rect.Bottom)
                                        {
                                            isOccluded = true;
                                            break;
                                        }
                                    }
                                }

                                if (!isOccluded)
                                {
                                    if (obs.Carve(lx, 3, 0.20f, carvedParticles, mouseDx, mouseDy, obstacles))
                                    {
                                        // Once carved topmost visible snow, do not punch through to windows behind it
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (carvedParticles.Count > 0)
                    {
                        lock (_spilledParticles)
                        {
                            const int maxParticles = 5500;
                            int overflow = (_spilledParticles.Count + carvedParticles.Count) - maxParticles;
                            if (overflow > 0)
                            {
                                _spilledParticles.RemoveRange(0, Math.Min(overflow, _spilledParticles.Count));
                            }
                            _spilledParticles.AddRange(carvedParticles);
                        }
                    }
                }

                // Update & Draw Falling Snowflakes (strictly scaled by dt for perfectly constant fall speed!)
                lock (_flakes)
                {
                    foreach (var flake in _flakes)
                    {
                        flake.Phase += dt * flake.SwaySpeed;
                        float sway = (float)Math.Sin(flake.Phase) * flake.SwayAmplitude;

                        flake.X += (flake.Vx + sway) * frameFactor;
                        flake.Y += flake.Vy * frameFactor;

                        int fx = (int)flake.X;
                        int fy = (int)flake.Y;

                        if (fx >= 0 && fx < _width && fy >= 5 && fy < _height)
                        {
                            foreach (var obs in obstacles)
                            {
                                if (!obs.CanLand || !ObstacleManager.SnowDriftsActive) continue;

                                if (fx >= obs.Rect.Left && fx < obs.Rect.Right)
                                {
                                    int lx = fx - obs.Rect.Left;
                                    float currentH = (lx >= 0 && lx < obs.SnowMap.Length) ? obs.SnowMap[lx] : 0f;
                                    float surfaceY = obs.Rect.Top - currentH;

                                    bool isCovered = false;
                                    for (int i = 0; i < obs.ZIndex && i < obstacles.Count; i++)
                                    {
                                        var higher = obstacles[i];
                                        if (fx >= higher.Rect.Left && fx <= higher.Rect.Right &&
                                            surfaceY >= higher.Rect.Top && surfaceY <= higher.Rect.Bottom)
                                        {
                                            isCovered = true;
                                            break;
                                        }
                                    }

                                    if (isCovered) continue;

                                    if (fy >= surfaceY - 2 && fy <= surfaceY + 3)
                                    {
                                        if (Rand.NextDouble() < 0.30)
                                        {
                                            obs.Deposit(lx, 0.16f, radius: 2, maxHeight: 15.0f);
                                            flake.Reset(_width, _height);
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (flake.Y > _height || flake.X < -20 || flake.X > _width + 20)
                        {
                            flake.Reset(_width, _height);
                        }
                    }
                }

                // 1. Draw the Scenery (Trees, Snowmen) FIRST at the bottom level, then Fluffy Snow Blanket ON TOP
                for (int o = obstacles.Count - 1; o >= 0; o--)
                {
                    DrawDecorations(pixels, obstacles[o], obstacles);
                    DrawSnowBlanket(pixels, obstacles[o], obstacles);
                }

                // 2. Draw Falling Background Snowflakes on top of window blankets
                lock (_flakes)
                {
                    foreach (var flake in _flakes)
                    {
                        DrawFlake(pixels, (int)flake.X, (int)flake.Y, flake.Radius, flake.Alpha);
                    }
                }

                // 3. Update & Draw Spilled Snow Particles (IN FRONT OF EVERYTHING, never hidden by background windows)
                lock (_spilledParticles)
                {
                    for (int i = _spilledParticles.Count - 1; i >= 0; i--)
                    {
                        var p = _spilledParticles[i];
                        p.Phase += dt * p.SwaySpeed;
                        float sway = (float)Math.Sin(p.Phase) * p.SwayAmp;

                        p.X += (p.Vx + sway) * frameFactor;
                        p.Y += p.Vy * frameFactor;
                        p.Vy = Math.Min(2.1f, p.Vy + 0.035f * frameFactor); // Terminal velocity: floats gently like down feathers
                        p.Vx *= (float)Math.Pow(0.92, frameFactor);          // Air drag dampens violent thrust into floating breeze

                        int px = (int)p.X;
                        int py = (int)p.Y;

                        bool landed = false;
                        if (px >= 0 && px < _width && py >= 5 && py < _height)
                        {
                            foreach (var obs in obstacles)
                            {
                                if (!obs.CanLand || !ObstacleManager.SnowDriftsActive) continue;
                                if (obs.Hwnd == p.SourceHwnd) continue; // Don't instantly re-land on the window it was shaken from
                                if (p.Vy <= 0.3f) continue;

                                if (px >= obs.Rect.Left && px < obs.Rect.Right)
                                {
                                    int lx = px - obs.Rect.Left;
                                    float h = (lx >= 0 && lx < obs.SnowMap.Length) ? obs.SnowMap[lx] : 0f;
                                    float surfaceY = obs.Rect.Top - h;

                                    if (py >= surfaceY - 2 && py <= surfaceY + 4)
                                    {
                                        // Is this landing spot on 'obs' hidden behind a foreground window?
                                        bool isCoveredByHigher = false;
                                        for (int j = 0; j < obs.ZIndex && j < obstacles.Count; j++)
                                        {
                                            var higher = obstacles[j];
                                            if (px >= higher.Rect.Left && px <= higher.Rect.Right &&
                                                py >= higher.Rect.Top && py <= higher.Rect.Bottom)
                                            {
                                                isCoveredByHigher = true;
                                                break;
                                            }
                                        }

                                        // If covered by a window in front, snow cannot land here - keep falling in front!
                                        if (isCoveredByHigher) continue;

                                        float depositAmount = Math.Clamp(p.Radius * 0.14f, 0.22f, 0.48f);
                                        int depositRadius = (p.Radius >= 2.4f) ? 3 : 2;
                                        obs.Deposit(lx, depositAmount, depositRadius, maxHeight: 42.0f);
                                        landed = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (landed || p.Y > _height || p.X < -20 || p.X > _width + 20)
                        {
                            _spilledParticles.RemoveAt(i);
                        }
                        else
                        {
                            _spilledParticles[i] = p;
                            DrawFlake(pixels, px, py, p.Radius, p.Alpha);
                        }
                    }
                }

                // Hardware presentation & VSync
                Native.UpdateLayeredWindow(
                    Handle,
                    IntPtr.Zero,
                    ref ptDst,
                    ref size,
                    _hMemDC,
                    ref ptSrc,
                    0,
                    ref blend,
                    Native.ULW_ALPHA);

                Native.DwmFlush();

                long nowMs = _sw.ElapsedMilliseconds;
                if (nowMs - lastMemoryTrim > 4000)
                {
                    Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                    lastMemoryTrim = nowMs;
                }

                Thread.Yield();
            }
        }

        private unsafe void DrawSnowBlanket(uint* pixels, WindowObstacle obs, List<WindowObstacle> allObstacles)
        {
            if (!obs.CanLand || !ObstacleManager.SnowDriftsActive) return;

            int left = obs.Rect.Left;
            int top = obs.Rect.Top;

            // When window is maximized or titlebar is off-screen, top is negative (e.g. -8)
            if (top < 2 || top >= _height) return;

            float[] map = obs.SnowMap;
            byte[]? icicles = obs.IcicleMap;
            int width = Math.Min(obs.Width, map.Length);
            if (width < 4 || map.Length < 4) return;

            bool isTaskbar = (top >= _height - 80);

            // Pre-calculate occlusion for each column of this window
            bool[] isOccluded = new bool[width];
            for (int lx = 0; lx < width; lx++)
            {
                int worldX = left + lx;
                if (worldX < 0 || worldX >= _width)
                {
                    isOccluded[lx] = true;
                    continue;
                }

                for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
                {
                    var higher = allObstacles[i];
                    if (isTaskbar)
                    {
                        // 1. Fullscreen window covers the taskbar completely
                        if (higher.Rect.Left <= 0 && higher.Rect.Top <= 0 &&
                            higher.Rect.Right >= _width && higher.Rect.Bottom >= _height)
                        {
                            isOccluded[lx] = true;
                            break;
                        }
                        // 2. An open window reaches down into or over the taskbar
                        if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right)
                        {
                            if (higher.Rect.Bottom > top + 4)
                            {
                                isOccluded[lx] = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Normal window: submerged threshold 125 px down
                        if (top > (higher.Rect.Top + 125) && top < (higher.Rect.Bottom - 4))
                        {
                            if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right)
                            {
                                isOccluded[lx] = true;
                                break;
                            }
                        }
                    }
                }
            }

            // Distance to empty calculation: smooths out natural window borders and holes
            int[] distToEmpty = new int[width];
            int currentDist = 0;

            for (int lx = 0; lx < width; lx++)
            {
                if (map[lx] <= 0.4f || lx < 2 || lx >= width - 2)
                {
                    currentDist = 0;
                }
                else
                {
                    currentDist++;
                }
                distToEmpty[lx] = currentDist;
            }

            currentDist = 0;
            for (int lx = width - 1; lx >= 0; lx--)
            {
                if (map[lx] <= 0.4f || lx < 2 || lx >= width - 2)
                {
                    currentDist = 0;
                }
                else
                {
                    currentDist++;
                }
                distToEmpty[lx] = Math.Min(distToEmpty[lx], currentDist);
            }

            for (int lx = 2; lx < width - 2; lx++)
            {
                if (isOccluded[lx]) continue;

                int worldX = left + lx;
                if (worldX < 0 || worldX >= _width) continue;

                float rawH = map[lx];
                if (rawH <= 0.4f) continue;

                float cornerScale = 1.0f;
                int d = distToEmpty[lx];
                const int roundSpan = 10;
                if (d < roundSpan)
                {
                    float t = (float)d / roundSpan;
                    cornerScale = (float)(0.5 - 0.5 * Math.Cos(t * Math.PI));
                }

                float h = rawH * cornerScale;
                if (h <= 0.35f) continue;

                int snowHeightPixels = (int)Math.Ceiling(h);
                int startY = Math.Max(0, top - snowHeightPixels);
                // SOLID FLAT BASELINE: always ends flush at top + 1
                int endY = Math.Min(_height - 1, top + 1);

                for (int y = startY; y <= endY; y++)
                {
                    // Strict foreground clipping: NEVER draw any pixel inside any higher window!
                    bool pixelCovered = false;
                    for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
                    {
                        var higher = allObstacles[i];
                        if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right)
                        {
                            if (y >= higher.Rect.Top && y <= higher.Rect.Bottom)
                            {
                                // If this is taskbar and higher window stops at taskbar top (maximized window):
                                // allow taskbar snow to poke slightly above taskbar top!
                                if (isTaskbar && higher.Rect.Bottom <= top + 2)
                                {
                                    continue;
                                }
                                pixelCovered = true;
                                break;
                            }
                        }
                    }
                    if (pixelCovered) continue;

                    int rowOffset = y * _width;
                    uint current = pixels[rowOffset + worldX];

                    byte a = 255;
                    if (y == startY)
                    {
                        float frac = h - (float)Math.Floor(h);
                        a = (byte)(255 * Math.Clamp(frac + 0.35f, 0.35f, 1.0f));
                    }

                    if (y == startY && current != 0)
                    {
                        // Softly blend snow edge over underlying decoration if present
                        byte curA = (byte)(current >> 24);
                        if (curA > 0)
                        {
                            byte invA = (byte)(255 - a);
                            byte r = (byte)(a + ((byte)(current >> 16) * invA / 255));
                            byte g = (byte)(a + ((byte)(current >> 8) * invA / 255));
                            byte b = (byte)(a + ((byte)current * invA / 255));
                            pixels[rowOffset + worldX] = (0xFF000000) | ((uint)r << 16) | ((uint)g << 8) | b;
                        }
                        else
                        {
                            pixels[rowOffset + worldX] = ((uint)a << 24) | ((uint)a << 16) | ((uint)a << 8) | a;
                        }
                    }
                    else
                    {
                        // Premultiplied pure snow white: R=a, G=a, B=a
                        pixels[rowOffset + worldX] = ((uint)a << 24) | ((uint)a << 16) | ((uint)a << 8) | a;
                    }
                }

                // Subtle frost glaze directly under snow blanket
                int frostY = top + 2;
                if (frostY >= 0 && frostY < _height && h > 2.0f)
                {
                    bool frostCovered = false;
                    for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
                    {
                        var higher = allObstacles[i];
                        if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right &&
                            frostY >= higher.Rect.Top && frostY <= higher.Rect.Bottom)
                        {
                            frostCovered = true;
                            break;
                        }
                    }
                    if (!frostCovered)
                    {
                        int frostOffset = frostY * _width + worldX;
                        uint current = pixels[frostOffset];
                        byte curA = (byte)(current >> 24);
                        byte frostA = 80;
                        if (frostA > curA)
                        {
                            byte r = (byte)(215 * frostA / 255);
                            byte g = (byte)(238 * frostA / 255);
                            byte b = (byte)(255 * frostA / 255);
                            pixels[frostOffset] = ((uint)frostA << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                        }
                    }
                }

                // Crystal icicles hanging down under the snow
                if (icicles != null && lx < icicles.Length && h > 3.0f)
                {
                    byte icicleLen = icicles[lx];
                    if (icicleLen > 0)
                    {
                        int minIcicleY = Math.Max(0, top + 2);
                        int maxIcicleY = Math.Min(_height - 1, top + 1 + icicleLen);
                        for (int iy = minIcicleY; iy <= maxIcicleY; iy++)
                        {
                            bool icicleCovered = false;
                            for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
                            {
                                var higher = allObstacles[i];
                                if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right &&
                                    iy >= higher.Rect.Top && iy <= higher.Rect.Bottom)
                                {
                                    icicleCovered = true;
                                    break;
                                }
                            }
                            if (icicleCovered) continue;

                            int icicleOffset = iy * _width + worldX;
                            uint current = pixels[icicleOffset];
                            byte curA = (byte)(current >> 24);

                            float t = (float)(iy - (top + 1)) / (icicleLen + 1.0f);
                            byte icicleA = (byte)(190 * (1.0f - t * 0.45f));
                            if (icicleA > curA)
                            {
                                byte r = (byte)(215 * icicleA / 255);
                                byte g = (byte)(240 * icicleA / 255);
                                byte b = (byte)(255 * icicleA / 255);
                                pixels[icicleOffset] = ((uint)icicleA << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void DrawFlake(uint* pixels, int cx, int cy, float radius, byte alpha)
        {
            int r = (int)Math.Ceiling(radius);
            int minX = Math.Max(0, cx - r);
            int maxX = Math.Min(_width - 1, cx + r);
            int minY = Math.Max(0, cy - r);
            int maxY = Math.Min(_height - 1, cy + r);

            float rSq = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - cy;
                int dySq = dy * dy;
                int rowOffset = y * _width;

                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - cx;
                    int dSq = dx * dx + dySq;

                    if (dSq <= rSq)
                    {
                        float factor = 1.0f - ((float)dSq / (rSq + 0.5f));
                        byte a = (byte)(alpha * factor);

                        uint current = pixels[rowOffset + x];
                        byte curA = (byte)(current >> 24);

                        if (a > curA)
                        {
                            pixels[rowOffset + x] = ((uint)a << 24) | ((uint)a << 16) | ((uint)a << 8) | a;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void PlotPixel(uint* pixels, WindowObstacle obs, List<WindowObstacle> allObstacles, int x, int y, uint color)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;

            bool isTaskbar = (obs.Rect.Top >= _height - 80);

            // Strict foreground occlusion check: never draw over higher windows
            for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
            {
                var higher = allObstacles[i];
                if (x >= higher.Rect.Left && x < higher.Rect.Right)
                {
                    if (y >= higher.Rect.Top && y <= higher.Rect.Bottom)
                    {
                        if (isTaskbar && higher.Rect.Bottom <= obs.Rect.Top + 2)
                        {
                            continue;
                        }
                        return;
                    }
                }
            }

            pixels[y * _width + x] = color;
        }

        private unsafe void DrawDecorations(uint* pixels, WindowObstacle obs, List<WindowObstacle> allObstacles)
        {
            if (!obs.CanLand || obs.Decorations.Count == 0) return;
            int left = obs.Rect.Left;
            int top = obs.Rect.Top;
            if (top < 2 || top >= _height) return;

            bool isTaskbar = (top >= _height - 80);

            foreach (var decor in obs.Decorations)
            {
                int lx = decor.LocalX;
                if (lx < 6 || lx >= obs.Width - 6 || lx >= obs.SnowMap.Length) continue;

                float snowH = obs.SnowMap[lx];
                if (snowH < 1.0f) continue; // Only show when there is snow on the window!

                int worldX = left + lx;
                if (worldX < 15 || worldX >= _width - 15) continue;

                bool isBlockedByForeground = false;
                for (int i = 0; i < obs.ZIndex && i < allObstacles.Count; i++)
                {
                    var higher = allObstacles[i];
                    if (isTaskbar)
                    {
                        // 1. Fullscreen window covers taskbar
                        if (higher.Rect.Left <= 0 && higher.Rect.Top <= 0 &&
                            higher.Rect.Right >= _width && higher.Rect.Bottom >= _height)
                        {
                            isBlockedByForeground = true;
                            break;
                        }
                        // 2. An open window reaches down into or over the taskbar
                        if (worldX >= higher.Rect.Left - 10 && worldX <= higher.Rect.Right + 10)
                        {
                            if (higher.Rect.Bottom > top + 4)
                            {
                                isBlockedByForeground = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right)
                        {
                            if (top > (higher.Rect.Top + 125))
                            {
                                isBlockedByForeground = true;
                                break;
                            }
                        }
                    }
                }
                if (isBlockedByForeground) continue;

                // Fixed bottom level: rooted directly at the window baseline (top + 1), does not move with snow
                int baseY = top + 1;
                if (baseY < 20 || baseY >= _height - 5) continue;

                if (decor.Type == DecorationType.Tree)
                {
                    DrawPineTree(pixels, obs, allObstacles, worldX, baseY, decor.Height, decor.Seed);
                }
                else if (decor.Type == DecorationType.Snowman)
                {
                    DrawSnowman(pixels, obs, allObstacles, worldX, baseY, decor.Height, decor.Seed);
                }
            }
        }

        private unsafe void DrawPineTree(uint* pixels, WindowObstacle obs, List<WindowObstacle> allObstacles, int rootX, int baseY, int height, int seed)
        {
            // Heavily snow-covered / frosty pine tree (сіро-біла та біло-зелена, засніжена)
            const uint cTrunkDark = 0xFF3D2617;
            const uint cTrunk = 0xFF583923;

            // Snowy / frosted foliage:
            const uint cSnowPure = 0xFFFFFFFF;       // Pure white snow cap
            const uint cSnowSoft = 0xFFF2F6F4;       // Soft bright snow
            const uint cSnowGrayWhite = 0xFFD8E2DC;  // Cool gray-white snow shadow
            const uint cSnowMint = 0xFFBFDBC9;       // Pale icy white-green
            const uint cFrostGreen = 0xFF85AF93;     // Frosted rime needle green
            const uint cPineCore = 0xFF315C3E;       // Deep inner branch shadow
            const uint cGold = 0xFFFFD54F;           // Golden star topper

            // 1. Trunk (3 rows tall, 3 pixels wide)
            int trunkHeight = 3;
            for (int ty = baseY; ty >= baseY - trunkHeight; ty--)
            {
                PlotPixel(pixels, obs, allObstacles, rootX - 1, ty, cTrunkDark);
                PlotPixel(pixels, obs, allObstacles, rootX, ty, cTrunk);
                PlotPixel(pixels, obs, allObstacles, rootX + 1, ty, cTrunkDark);
            }

            // 2. Heavily frosted evergreen branches in 3 tiers
            int foliageBottom = baseY - trunkHeight + 1;
            int foliageTop = baseY - height;

            void DrawTier(int tBottom, int tTop, int maxR)
            {
                int tierH = Math.Max(1, tBottom - tTop);
                for (int y = tBottom; y >= tTop; y--)
                {
                    int rowFromTop = y - tTop; // 0 is top row of tier, tierH is bottom row
                    float t = (float)rowFromTop / tierH;
                    int r = Math.Max(0, (int)Math.Round(maxR * t));

                    for (int dx = -r; dx <= r; dx++)
                    {
                        int absDx = Math.Abs(dx);
                        uint col;

                        // Top rows of each tier are completely covered in thick snow!
                        if (rowFromTop <= 1)
                        {
                            col = (absDx == r) ? cSnowGrayWhite : cSnowPure;
                        }
                        else if (rowFromTop == 2)
                        {
                            if (absDx >= r - 1) col = cSnowPure;
                            else col = (absDx % 2 == 0) ? cSnowSoft : cSnowMint;
                        }
                        else
                        {
                            // Lower rows of tier: tips are frosted white-green & gray-white, inside is pale frosted
                            if (absDx == r)
                            {
                                col = cSnowSoft; // Branch tip snow
                            }
                            else if (absDx >= r - 1)
                            {
                                col = cSnowMint; // Pale icy white-green
                            }
                            else if (absDx >= r - 2)
                            {
                                col = cSnowGrayWhite; // Gray-white rime
                            }
                            else
                            {
                                col = (absDx == 0 && rowFromTop == tierH) ? cPineCore : cFrostGreen;
                            }
                        }

                        PlotPixel(pixels, obs, allObstacles, rootX + dx, y, col);
                    }
                }
            }

            // Tier 1: Bottom tier (widest, heavy snow on top)
            int t1Bottom = foliageBottom;
            int t1Top = foliageBottom - (int)(height * 0.38f);
            int t1MaxR = Math.Clamp((int)(height * 0.33f), 5, 8);
            DrawTier(t1Bottom, t1Top, t1MaxR);

            // Tier 2: Middle tier
            int t2Bottom = t1Top + 2;
            int t2Top = t1Top - (int)(height * 0.30f);
            int t2MaxR = Math.Max(2, t1MaxR - 2);
            DrawTier(t2Bottom, t2Top, t2MaxR);

            // Tier 3: Top cone
            int t3Bottom = t2Top + 2;
            int t3Top = foliageTop;
            int t3MaxR = Math.Max(1, t2MaxR - 2);
            DrawTier(t3Bottom, t3Top, t3MaxR);

            // 3. Golden Star / Star Topper at tip
            PlotPixel(pixels, obs, allObstacles, rootX, foliageTop - 1, cGold);
            PlotPixel(pixels, obs, allObstacles, rootX, foliageTop - 2, cGold);
            PlotPixel(pixels, obs, allObstacles, rootX - 1, foliageTop - 1, cGold);
            PlotPixel(pixels, obs, allObstacles, rootX + 1, foliageTop - 1, cGold);
        }

        private unsafe void DrawSnowman(uint* pixels, WindowObstacle obs, List<WindowObstacle> allObstacles, int rootX, int baseY, int height, int seed)
        {
            const uint cSnow = 0xFFFFFFFF;
            const uint cSnowShade = 0xFFD2E3F0;
            const uint cCoal = 0xFF212529;
            const uint cCarrot = 0xFFFF7A00;
            const uint cScarf = 0xFFE53935;
            const uint cScarfDark = 0xFFB71C1C;
            const uint cHat = 0xFF263238;

            int r1 = (height >= 18) ? 4 : 3; // Bottom ball radius
            int cy1 = baseY - r1;

            // Draw Bottom Ball
            for (int dy = -r1; dy <= r1; dy++)
            {
                int py = cy1 + dy;
                for (int dx = -r1; dx <= r1; dx++)
                {
                    if (dx * dx + dy * dy <= r1 * r1)
                    {
                        uint col = (dy >= 1 || dx >= 2) ? cSnowShade : cSnow;
                        PlotPixel(pixels, obs, allObstacles, rootX + dx, py, col);
                    }
                }
            }

            // Middle Ball
            int r2 = r1 - 1;
            int cy2 = cy1 - r1 - r2 + 1;
            for (int dy = -r2; dy <= r2; dy++)
            {
                int py = cy2 + dy;
                for (int dx = -r2; dx <= r2; dx++)
                {
                    if (dx * dx + dy * dy <= r2 * r2)
                    {
                        uint col = (dy >= 1 || dx >= 2) ? cSnowShade : cSnow;
                        PlotPixel(pixels, obs, allObstacles, rootX + dx, py, col);
                    }
                }
            }

            // 2 Charcoal Buttons on Middle Ball
            PlotPixel(pixels, obs, allObstacles, rootX, cy2 - 1, cCoal);
            PlotPixel(pixels, obs, allObstacles, rootX, cy2 + 1, cCoal);

            // Red Scarf around neck
            int neckY = cy2 - r2;
            for (int dx = -r2 - 1; dx <= r2 + 1; dx++)
            {
                PlotPixel(pixels, obs, allObstacles, rootX + dx, neckY, cScarf);
                PlotPixel(pixels, obs, allObstacles, rootX + dx, neckY + 1, cScarfDark);
            }
            // Little scarf tail draping down on right
            PlotPixel(pixels, obs, allObstacles, rootX + r2, neckY + 2, cScarf);
            PlotPixel(pixels, obs, allObstacles, rootX + r2, neckY + 3, cScarfDark);

            // Head Ball
            int r3 = Math.Max(2, r2 - 1);
            int cy3 = neckY - r3;
            for (int dy = -r3; dy <= r3; dy++)
            {
                int py = cy3 + dy;
                for (int dx = -r3; dx <= r3; dx++)
                {
                    if (dx * dx + dy * dy <= r3 * r3)
                    {
                        uint col = (dy >= 1 || dx >= 2) ? cSnowShade : cSnow;
                        PlotPixel(pixels, obs, allObstacles, rootX + dx, py, col);
                    }
                }
            }

            // Coal Eyes
            PlotPixel(pixels, obs, allObstacles, rootX - 1, cy3 - 1, cCoal);
            PlotPixel(pixels, obs, allObstacles, rootX + 1, cy3 - 1, cCoal);

            // Orange Carrot Nose
            PlotPixel(pixels, obs, allObstacles, rootX, cy3, cCarrot);
            PlotPixel(pixels, obs, allObstacles, rootX + 1, cy3, cCarrot);

            // Tiny winter beanie / hat on top
            for (int dx = -r3; dx <= r3; dx++)
            {
                PlotPixel(pixels, obs, allObstacles, rootX + dx, cy3 - r3, cHat);
            }
            PlotPixel(pixels, obs, allObstacles, rootX, cy3 - r3 - 1, cScarf); // Pom-pom on top of hat!
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isRunning = false;
            _obstacleManager?.Stop();
            _renderThread?.Join(500);

            if (_hMemDC != IntPtr.Zero)
            {
                if (_oldBitmap != IntPtr.Zero)
                {
                    Native.SelectObject(_hMemDC, _oldBitmap);
                }
                if (_hBitmap != IntPtr.Zero)
                {
                    Native.DeleteObject(_hBitmap);
                }
                Native.DeleteDC(_hMemDC);
            }

            base.OnFormClosing(e);
        }
    }
}
