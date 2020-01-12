﻿using Meadow;
using System.Threading;

namespace Sensors.Atmospheric.Bmp085_Sample
{
    class Program
    {
        static IApp app;

        public static void Main(string[] args)
        {
            app = new MeadowApp();

            Thread.Sleep(Timeout.Infinite);
        }
    }
}