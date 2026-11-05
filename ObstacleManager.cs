using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SnowApp
{
    public struct SpilledParticle
    {
        public IntPtr SourceHwnd;
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Radius;
        public byte Alpha;
        public float Phase;
        public float SwaySpeed;
        public float SwayAmp;
    }

    public enum DecorationType
    {
        Tree,
        Snowman
    }

    public class WindowDecoration
    {
        public DecorationType Type;
        public int LocalX;
        public int Height;
        public int Seed;
    }

    public class WindowObstacle
    {
        public IntPtr Hwnd;
        public Native.RECT Rect;
        public int ZIndex;
        public int DeltaX;
        public int DeltaY;
        public float PrevVx;
        public float PrevVy;
        public bool IsMoved;
        public bool IsClosedOrMinimized;
        public bool CanLand;

        public float[] SnowMap;
        public byte[] IcicleMap; // Procedural icicle length at each column
        public List<WindowDecoration> Decorations = new();
        public int Width => Math.Max(1, Rect.Width);

        private static readonly Random Rand = new Random();

        public WindowObstacle(IntPtr hwnd, Native.RECT rect, int zIndex, bool canLand)
        {
            Hwnd = hwnd;
            Rect = rect;
            ZIndex = zIndex;
            CanLand = canLand;
            SnowMap = new float[Width];
            IcicleMap = new byte[Width];

            if (canLand && ObstacleManager.SnowDriftsActive)
            {
                InitSnowBlanket();
                GenerateIcicles();
                GenerateDecorations();
            }
        }

        public void InitSnowBlanket()
        {
            int len = SnowMap.Length;
            for (int x = 0; x < len; x++)
            {
                float wave = (float)(Math.Sin(x * 0.03) * 2.0 + Math.Sin(x * 0.075) * 1.0);
                SnowMap[x] = Math.Clamp(8.5f + wave, 6.0f, 13.0f);
            }
            Smooth(0.5f);
        }

        public void GenerateIcicles()
        {
            int len = IcicleMap.Length;
            Array.Clear(IcicleMap, 0, len);
            if (len < 320 || Rect.Top > 950) return; // Do NOT generate icicles on context menus, narrow dialogs, or bottom taskbar!

            // Generate delicate, stylish icicles away from window corners
            int x = 30 + (int)(hwndHash(Hwnd, 1) % 20);
            while (x < len - 35)
            {
                int icicleLen = 3 + (int)(hwndHash(Hwnd, x) % 6); // 3 to 8 px tall
                int icicleWidth = (icicleLen >= 6) ? 2 : 1;        // 3 to 5 px wide

                for (int dx = -icicleWidth; dx <= icicleWidth; dx++)
                {
                    int col = x + dx;
                    if (col >= 0 && col < len)
                    {
                        float taper = 1.0f - (float)Math.Abs(dx) / (icicleWidth + 0.6f);
                        byte h = (byte)Math.Max(1, (int)(icicleLen * taper));
                        if (h > IcicleMap[col])
                        {
                            IcicleMap[col] = h;
                        }
                    }
                }

                x += 24 + (int)(hwndHash(Hwnd, x + 31) % 28);
            }
        }

        public void GenerateDecorations()
        {
            Decorations.Clear();
            int len = Width;
            if (len < 380 || !CanLand) return;

            // Extra wide windows & Taskbar: spacious festive distribution
            if (len >= 1200)
            {
                // Tree 1 (Left-center)
                int t1X = 220 + (int)(hwndHash(Hwnd, 101) % 160);
                int t1H = 20 + (int)(hwndHash(Hwnd, t1X) % 9);
                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = t1X, Height = t1H, Seed = (int)hwndHash(Hwnd, 1) });

                // Snowman (Mid)
                int smX = 620 + (int)(hwndHash(Hwnd, 202) % 180);
                int smH = 17 + (int)(hwndHash(Hwnd, smX) % 4);
                Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = smX, Height = smH, Seed = (int)hwndHash(Hwnd, 2) });

                // Tree 2 (Right-center)
                int t2X = 1000 + (int)(hwndHash(Hwnd, 303) % 160);
                int t2H = 19 + (int)(hwndHash(Hwnd, t2X) % 10);
                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = t2X, Height = t2H, Seed = (int)hwndHash(Hwnd, 3) });

                if (len >= 1600)
                {
                    int t3X = 1420 + (int)(hwndHash(Hwnd, 404) % 140);
                    int t3H = 22 + (int)(hwndHash(Hwnd, t3X) % 7);
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = t3X, Height = t3H, Seed = (int)hwndHash(Hwnd, 4) });
                }
                return;
            }

            // Normal application windows (380 to 1200 px wide)
            // Roll distribution:
            // 0..19: Just snow (20%)
            // 20..39: 1 Tree (20%)
            // 40..54: 1 Snowman (15%)
            // 55..69: 2 Trees (15%)
            // 70..84: 1 Tree + 1 Snowman (15%)
            // 85..99: 2 Trees + 1 Snowman (15%)
            uint roll = hwndHash(Hwnd, 777) % 100;
            if (roll < 20) return; // Pure snow

            int startX = 50;
            int endX = len - 70;
            int span = endX - startX;
            if (span < 70) return;

            if (roll < 40)
            {
                // 1 Tree
                int x = startX + (int)(hwndHash(Hwnd, 10) % span);
                int h = 18 + (int)(hwndHash(Hwnd, 11) % 11);
                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x, Height = h, Seed = (int)hwndHash(Hwnd, 12) });
            }
            else if (roll < 55)
            {
                // 1 Snowman
                int x = startX + (int)(hwndHash(Hwnd, 20) % span);
                int h = 16 + (int)(hwndHash(Hwnd, 21) % 5);
                Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = x, Height = h, Seed = (int)hwndHash(Hwnd, 22) });
            }
            else if (roll < 70)
            {
                // 2 Trees (one shorter, one taller)
                int x1 = startX + 10 + (int)(hwndHash(Hwnd, 30) % Math.Max(10, span / 2 - 40));
                int h1 = 18 + (int)(hwndHash(Hwnd, 31) % 6); // Shorter: 18..23 px
                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x1, Height = h1, Seed = (int)hwndHash(Hwnd, 32) });

                int x2 = startX + span / 2 + 25 + (int)(hwndHash(Hwnd, 33) % Math.Max(10, span / 2 - 45));
                int h2 = 23 + (int)(hwndHash(Hwnd, 34) % 6); // Taller: 23..28 px
                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x2, Height = h2, Seed = (int)hwndHash(Hwnd, 35) });
            }
            else if (roll < 85)
            {
                // 1 Tree + 1 Snowman
                int x1 = startX + 10 + (int)(hwndHash(Hwnd, 40) % Math.Max(10, span / 2 - 40));
                int x2 = startX + span / 2 + 25 + (int)(hwndHash(Hwnd, 41) % Math.Max(10, span / 2 - 45));
                bool treeFirst = (hwndHash(Hwnd, 42) % 2) == 0;

                int treeX = treeFirst ? x1 : x2;
                int snowmanX = treeFirst ? x2 : x1;

                int treeH = 19 + (int)(hwndHash(Hwnd, 43) % 10);
                int snowmanH = 16 + (int)(hwndHash(Hwnd, 44) % 5);

                Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = treeX, Height = treeH, Seed = (int)hwndHash(Hwnd, 45) });
                Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = snowmanX, Height = snowmanH, Seed = (int)hwndHash(Hwnd, 46) });
            }
            else
            {
                // 2 Trees + 1 Snowman
                int subSpan = span / 3;
                int x1 = startX + 10 + (int)(hwndHash(Hwnd, 50) % Math.Max(10, subSpan - 30));
                int x2 = startX + subSpan + 15 + (int)(hwndHash(Hwnd, 51) % Math.Max(10, subSpan - 30));
                int x3 = startX + (2 * subSpan) + 20 + (int)(hwndHash(Hwnd, 52) % Math.Max(10, subSpan - 30));

                int snowmanSlot = (int)(hwndHash(Hwnd, 53) % 3);

                int h1 = 18 + (int)(hwndHash(Hwnd, 54) % 7);
                int h2 = 23 + (int)(hwndHash(Hwnd, 55) % 6);
                int smH = 16 + (int)(hwndHash(Hwnd, 56) % 5);

                if (snowmanSlot == 1)
                {
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x1, Height = h1, Seed = 1 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = x2, Height = smH, Seed = 2 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x3, Height = h2, Seed = 3 });
                }
                else if (snowmanSlot == 0)
                {
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = x1, Height = smH, Seed = 1 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x2, Height = h1, Seed = 2 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x3, Height = h2, Seed = 3 });
                }
                else
                {
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x1, Height = h1, Seed = 1 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Tree, LocalX = x2, Height = h2, Seed = 2 });
                    Decorations.Add(new WindowDecoration { Type = DecorationType.Snowman, LocalX = x3, Height = smH, Seed = 3 });
                }
            }
        }

        private static uint hwndHash(IntPtr hwnd, int seed)
        {
            uint h = (uint)hwnd.ToInt64() ^ (uint)(seed * 2654435761u);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }

        public void EnsureWidth(int newWidth)
        {
            if (newWidth <= 0) return;
            if (SnowMap.Length != newWidth)
            {
                var newMap = new float[newWidth];
                int copyLen = Math.Min(SnowMap.Length, newWidth);
                Array.Copy(SnowMap, newMap, copyLen);

                for (int x = copyLen; x < newWidth; x++)
                {
                    float wave = (float)(Math.Sin(x * 0.03) * 2.0 + Math.Sin(x * 0.075) * 1.0);
                    newMap[x] = Math.Clamp(8.5f + wave, 6.0f, 13.0f);
                }
                SnowMap = newMap;
                IcicleMap = new byte[newWidth];
                GenerateIcicles();
                if (Decorations.Count == 0 && newWidth >= 380)
                {
                    GenerateDecorations();
                }
                else
                {
                    for (int i = Decorations.Count - 1; i >= 0; i--)
                    {
                        if (Decorations[i].LocalX >= newWidth - 45)
                        {
                            Decorations[i].LocalX = Math.Max(45, newWidth - 55);
                        }
                    }
                }
                Smooth(0.4f);
            }
        }

        public void Smooth(float factor = 0.35f)
        {
            int len = SnowMap.Length;
            if (len < 3) return;

            for (int x = 1; x < len - 1; x++)
            {
                float avg = (SnowMap[x - 1] + SnowMap[x + 1]) * 0.5f;
                SnowMap[x] = SnowMap[x] * (1f - factor) + avg * factor;
            }
        }

        public void RelaxSlopes()
        {
            int len = SnowMap.Length;
            if (len < 2) return;

            const float maxSlope = 0.72f;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int x = 1; x < len; x++)
                {
                    if (SnowMap[x] - SnowMap[x - 1] > maxSlope)
                        SnowMap[x] = SnowMap[x - 1] + maxSlope;
                }
                for (int x = len - 2; x >= 0; x--)
                {
                    if (SnowMap[x] - SnowMap[x + 1] > maxSlope)
                        SnowMap[x] = SnowMap[x + 1] + maxSlope;
                }
            }
        }

        public void Deposit(int localX, float amount, int radius = 2, float maxHeight = 42.0f)
        {
            int len = SnowMap.Length;
            if (len <= 0) return;

            int start = Math.Max(0, localX - radius);
            int end = Math.Min(len - 1, localX + radius);

            for (int x = start; x <= end; x++)
            {
                int dist = Math.Abs(x - localX);
                float falloff = (float)Math.Cos(dist * (Math.PI * 0.5 / (radius + 1)));
                float noise = 0.90f + (float)(Rand.NextDouble() * 0.20);
                float add = amount * falloff * noise;

                if (SnowMap[x] < maxHeight)
                {
                    float room = maxHeight - SnowMap[x];
                    if (room < 8.0f)
                    {
                        add *= (room / 8.0f);
                    }
                    SnowMap[x] += add;
                }
                else
                {
                    // Granular avalanche: excess snow cascades to adjacent dips
                    int side = Rand.Next(2) == 0 ? -1 : 1;
                    int targetCol = Math.Clamp(x + side * 4, 0, len - 1);
                    if (SnowMap[targetCol] < maxHeight)
                    {
                        SnowMap[targetCol] += add * 0.45f;
                    }
                }
            }

            RelaxSlopes();
            Smooth(0.25f);
        }

        public static float RandomFlakeRadius()
        {
            double roll = Rand.NextDouble();
            if (roll < 0.50)
                return (float)(Rand.NextDouble() * 0.6 + 1.1); // 1.1 - 1.7 px (powder crumb)
            if (roll < 0.85)
                return (float)(Rand.NextDouble() * 0.7 + 1.8); // 1.8 - 2.5 px (medium flake)
            return (float)(Rand.NextDouble() * 0.8 + 2.6);      // 2.6 - 3.4 px (fluffy clump)
        }

        public void HandleWindowMovement(int dx, int dy, List<SpilledParticle> spilledOut)
        {
            int len = SnowMap.Length;
            if (len <= 0) return;

            float speed = (float)Math.Sqrt(dx * dx + dy * dy);
            float accel = Math.Abs(dx - PrevVx) + Math.Abs(dy - PrevVy);
            PrevVx = dx;
            PrevVy = dy;

            // 1. SHAKING: Dislodges a shower of fluffy swirling snow particles across the top edge
            if (speed > 3.6f || accel > 3.0f)
            {
                int count = (int)Math.Clamp(speed * 0.9f + accel * 0.7f, 3, 18);

                for (int k = 0; k < count; k++)
                {
                    int rx = Rand.Next(2, Math.Max(3, len - 3));
                    if (SnowMap[rx] > 0.6f)
                    {
                        float drop = Math.Min(SnowMap[rx] * 0.12f, 0.6f);
                        SnowMap[rx] -= drop;

                        // Fluffy snow physics: minimal upward flight, immediately floats & sways down
                        spilledOut.Add(new SpilledParticle
                        {
                            SourceHwnd = Hwnd,
                            X = Rect.Left + rx,
                            Y = Rect.Top - SnowMap[rx],
                            Vx = -dx * 0.05f + (float)(Rand.NextDouble() * 1.8 - 0.9),
                            Vy = 0.15f + (float)(Rand.NextDouble() * 0.35f),
                            Radius = RandomFlakeRadius(),
                            Alpha = 245,
                            Phase = (float)(Rand.NextDouble() * Math.PI * 2),
                            SwaySpeed = (float)(Rand.NextDouble() * 2.0 + 1.5),
                            SwayAmp = (float)(Rand.NextDouble() * 0.9 + 0.4)
                        });
                    }
                }
                RelaxSlopes();
                Smooth(0.35f);
            }

            // 2. SIDEWAYS INERTIA TRICKLE: Snow directly rolls off the edge of the real pile
            if (dx > 0)
            {
                // Window moves right -> snow at the left edge slides off
                int trickleCount = Math.Min(3, 1 + (int)(dx / 3));
                for (int t = 0; t < trickleCount; t++)
                {
                    int x = Rand.Next(2, Math.Min(len, 12));
                    if (SnowMap[x] > 0.5f)
                    {
                        float drop = Math.Min(SnowMap[x] * 0.2f, 0.45f);
                        float oldH = SnowMap[x];
                        SnowMap[x] -= drop;

                        // Spawns PRECISELY on the snow surface at column x (no gap in air!)
                        spilledOut.Add(new SpilledParticle
                        {
                            SourceHwnd = Hwnd,
                            X = Rect.Left + x,
                            Y = Rect.Top - oldH,
                            Vx = -0.35f - (float)(Rand.NextDouble() * 0.45),
                            Vy = 0.35f + (float)(Rand.NextDouble() * 0.35f),
                            Radius = RandomFlakeRadius(),
                            Alpha = 240,
                            Phase = (float)(Rand.NextDouble() * Math.PI * 2),
                            SwaySpeed = 1.8f,
                            SwayAmp = 0.5f
                        });
                    }
                }
                RelaxSlopes();
            }
            else if (dx < 0)
            {
                // Window moves left -> snow at the right edge slides off
                int trickleCount = Math.Min(3, 1 + (int)(-dx / 3));
                for (int t = 0; t < trickleCount; t++)
                {
                    int x = Rand.Next(Math.Max(0, len - 12), len - 2);
                    if (SnowMap[x] > 0.5f)
                    {
                        float drop = Math.Min(SnowMap[x] * 0.2f, 0.45f);
                        float oldH = SnowMap[x];
                        SnowMap[x] -= drop;

                        // Spawns PRECISELY on the snow surface at column x (no gap in air!)
                        spilledOut.Add(new SpilledParticle
                        {
                            SourceHwnd = Hwnd,
                            X = Rect.Left + x,
                            Y = Rect.Top - oldH,
                            Vx = 0.35f + (float)(Rand.NextDouble() * 0.45),
                            Vy = 0.35f + (float)(Rand.NextDouble() * 0.35f),
                            Radius = RandomFlakeRadius(),
                            Alpha = 240,
                            Phase = (float)(Rand.NextDouble() * Math.PI * 2),
                            SwaySpeed = 1.8f,
                            SwayAmp = 0.5f
                        });
                    }
                }
                RelaxSlopes();
            }
        }

        public bool Carve(int localX, int radius, float amount, List<SpilledParticle> particles, float mouseVx, float mouseVy, List<WindowObstacle>? allObstacles = null)
        {
            bool carvedAny = false;
            int start = Math.Max(0, localX - radius);
            int end = Math.Min(SnowMap.Length - 1, localX + radius);

            for (int x = start; x <= end; x++)
            {
                if (SnowMap[x] > 0.3f)
                {
                    int worldX = Rect.Left + x;

                    // If column is occluded behind a foreground window, do NOT carve or spawn particles!
                    if (allObstacles != null)
                    {
                        bool colOccluded = false;
                        for (int j = 0; j < ZIndex && j < allObstacles.Count; j++)
                        {
                            var higher = allObstacles[j];
                            if (Rect.Top > (higher.Rect.Top + 120) && Rect.Top < (higher.Rect.Bottom - 4))
                            {
                                if (worldX >= higher.Rect.Left && worldX < higher.Rect.Right)
                                {
                                    colOccluded = true;
                                    break;
                                }
                            }
                        }
                        if (colOccluded) continue;
                    }

                    float dist = Math.Abs(x - localX);
                    float factor = (float)Math.Cos(dist / (radius + 1f) * Math.PI * 0.5);
                    float targetDrop = amount * factor;
                    float oldH = SnowMap[x];
                    float newH = Math.Max(0f, oldH - targetDrop);
                    float removed = oldH - newH;

                    if (removed > 0.05f)
                    {
                        SnowMap[x] = newH;
                        carvedAny = true;

                        // Emit natural powdery spray: emit on ~42% of columns or if significant removal
                        if (Rand.NextDouble() < 0.42 || removed > 0.25f)
                        {
                            float pVx = mouseVx * 0.10f + (float)(Rand.NextDouble() * 0.8 - 0.4);
                            float pVy = Math.Max(0.2f, mouseVy * 0.08f) + 0.30f + (float)(Rand.NextDouble() * 0.30);

                            particles.Add(new SpilledParticle
                            {
                                SourceHwnd = Hwnd,
                                X = worldX,
                                Y = Rect.Top - (oldH + newH) * 0.5f,
                                Vx = pVx,
                                Vy = pVy,
                                Radius = RandomFlakeRadius(),
                                Alpha = 245,
                                Phase = (float)(Rand.NextDouble() * Math.PI * 2),
                                SwaySpeed = (float)(Rand.NextDouble() * 2.0 + 1.5),
                                SwayAmp = (float)(Rand.NextDouble() * 0.8 + 0.4)
                            });
                        }
                    }
                }
            }

            if (carvedAny)
            {
                RelaxSlopes();
                Smooth(0.35f);
            }
            return carvedAny;
        }

        public void Melt(float dt)
        {
            float meltAmount = dt * 0.03f;
            for (int i = 0; i < SnowMap.Length; i++)
            {
                if (SnowMap[i] > 6.5f)
                {
                    SnowMap[i] = Math.Max(6.5f, SnowMap[i] - meltAmount);
                }
            }
        }
    }

    public class ObstacleManager
    {
        public static volatile bool SnowDriftsActive = true;
        private readonly IntPtr _overlayHwnd;
        private readonly IntPtr _shellWindow;
        private readonly Dictionary<IntPtr, WindowObstacle> _trackedWindows = new();
        private volatile List<WindowObstacle> _snapshot = new();
        private readonly Thread _trackerThread;
        private volatile bool _isRunning;
        private int _screenWidth;
        private int _screenHeight;

        public ObstacleManager(IntPtr overlayHwnd, int screenWidth, int screenHeight)
        {
            _overlayHwnd = overlayHwnd;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            _shellWindow = Native.GetShellWindow();

            _isRunning = true;
            _trackerThread = new Thread(TrackLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Normal,
                Name = "WindowObstacleTracker"
            };
            _trackerThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
        }

        public List<WindowObstacle> GetSnapshot() => _snapshot;

        public void ClearAllSnowBlankets(List<SpilledParticle> spilledOut)
        {
            SnowDriftsActive = false;
            var snap = _snapshot;
            var rand = new Random();

            foreach (var obs in snap)
            {
                if (!obs.CanLand) continue;

                int len = Math.Min(obs.Width, obs.SnowMap.Length);
                for (int lx = 4; lx < len - 4; lx += 6)
                {
                    float h = obs.SnowMap[lx];
                    if (h > 1.0f)
                    {
                        spilledOut.Add(new SpilledParticle
                        {
                            X = obs.Rect.Left + lx,
                            Y = obs.Rect.Top - h + rand.Next(-1, 2),
                            Vx = (float)(rand.NextDouble() * 3.0 - 1.5),
                            Vy = (float)(rand.NextDouble() * 1.8 + 0.6),
                            Radius = 1.4f + (float)rand.NextDouble() * 1.6f,
                            SourceHwnd = obs.Hwnd,
                            Phase = (float)(rand.NextDouble() * Math.PI * 2),
                            SwayAmp = 0.5f + (float)rand.NextDouble() * 0.8f,
                            SwaySpeed = 1.5f + (float)rand.NextDouble() * 2.0f
                        });
                    }
                }

                Array.Clear(obs.SnowMap, 0, obs.SnowMap.Length);
                if (obs.IcicleMap != null) Array.Clear(obs.IcicleMap, 0, obs.IcicleMap.Length);
                obs.Decorations.Clear();
            }
        }

        public void RestoreAllSnowBlankets()
        {
            SnowDriftsActive = true;
            var snap = _snapshot;
            foreach (var obs in snap)
            {
                if (obs.CanLand)
                {
                    obs.InitSnowBlanket();
                    obs.GenerateIcicles();
                    obs.GenerateDecorations();
                }
            }
        }

        public static bool IsAltTabOrSystemOverlay(string cls)
        {
            return cls == "MultitaskingViewFrame" ||
                   cls == "TaskSwitcherWnd" ||
                   cls == "ForegroundStaging" ||
                   cls == "Progman" ||
                   cls == "WorkerW" ||
                   cls == "Shell_TrayWnd" ||
                   cls == "Shell_SecondaryTrayWnd" ||
                   cls == "TaskListOverlayWnd" ||
                   cls.IndexOf("Drag", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TrackLoop()
        {
            var sbClass = new StringBuilder(128);

            while (_isRunning)
            {
                try
                {
                    var rawList = new List<(IntPtr Hwnd, Native.RECT Rect, bool CanLand)>();

                    Native.EnumWindows((hwnd, lParam) =>
                    {
                        if (hwnd == _overlayHwnd || hwnd == _shellWindow) return true;
                        if (!Native.IsWindowVisible(hwnd)) return true;
                        if (Native.IsIconic(hwnd)) return true;

                        sbClass.Clear();
                        Native.GetClassName(hwnd, sbClass, 128);
                        string cls = sbClass.ToString();

                        if (IsAltTabOrSystemOverlay(cls))
                        {
                            return true;
                        }

                        if (!Native.GetExactWindowRect(hwnd, out Native.RECT r)) return true;

                        if (r.Width < 40 || r.Height < 25) return true;
                        if (r.Right <= 0 || r.Left >= _screenWidth || r.Bottom <= 0 || r.Top >= _screenHeight) return true;

                        // Filter out full-width invisible companion overlays near the taskbar (e.g. preview hover containers)
                        if (r.Width >= (_screenWidth - 10) && r.Top > (_screenHeight / 2)) return true;

                        bool canLand = r.Top >= 5 && r.Top < (_screenHeight - 10);

                        rawList.Add((hwnd, r, canLand));
                        return true;
                    }, IntPtr.Zero);

                    IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
                    if (taskbar != IntPtr.Zero && Native.IsWindowVisible(taskbar))
                    {
                        if (Native.GetExactWindowRect(taskbar, out Native.RECT tbRect))
                        {
                            if (tbRect.Width > 50 && tbRect.Height > 10)
                            {
                                rawList.Add((taskbar, tbRect, true));
                            }
                        }
                    }

                    IntPtr secTaskbar = Native.FindWindow("Shell_SecondaryTrayWnd", null);
                    if (secTaskbar != IntPtr.Zero && Native.IsWindowVisible(secTaskbar))
                    {
                        if (Native.GetExactWindowRect(secTaskbar, out Native.RECT secRect))
                        {
                            if (secRect.Width > 50 && secRect.Height > 10)
                            {
                                rawList.Add((secTaskbar, secRect, true));
                            }
                        }
                    }

                    var newList = new List<WindowObstacle>(rawList.Count);
                    var seen = new HashSet<IntPtr>();

                    for (int i = 0; i < rawList.Count; i++)
                    {
                        var (hwnd, rect, canLand) = rawList[i];
                        seen.Add(hwnd);

                        if (_trackedWindows.TryGetValue(hwnd, out var obs))
                        {
                            obs.ZIndex = i;
                            obs.CanLand = canLand;
                            obs.EnsureWidth(Math.Max(1, rect.Width));

                            newList.Add(obs);
                        }
                        else
                        {
                            var newObs = new WindowObstacle(hwnd, rect, i, canLand);
                            _trackedWindows[hwnd] = newObs;
                            newList.Add(newObs);
                        }
                    }

                    var toRemove = new List<IntPtr>();
                    foreach (var kvp in _trackedWindows)
                    {
                        if (!seen.Contains(kvp.Key))
                        {
                            kvp.Value.IsClosedOrMinimized = true;
                            toRemove.Add(kvp.Key);
                        }
                    }

                    foreach (var removed in toRemove)
                    {
                        _trackedWindows.Remove(removed);
                    }

                    _snapshot = newList;
                }
                catch
                {
                }

                Thread.Sleep(100);
            }
        }
    }
}
