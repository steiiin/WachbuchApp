using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WachbuchApp
{

    public partial class ControlWindowTitlebar : UserControl
    {

        #region  DependencyProperty: Title

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(ControlWindowTitlebar), new PropertyMetadata(MainServiceHelper.GetString("MainWindow_Title")));

        #endregion
        #region  DependencyProperty: IsExitButtonVisible

        public bool IsExitButtonVisible
        {
            get { return (bool)GetValue(IsExitButtonVisibleProperty); }
            set { SetValue(IsExitButtonVisibleProperty, value); }
        }

        public static readonly DependencyProperty IsExitButtonVisibleProperty = DependencyProperty.Register("IsExitButtonVisible", typeof(bool), typeof(ControlWindowTitlebar), new PropertyMetadata(true));

        #endregion

        public event EventHandler? ExitButtonClick;

        public ControlWindowTitlebar()
        {
            InitializeComponent();
            DataContext = this;
        }

        #region Titlebar-CloseButton 

        private void BtnTitlebarClose_MouseEnter(object sender, MouseEventArgs e)
        {
            this.btnTitlebarClose.Fill = Brushes.Red;
        }
        private void BtnTitlebarClose_MouseLeave(object sender, MouseEventArgs e)
        {
            this.btnTitlebarClose.Fill = Brushes.Black;
        }
        private void BtnTitlebarClose_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.btnTitlebarClose.Fill = Brushes.DarkRed;
        }
        private void BtnTitlebarClose_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            this.btnTitlebarClose.Fill = Brushes.Red;
            ExitButtonClick?.Invoke(this, new());
        }

        #endregion

        private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window.GetWindow(this)?.DragMove();
        }

    }

}
