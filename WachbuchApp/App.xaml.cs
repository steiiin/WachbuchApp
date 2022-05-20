using System;
using System.Threading;
using System.Windows;

namespace WachbuchApp
{

    public partial class App : Application
    {

        private const string UniqueEventName = "{77b560cb-200b-49d1-abd4-b37015df62a5}";
        private const string UniqueMutexName = "{2f2cbb26-8573-4616-9918-f7e6628fc961}";

        private EventWaitHandle? singleinstanceEventWaitHandle;
        private Mutex? singleinstanceMutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {

            try
            {

                // Mutex & WaitHandle erstellen & und für GarbageCollection sperren -- Wenn zweite Instanz vorhanden (im Hintergrund), dann isOwned == false
                this.singleinstanceMutex = new Mutex(true, UniqueMutexName, out bool isOwned);
                this.singleinstanceEventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UniqueEventName);
                GC.KeepAlive(this.singleinstanceMutex);

                if (isOwned)
                {

                    // Fenster aufrufen
                    MainWindow wnd = new();
                    MainWindow = wnd;
                    wnd.StartInState(Environment.CommandLine.Contains("/preload"));

                    // Backgroundthread, der auf zweite Instanz wartet
                    var thread = new Thread(() =>
                    {
                        while (this.singleinstanceEventWaitHandle.WaitOne())
                        {
                            Current.Dispatcher.BeginInvoke(
                            () => ((MainWindow)Current.MainWindow).StartInState(false));
                        }
                    })
                    {
                        IsBackground = true,
                        
                    };
                    thread.Start();
                    return;

                }

                // Zweite Instanz aktiviert eventWaitHandle & beendet sich.
                this.singleinstanceEventWaitHandle.Set();
                this.Shutdown();

            }
            catch (Exception ex)
            {

                if (ex is ObjectDisposedException ||
                    ex is AbandonedMutexException ||
                    ex is InvalidOperationException)
                {
                    // WaitHandle wurde bereits beendet -- Sollte beim nächsten Start erneuert werden -- Beende Anwendung, der Anwender wird von allein neustarten
                    // WaitHandle wurde ausgelöst, weil das Mutex verwaist wurde -- Beende Anwendung, der Anwender wird von allein neustarten
                    // WaitHandle im Netzwerk -- Kann nicht passieren, wurde dafür nicht konzipiert // Fange trotzdem ab, falls jemand rumspielt

                    AppLog.Error(ex);
                    this.Shutdown(-1);
                    return;

                }

                // Mutex o. WaitHandle konnte nicht erstellt werden -- Zugriffsfehler // Kann eigentlich nicht passieren
                // Kein Arbeitsspeicher zum Start der Anwendung -- Fatal // Sollte eigentlich nicht passieren, außer - der Arbeitsspeicher ist voll - also Abbruch.
                // Mutex o. WaitHandle konnte nicht erstellt werden -- Ein anderes Mutex am System hat denselben Namen // Kann eigentlich nicht passieren
                // Mutex o. WaitHandle konnte nicht erstellt werden -- Keine Schreibrechte // Evtl. Festplatte voll

                FatalError(ex);
                
            }

        }
    
        private void FatalError(Exception ex)
        {
            MessageBox.Show("Die Anwendung konnte nicht gestartet werden. Informiere den IT-Verantwortlichen.", "Fehler beim Start", MessageBoxButton.OK, MessageBoxImage.Error);
            AppLog.Error(ex);
            this.Shutdown(-1);
        }

    }

    public static class AppLog
    { 

        public static void Error(string message)
        {
            Error(message, "");
        }
        public static void Error(string message, string? stacktrace)
        {

            try
            {

                // Log-Builder
                System.Text.StringBuilder content = new();

                // Aktueller Pfad
                string SAVEPATH = System.IO.Path.Combine(Environment.CurrentDirectory, "error.log");

                // Alten Log auslesen
                if (System.IO.File.Exists(SAVEPATH))
                {
                    content.Append(System.IO.File.ReadAllText(SAVEPATH));
                }

                // Daten kürzen
                if (content.Length > 200000)
                {
                    content.Remove(0, content.Length - 200000);
                }

                // Neuer Eintrag anfügen
                content.AppendLine(Environment.NewLine +
                "####################################################################################################" + Environment.NewLine +
                DateTime.Now.ToString() + Environment.NewLine +
                "----------------------------------------------------------------------------------------------------" + Environment.NewLine +
                message + Environment.NewLine +
                (string.IsNullOrWhiteSpace(stacktrace) ? "" :
                "----------------------------------------------------------------------------------------------------" + Environment.NewLine +
                stacktrace + Environment.NewLine) +
                "####################################################################################################");

                // Daten schreiben
                System.IO.File.WriteAllText(SAVEPATH, content.ToString());

            }
            catch (Exception)
            {
                Console.WriteLine("Unprotokollierbarer Fehler. Schreibrechte nicht vorhanden / Festplatte voll.");
            }
            
        }

        public static void Error(Exception ex)
        {
            if (ex == null || ex.Message == null)
            {
                Error("Unbekannter Fehler.");
                return;
            }
            Error(ex.Message, ex.StackTrace);
        }

    }

}
