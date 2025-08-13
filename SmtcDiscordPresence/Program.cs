using System;
using System.Threading;
using System.Windows.Forms;

namespace SmtcDiscordPresence
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "SmtcDiscordPresence.Singleton", out createdNew);
            if (!createdNew) return;

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext());

            _mutex.ReleaseMutex();
        }
    }
}