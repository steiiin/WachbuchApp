using System;
using System.Windows;

namespace WachbuchApp
{

    public partial class DialogAdminMenu : Window
    {

        private MainService? _ownerService;

        #region Dialog-Start

        public DialogAdminMenu()
        {
            InitializeComponent();
        }

        internal static DialogAdminMenu GetInstance(Window owner, MainService service)
        {
            DialogAdminMenu wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._ownerService = service;

            return wnd;
        }

        #endregion
        #region Dialog-Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            // Fehlerquellen abfangen & Dialog sofort schließen -- sollte nicht auftreten
            if (_ownerService == null)
            {
                AppLog.Error("Der Dialog darf nicht direkt initialisiert werden, sondern mit GetInstance erstellt werden.");
                this.Close();
            }

        }

        private void Window_Titlebar_Exited(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        // ########################################################################################

        #region Admin-Actions

        private void BtnActionMissingQuali_Click(object sender, RoutedEventArgs e)
        {

            DialogBulkEdit wnd = DialogBulkEdit.GetInstance(this, _ownerService!);
            wnd.ShowDialog();

        }

        private void BtnActionDeleteDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (_ownerService == null) { return; }
            if (MessageBox.Show("Soll die Datenbank und Konfiguration zurückgesetzt werden?", "", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {

                _ownerService.Configuration.DeleteConfiguration();
                _ownerService.Database.DeleteDatabase();

                MessageBox.Show("Die Anwendung wird nun beendet. Starte sie erneut.");
                Application.Current.Shutdown();

            }

        }

        private void BtnActionHardExit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        #endregion

    }

}
