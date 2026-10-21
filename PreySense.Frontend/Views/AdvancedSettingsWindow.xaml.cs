using System.Windows;
using System.Windows.Input;

namespace PreySense.Frontend.Views
{
    public partial class AdvancedSettingsWindow : Window
    {
        public AdvancedSettingsWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height - 40;
                SettingsScroll.MaxHeight = Math.Max(240, MaxHeight - 56);
            Loaded += (s, e) =>
            {
                (DataContext as ViewModels.MainViewModel)?.RefreshOpenCoreStatus();
            };
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
