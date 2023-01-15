using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WachbuchApp
{

    public partial class DialogEditEmployee : Window
    {

        private MainService? _ownerService;
        private long _employeeId;

        #region Dialog-Start

        public DialogEditEmployee()
        {
            InitializeComponent();
        }

        internal static DialogEditEmployee GetInstance(Window owner, MainService service, DialogEditEmployeeEditAction EditAction = DialogEditEmployeeEditAction.EDIT_QUALIFICATION, long EmployeeId = -1, string BookEntryText = "")
        {
            DialogEditEmployee wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._ownerService = service;
            wnd._employeeId = EmployeeId;

            // Statische Fenster-Eigenschaften zurücksetzen
            DialogEditEmployee.SelectedAction = EditAction;
            DialogEditEmployee.SelectedBookEntryText = BookEntryText;
            DialogEditEmployee.SelectedEmployeeQualification = EmployeeQualification.UNKNOWN;

            return wnd;
        }

        #endregion
        #region Dialog-Result

        internal static DialogEditEmployeeEditAction SelectedAction { get; private set; } = DialogEditEmployeeEditAction.EDIT_QUALIFICATION;
        internal static string SelectedBookEntryText { get; private set; } = "";
        internal static EmployeeQualification SelectedEmployeeQualification { get; private set; } = EmployeeQualification.UNKNOWN;

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

            // Mitarbeiter laden
            var employee = _ownerService!.Database.GetEmployee(_employeeId);

            // Erste Auswahl festlegen
            if (employee == null)
            {
                SelectedAction = DialogEditEmployeeEditAction.EDIT_ENTRYTEXT;
                groupSelection.Visibility = Visibility.Collapsed;
                groupQuali.Visibility = Visibility.Collapsed;
            }
            else if (SelectedAction == DialogEditEmployeeEditAction.EDIT_QUALIFICATION)
            {
                radioQuali.IsChecked = true;
            }
            else if (SelectedAction == DialogEditEmployeeEditAction.EDIT_ENTRYTEXT)
            {
                radioEntryText.IsChecked = true;
            }

            btnBookEntryClear.Visibility = (string.IsNullOrWhiteSpace(SelectedBookEntryText) ? Visibility.Collapsed : Visibility.Visible);
            btnBookEntryClear.Content = employee == null ? MainServiceHelper.GetString("Common_Button_DeleteEntry") : MainServiceHelper.GetString("Common_Button_ResetEntry");
            btnBookEntryClear.Tag = "";

            // Mitarbeiterinfo setzen
            if (employee != null)
            {

                // Combo befüllen
                comboQualiSelect.Items.Clear();
                foreach (EmployeeQualification qualification in Enum.GetValues(typeof(EmployeeQualification)))
                {
                    ComboBoxItem item = new() { Content = MainServiceHelper.GetQualificationTextFull(qualification), Tag = qualification, IsSelected = (qualification == employee.Qualification) };
                    comboQualiSelect.Items.Add(item);
                }

                // Namen anzeigen
                txtQualiEmployeeName.Text = employee.EmployeeNameText;

                // FreiText befüllen, wenn bisher leer
                if (string.IsNullOrWhiteSpace(SelectedBookEntryText))
                {
                    SelectedBookEntryText = employee.EmployeeLabelText;
                }
                btnBookEntryClear.Tag = employee.EmployeeLabelText;

            }

            // Oberfläche einsetzen
            textBookEntry.Text = SelectedBookEntryText;

            // Dialog validieren
            _dialogOriginalStates = GenerateDialogStates();
            SetupDialog();

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
            statesString.Append(comboQualiSelect.SelectedItem?.ToString() ?? "NONE"); // OutOfRange wird nie auftreten, da alle Werte bekannt und definitiv < Int.MaxValue
            statesString.Append(textBookEntry.Text ?? "NONE");
            return statesString.ToString();
        }

        #endregion

        // ########################################################################################

        #region Dialog-Buttons

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        #endregion

        #region EditEmployee-UI

        private void SetupDialog()
        {

            // UI setzen, je nach ausgewählter Aktion
            switch (SelectedAction)
            {

                case DialogEditEmployeeEditAction.EDIT_QUALIFICATION:

                    groupName.Visibility = Visibility.Collapsed;
                    groupQuali.Visibility = Visibility.Visible;
                    break;

                case DialogEditEmployeeEditAction.EDIT_ENTRYTEXT:

                    groupQuali.Visibility = Visibility.Collapsed;
                    groupName.Visibility = Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(SelectedBookEntryText)) { textBookEntry.SelectAll(); }
                    textBookEntry.Focus();
                    break;

            }

            ValidateDialog();

        }

        // ########################################################################################

        private void RadioQuali_Checked(object sender, RoutedEventArgs e)
        {
            SelectedAction = DialogEditEmployeeEditAction.EDIT_QUALIFICATION;
            SetupDialog();
        }

        private void RadioEntryText_Checked(object sender, RoutedEventArgs e)
        {
            SelectedAction = DialogEditEmployeeEditAction.EDIT_ENTRYTEXT;
            SetupDialog();
        }

        // ########################################################################################

        private void ComboTypeSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ComboQualiSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidateDialog();
        }

        private void TextBookEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateDialog();
        }

        private void BtnBookEntryClear_Click(object sender, RoutedEventArgs e)
        {
            textBookEntry.Text = btnBookEntryClear.Tag.ToString();
            Save();
        }

        // ########################################################################################

        private void ValidateDialog()
        {

            _dialogChangedStates = GenerateDialogStates();
            bool isValid = true;

            // Wenn Qualifikation geändert werden soll, aber UNBEKANNT ausgewählt
            if (SelectedAction == DialogEditEmployeeEditAction.EDIT_QUALIFICATION && (comboQualiSelect.SelectedItem == null || ((EmployeeQualification)((ComboBoxItem)comboQualiSelect.SelectedItem).Tag) == EmployeeQualification.UNKNOWN))
            {
                isValid = false;
                btnSave.Content = MainServiceHelper.GetString("DialogEditEmployee_Dialog_QualiUnknown");
            }

            // Wenn Text geändert werden soll, aber leer
            if (SelectedAction == DialogEditEmployeeEditAction.EDIT_ENTRYTEXT && !btnBookEntryClear.IsVisible && string.IsNullOrWhiteSpace(textBookEntry.Text))
            {
                isValid = false;
                btnSave.Content = MainServiceHelper.GetString("DialogEditEmployee_Dialog_EmptyEntry");
            }

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
        #region EditEmployee

        private void Save()
        {

            switch (SelectedAction)
            {
                case DialogEditEmployeeEditAction.EDIT_QUALIFICATION:

                    // Derzeitige Wahl festlegen
                    if (comboQualiSelect.SelectedItem == null || _ownerService == null) { DialogResult = false; this.Close(); return; }
                    SelectedEmployeeQualification = ((EmployeeQualification)((ComboBoxItem)comboQualiSelect.SelectedItem).Tag);

                    // Direkt speichern
                    _ownerService.Database.SetEmployeeQualification(_employeeId, SelectedEmployeeQualification);

                    DialogResult = true;
                    this.Close();
                    break;

                case DialogEditEmployeeEditAction.EDIT_ENTRYTEXT:

                    // Derzeitige Wahl festlegen
                    SelectedBookEntryText = textBookEntry.Text;
                    if (btnBookEntryClear.Tag.ToString() == SelectedBookEntryText) { SelectedBookEntryText = ""; }

                    DialogResult = true;
                    this.Close();
                    break;

            }

        }

        #endregion

    }

    // ########################################################################################

    internal enum DialogEditEmployeeEditAction
    {
        EDIT_QUALIFICATION,
        EDIT_ENTRYTEXT
    }

}
