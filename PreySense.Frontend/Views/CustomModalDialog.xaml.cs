using System.Windows;
using System.Windows.Input;

namespace PreySense.Frontend.Views
{
    public partial class CustomModalDialog : Window
    {
        private bool? _result;

        public CustomModalDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the window modally and returns the choice recorded by its custom buttons.
        /// Keeping the result ourselves avoids assigning Window.DialogResult after WPF has
        /// already begun closing the borderless window.
        /// </summary>
        public new bool? ShowDialog()
        {
            _result = null;
            base.ShowDialog();
            return _result;
        }

        public string HeaderSubtitle
        {
            get => HeaderSubtitleText.Text;
            set => HeaderSubtitleText.Text = value;
        }

        public string Message
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        public string ConfirmText
        {
            get => ConfirmButton.Content?.ToString() ?? string.Empty;
            set => ConfirmButton.Content = value;
        }

        public string CancelText
        {
            get => CancelButton.Content?.ToString() ?? string.Empty;
            set => CancelButton.Content = value;
        }

        public bool ConfirmEnabled
        {
            get => ConfirmButton.IsEnabled;
            set => ConfirmButton.IsEnabled = value;
        }

        public bool ShowCancelButton
        {
            get => CancelButton.Visibility == Visibility.Visible;
            set
            {
                CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                ButtonsPanel.Columns = value ? 2 : 1;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }
    }
}
