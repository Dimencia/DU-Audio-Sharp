using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;

namespace DU_Audio_Test_2
{

    class AudioPlaybackEngine : IDisposable
    {
        #region Properties
        public static readonly AudioPlaybackEngine Instance = new AudioPlaybackEngine();
        private readonly IWavePlayer outputDevice;
        private readonly MixingSampleProvider mixer;

        private bool disposedValue;
        private StreamWatcher logStream = null;
        private string mostRecentLog = null;

        private Timer QueueTimer = new Timer();
        private Timer LogWatchTimer = new Timer();
        private Timer NotificationTimer = new Timer();

        private List<PendingSound> QueuedSounds = new List<PendingSound>();
        private List<PendingSound> QueuedNotifications = new List<PendingSound>();

        private ConcurrentDictionary<string, ActiveSound> PausedSounds = new ConcurrentDictionary<string, ActiveSound>();
        private ConcurrentDictionary<string, ActiveSound> ActiveSounds = new ConcurrentDictionary<string, ActiveSound>();
        #endregion

        #region Setup
        public AudioPlaybackEngine(int sampleRate = 44100, int channelCount = 2)
        {
            outputDevice = new WaveOutEvent();
            mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount));
            mixer.ReadFully = true;
            outputDevice.Init(mixer);
            outputDevice.Play();

            QueueTimer.Elapsed += QueueElapsed;
            NotificationTimer.Elapsed += NotificationElapsed;

            var logpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"NQ\DualUniverse\log");


            LogWatchTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                UpdateLogFile(logpath);
            };
            LogWatchTimer.Interval = 1000;
            LogWatchTimer.Start();
            LogWatchTimer.AutoReset = true;
        }


        private void UpdateLogFile(string logpath)
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

        private static Dictionary<string, Action<string[]>> commandMap = new Dictionary<string, Action<string[]>>()
        {
            { "sound_play", Program.sound_play },
            { "sound_notification", Program.sound_notification },
            { "sound_q", Program.sound_q },
            { "sound_volume", Program.sound_volume },
            { "sound_pause", Program.sound_pause },
            { "sound_stop", Program.sound_stop },
            { "sound_resume", Program.sound_resume }
        };

        private Regex watcherReg = new Regex(@"<message>4176790050\|([^\r\n]*)");

        private void Watcher_MessageAvailable(object sender, MessageAvailableEventArgs e)
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
                    var arguments = match.Groups[1].Value.Replace("&quot;", "").Split("|");
                    string command = arguments[0];
                    if (commandMap.ContainsKey(command))
                    {
                        arguments = arguments.Skip(1).SkipLast(1).ToArray();
                        commandMap[command](arguments); // Execute the mapped command, giving it the remaining args
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}\nSkipping message");
            }
        }
        #endregion

        #region PlaySound Helpers
        public ActiveSound PlaySound(PendingSound sound) // The public version should StopSound, but the internal one doesn't
        {
            StopSound(sound.Key);
            return PlaySoundInternal(sound);
        }
        private ActiveSound PlaySoundInternal(PendingSound sound)
        {
            var sr = new VolumeSampleProvider(new CachedSoundSampleProvider(sound.Sound));
            sr.Volume = sound.Volume / 100f;

            // StopSound(sound.Key);


            // Add it to ActiveSounds, and setup a timer to remove it after a certain duration
            var t = new PausableTimer(sound.Sound.Length);
            t.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                if (ActiveSounds.ContainsKey(sound.Key)) // Stop inputs from anything with a matching key
                {
                    sound = ActiveSounds[sound.Key];
                    mixer.RemoveMixerInput(ActiveSounds[sound.Key].Provider);
                    ActiveSounds.Remove(sound.Key, out _);
                }
                else if (PausedSounds.ContainsKey(sound.Key))
                {
                    sound = PausedSounds[sound.Key];
                    PausedSounds.Remove(sound.Key, out _);
                }

                // Advance any timers if available
                if (sound is ActiveSound)
                {
                    var active = sound as ActiveSound;
                    active.Timer.Dispose();
                    active.Timer = null;
                    if (active.NotificationTimer != null)
                        active.NotificationTimer.Interval = 1;
                }
                
            };
            t.AutoReset = false;
            t.Start();


            var activeSound = new ActiveSound(sound) { Provider = sr, Timer = t };

            ActiveSounds[sound.Key] = activeSound;
            mixer.AddMixerInput(sr);
            return activeSound;
        }

        private ActiveSound ResumeAnySound(PendingSound sound, Timer timer)
        {
            if (sound is ActiveSound)
            {
                var activeSound = sound as ActiveSound;
                return ResumeActiveSound(activeSound, timer);
            }
            else
            {
                timer.Interval = sound.Sound.Length;
                timer.Enabled = true;
                Console.WriteLine("Playing sound internally for " + sound.Key);
                return PlaySoundInternal(sound);
            }
        }

        private ActiveSound ResumeActiveSound(ActiveSound activeSound, Timer timer)
        {
            mixer.AddMixerInput(activeSound.Provider);
            timer.Interval = activeSound.Timer.RemainingAfterPause;
            Console.WriteLine("Resuming with interval " + timer.Interval);
            timer.Start();
            ActiveSounds[activeSound.Key] = activeSound;
            activeSound.Timer.Resume();
            if (activeSound.QueueType == QueueType.Notification && !QueuedNotifications.Any(q => q.Key == activeSound.Key))
            {
                QueuedNotifications.Insert(0, activeSound);
            }
            else if (activeSound.QueueType == QueueType.Queued && !QueuedSounds.Any(q => q.Key == activeSound.Key))
            {
                QueuedSounds.Insert(0, activeSound);
            }
            return activeSound;
        }

        public void PlayNotification(PendingSound sound)
        {
            // Lower volume on all ActiveSounds
            // For posterity, track the originals so we can safely put it back
            Dictionary<ActiveSound, float> previousVolumes = new Dictionary<ActiveSound, float>();
            foreach (var s in ActiveSounds.Values)
            {
                previousVolumes[s] = s.Provider.Volume;
                s.Provider.Volume /= 3;
            }
            // Setup a timer to put the volume back afterward
            var t = new PausableTimer(sound.Sound.Length);
            t.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                foreach (var kvp in previousVolumes)
                    kvp.Key.Provider.Volume = kvp.Value;
                t.Dispose();
            };
            t.AutoReset = false;
            t.Start();

            var activeSound = ResumeAnySound(sound, NotificationTimer);
            activeSound.NotificationTimer = t;
        }
        #endregion

        #region Timer Elapsed Events
        private void QueueElapsed(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine($"Queue Elapsed - Removing: {QueuedSounds.FirstOrDefault()?.Key}");
            if (QueuedSounds.Count > 0)
            {
                // TODO: Figure out how to do a StopSound, without it interfering with paused sounds (sometimes getting here means it was paused)
                QueuedSounds.RemoveAt(0);
                //StopSound(QueuedSounds[0].Key);
            }
            Console.WriteLine($"Remaining:");
            foreach (var s in QueuedSounds)
                Console.WriteLine(s.Key);
            PlayQueuedSound();
        }

        private void PlayQueuedSound()
        {
            if (QueuedSounds.Count > 0)
            {
                var queuedSound = QueuedSounds.First();
                ResumeAnySound(queuedSound, QueueTimer);
            }
            else
                QueueTimer.Stop();
        }

        private void NotificationElapsed(object sender, ElapsedEventArgs e)
        {
            if (QueuedNotifications.Count > 0)
            {
                QueuedNotifications.RemoveAt(0);
                //StopSound(QueuedNotifications[0].Key);
            }
            if (QueuedNotifications.Count > 0)
            {
                var queuedSound = QueuedNotifications.First();
                PlayNotification(queuedSound);
            }
            else
            {
                NotificationTimer.Stop();
                // If there are not any active sounds that are Queued (with an active timer, though that's probably unnecessary)
                var activeQueued = ActiveSounds.Where(s => s.Value.QueueType == QueueType.Queued).Select(s => s.Value).FirstOrDefault();
                var remaining = activeQueued.Timer.GetRemainingTime();
                if (activeQueued == null || remaining <= 0)
                {
                    Console.WriteLine("Playing queued sound after notification elapsed");
                    PlayQueuedSound(); // If nothing is active, play the next sound (but don't invoke the elapsed, which removes the first one)
                }
                else
                {
                    Console.WriteLine("Waiting for current Queue sound to finish after finishing notification");
                    // Something is still active.  We need to resume the QueueTimer with the remaining duration of the active thing
                    QueueTimer.Interval = remaining;
                    QueueTimer.Start();
                }
            }
        }
        #endregion

        #region Accessible Functions

        public void StopSound(string key)
        {
            PendingSound sound = null;
            if (QueuedNotifications.Any(q => q.Key == key))
            {
                var queue = QueuedNotifications.Where(q => q.Key == key).First();
                sound = queue;
                QueuedNotifications.Remove(queue);
            }
            if (QueuedSounds.Any(q => q.Key == key))
            {
                var queue = QueuedSounds.Where(q => q.Key == key).First();
                sound = queue;
                Console.WriteLine("Removing queue for colliding key " + key);
                QueuedSounds.Remove(queue);
            }
            // Check if any ActiveSounds already have this key, and stop them

            if (ActiveSounds.ContainsKey(key)) // Stop inputs from anything with a matching key
            {
                sound = ActiveSounds[key];
                mixer.RemoveMixerInput(ActiveSounds[key].Provider);
            }
            else if (PausedSounds.ContainsKey(key))
            {
                sound = PausedSounds[key];
                PausedSounds.Remove(key, out _);
            }

            // Advance any timers if available
            if (sound is ActiveSound)
            {
                var active = sound as ActiveSound;
                if (active.Timer != null)
                {
                    active.Timer.Dispose(); // We already cleaned up, it would just call this
                    active.Timer = null;
                }
                if (active.NotificationTimer != null)
                    active.NotificationTimer.Interval = 1;
            }
        }

        // Pause is awkward because they could pause a sound that isn't playing yet.  That needs to be handled.  
        // Mostly, in ActiveSounds, each sound should have a Timer that we can pause, which removes itself from ActiveSounds
        // And then when we pause, we check both Queues.  If it's in them, remove it until we resume.  If it's at the front of them, immediately invoke their timer
        // This means we need to store the pausedQueues and pausedNotifications seperately
        public void PauseSound(string key)
        {
            if (ActiveSounds.ContainsKey(key))
            {
                var sound = ActiveSounds[key];
                mixer.RemoveMixerInput(sound.Provider);
                ActiveSounds.Remove(key, out _);

                PausedSounds[key] = sound;
                // Pause the timer for this key, if it has one
                if (sound.Timer != null)
                    sound.Timer.Pause();
                // Check if this is in either of the Queues.  If it is, remove it and trigger them
                if (sound.QueueType == QueueType.Queued && QueuedSounds.Any(s => s.Key == key))
                {
                    if (QueuedSounds.First().Key == key) // Trigger the queue if it's first
                        QueueElapsed(this, null); // There's a race condition here, if the timer triggered between reading the QueueType and now, this will trigger another sound...
                    else
                        QueuedSounds.Remove(QueuedSounds.Where(s => s.Key == key).First());

                }
                else if (sound.QueueType == QueueType.Notification && QueuedNotifications.Any(s => s.Key == key))
                {
                    if (QueuedNotifications.First().Key == key)
                        NotificationElapsed(this, null); // There's a race condition here, if the timer triggered between reading the QueueType and now, this will trigger another sound...
                    else
                        QueuedNotifications.Remove(QueuedNotifications.Where(s => s.Key == key).First());

                }

                if (sound.NotificationTimer != null)
                    sound.NotificationTimer.Interval = 1; // Clear the muting for the notification
            }
        }

        // Great, so, what happens if you resume a sound that was in a queue?
        // We can't realistically interrupt, not without 'pause'ing the one currently playing
        // But if they resumed, they probably want it soon.  So put it at the front of the queue
        public void ResumeSound(string key)
        {
            if (PausedSounds.ContainsKey(key))
            {
                var sound = PausedSounds[key];
                PausedSounds.Remove(key, out _);

                if (sound.QueueType == QueueType.Instant)
                {
                    mixer.AddMixerInput(PausedSounds[key].Provider);
                    ActiveSounds[key] = sound;
                    if (sound.Timer != null)
                        sound.Timer.Resume();
                }
                else if (sound.QueueType == QueueType.Queued)
                {
                    Console.WriteLine("Resuming queued sound " + key);
                    QueueSound(sound, true);
                }
                else if (sound.QueueType == QueueType.Notification)
                {
                    QueueNotification(sound, true);
                }
            }
        }

        public void SetVolume(string key, int volume)
        {
            if (ActiveSounds.ContainsKey(key))
                ActiveSounds[key].Provider.Volume = volume / 100f;
            else if (PausedSounds.ContainsKey(key))
                PausedSounds[key].Provider.Volume = volume / 100f;
        }





        public void QueueNotification(PendingSound sound, bool front = false)
        {
            sound.QueueType = QueueType.Notification;
            // Check if any QueuedNotifications already have this key, and remove them
            StopSound(sound.Key);

            // If any queued sounds are already active, insert it at 1 instead of 0
            if (front)// If any queued sounds are already active, insert it at 1 instead of 0
                if (ActiveSounds.Any(a => a.Value.QueueType == QueueType.Notification))
                    QueuedNotifications.Insert(1, sound);
                else
                    QueuedNotifications.Insert(0, sound);
            else
                QueuedNotifications.Add(sound);
            if (QueuedNotifications.Count == 1)
            {
                // Pause the QueueTimer, and when all of them are finished, unpause it and set the interval to 0
                // This is done in the NotificationTimer
                QueueTimer.Enabled = false;
                PlayNotification(sound);
            }
        }

        public void QueueSound(PendingSound sound, bool front = false)
        {
            sound.QueueType = QueueType.Queued;

            StopSound(sound.Key);


            // If any queued sounds are already active, insert it at 1 instead of 0
            if (front)
                if (ActiveSounds.Any(a => a.Value.QueueType == QueueType.Queued))
                    QueuedSounds.Insert(1, sound);
                else
                    QueuedSounds.Insert(0, sound);
            else
                QueuedSounds.Add(sound);
            if (QueuedSounds.Count == 1 && QueuedNotifications.Count == 0) // If it's the only sound and there are no notifications, play it now
            {
                ResumeAnySound(sound, QueueTimer);
            }
            // If there are other sounds, or notifications are going, the QueueTimer will trigger when finished and play it next
        }
        #endregion

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    outputDevice.Dispose();
                    LogWatchTimer.Dispose();
                    QueueTimer.Dispose();
                    NotificationTimer.Dispose();
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
        #endregion
    }
}
