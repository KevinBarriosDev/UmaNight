using System;
using System.Threading;
using System.Windows;

namespace UmamusumeDarkMode
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Evita que se abran dos copias (dos overlays, dos iconos en la bandeja, etc.)
            _singleInstanceMutex = new Mutex(true, @"Local\UmaNight_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                Environment.Exit(0);
                return;
            }
            base.OnStartup(e);
        }
    }
}