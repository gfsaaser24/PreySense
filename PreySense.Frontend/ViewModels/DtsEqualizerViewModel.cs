using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Audio;

namespace PreySense.Frontend.ViewModels
{
    public partial class DtsEqualizerViewModel : ObservableObject
    {
        private readonly DtsAudioViewModel _owner;
        private bool _suppress;
        private DtsEqualizerSettings? _loaded;

        public DtsEqualizerViewModel(DtsAudioViewModel owner, DtsAudioEndpointViewModel endpoint)
        {
            _owner = owner;
            SelectedEndpoint = endpoint;
            for (int band = 0; band < DtsEqualizerSettings.BandCount; band++)
                Bands.Add(new DtsEqualizerBandViewModel(band));
            Load();
        }

        public event Action? CloseRequested;

        public ObservableCollection<DtsEqualizerBandViewModel> Bands { get; } = new();

        public DtsAudioEndpointViewModel SelectedEndpoint { get; }

        [ObservableProperty] private bool _canEdit;
        [ObservableProperty] private bool _enabled;

        public bool SlidersEnabled => CanEdit && Enabled;

        public void Load()
        {
            if (!_owner.TryGetEqualizer(SelectedEndpoint.Id, out DtsEqualizerSettings? settings, out _) || settings == null)
            {
                CanEdit = false;
                _loaded = null;
                return;
            }

            _loaded = settings;
            _suppress = true;
            CanEdit = settings.IsCustomActive;
            Enabled = settings.Enabled;
            for (int band = 0; band < Bands.Count; band++)
                Bands[band].Decibels = DtsEqualizerSettings.FromCom(settings.ComGains[band]);
            _suppress = false;
            OnPropertyChanged(nameof(SlidersEnabled));
        }

        partial void OnCanEditChanged(bool value) => OnPropertyChanged(nameof(SlidersEnabled));

        partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(SlidersEnabled));

        [RelayCommand]
        private void ApplySettings() => Commit(close: false);

        [RelayCommand]
        private void SaveSettings() => Commit(close: true);

        private void Commit(bool close)
        {
            if (_suppress || !CanEdit)
                return;

            string id = SelectedEndpoint.Id;
            if (!Write(id, out _)
                || !_owner.TryGetEqualizer(id, out DtsEqualizerSettings? saved, out _)
                || saved == null
                || !Matches(saved))
            {
                Restore(id, _loaded);
                Load();
                return;
            }

            _loaded = saved;
            if (close)
                CloseRequested?.Invoke();
        }

        private bool Write(string endpointId, out string? error)
        {
            if (!_owner.TrySetEqualizerEnabled(endpointId, Enabled, out error))
                return false;
            for (int band = 0; band < Bands.Count; band++)
            {
                int com = DtsEqualizerSettings.ToCom((int)Math.Round(Bands[band].Decibels));
                if (!_owner.TrySetEqualizerGain(endpointId, band, com, out error))
                    return false;
            }

            return true;
        }

        private void Restore(string endpointId, DtsEqualizerSettings? snapshot)
        {
            if (snapshot == null)
                return;
            _owner.TrySetEqualizerEnabled(endpointId, snapshot.Enabled, out _);
            for (int band = 0; band < snapshot.ComGains.Count; band++)
                _owner.TrySetEqualizerGain(endpointId, band, snapshot.ComGains[band], out _);
        }

        private bool Matches(DtsEqualizerSettings saved)
        {
            if (!saved.IsCustomActive || saved.Enabled != Enabled || saved.ComGains.Count != Bands.Count)
                return false;
            for (int band = 0; band < Bands.Count; band++)
            {
                int com = DtsEqualizerSettings.ToCom((int)Math.Round(Bands[band].Decibels));
                if (saved.ComGains[band] != com)
                    return false;
            }

            return true;
        }
    }

    public partial class DtsEqualizerBandViewModel : ObservableObject
    {
        public DtsEqualizerBandViewModel(int index)
        {
            Index = index;
            Label = DtsEqualizerSettings.Labels[index];
        }

        public int Index { get; }

        public string Label { get; }

        [ObservableProperty] private double _decibels;

        public string ValueText => $"{Decibels.ToString("+0;-0;0")} dB";

        partial void OnDecibelsChanged(double value) => OnPropertyChanged(nameof(ValueText));
    }
}
