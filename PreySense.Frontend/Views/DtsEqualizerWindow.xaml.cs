using System.Windows;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views
{
    public partial class DtsEqualizerWindow : Window
    {
        public DtsEqualizerWindow(DtsAudioViewModel dts, DtsAudioEndpointViewModel endpoint)
        {
            InitializeComponent();
            var viewModel = new DtsEqualizerViewModel(dts, endpoint);
            viewModel.CloseRequested += Close;
            DataContext = viewModel;
        }
    }
}
