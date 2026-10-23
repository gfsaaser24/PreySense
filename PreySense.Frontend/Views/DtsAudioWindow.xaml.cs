using System.Windows;

namespace PreySense.Frontend.Views
{
    public partial class DtsAudioWindow : Window
    {
        public DtsAudioWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                    viewModel.Dts.Refresh();
            };
        }

        private void CustomizeAudio_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel viewModel)
                return;
            if (sender is not FrameworkElement { DataContext: ViewModels.DtsAudioEndpointViewModel endpoint } || !endpoint.IsCustom)
                return;
            var window = new CustomAudioWindow(viewModel.Dts, endpoint)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void Equalizer_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel viewModel)
                return;
            if (sender is not FrameworkElement { DataContext: ViewModels.DtsAudioEndpointViewModel endpoint } || !endpoint.IsCustom)
                return;
            var window = new DtsEqualizerWindow(viewModel.Dts, endpoint)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void FactoryReset_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel viewModel)
                return;
            viewModel.Dts.RestoreAllFactory(out _);
        }
    }
}
