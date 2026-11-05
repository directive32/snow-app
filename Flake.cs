using System;

namespace SnowApp
{
    public class Flake
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Radius;
        public byte Alpha;

        public float Phase;
        public float SwaySpeed;
        public float SwayAmplitude;

        private static readonly Random Rand = new Random();

        public void Reset(int screenWidth, int screenHeight, bool fullScreenInit = false)
        {
            X = (float)(Rand.NextDouble() * screenWidth);
            Y = fullScreenInit ? (float)(Rand.NextDouble() * screenHeight) : -(float)(Rand.NextDouble() * 30 + 10);

            Radius = (float)(Rand.NextDouble() * 1.8 + 1.2); // 1.2 to 3.0 px
            Alpha = (byte)Rand.Next(170, 245);

            float speedFactor = Radius / 3.0f;
            Vy = 0.85f + speedFactor * 1.2f;
            Vx = (float)(Rand.NextDouble() * 0.3 - 0.15);

            Phase = (float)(Rand.NextDouble() * Math.PI * 2);
            SwaySpeed = (float)(Rand.NextDouble() * 1.2 + 0.8);
            SwayAmplitude = (float)(Rand.NextDouble() * 0.7 + 0.3);
        }

        public void SpawnCrumb(float startX, float startY)
        {
            X = startX + (float)(Rand.NextDouble() * 6 - 3);
            Y = startY + (float)(Rand.NextDouble() * 3);
            Radius = (float)(Rand.NextDouble() * 1.2 + 1.0);
            Alpha = (byte)Rand.Next(180, 230);
            Vy = 1.0f + (float)Rand.NextDouble() * 0.8f; // Soft, slow gentle fall
            Vx = (float)(Rand.NextDouble() * 0.6 - 0.3);
            Phase = (float)(Rand.NextDouble() * Math.PI * 2);
            SwaySpeed = 1.0f;
            SwayAmplitude = 0.4f;
        }
    }
}
