using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Generic;
using System.Text;

namespace DU_Audio_Test_2
{
    public enum QueueType
    {
        Instant, Queued, Notification
    }

    public class PendingSound
    {
        public CachedSound Sound { get; set; }
        public int Volume { get; set; } = 100;
        public string Key { get; set; }
        public QueueType QueueType { get; set; }
        public PendingSound(CachedSound sound, int volume, string key)
        {
            Sound = sound;
            Volume = volume;
            Key = key;
        }
    }

    public class ActiveSound : PendingSound
    {
        public VolumeSampleProvider Provider { get; set; }
        public PausableTimer DisposalTimer { get; set; }
        public PausableTimer NotificationTimer { get; set; }

        public ActiveSound(PendingSound p) : base(p.Sound, p.Volume, p.Key)
        {
            QueueType = p.QueueType;
        }
    }
}
