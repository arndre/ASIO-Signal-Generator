using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AsioSignalGenerator.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AsioSignalGenerator
{
    public partial class MainWindow : Window
    {
        // Common candidate rates we probe against the selected driver. NAudio's AsioOut exposes
        // IsSampleRateSupported(), which queries the driver directly - that's the real source of
        // truth (unlike bit depth, which NAudio always abstracts away as 32-bit float).
        private static readonly int[] CandidateSampleRates = { 44100, 48000, 88200, 96000, 176400, 192000 };
        private const int PreferredDefaultSampleRate = 48000;

        private AsioOut? asioOut;
        private VolumeSampleProvider? volumeProvider;
        private ChannelRoutingSampleProvider? routingProvider;
        private IFrequencyControl? activeFrequencyControl;

        private readonly System.Collections.Generic.List<CheckBox> channelCheckBoxes = new();
        private readonly ObservableCollection<string> logEntries = new();

        public MainWindow()
        {
            InitializeComponent();
            LogListBox.ItemsSource = logEntries;
            LoadDrivers();
        }

        // ---------------------------------------------------------------
        // Device enumeration / selection
        // ---------------------------------------------------------------

        private void LoadDrivers()
        {
            try
            {
                var drivers = AsioOut.GetDriverNames();
                DeviceComboBox.ItemsSource = drivers;

                if (drivers.Length > 0)
                {
                    DeviceComboBox.SelectedIndex = 0;
                    Log($"Found {drivers.Length} ASIO driver(s).");
                }
                else
                {
                    Log("No ASIO drivers found on this system. Install an ASIO driver (e.g. ASIO4ALL) or use your interface's native driver.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error enumerating ASIO drivers: {ex.Message}");
            }
        }

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlaying)
            {
                Log("Stop playback before refreshing devices.");
                return;
            }
            TeardownAsio();
            LoadDrivers();
        }

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceComboBox.SelectedItem is not string driverName)
                return;

            if (IsPlaying)
            {
                Log("Stop playback before switching ASIO devices.");
                return;
            }

            TeardownAsio();

            try
            {
                asioOut = new AsioOut(driverName);
                asioOut.PlaybackStopped += AsioOut_PlaybackStopped;

                int outputChannels = asioOut.DriverOutputChannelCount;
                PopulateChannels(outputChannels);
                PopulateSampleRates();

                Log($"Loaded driver '{driverName}' — {outputChannels} output channel(s), {asioOut.DriverInputChannelCount} input channel(s).");
            }
            catch (Exception ex)
            {
                Log($"Failed to load driver '{driverName}': {ex.Message}");
                asioOut = null;
            }
        }

        private void PopulateChannels(int outputChannelCount)
        {
            ChannelsPanel.Children.Clear();
            channelCheckBoxes.Clear();

            for (int i = 0; i < outputChannelCount; i++)
            {
                var checkBox = new CheckBox
                {
                    Content = $"Output {i + 1}",
                    Tag = i,
                    IsChecked = i < 2 // default: first stereo pair selected
                };
                checkBox.Checked += ChannelCheckBox_Toggled;
                checkBox.Unchecked += ChannelCheckBox_Toggled;

                channelCheckBoxes.Add(checkBox);
                ChannelsPanel.Children.Add(checkBox);
            }

            ChannelsHintText.Text = outputChannelCount == 0
                ? "This driver reports zero output channels."
                : $"{outputChannelCount} output channel(s) available. Select one or more.";
        }

        private void ChannelCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Tag is not int index)
                return;

            // Live update: if currently playing, change routing immediately without restarting.
            if (routingProvider != null && index < routingProvider.ActiveChannels.Length)
            {
                routingProvider.ActiveChannels[index] = checkBox.IsChecked == true;
                if (IsPlaying)
                    Log($"Output {index + 1} {(checkBox.IsChecked == true ? "enabled" : "disabled")} (live).");
            }
        }

        /// <summary>
        /// Probes the currently loaded driver against a list of standard sample rates and
        /// builds the Sample Rate combo box, greying out / disabling any rate the driver
        /// reports as unsupported. Defaults to 48 kHz if supported, otherwise the first
        /// supported rate found.
        /// </summary>
        private void PopulateSampleRates()
        {
            SampleRateComboBox.Items.Clear();

            if (asioOut == null)
                return;

            var mutedBrush = (Brush)(TryFindResource("MutedTextBrush") ?? Brushes.Gray);
            var textBrush = (Brush)(TryFindResource("TextBrush") ?? Brushes.White);

            var supportedRates = new System.Collections.Generic.List<int>();
            ComboBoxItem? defaultItem = null;

            foreach (int rate in CandidateSampleRates)
            {
                bool supported;
                try
                {
                    supported = asioOut.IsSampleRateSupported(rate);
                }
                catch
                {
                    // Some drivers throw rather than return false for unsupported rates.
                    supported = false;
                }

                var item = new ComboBoxItem
                {
                    Tag = rate,
                    Content = supported ? $"{rate:N0} Hz" : $"{rate:N0} Hz  (not supported)",
                    IsEnabled = supported,
                    Foreground = supported ? textBrush : mutedBrush,
                    FontWeight = supported ? FontWeights.SemiBold : FontWeights.Normal
                };

                if (supported)
                {
                    supportedRates.Add(rate);
                    if (rate == PreferredDefaultSampleRate || defaultItem == null)
                        defaultItem = item;
                }

                SampleRateComboBox.Items.Add(item);
            }

            if (defaultItem != null)
            {
                SampleRateComboBox.SelectedItem = defaultItem;
            }
            else if (SampleRateComboBox.Items.Count > 0)
            {
                // Nothing came back as supported (unusual) - fall back to the first entry so
                // the UI isn't left with no selection at all.
                SampleRateComboBox.SelectedIndex = 0;
                Log("Warning: driver did not report any of the standard sample rates as supported. Check the ASIO Control Panel.");
            }

            Log(supportedRates.Count > 0
                ? $"Supported sample rates: {string.Join(", ", supportedRates.ConvertAll(r => r.ToString("N0")))} Hz."
                : "Could not determine supported sample rates from this driver.");
        }

        private int SelectedSampleRate =>
            SampleRateComboBox.SelectedItem is ComboBoxItem item && item.Tag is int rate ? rate : PreferredDefaultSampleRate;

        private void SampleRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SampleRateComboBox.SelectedItem is ComboBoxItem item && item.Tag is int rate && !IsPlaying)
            {
                Log($"Sample rate set to {rate:N0} Hz.");
            }
        }

        private void ControlPanelButton_Click(object sender, RoutedEventArgs e)
        {
            if (asioOut == null)
            {
                Log("Select an ASIO device first.");
                return;
            }

            try
            {
                asioOut.ShowControlPanel();
                Log("Opened ASIO driver control panel.");
            }
            catch (Exception ex)
            {
                Log($"Could not open control panel: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Waveform / frequency / volume controls
        // ---------------------------------------------------------------

        private void WaveformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasFrequency = SelectedWaveform is WaveformType.Sine or WaveformType.Square or WaveformType.Triangle or WaveformType.Sawtooth;
            if (FrequencySlider != null) FrequencySlider.IsEnabled = hasFrequency;
            if (FrequencyTextBox != null) FrequencyTextBox.IsEnabled = hasFrequency;

            if (IsPlaying)
                Log($"Waveform changed to {SelectedWaveform}. Stop and Start again to apply.");
        }

        private WaveformType SelectedWaveform
        {
            get
            {
                if (WaveformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
                    Enum.TryParse<WaveformType>(tag, out var type))
                {
                    return type;
                }
                return WaveformType.Sine;
            }
        }

        private void FrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FrequencyValueText == null) return;
            FrequencyValueText.Text = $"{e.NewValue:0} Hz";
            if (FrequencyTextBox != null) FrequencyTextBox.Text = $"{e.NewValue:0}";

            if (activeFrequencyControl != null)
                activeFrequencyControl.Frequency = e.NewValue;
        }

        private void FrequencyApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(FrequencyTextBox.Text, out double freq))
            {
                freq = Math.Clamp(freq, FrequencySlider.Minimum, FrequencySlider.Maximum);
                FrequencySlider.Value = freq; // triggers ValueChanged above
            }
            else
            {
                Log("Invalid frequency value.");
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeValueText == null) return;
            VolumeValueText.Text = $"{e.NewValue:0.0} dB";

            if (volumeProvider != null)
                volumeProvider.Volume = DbToLinear(e.NewValue);
        }

        private static float DbToLinear(double db) => (float)Math.Pow(10.0, db / 20.0);

        // ---------------------------------------------------------------
        // Transport
        // ---------------------------------------------------------------

        private bool IsPlaying => asioOut != null && asioOut.PlaybackState == PlaybackState.Playing;

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (asioOut == null)
            {
                Log("Select an ASIO device first.");
                return;
            }

            if (IsPlaying)
            {
                Log("Already playing.");
                return;
            }

            if (channelCheckBoxes.Count == 0)
            {
                Log("This driver has no output channels to play to.");
                return;
            }

            if (!channelCheckBoxes.Any(c => c.IsChecked == true))
            {
                Log("Select at least one output channel.");
                return;
            }

            try
            {
                int sampleRate = SelectedSampleRate;
                double frequency = FrequencySlider.Value;
                var generator = CreateGenerator(SelectedWaveform, sampleRate, frequency);
                activeFrequencyControl = generator as IFrequencyControl;

                volumeProvider = new VolumeSampleProvider(generator)
                {
                    Volume = DbToLinear(VolumeSlider.Value)
                };

                bool[] active = channelCheckBoxes.Select(c => c.IsChecked == true).ToArray();
                routingProvider = new ChannelRoutingSampleProvider(volumeProvider, asioOut.DriverOutputChannelCount, active);

                var waveProvider = new FloatSampleToWaveProvider(routingProvider);

                asioOut.Init(waveProvider);
                asioOut.Play();

                var channelList = string.Join(", ", active
                    .Select((isActive, idx) => (isActive, idx))
                    .Where(t => t.isActive)
                    .Select(t => (t.idx + 1).ToString()));

                Log($"Started: {SelectedWaveform} @ {frequency:0.##} Hz, {VolumeSlider.Value:0.0} dB, channel(s): {channelList} @ {sampleRate:N0} Hz / 32-bit float.");
                SetPlayingUI(true);
            }
            catch (Exception ex)
            {
                Log($"Failed to start playback: {ex.Message}");
                SetPlayingUI(false);
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            string? driverName = DeviceComboBox.SelectedItem as string;

            try
            {
                asioOut?.Stop();
                Log("Playback stopped.");
            }
            catch (Exception ex)
            {
                Log($"Error while stopping: {ex.Message}");
            }
            finally
            {
                SetPlayingUI(false);
            }

            // Fully release and then re-open the driver so it's left in a clean stopped state.
            if (driverName != null)
            {
                try
                {
                    // Dispose current instance (TeardownAsio already handles this cleanly).
                    TeardownAsio();

                    // Small delay to give the ASIO driver time to release underlying resources.
                    await Task.Delay(200);

                    // Re-create the AsioOut for the same driver so the UI remains populated and driver is ready.
                    asioOut = new AsioOut(driverName);
                    asioOut.PlaybackStopped += AsioOut_PlaybackStopped;

                    int outputChannels = asioOut.DriverOutputChannelCount;
                    PopulateChannels(outputChannels);
                    PopulateSampleRates();

                    Log($"Reinitialised driver '{driverName}' after stopping playback.");
                }
                catch (Exception ex)
                {
                    Log($"Failed to reinitialise ASIO driver after stop: {ex.Message}");
                    asioOut = null;
                }
            }
        }

        private void AsioOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Exception != null)
                    Log($"Playback stopped due to an error: {e.Exception.Message}");
                SetPlayingUI(false);
            });
        }

        private static ISampleProvider CreateGenerator(WaveformType type, int sampleRate, double frequency)
        {
            return type switch
            {
                WaveformType.Sine => new SineGenerator(sampleRate) { Frequency = frequency },
                WaveformType.Square => new SquareGenerator(sampleRate) { Frequency = frequency },
                WaveformType.Triangle => new TriangleGenerator(sampleRate) { Frequency = frequency },
                WaveformType.Sawtooth => new SawtoothGenerator(sampleRate) { Frequency = frequency },
                WaveformType.WhiteNoise => new WhiteNoiseGenerator(sampleRate),
                WaveformType.PinkNoise => new PinkNoiseGenerator(sampleRate),
                _ => new SineGenerator(sampleRate) { Frequency = frequency }
            };
        }

        private void SetPlayingUI(bool playing)
        {
            StartButton.IsEnabled = !playing;
            StopButton.IsEnabled = playing;
            DeviceComboBox.IsEnabled = !playing;
            SampleRateComboBox.IsEnabled = !playing;

            StatusText.Text = playing ? "Playing" : "Stopped";
            StatusDot.Fill = playing
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xE0, 0x8A))
                : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x6A));
        }

        // ---------------------------------------------------------------
        // Logging
        // ---------------------------------------------------------------

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            logEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

            const int maxEntries = 500;
            while (logEntries.Count > maxEntries)
                logEntries.RemoveAt(0);

            if (logEntries.Count > 0)
                LogListBox.ScrollIntoView(logEntries[^1]);
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            logEntries.Clear();
        }

        // ---------------------------------------------------------------
        // Cleanup
        // ---------------------------------------------------------------

        private void TeardownAsio()
        {
            if (asioOut == null) return;

            try
            {
                if (asioOut.PlaybackState == PlaybackState.Playing)
                    asioOut.Stop();
            }
            catch { /* ignore */ }

            asioOut.PlaybackStopped -= AsioOut_PlaybackStopped;
            asioOut.Dispose();
            asioOut = null;

            volumeProvider = null;
            routingProvider = null;
            activeFrequencyControl = null;

            SampleRateComboBox.Items.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            TeardownAsio();
            base.OnClosed(e);
        }
    }
}
