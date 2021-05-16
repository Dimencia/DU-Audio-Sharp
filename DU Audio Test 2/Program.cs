using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;

namespace DU_Audio_Test_2
{
    class Program
    {
        private static StreamWatcher logStream = null;
        private static string logpath = null;
        private static string mostRecentLog = null;
        static void Main(string[] args)
        {
            //var seatbelt = new CachedSound("Ripley Galactic_KICS 4_Audio_KICS_SealtBeltSign.mp3");
            //var problem = new CachedSound("Ripley Galactic_KICS 4_Audio_KICS_WeHaveALittleProblem.mp3");
            //var really = new CachedSound("Ripley Galactic_KICS 4_Audio_Requests_YourShouldReally.mp3");
            //
            //AudioPlaybackEngine.Instance.PlaySound(seatbelt, "sb");
            //AudioPlaybackEngine.Instance.QueueSound(problem, "prb");
            //AudioPlaybackEngine.Instance.QueueSound(really, "rly");
            //Thread.Sleep(1000);
            //AudioPlaybackEngine.Instance.StopSound("sb");
            //Console.Read();

            // Yep all that works
            // Alright next steps, find the DU logfile location
            // Start a routine that, say every second or so, tries to find the newest logfile and open a stream to it
            logpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"NQ\DualUniverse\log");

            UpdateLogFile();

            var logWatchTimer = new System.Timers.Timer();
            logWatchTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                UpdateLogFile();
            };
            logWatchTimer.Interval = 1000;
            logWatchTimer.Start();
            logWatchTimer.AutoReset = true;
            Console.Read();
        }

        private static void UpdateLogFile()
        {
            var files = Directory.GetFiles(logpath);
            if (files.Length > 0)
            {
                var mostRecent = files.OrderByDescending(f => File.GetLastWriteTime(f)).First();

                if (mostRecentLog != mostRecent)
                {
                    if (logStream != null)
                    {
                        logStream.stream.Dispose();
                    }
                    mostRecentLog = mostRecent;

                    var ls = new FileStream(mostRecent, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    ls.Seek(0, SeekOrigin.End);
                    logStream = new StreamWatcher(ls);
                    logStream.MessageAvailable += Watcher_MessageAvailable; // And register events to deal with new entries
                }
            }
        }

        private static Regex watcherReg = new Regex(@"<message>4176790050\|([^\r\n]*)");
        private static Dictionary<string, CachedSound> cachedFileMap = new Dictionary<string, CachedSound>();
        private static void Watcher_MessageAvailable(object sender, MessageAvailableEventArgs e)
        {
            // Look for the ID of a lua-generated message, which has '<message>4176790050|'

            // The event args just contain the message size.  We need to get the contents
            var watcher = sender as StreamWatcher;
            // Read the stream
            var buffer = new byte[e.MessageSize];
            watcher.stream.Read(buffer);
            var contents = System.Text.Encoding.ASCII.GetString(buffer);

            //Console.WriteLine(contents);

            try
            {
                var matches = watcherReg.Matches(contents);
                foreach (Match match in matches)
                {
                    var arguments = match.Groups[1].Value.Replace("&quot;","").Split("|");
                    string id;
                    switch (arguments[0])
                    {

                        case "playsound":
                            // Format: playsound|soundpackFolder|filename|ID (optional)
                            // Remove ../ and ..\ from the filename for security
                            arguments[2] = arguments[2].Replace("../", "").Replace("..\\", "");
                            if (File.Exists(arguments[2]))
                            {
                                if (!cachedFileMap.ContainsKey(arguments[2]))
                                {
                                    cachedFileMap[arguments[2]] = new CachedSound(arguments[2]);
                                }
                                if (arguments.Length > 3)
                                    id = arguments[3];
                                else
                                    id = Guid.NewGuid().ToString();
                                AudioPlaybackEngine.Instance.PlaySound(cachedFileMap[arguments[2]], id);
                            }
                            break;
                        case "qsound":
                            // Format: qsound|soundpackFolder|filename|ID (optional)
                            // Remove ../ and ..\ from the filename for security
                            arguments[2] = arguments[2].Replace("../", "").Replace("..\\", "");
                            if (File.Exists(arguments[2]))
                            {
                                if (!cachedFileMap.ContainsKey(arguments[2]))
                                {
                                    cachedFileMap[arguments[2]] = new CachedSound(arguments[2]);
                                }
                                if (arguments.Length > 3)
                                    id = arguments[3];
                                else
                                    id = Guid.NewGuid().ToString();
                                AudioPlaybackEngine.Instance.QueueSound(cachedFileMap[arguments[2]], id);
                            }
                            break;
                        case "stopsound":
                            AudioPlaybackEngine.Instance.StopSound(arguments[1]);
                            break;
                        case "pausesound":
                            // Format: pausesound|ID
                            // TODO - needs implementing, a collection of paused sounds
                            break;
                        case "resumesound":
                            // Format: resumesound|ID
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}\nSkipping message");
            }
        }
    }

    class AudioPlaybackEngine : IDisposable
    {
        private readonly IWavePlayer outputDevice;
        private readonly MixingSampleProvider mixer;

        public Dictionary<string, ISampleProvider> SampleMap { get; private set; } = new Dictionary<string, ISampleProvider>();
        private List<(CachedSound, string)> QueuedSounds = new List<(CachedSound, string)>();
        private bool disposedValue;

        public AudioPlaybackEngine(int sampleRate = 48000, int channelCount = 2)
        {
            outputDevice = new WaveOutEvent();
            mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount));
            mixer.ReadFully = true;
            outputDevice.Init(mixer);
            outputDevice.Play();
        }

        public void StopSound(string key)
        {
            if (SampleMap.ContainsKey(key)) // Stop inputs from anything with a matching key
                mixer.RemoveMixerInput(SampleMap[key]);
        }

        public ISampleProvider PlaySound(CachedSound sound, string key)
        {
            var sr = new CachedSoundSampleProvider(sound);
            if (SampleMap.ContainsKey(key)) // Stop inputs from anything with a matching key
                mixer.RemoveMixerInput(SampleMap[key]);
            SampleMap[key] = sr;
            AddMixerInput(sr);
            return sr;
        }

        public void QueueSound(CachedSound sound, string key)
        {
            // Begins playback when all other queued sounds are finished

            // Uhhh.  This is hard.
            // So obviously add it to QueuedSounds but, then what?

            // If QueuedSounds is empty, fire it immediately
            // As well as a timer to remove it from QueuedSounds after its duration, and to start the next
            QueuedSounds.Add((sound, key));
            if (QueuedSounds.Count == 1)
            {
                var fr = PlaySound(sound, key);
                System.Timers.Timer timer = new System.Timers.Timer();
                timer.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    QueuedSounds.RemoveAt(0);
                    if (QueuedSounds.Count > 0)
                    {
                        var queuedSound = QueuedSounds.First();
                        var fileReader = PlaySound(queuedSound.Item1, queuedSound.Item2);
                        timer.Interval = queuedSound.Item1.Length;
                    }
                    else
                        timer.Stop();
                };
                timer.Interval = sound.Length;
                timer.Start();
            }
        }

        private void AddMixerInput(ISampleProvider input)
        {
            mixer.AddMixerInput(input);
        }



        public static readonly AudioPlaybackEngine Instance = new AudioPlaybackEngine();

        // TODO: Verify if I did this dispose right
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    outputDevice.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    class CachedSound
    {
        public float[] AudioData { get; private set; }
        public WaveFormat WaveFormat { get; private set; }
        public double Length { get; private set; }
        public CachedSound(string audioFileName)
        {
            using (var audioFileReader = new AudioFileReader(audioFileName))
            {
                int outRate = 48000;
                var resampler = new WdlResamplingSampleProvider(audioFileReader, outRate);
                var source = resampler.ToStereo().ToWaveProvider16();
                Length = audioFileReader.TotalTime.TotalMilliseconds;
                using (var outputStream = new MemoryStream())
                {
                    WaveFormat = resampler.WaveFormat;
                    var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
                    var readBuffer = new float[source.WaveFormat.SampleRate * source.WaveFormat.Channels];
                    int samplesRead;
                    while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                    {
                        wholeFile.AddRange(readBuffer.Take(samplesRead));
                    }
                    AudioData = wholeFile.ToArray();
                }
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

    public delegate void MessageAvailableEventHandler(object sender,
    MessageAvailableEventArgs e);

    public class MessageAvailableEventArgs : EventArgs
    {
        public MessageAvailableEventArgs(int messageSize) : base()
        {
            this.MessageSize = messageSize;
        }

        public int MessageSize { get; private set; }
    }

    public class StreamWatcher
    {
        public Stream stream { get; private set; }

        private byte[] sizeBuffer = new byte[2];

        public StreamWatcher(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            this.stream = stream;
            WatchNext();
        }

        protected void OnMessageAvailable(MessageAvailableEventArgs e)
        {
            var handler = MessageAvailable;
            if (handler != null)
                handler(this, e);
        }

        protected void WatchNext()
        {
            stream.BeginRead(sizeBuffer, 0, 2, new AsyncCallback(ReadCallback),
                null);
        }

        private void ReadCallback(IAsyncResult ar)
        {
            int bytesRead = stream.EndRead(ar);
            if (bytesRead != 2)
            {
                WatchNext();
                return;
            }
            int messageSize = sizeBuffer[1] << 8 + sizeBuffer[0]; // wtf is this?  Thx internet... 
            // I am unsure if it's skipping the first 2 bytes, do I need to seek back?
            OnMessageAvailable(new MessageAvailableEventArgs(messageSize));
            WatchNext();
        }

        public event MessageAvailableEventHandler MessageAvailable;
    }
}
