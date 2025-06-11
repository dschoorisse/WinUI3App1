using EDSDKLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace WinUI3App1
{
    #region Enums voor Camera Status
    // Deze enums vertalen de uint-codes van de SDK naar leesbare waarden.
    // De waarden komen overeen met de constanten in EDSDK.cs

    public enum AEMode : uint
    {
        Program = EDSDK.AEMode_Program,
        Tv = EDSDK.AEMode_Tv,
        Av = EDSDK.AEMode_Av,
        Manual = EDSDK.AEMode_Mamual,
        Bulb = EDSDK.AEMode_Bulb,
        A_DEP = EDSDK.AEMode_A_DEP,
        DEP = EDSDK.AEMode_DEP,
        Custom = EDSDK.AEMode_Custom,
        Lock = EDSDK.AEMode_Lock,
        Green = EDSDK.AEMode_Green,
        NightPortrait = EDSDK.AEMode_NigntPortrait,
        Sports = EDSDK.AEMode_Sports,
        Portrait = EDSDK.AEMode_Portrait,
        Landscape = EDSDK.AEMode_Landscape,
        Closeup = EDSDK.AEMode_Closeup,
        FlashOff = EDSDK.AEMode_FlashOff,
        CreativeAuto = EDSDK.AEMode_CreativeAuto,
        Movie = EDSDK.AEMode_Movie,
        PhotoInMovie = EDSDK.AEMode_PhotoInMovie,
        SceneIntelligentAuto = EDSDK.AEMode_SceneIntelligentAuto,
        SCN = EDSDK.AEMode_SCN,
        Unknown = EDSDK.AEMode_Unknown
    }

    public enum AFMode : uint
    {
        OneShot = 0,
        AIServo = 1,
        AIFocus = 2,
        Manual = 3,
        Unknown = 0xFFFFFFFF
    }
    #endregion

    // Om te voorkomen dat CameraService een gigantische klasse wordt met tientallen
    // losse properties, creëren we een speciale ViewModel of State klasse die de status
    // bijhoudt. Deze klasse kan dan INotifyPropertyChanged implementeren, wat essentieel is voor UI-binding.
    // Deze klasse implementeert INotifyPropertyChanged zodat de UI automatisch
    // kan updaten wanneer een waarde verandert.
    public class ObservableCameraState : INotifyPropertyChanged
    {
        // Connection & Basic State
        private bool _isCameraConnected;
        public bool IsCameraConnected { get => _isCameraConnected; set => SetProperty(ref _isCameraConnected, value); }

        private string _modelName;
        public string ModelName { get => _modelName; set => SetProperty(ref _modelName, value); }

        private int _batteryLevel;
        public int BatteryLevel { get => _batteryLevel; set => SetProperty(ref _batteryLevel, value); }

        // Shooting State
        private uint _availableShots;
        public uint AvailableShots { get => _availableShots; set => SetProperty(ref _availableShots, value); }

        private AEMode _aeMode;
        public AEMode AeMode { get => _aeMode; set => SetProperty(ref _aeMode, value); }

        private AFMode _afMode;
        public AFMode AfMode { get => _afMode; set => SetProperty(ref _afMode, value); }

        private string _isoSpeed;
        public string IsoSpeed { get => _isoSpeed; set => SetProperty(ref _isoSpeed, value); }

        private string _exposureCompensation;
        public string ExposureCompensation { get => _exposureCompensation; set => SetProperty(ref _exposureCompensation, value); }

        private string _imageQuality;
        public string ImageQuality { get => _imageQuality; set => SetProperty(ref _imageQuality, value); }

        private string _orientation;
        public string Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }

        // Lens & Focus
        private string _focalLength;
        public string FocalLength { get => _focalLength; set => SetProperty(ref _focalLength, value); }

        private bool _isFocused;
        public bool IsFocused { get => _isFocused; set => SetProperty(ref _isFocused, value); }

        // Flash State
        private bool _isFlashOn;
        public bool IsFlashOn { get => _isFlashOn; set => SetProperty(ref _isFlashOn, value); }

        private string _flashMode;
        public string FlashMode { get => _flashMode; set => SetProperty(ref _flashMode, value); }

        // Live View & Recording State
        private bool _isLiveViewActive;
        public bool IsLiveViewActive { get => _isLiveViewActive; set => SetProperty(ref _isLiveViewActive, value); }

        private bool _isRecordingMovie;
        public bool IsRecordingMovie { get => _isRecordingMovie; set => SetProperty(ref _isRecordingMovie, value); }


        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}