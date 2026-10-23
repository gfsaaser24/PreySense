using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Audio;

namespace PreySense.Frontend.ViewModels
{
    public partial class DtsCustomAudioViewModel : ObservableObject
    {
        private readonly DtsAudioViewModel _owner;
        private bool _suppress;
        private DtsCustomAudioSettings? _loaded;

        public DtsCustomAudioViewModel(DtsAudioViewModel owner, DtsAudioEndpointViewModel? endpoint)
        {
            _owner = owner;
            SelectedEndpoint = endpoint != null && endpoint.IsCustom
                ? endpoint
                : owner.Endpoints.FirstOrDefault(item => item.IsCustom);
        }

        public event Action? CloseRequested;

        [ObservableProperty] private DtsAudioEndpointViewModel? _selectedEndpoint;
        [ObservableProperty] private bool _canEdit;
        [ObservableProperty] private bool _isFront;
        [ObservableProperty] private bool _isWide;
        [ObservableProperty] private bool _isTraditional;
        [ObservableProperty] private bool _trebleEnabled;
        [ObservableProperty] private double _trebleLevel;
        [ObservableProperty] private bool _bassEnabled;
        [ObservableProperty] private double _bassLevel;
        [ObservableProperty] private bool _dialogEnabled;
        [ObservableProperty] private double _dialogLevel;
        [ObservableProperty] private bool _volumeSmoothing;

        public bool TrebleSliderEnabled => CanEdit && TrebleEnabled;
        public bool BassSliderEnabled => CanEdit && BassEnabled;
        public bool DialogSliderEnabled => CanEdit && DialogEnabled;

        public void Load()
        {
            if (SelectedEndpoint == null)
            {
                CanEdit = false;
                return;
            }

            if (!_owner.TryGetCustom(SelectedEndpoint.Id, out DtsCustomAudioSettings? settings, out _) || settings == null)
            {
                CanEdit = false;
                _loaded = null;
                return;
            }

            _loaded = settings;
            _suppress = true;
            CanEdit = settings.IsCustomActive;
            IsFront = settings.Stereo == DtsStereoMode.Front;
            IsWide = settings.Stereo == DtsStereoMode.Wide;
            IsTraditional = settings.Stereo == DtsStereoMode.Traditional;
            TrebleEnabled = settings.TrebleEnabled;
            TrebleLevel = settings.TrebleLevel;
            BassEnabled = settings.BassEnabled;
            BassLevel = settings.BassLevel;
            DialogEnabled = settings.DialogEnabled;
            DialogLevel = settings.DialogLevel;
            VolumeSmoothing = settings.VolumeSmoothing;
            _suppress = false;
            NotifySliders();
        }

        partial void OnSelectedEndpointChanged(DtsAudioEndpointViewModel? value) => Load();

        partial void OnCanEditChanged(bool value) => NotifySliders();

        partial void OnTrebleEnabledChanged(bool value) => NotifySliders();

        partial void OnBassEnabledChanged(bool value) => NotifySliders();

        partial void OnDialogEnabledChanged(bool value) => NotifySliders();

        [RelayCommand]
        private void ApplySettings() => Commit(close: false);

        [RelayCommand]
        private void SaveSettings() => Commit(close: true);

        private void Commit(bool close)
        {
            if (_suppress || SelectedEndpoint == null || !CanEdit)
                return;

            DtsStereoMode? stereo = SelectedStereo();
            if (stereo == null)
                return;

            string id = SelectedEndpoint.Id;
            if (!Write(id, stereo.Value, out _)
                || !_owner.TryGetCustom(id, out DtsCustomAudioSettings? saved, out _)
                || saved == null
                || !Matches(saved, stereo.Value))
            {
                Restore(id, _loaded);
                Load();
                return;
            }

            _loaded = saved;
            if (close)
                CloseRequested?.Invoke();
        }

        private bool Write(string endpointId, DtsStereoMode stereo, out string? error)
        {
            if (!_owner.TrySetStereo(endpointId, stereo, out error))
                return false;
            if (!_owner.TrySetTrebleEnabled(endpointId, TrebleEnabled, out error))
                return false;
            if (!_owner.TrySetTrebleLevel(endpointId, Level(TrebleLevel), out error))
                return false;
            if (!_owner.TrySetBassEnabled(endpointId, BassEnabled, out error))
                return false;
            if (!_owner.TrySetBassLevel(endpointId, Level(BassLevel), out error))
                return false;
            if (!_owner.TrySetDialogEnabled(endpointId, DialogEnabled, out error))
                return false;
            if (!_owner.TrySetDialogLevel(endpointId, Level(DialogLevel), out error))
                return false;
            return _owner.TrySetVolumeSmoothing(endpointId, VolumeSmoothing, out error);
        }

        private void Restore(string endpointId, DtsCustomAudioSettings? snapshot)
        {
            if (snapshot == null)
                return;
            if (snapshot.Stereo is DtsStereoMode stereo)
                _owner.TrySetStereo(endpointId, stereo, out _);
            _owner.TrySetTrebleEnabled(endpointId, snapshot.TrebleEnabled, out _);
            _owner.TrySetTrebleLevel(endpointId, snapshot.TrebleLevel, out _);
            _owner.TrySetBassEnabled(endpointId, snapshot.BassEnabled, out _);
            _owner.TrySetBassLevel(endpointId, snapshot.BassLevel, out _);
            _owner.TrySetDialogEnabled(endpointId, snapshot.DialogEnabled, out _);
            _owner.TrySetDialogLevel(endpointId, snapshot.DialogLevel, out _);
            _owner.TrySetVolumeSmoothing(endpointId, snapshot.VolumeSmoothing, out _);
        }

        private bool Matches(DtsCustomAudioSettings saved, DtsStereoMode stereo)
        {
            return saved.IsCustomActive
                && saved.Stereo == stereo
                && saved.TrebleEnabled == TrebleEnabled
                && saved.TrebleLevel == Level(TrebleLevel)
                && saved.BassEnabled == BassEnabled
                && saved.BassLevel == Level(BassLevel)
                && saved.DialogEnabled == DialogEnabled
                && saved.DialogLevel == Level(DialogLevel)
                && saved.VolumeSmoothing == VolumeSmoothing;
        }

        private DtsStereoMode? SelectedStereo()
        {
            if (IsFront)
                return DtsStereoMode.Front;
            if (IsWide)
                return DtsStereoMode.Wide;
            if (IsTraditional)
                return DtsStereoMode.Traditional;
            return null;
        }

        private static int Level(double value) => (int)Math.Round(value);

        private void NotifySliders()
        {
            OnPropertyChanged(nameof(TrebleSliderEnabled));
            OnPropertyChanged(nameof(BassSliderEnabled));
            OnPropertyChanged(nameof(DialogSliderEnabled));
        }
    }
}
