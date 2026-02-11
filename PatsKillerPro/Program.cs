using System;
using System.Threading;
using System.Windows.Forms;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    internal static class Program
    {
        private static Mutex? _mutex;
        
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Ensure single instance
            const string mutexName = "PatsKillerPro_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            
            if (!createdNew)
            {
                MessageBox.Show(
                    "PatsKiller Pro is already running.",
                    "PatsKiller Pro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            
            try
            {
                // Initialize logging
                Logger.Initialize();
                Logger.Info($"PatsKiller Pro v{AppVersion.Display} starting...");
                
                // Configure application
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                // Set up global exception handling
                Application.ThreadException += Application_ThreadException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                
                // Run main form
                Application.Run(new MainForm());
                
                Logger.Info("PatsKiller Pro shutting down normally.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Fatal error during startup: {ex.Message}", ex);
                MessageBox.Show(
                    $"A fatal error occurred:\n\n{ex.Message}\n\nPlease contact support@patskiller.com",
                    "PatsKiller Pro - Fatal Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
        }
        
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error($"Unhandled thread exception: {e.Exception.Message}", e.Exception);
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will attempt to continue.",
                "PatsKiller Pro - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error($"Unhandled domain exception: {ex?.Message}", ex);
            
            if (e.IsTerminating)
            {
                MessageBox.Show(
                    $"A critical error occurred:\n\n{ex?.Message}\n\nThe application must close.",
                    "PatsKiller Pro - Critical Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
