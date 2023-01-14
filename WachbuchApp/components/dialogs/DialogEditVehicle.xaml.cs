using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WachbuchApp
{

    public partial class DialogEditVehicle : Window
    {

        private MainService? _ownerService;
        private List<MainServiceConfiguration.BookVehicle> _bookVehicles = new();

        #region Dialog-Start

        public DialogEditVehicle()
        {
            InitializeComponent();
        }

        internal static DialogEditVehicle GetInstance(Window owner, MainService service, List<MainServiceConfiguration.BookVehicle> bookVehicles, MainServiceConfiguration.BookVehicle currentVehicle)
        {
            DialogEditVehicle wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._ownerService = service;
            wnd._bookVehicles = bookVehicles;

            // Statische Fenster-Variablen zurücksetzen
            SelectedVehicle = currentVehicle;

            return wnd;
        }

        #endregion
        #region Dialog-Result

        internal static MainServiceConfiguration.BookVehicle SelectedVehicle = new("", "");

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

            // Fahrzeuge laden
            comboVehicleSelect.Items.Clear();
            foreach (var vehicle in _bookVehicles)
            {
                ComboBoxItem item = new()
                {
                    Content = string.Format("{0} ({1})", vehicle.FunkId, vehicle.Keyplate),
                    Tag = vehicle,
                    IsSelected = (vehicle.Keyplate == SelectedVehicle.Keyplate && vehicle.FunkId == SelectedVehicle.FunkId)
                };
                comboVehicleSelect.Items.Add(item);
            }
            comboVehicleSelect.IsDropDownOpen = true;

            // Dialog validieren
            _dialogOriginalStates = GenerateDialogStates();
            ValidateDialog();

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

            // Wenn bereits erfolgreich geschlossen > Schließen
            if (DialogResult.HasValue) { return; }

            // Wenn nichts geändert wurde > Schließen
            if (_dialogOriginalStates == _dialogChangedStates) { return; }

            // Dialog anzeigen, ob abbgebrochen gewünscht ist
            DialogMessageBox message = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("Common_Dialog_ExitNag_Title"),
                Message: MainServiceHelper.GetString("Common_Dialog_ExitNag"),
                MessageIcon: MessageBoxImage.Question,
                PositiveAction: new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_ExitNag_Yes"), () => { }),
                NegativeAction: new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_ExitNag_No"), () => { }));

            if (MainServiceHelper.ShowDialog(overlayDialog, message) == true)
            {

                // Wenn abbruch bestätigt > Schließen
                return;

            }

            // Sonst Schließen abbrechen
            e.Cancel = true;

        }

        private void Window_Titlebar_Exited(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        // ########################################################################################

        #region Dialog-States

        private string _dialogOriginalStates = "";
        private string _dialogChangedStates = "";

        private string GenerateDialogStates()
        {
            StringBuilder statesString = new();
            statesString.Append(comboVehicleSelect.SelectedItem?.ToString() ?? "NONE"); // OutOfRange wird nie auftreten, da alle Werte bekannt und definitiv < Int.MaxValue
            return statesString.ToString();
        }

        #endregion

        // ########################################################################################

        #region Dialog-Buttons

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnSave_Click(object sender, RoutedEventArgs e) => Save();

        #endregion

        // ########################################################################################

        #region EditVehicle-UI

        private void ComboVehicleSelect_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateDialog();

        // ########################################################################################

        private void ValidateDialog()
        {

            _dialogChangedStates = GenerateDialogStates();
            bool isValid = true;

            // Wenn nichts geändert wurde, nichts speichern
            if (_dialogChangedStates == _dialogOriginalStates)
            {
                isValid = false;
                btnSave.Content = MainServiceHelper.GetString("Common_Dialog_NoneChanged");
            }

            // Speichern Button
            if (isValid) { btnSave.Content = MainServiceHelper.GetString("Common_Button_Save"); }

            btnSave.IsEnabled = isValid;

        }

        #endregion
        #region EditVehicle

        private void Save()
        {

            if (comboVehicleSelect.SelectedItem == null || _ownerService == null) { DialogResult = false; this.Close(); return; }
            SelectedVehicle = ((MainServiceConfiguration.BookVehicle)((ComboBoxItem)comboVehicleSelect.SelectedItem).Tag);

            DialogResult = true;
            this.Close();

        }

        #endregion

    }

}
