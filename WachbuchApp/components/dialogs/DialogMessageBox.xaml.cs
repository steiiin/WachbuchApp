using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace WachbuchApp
{

    public partial class DialogMessageBox : Window
    {

        #region Dialog-Start

        public DialogMessageBox()
        {
            InitializeComponent();
        }

        internal static DialogMessageBox GetInstance(Window owner, string Title = "", string Message = "", MessageBoxImage? MessageIcon = null, KeyValuePair<string, Action>? PositiveAction = null, KeyValuePair<string, Action>? NegativeAction = null)
        {

            DialogMessageBox wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;

            // Fenster-Steuerelemente setzen
            // 1. Title & Message
            if (!string.IsNullOrEmpty(Title)) { wnd.windowTitlebar.Title = Title; }
            if (string.IsNullOrEmpty(Message))
            {
                wnd.txtMessage.Text = "";
                wnd.txtMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                wnd.txtMessage.Text = Message;
                wnd.txtMessage.Visibility = Visibility.Visible;
            }

            // 2. MessageIcon
            if (MessageIcon != null)
            {
                wnd.imgMessageIconQuestion.Visibility = (MessageIcon == MessageBoxImage.Question ? Visibility.Visible : Visibility.Collapsed);
                wnd.imgMessageIconInfo.Visibility = (MessageIcon == MessageBoxImage.Information ? Visibility.Visible : Visibility.Collapsed);
                wnd.imgMessageIconWarn.Visibility = (MessageIcon == MessageBoxImage.Warning ? Visibility.Visible : Visibility.Collapsed);
                wnd.imgMessageIconError.Visibility = (MessageIcon == MessageBoxImage.Error ? Visibility.Visible : Visibility.Collapsed);
            }
            else
            {
                wnd.imgMessageIconQuestion.Visibility = Visibility.Collapsed;
                wnd.imgMessageIconInfo.Visibility = Visibility.Collapsed;
                wnd.imgMessageIconWarn.Visibility = Visibility.Collapsed;
                wnd.imgMessageIconError.Visibility = Visibility.Collapsed;
            }

            // 3. ActionButton
            if (PositiveAction != null && !string.IsNullOrEmpty(PositiveAction.Value.Key) && (PositiveAction.Value.Value != null))
            {

                wnd.btnOk.Tag = PositiveAction.Value.Value;
                wnd.btnOk.Content = PositiveAction.Value.Key;
                wnd.btnOk.Visibility = Visibility.Visible;
                wnd.btnOk.Tag = PositiveAction.Value.Value;

                if (NegativeAction != null && !string.IsNullOrEmpty(NegativeAction.Value.Key) && (NegativeAction.Value.Value != null))
                {

                    wnd.btnCancel.Tag = NegativeAction.Value.Value;
                    wnd.btnCancel.Content = NegativeAction.Value.Key;
                    wnd.btnCancel.Visibility = Visibility.Visible;
                    wnd.btnCancel.Tag = NegativeAction.Value.Value;

                }
                else
                {
                    wnd.btnCancel.Visibility = Visibility.Collapsed;
                }

                wnd.panelActionbuttons.Visibility = Visibility.Visible;

            }
            else
            {
                wnd.panelActionbuttons.Visibility = Visibility.Collapsed;
            }

            return wnd;
        }

        #endregion

        // ########################################################################################

        #region Dialog-Buttons

        private async void BtnOk_Click(object sender, RoutedEventArgs e)
        {

            // Fehler abfangen -- kann eigentlich nicht auftreten
            if (btnOk.Tag == null || btnOk.Tag.GetType() != typeof(Action)) { return; }

            btnOk.IsEnabled = false;
            await Task.Delay(100);

            ((Action)btnOk.Tag).Invoke();

            btnOk.IsEnabled = true;
            DialogResult = true;
            this.Close();
        }

        private async void BtnCancel_Click(object sender, RoutedEventArgs e)
        {

            // Fehler abfangen -- kann eigentlich nicht auftreten
            if (btnCancel.Tag == null || btnCancel.Tag.GetType() != typeof(Action)) { return; }

            btnCancel.IsEnabled = false;
            await Task.Delay(100);

            ((Action)btnCancel.Tag).Invoke();

            btnCancel.IsEnabled = true;
            DialogResult = false;
            this.Close();
        }

        #endregion

    }

}
