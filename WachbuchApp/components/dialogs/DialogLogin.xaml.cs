using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace WachbuchApp
{

    public partial class DialogLogin : Window
    {


        private DialogLoginDomain _loginReason;
        private MainService? _ownerService;

        #region Dialog-Start

        public DialogLogin()
        {
            InitializeComponent();
        }

        // ########################################################################################

        private static DialogLogin GetInstance(Window owner, MainService service, DialogLoginDomain reason)
        {
            DialogLogin wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd._loginReason = reason;
            wnd._ownerService = service;

            return wnd;
        }

        internal static DialogLogin GetPublicInstance(Window owner, MainService service)
        {
            return GetInstance(owner, service, DialogLoginDomain.CREDENTIAL_PUBLIC);
        }
        internal static DialogLogin GetPrivateInstance(Window owner, MainService service)
        {
            return GetInstance(owner, service, DialogLoginDomain.CREDENTIAL_PRIVATE);
        }

        #endregion
        #region Dialog-Result

        internal static string SelectedUsername { get; private set; } = "";
        internal static string SelectedPassword { get; private set; } = "";

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

            // Login-UI zurücksetzen
            txtMessage.Text = DialogMessageText;
            ResetLogin();

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

            // Wenn bereits erfolgreich geschlossen > Schließen
            if (DialogResult.HasValue) { return; }

            // Dialog anzeigen, ob abbgebrochen gewünscht ist
            DialogMessageBox message = DialogMessageBox.GetInstance(this,
                Title: MainServiceHelper.GetString("DialogLogin_ExitNag_Title"),
                Message: DialogCancelText,
                MessageIcon: MessageBoxImage.Question,
                PositiveAction: new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_ExitNag_Yes"), () => { }),
                NegativeAction: new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_ExitNag_No"), () => { }));

            if (MainServiceHelper.ShowDialog(overlayDialog, message) == true)
            {

                // Wenn abbruch bestätigt > Schließen
                return;

            }

        }

        private void Window_Titlebar_Exited(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        // ########################################################################################

        #region Dialog-Benachrichtigung

        private void ShowProgress()
        {
            overlayDialog.Visibility = Visibility.Visible;
            overlayProgress.Visibility = Visibility.Visible;
        }

        private void HideProgress()
        {
            overlayProgress.Visibility = Visibility.Collapsed;
            overlayDialog.Visibility = Visibility.Collapsed;
        }

        // ########################################################################################

        #endregion
        #region Dialog-Buttons

        private void BtnLogin_Click(object sender, RoutedEventArgs e) => Login();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        // ########################################################################################

        #region Login-UI 

        private void ResetLogin(bool ClearUserBox = true, bool ClearPassBox = true)
        {

            HideProgress();

            if (ClearUserBox)
            {
                textUsername.Text = string.Empty;
                SelectedUsername = string.Empty;
            }
            if (ClearPassBox)
            {
                textPassword.Password = string.Empty;
                SelectedPassword = string.Empty;
            }

            ValidateDialog();

            if (!ClearUserBox && ClearPassBox) 
            { 
                textPassword.Focus();

                // Falsch-Animation erstellen
                Storyboard wrongShake = (Storyboard)FindResource("AnimationWrongShake");
                textPassword.BeginStoryboard(wrongShake);

            }
            else { textUsername.Focus(); }

        }

        // ########################################################################################

        private void DialogInput_TextChanged(object sender, RoutedEventArgs e) => ValidateDialog();

        private void DialogInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && btnLogin.IsEnabled) { Login(); }
        }

        // ########################################################################################

        private void ValidateDialog()
        {

            bool isValid = true;

            if (string.IsNullOrWhiteSpace(textUsername.Text)) { isValid = false; }
            if (string.IsNullOrEmpty(textPassword.Password)) { isValid = false; }

            btnLogin.IsEnabled = isValid;

        }

        // ########################################################################################

        private string DialogMessageText => _loginReason switch
        {
            DialogLoginDomain.CREDENTIAL_PUBLIC => MainServiceHelper.GetString("DialogLogin_Message_Public"),
            DialogLoginDomain.CREDENTIAL_PRIVATE => MainServiceHelper.GetString("DialogLogin_Message_Private"),
            _ => ""
        };

        private string DialogCancelText => _loginReason switch
        {
            DialogLoginDomain.CREDENTIAL_PUBLIC => MainServiceHelper.GetString("DialogLogin_ExitNag_Public"),
            DialogLoginDomain.CREDENTIAL_PRIVATE => MainServiceHelper.GetString("DialogLogin_ExitNag_Private"),
            _ => ""
        };

        #endregion
        #region Login

        private async void Login()
        {

            // Evtl. Fehlerdialog
            string errorMessage = "";

            // Dialog sperren
            ShowProgress();

            // Login ausführen und Rückgabe verarbeiten
            var state = await _ownerService!.SetNewCredentials(textUsername.Text, textPassword.Password);
            switch (state)
            {

                // Neue Zugangsdaten geprüft und erfolgreich gesetzt > Schließen
                case VivendiApiState.SUCCESSFUL:

                    DialogResult = true;
                    this.Close();
                    return;

                // Eingegebene Zugangsdaten nicht korrekt
                case VivendiApiState.CREDENTIALS_ERROR:

                    ResetLogin(ClearUserBox: false, ClearPassBox: true);
                    return;

                // Fehler: Verbindung
                case VivendiApiState.CONNECTION_ERROR:

                    errorMessage = MainServiceHelper.GetString("DialogLogin_Error_Connection");
                    break;

                // Fehler: Unbekannt
                case VivendiApiState.SERVER_APP_ERROR:
                default:

                    errorMessage = MainServiceHelper.GetString("DialogLogin_Error_Connection");
                    break;

            }

            // Fehlerdialog anzeigen
            ResetLogin(ClearUserBox: false, ClearPassBox: false);

            DialogMessageBox messageBox = DialogMessageBox.GetInstance(this, 
                Title: MainServiceHelper.GetString("DialogLogin_Error_Title"), 
                Message: errorMessage, MessageIcon: MessageBoxImage.Error,
                PositiveAction: new KeyValuePair<string, Action>(MainServiceHelper.GetString("Common_Button_Ok"), () => { }));

            MainServiceHelper.ShowDialog(overlayDialog, messageBox);

        }

        #endregion

    }

    // ########################################################################################

    internal enum DialogLoginDomain
    {
        CREDENTIAL_PUBLIC,
        CREDENTIAL_PRIVATE
    }


}
