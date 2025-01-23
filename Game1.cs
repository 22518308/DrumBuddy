using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NAudio.CoreAudioApi;
using NAudio.Midi;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;

namespace fireworks
{

    class AudioPlaybackEngine : IDisposable
    {
        private readonly IWavePlayer outputDevice;
        private readonly MixingSampleProvider mixer;

        public AudioPlaybackEngine(int sampleRate = 44100, int channelCount = 2)
        {
            try
            {
                outputDevice = new WasapiOut(AudioClientShareMode.Shared, true, 10); // 10 ms latency
                Console.WriteLine("Using WASAPI for audio output.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WASAPI initialization failed: {ex.Message}");
                Console.WriteLine("Falling back to WaveOutEvent...");
                outputDevice = new WaveOutEvent();
            }

            // Setup mixer
            mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount));
            mixer.ReadFully = true;
            outputDevice.Init(mixer);
            outputDevice.Play();
        }

        public void PlaySound(string fileName, float volume = 1.0f)
        {
            var input = new AudioFileReader(fileName);
            AddMixerInput(new AutoDisposeFileReader(input));
        }

        private ISampleProvider ConvertToRightChannelCount(ISampleProvider input)
        {
            if (input.WaveFormat.Channels == mixer.WaveFormat.Channels)
            {
                return input;
            }
            if (input.WaveFormat.Channels == 1 && mixer.WaveFormat.Channels == 2)
            {
                return new MonoToStereoSampleProvider(input);
            }
            throw new NotImplementedException("Not yet implemented this channel count conversion");
        }

        public void PlaySound(CachedSound sound, float volume)
        {
            var provider = new CachedSoundSampleProvider(sound);
            var volumeProvider = new VolumeSampleProvider(provider) { Volume = volume };
            AddMixerInput(volumeProvider);
        }

        private void AddMixerInput(ISampleProvider input)
        {
            mixer.AddMixerInput(ConvertToRightChannelCount(input));
        }

        public void Dispose()
        {
            outputDevice.Dispose();
        }

        public static readonly AudioPlaybackEngine Instance = new AudioPlaybackEngine(44100, 2);
    }

    class CachedSound
    {
        public float[] AudioData { get; private set; }
        public WaveFormat WaveFormat { get; private set; }
        public CachedSound(string audioFileName)
        {
            using (var audioFileReader = new AudioFileReader(audioFileName))
            {
                // TODO: could add resampling in here if required
                WaveFormat = audioFileReader.WaveFormat;
                var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
                var readBuffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
                int samplesRead;
                while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    wholeFile.AddRange(readBuffer.Take(samplesRead));
                }
                AudioData = wholeFile.ToArray();
            }
        }
    }

    class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound cachedSound;
        private long position;

        public CachedSoundSampleProvider(CachedSound cachedSound)
        {
            this.cachedSound = cachedSound;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var availableSamples = cachedSound.AudioData.Length - position;
            var samplesToCopy = Math.Min(availableSamples, count);
            Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
            position += samplesToCopy;
            return (int)samplesToCopy;
        }

        public WaveFormat WaveFormat { get { return cachedSound.WaveFormat; } }
    }

    class AutoDisposeFileReader : ISampleProvider
    {
        private readonly AudioFileReader reader;
        private bool isDisposed;
        public AutoDisposeFileReader(AudioFileReader reader)
        {
            this.reader = reader;
            this.WaveFormat = reader.WaveFormat;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (isDisposed)
                return 0;
            int read = reader.Read(buffer, offset, count);
            if (read == 0)
            {
                reader.Dispose();
                isDisposed = true;
            }
            return read;
        }

        public WaveFormat WaveFormat { get; private set; }
    }
    public class Game1 : Game
    {
        int x = 0;

        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;

        private Texture2D snareTexture, kickTexture, hiHatTexture, crashTexture;
        private Vector2 snarePosition = new Vector2(100, 100);
        private Vector2 kickPosition = new Vector2(200, 100);
        private Vector2 hiHatPosition = new Vector2(300, 100);
        private Vector2 crashPosition = new Vector2(400, 100);

        private static CachedSound AcousticSnare;
        private static CachedSound BassDrum1;
        private static CachedSound OpenHiHat;
        private static CachedSound CrashCymbal1;
        private static CachedSound RideCymbal1;
        private static CachedSound HiMidTom;
        private static CachedSound HighFloorTom;
        private static CachedSound LowTom;
        private static CachedSound PedalHiHat;
        private static CachedSound RideCymbal2;

        private MidiIn midiInputDevice;

        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
        }

        protected override void Initialize()
        {
            int deviceCount = MidiIn.NumberOfDevices;
            if (deviceCount > 0)
            {
                try
                {
                    midiInputDevice = new MidiIn(0);
                    midiInputDevice.MessageReceived += OnMidiMessageReceived;
                    midiInputDevice.Start();
                }
                catch (NAudio.MmException ex)
                {
                    Console.WriteLine($"Failed to open MIDI device: {ex.Message}");
                }
            }

            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);

            // Load textures for each instrument (replace with actual paths)
            snareTexture = Content.Load<Texture2D>("1 - Whole");
            //kickTexture = Content.Load<Texture2D>("kick");
            //hiHatTexture = Content.Load<Texture2D>("hihat");
            //crashTexture = Content.Load<Texture2D>("crash");

            // Load sounds
            AcousticSnare = new CachedSound("sounds\\SNARE.wav");
            BassDrum1 = new CachedSound("sounds\\KICK_DRUM.wav");
            OpenHiHat = new CachedSound("sounds\\HI_HAT_OPEN.wav");
            CrashCymbal1 = new CachedSound("sounds\\CYMBAL_CRASH.wav");
            RideCymbal1 = new CachedSound("sounds\\CYMBAL_RIDE.wav");
            HiMidTom = new CachedSound("sounds\\HI_TOM.wav");
            HighFloorTom = new CachedSound("sounds\\FLOOR_TOM.wav");
            LowTom = new CachedSound("sounds\\MID_TOM.wav");
            PedalHiHat = new CachedSound("sounds\\HI_HAT_PEDAL.wav");
            RideCymbal2 = new CachedSound("sounds\\CYMBAL_RIDE2.wav");
        }

        protected override void Update(GameTime gameTime)
        {
            var keyboardState = Keyboard.GetState();

            // Keyboard mappings for audio playback
            if (keyboardState.IsKeyDown(Keys.D1))
            {
                AudioPlaybackEngine.Instance.PlaySound(AcousticSnare, 1.0f);
                x = 1;
            }
            else if (keyboardState.IsKeyDown(Keys.D2))
            {
                AudioPlaybackEngine.Instance.PlaySound(BassDrum1, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D3))
            {
                AudioPlaybackEngine.Instance.PlaySound(OpenHiHat, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D4))
            {
                AudioPlaybackEngine.Instance.PlaySound(CrashCymbal1, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D5))
            {
                AudioPlaybackEngine.Instance.PlaySound(RideCymbal1, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D6))
            {
                AudioPlaybackEngine.Instance.PlaySound(HiMidTom, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D7))
            {
                AudioPlaybackEngine.Instance.PlaySound(HighFloorTom, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D8))
            {
                AudioPlaybackEngine.Instance.PlaySound(PedalHiHat, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D9))
            {
                AudioPlaybackEngine.Instance.PlaySound(LowTom, 1.0f);
            }
            else if (keyboardState.IsKeyDown(Keys.D0))
            {
                AudioPlaybackEngine.Instance.PlaySound(RideCymbal2, 1.0f);
            }



            base.Update(gameTime);
        }

        private void OnMidiMessageReceived(object sender, MidiInMessageEventArgs e)
        {
            if (e.MidiEvent is NoteOnEvent noteOnEvent && noteOnEvent.Velocity > 0)
            {
                var velocityFactor = noteOnEvent.Velocity / 127f;
                switch (noteOnEvent.NoteName)
                {
                    case "Acoustic Snare":
                        AudioPlaybackEngine.Instance.PlaySound(AcousticSnare, velocityFactor);
                        break;
                    case "Bass Drum 1":
                        AudioPlaybackEngine.Instance.PlaySound(BassDrum1, velocityFactor);
                        break;
                    case "Open Hi-Hat":
                        AudioPlaybackEngine.Instance.PlaySound(OpenHiHat, velocityFactor);
                        break;
                    case "Crash Cymbal 1":
                        AudioPlaybackEngine.Instance.PlaySound(CrashCymbal1, velocityFactor);
                        break;
                    case "Ride Cymbal 1":
                        AudioPlaybackEngine.Instance.PlaySound(RideCymbal1, velocityFactor);
                        break;
                    case "Hi-Mid Tom":
                        AudioPlaybackEngine.Instance.PlaySound(HiMidTom, velocityFactor);
                        break;
                    case "High Floor Tom":
                        AudioPlaybackEngine.Instance.PlaySound(HighFloorTom, velocityFactor);
                        break;
                    case "Low Tom":
                        AudioPlaybackEngine.Instance.PlaySound(LowTom, velocityFactor);
                        break;
                    case "Pedal Hi-Hat":
                        AudioPlaybackEngine.Instance.PlaySound(PedalHiHat, velocityFactor);
                        break;
                    case "Ride Cymbal 2":
                        AudioPlaybackEngine.Instance.PlaySound(RideCymbal2, velocityFactor);
                        break;
                }
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            spriteBatch.Begin();

            if (x == 1)
            {
                spriteBatch.Draw(snareTexture, snarePosition, Color.White);
            }

            // Draw textures on screen to represent each sound trigger point
            //spriteBatch.Draw(snareTexture, snarePosition, Color.White);
            //spriteBatch.Draw(kickTexture, kickPosition, Color.White);
            //spriteBatch.Draw(hiHatTexture, hiHatPosition, Color.White);
            //spriteBatch.Draw(crashTexture, crashPosition, Color.White);

            spriteBatch.End();

            base.Draw(gameTime);

            x = 0;
        }
        protected override void UnloadContent()
        {
            midiInputDevice?.Stop();
            midiInputDevice?.Dispose();
            AudioPlaybackEngine.Instance.Dispose();
        }

    }
}
