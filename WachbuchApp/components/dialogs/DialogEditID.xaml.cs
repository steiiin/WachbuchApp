using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WachbuchApp
{

    public partial class DialogEditID : Window
    {

        private MainService? _ownerService;
        private DialogEditIdEditEntry? _idEntry;
        private long _employeeId;

        #region Dialog-Start

        public DialogEditID()
        {
            InitializeComponent();
        }

        internal static DialogEditID GetInstance(Window owner, MainService service, DialogEditIdEditEntry entry, DialogEditIdEditAction EditAction = DialogEditIdEditAction.EDIT_ASSIGNEDSTATION, long EmployeeId = -1)
        {
            DialogEditID wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._ownerService = service;
            wnd._employeeId = EmployeeId;
            wnd._idEntry = entry;

            // Statische Fenster-Eigenschaften zurücksetzen
            DialogEditID.SelectedAction = EditAction;
            DialogEditID.SelectedIdEntry = entry;
            DialogEditID.SelectedEmployeeAssignedStation = service.Configuration.Books[0].StationName;

            return wnd;
        }

        #endregion
        #region Dialog-Result

        internal static DialogEditIdEditAction SelectedAction { get; private set; } = DialogEditIdEditAction.EDIT_ASSIGNEDSTATION;
        internal static DialogEditIdEditEntry SelectedIdEntry { get; private set; } = DialogEditIdEditEntry.Empty;
        internal static string SelectedEmployeeAssignedStation { get; private set; } = "";

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

            // Combo befüllen
            comboTypeSelect.Items.Clear();
            comboTypeSelect.Items.Add(new ComboBoxItem() { Content = "Innendienst", Tag = "ID", IsSelected = true });
            comboTypeSelect.Items.Add(new ComboBoxItem() { Content = "Monatsdesinfektion", Tag = "MDR", IsSelected = SelectedIdEntry.TypeShort == "MDR" });

            // Mitarbeiter laden
            var employee = _ownerService!.Database.GetEmployee(_employeeId);

            // Erste Auswahl festlegen
            if (employee == null)
            {

                SelectedAction = DialogEditIdEditAction.EDIT_ENTRY;
                groupSelection.Visibility = Visibility.Collapsed;
                groupStation.Visibility = Visibility.Collapsed;

            }
            else if (SelectedAction == DialogEditIdEditAction.EDIT_ASSIGNEDSTATION)
            {
                radioStation.IsChecked = true;
            }
            else if (SelectedAction == DialogEditIdEditAction.EDIT_ENTRY)
            {
                radioEntryText.IsChecked = true;
            }

            btnBookEntryClear.Visibility = (SelectedIdEntry.IsEmpty ? Visibility.Collapsed : Visibility.Visible);
            btnBookEntryClear.Content = employee == null ? MainServiceHelper.GetString("Common_Button_DeleteEntry") : MainServiceHelper.GetString("Common_Button_ResetEntry");
            btnBookEntryClear.Tag = "";

            // Mitarbeiterinfo setzen
            if (employee != null)
            {

                // Station füllen & einstellen
                comboStationSelect.Items.Clear();
                foreach (var book in _ownerService.Configuration.Books)
                {
                    if (book.IDs == null) { continue; }
                    comboStationSelect.Items.Add(new ComboBoxItem()
                    {
                        Content = book.StationName,
                        Tag = book.StationName,
                        IsSelected = book.StationName == employee.AssignedStation
                    });
                }

                // Namen anzeigen
                txtStationEmployeeName.Text = employee.EmployeeNameText;

                // FreiText befüllen, wenn bisher leer
                if (string.IsNullOrWhiteSpace(SelectedIdEntry.EmployeeText))
                {
                    SelectedIdEntry.EmployeeText = employee.EmployeeLabelText;
                }

            }

            // Oberfläche einsetzen
            textBookEntry.Text = SelectedIdEntry.EmployeeText;

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
            statesString.Append(comboStationSelect.SelectedItem?.ToString() ?? "NONE"); // OutOfRange wird nie auftreten, da alle Werte bekannt und definitiv < Int.MaxValue
            statesString.Append(comboTypeSelect.SelectedItem?.ToString() ?? "NONE");
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

                case DialogEditIdEditAction.EDIT_ASSIGNEDSTATION:

                    groupEntryType.Visibility = Visibility.Collapsed;
                    groupEntryText.Visibility = Visibility.Collapsed;
                    groupStation.Visibility = Visibility.Visible;
                    break;

                case DialogEditIdEditAction.EDIT_ENTRY:

                    groupStation.Visibility = Visibility.Collapsed;
                    groupEntryType.Visibility = Visibility.Visible;
                    groupEntryText.Visibility = Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(SelectedIdEntry.EmployeeText)) { textBookEntry.SelectAll(); }
                    textBookEntry.Focus();
                    break;

            }

            ValidateDialog();

        }

        // ########################################################################################

        private void RadioStation_Checked(object sender, RoutedEventArgs e)
        {
            SelectedAction = DialogEditIdEditAction.EDIT_ASSIGNEDSTATION;
            SetupDialog();
        }

        private void RadioEntryText_Checked(object sender, RoutedEventArgs e)
        {
            SelectedAction = DialogEditIdEditAction.EDIT_ENTRY;
            SetupDialog();
        }

        // ########################################################################################

        private void ComboStationSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            if (SelectedAction == DialogEditIdEditAction.EDIT_ASSIGNEDSTATION && comboStationSelect.SelectedItem == null)
            {
                isValid = false;
                btnSave.Content = MainServiceHelper.GetString("DialogEditEmployee_Dialog_QualiUnknown");
            }

            // Wenn Text geändert werden soll, aber leer
            if (SelectedAction == DialogEditIdEditAction.EDIT_ENTRY && !btnBookEntryClear.IsVisible && string.IsNullOrWhiteSpace(textBookEntry.Text))
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
                case DialogEditIdEditAction.EDIT_ASSIGNEDSTATION:

                    // Derzeitige Wahl festlegen
                    if (comboStationSelect.SelectedItem == null || _ownerService == null) { DialogResult = false; this.Close(); return; }
                    SelectedEmployeeAssignedStation = (string)((ComboBoxItem)comboStationSelect.SelectedItem).Tag;

                    // Direkt speichern
                    _ownerService.Database.SetEmployeeAssignedStation(_employeeId, SelectedEmployeeAssignedStation);

                    DialogResult = true;
                    this.Close();
                    break;

                case DialogEditIdEditAction.EDIT_ENTRY:

                    // Derzeitige Wahl festlegen
                    SelectedIdEntry = new((string)((ComboBoxItem)comboTypeSelect.SelectedItem).Tag, textBookEntry.Text);

                    DialogResult = true;
                    this.Close();
                    break;

            }

        }

        #endregion

    }

    // ########################################################################################

    internal enum DialogEditIdEditAction
    {
        EDIT_ASSIGNEDSTATION,
        EDIT_ENTRY
    }

    internal class DialogEditIdEditEntry
    {

        public string TypeShort { get; set; }
        public string EmployeeText { get; set; }

        public bool IsEmpty => TypeShort == "" || EmployeeText == "";

        public DialogEditIdEditEntry(string type, string empText) { TypeShort = type; EmployeeText = empText; }

        public readonly static DialogEditIdEditEntry Empty = new("", "");

    }

}
