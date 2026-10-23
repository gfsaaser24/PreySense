using System.Windows;

namespace PreySense.Frontend.Views
{
    public partial class AdvancedWindow : Window
    {
        public AdvancedWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            SettingsScroll.MaxHeight = Math.Max(240, MaxHeight - 56);
            Loaded += (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                    viewModel.RefreshOpenCoreStatus();
            };
        }

    }
}
