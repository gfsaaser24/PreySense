using System.Windows;
using System.Windows.Input;

namespace PreySense.Frontend.Views
{
    public partial class GamesWindow : Window
    {
        public GamesWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
