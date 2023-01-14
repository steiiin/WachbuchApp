using System.Threading.Tasks;
using System.Windows;

namespace WachbuchApp
{

    public partial class DialogInputBox : Window
    {

        private DialogInputBoxValidation inputValidation = DialogInputBoxValidation.NOT_EMPTY;

        #region Dialog-Start

        public DialogInputBox()
        {
            InitializeComponent();
        }

        internal static DialogInputBox GetInstance(Window owner, string Message = "", string TextLabel = "", DialogInputBoxValidation Validation = DialogInputBoxValidation.NOT_EMPTY)
        {

            DialogInputBox wnd = new();

            // Fenster-Variablen festlegen
            wnd.Owner = owner;
            wnd.inputValidation = Validation;

            // Fenster-Steuerelemente setzen
            if (!string.IsNullOrEmpty(Message)) { wnd.txtMessage.Text = Message; }
            if (!string.IsNullOrEmpty(TextLabel)) { wnd.labelInput.Text = TextLabel; }

            // Statische Fenster-Eigenschaften zurücksetzen
            SelectedInputText = "";

            return wnd;
        }

        #endregion
        #region Dialog-Result

        internal static string SelectedInputText = "";

        #endregion

        // ########################################################################################

        #region Dialog-Buttons

        private readonly System.ComponentModel.DataAnnotations.EmailAddressAttribute ValidatorEmail = new();

        private void DialogInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

            bool isValid = true;

            switch (inputValidation)
            {

                case DialogInputBoxValidation.NOT_EMPTY:
                    if (string.IsNullOrWhiteSpace(textInput.Text)) { isValid = false; }
                    break;

                case DialogInputBoxValidation.EMAIL:
                    if (!ValidatorEmail.IsValid(textInput.Text)) { isValid = false; }
                    break;

            }


            btnOk.IsEnabled = isValid;
        }

        // ########################################################################################

        private async void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            btnOk.IsEnabled = false;
            await Task.Delay(100);

            SelectedInputText = textInput.Text;

            btnOk.IsEnabled = true;
            DialogResult = true;
            this.Close();
        }

        private async void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            btnCancel.IsEnabled = false;
            await Task.Delay(100);

            btnCancel.IsEnabled = true;
            DialogResult = false;
            this.Close();
        }

        #endregion

    }

    // ########################################################################################

    internal enum DialogInputBoxValidation
    {
        NOT_EMPTY,
        EMAIL
    }

}
