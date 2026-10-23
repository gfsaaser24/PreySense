using System.Windows;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views
{
    public partial class CustomAudioWindow : Window
    {
        public CustomAudioWindow(DtsAudioViewModel dts, DtsAudioEndpointViewModel? endpoint)
        {
            InitializeComponent();
            var viewModel = new DtsCustomAudioViewModel(dts, endpoint);
            viewModel.CloseRequested += Close;
            DataContext = viewModel;
        }
    }
}
