using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DU_Audio_Test_2
{
    public class CachedSound
    {
        public float[] AudioData { get; private set; }
        public WaveFormat WaveFormat { get; private set; }
        public double Length { get; private set; }
        public CachedSound(string audioFileName)
        {
            using (var audioFileReader = new AudioFileReader(audioFileName))
            {
                int outRate = 44100;
                var resampler = new WdlResamplingSampleProvider(audioFileReader, outRate).ToStereo();

                Length = audioFileReader.TotalTime.TotalMilliseconds; // Get the total time of the resampled thing instead?  Should match now that resamping works

                WaveFormat = resampler.WaveFormat;
                var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
                var readBuffer = new float[resampler.WaveFormat.SampleRate * resampler.WaveFormat.Channels];
                int samplesRead;
                while ((samplesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    wholeFile.AddRange(readBuffer.Take(samplesRead));
                }
                AudioData = wholeFile.ToArray();

                //Length = AudioData.Length / (WaveFormat.SampleRate * 1.0 * WaveFormat.BitsPerSample / 8.0);

            }
        }
    }
}
