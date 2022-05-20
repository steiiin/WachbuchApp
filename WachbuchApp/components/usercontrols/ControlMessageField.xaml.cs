using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WachbuchApp
{
    
    public partial class ControlMessageField : UserControl
    {

        #region Control-Start

        public ControlMessageField()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        #endregion
        #region Control-UI

        internal void SetMessageField(string MessageTitle = "", string MessageText = "", MessageBoxImage MessageIcon = MessageBoxImage.None, KeyValuePair<string, Action>? ActionButton = null)
        {

            // MessageIcon-Sichtbarkeit
            iconError.Visibility = (MessageIcon == MessageBoxImage.Error ? Visibility.Visible : Visibility.Collapsed);
            iconWarn.Visibility = (MessageIcon == MessageBoxImage.Warning ? Visibility.Visible : Visibility.Collapsed);
            iconInfo.Visibility = (MessageIcon == MessageBoxImage.Information ? Visibility.Visible : Visibility.Collapsed);
            iconQuestion.Visibility = (MessageIcon == MessageBoxImage.Question ? Visibility.Visible : Visibility.Collapsed);

            // MessageText setzen
            txtTitle.Text = MessageTitle;
            txtMessage.Text = MessageText;

            // ActionButton anzeigen, wenn verfügbar
            if (ActionButton == null)
            {
                btnAction.Visibility = Visibility.Collapsed;
            }
            else
            {
                btnAction.Content = ActionButton.Value.Key;
                btnAction.Tag = ActionButton.Value.Value;
                btnAction.Visibility = Visibility.Visible;
            }

        }

        // ########################################################################################

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {

            // Fehler abfangen -- kann eigentlich nicht auftreten
            if (btnAction.Tag == null || btnAction.Tag.GetType() != typeof(Action)) { return; }

            // Beim Einstellen wird im Tag-Feld des Buttons der Delegat gespeichert & jetzt aufgerufen
            btnAction.IsEnabled = false;
            ((Action)btnAction.Tag).Invoke();
            btnAction.IsEnabled = true;

        }

        #endregion

    }

}
