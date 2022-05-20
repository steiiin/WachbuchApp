using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

            foreach (var employeeId in _ownerService!.Database.GetUnknownEmployees)
            {

                DialogEditEmployee editEmployee = DialogEditEmployee.GetInstance(this, _ownerService!, DialogEditEmployeeEditAction.EDIT_QUALIFICATION, EmployeeId: employeeId);
                if (MainServiceHelper.ShowDialog(overlayDialog, editEmployee) != true)
                {

                    if (MessageBox.Show("Mitarbeiter überspringen? Wenn nein, wird die Liste abgebrochen.", "", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
                    {
                        MessageBox.Show("Stapelverarbeitung abgebrochen.");
                        return;
                    }

                }

            }

        }

        private void BtnActionHardExit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        #endregion

    }

}
