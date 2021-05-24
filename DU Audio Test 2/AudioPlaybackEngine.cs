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

        // Checks the DU filepath for the most recent logfile, and redirects our StreamWatcher to it if necessary
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
        // A map of commands we see from the logfiles vs the methods that handle them
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
        // A regex to match the contents of any lua-sent logfile message in Group 1
        private Regex watcherReg = new Regex(@"<message>([^<]*)");

        private void Watcher_MessageAvailable(object sender, MessageAvailableEventArgs e)
        {
            // The event args just contain the message size.  We need to get the contents
            var watcher = sender as StreamWatcher;
            // Read the stream into contents
            var buffer = new byte[e.MessageSize];
            watcher.stream.Read(buffer);
            var contents = System.Text.Encoding.ASCII.GetString(buffer);

            //Console.WriteLine(contents);

            try
            {
                // Look for lua commands
                var matches = watcherReg.Matches(contents);
                foreach (Match match in matches)
                {
                    // Parse out some garbage characters and split on our delimiter
                    var arguments = match.Groups[1].Value.Replace("&quot;", "").Split("|");
                    string command = arguments[0];
                    if (commandMap.ContainsKey(command))
                    {
                        arguments = arguments.Skip(1).SkipLast(1).ToArray(); // The first and last entry are garbage (first is the command, last is empty, it ends with |)
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
        // The public version will StopSound, but the internal one doesn't
        // Internally we often need to play one without stopping it (which removes it from all our collections)
        // But for the end-user we want to cancel all sounds with the same ID, when they start a new sound with that ID
        public ActiveSound PlaySound(PendingSound sound) 
        {
            StopSound(sound.Key);
            return PlaySoundInternal(sound);
        }

        private ActiveSound PlaySoundInternal(PendingSound sound)
        {
            var sr = new VolumeSampleProvider(new CachedSoundSampleProvider(sound.Sound));
            sr.Volume = sound.Volume / 100f;
            // Volume 0-100 seemed more understandable to end-users
            
            // Setup a timer to dispose of the sound after playback, removing it from all relevant collections
            var t = new PausableTimer(sound.Sound.Length);
            t.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                // We remove it from Active or Paused sounds, but we don't touch QueuedSounds or QueuedNotifications
                // The timers for those will remove them at the same time as we're doing this (race condition)
                if (ActiveSounds.ContainsKey(sound.Key)) // Stop inputs from anything with a matching key
                {
                    if (ActiveSounds.Remove(sound.Key, out var activeSound))
                    {
                        sound = activeSound;
                        // Directly stop it from mixing (immediate stop)
                        mixer.RemoveMixerInput(activeSound.Provider);
                    }
                    
                }
                else if (PausedSounds.ContainsKey(sound.Key))
                {
                    if(PausedSounds.Remove(sound.Key, out var activeSound))
                        sound = activeSound;
                }

                // Handle timers
                if (sound is ActiveSound)
                {
                    var active = sound as ActiveSound;
                    active.DisposalTimer.Dispose(); // This should be the current timer that's triggering
                    active.DisposalTimer = null;

                    // This feels weird here, but we can't StopSound because it would interfere with the Queues
                    // All the collections are handled by their own timers, this should maybe go there instead
                    if (active.NotificationTimer != null) // NotificationTimer's purpose is to restore volume levels to normal after playback
                        active.NotificationTimer.Interval = 1; // Advance it immediately
                }
            };
            t.AutoReset = false;
            t.Start();


            var activeSound = new ActiveSound(sound) { Provider = sr, DisposalTimer = t };

            ActiveSounds[sound.Key] = activeSound;
            mixer.AddMixerInput(sr);
            return activeSound;
        }

        // Generically play or resume an Active or Pending sound, setting up the given timer to trigger when it ends
        private ActiveSound ResumeAnySound(PendingSound sound, Timer queueTimer)
        {
            if (sound is ActiveSound)
            {
                var activeSound = sound as ActiveSound;
                return ResumeActiveSound(activeSound, queueTimer);
            }
            else
            {
                queueTimer.Interval = sound.Sound.Length;
                queueTimer.Enabled = true;
                Console.WriteLine("Playing sound internally for " + sound.Key);
                return PlaySoundInternal(sound);
            }
        }

        // Resume an ActiveSound that was previously paused, setting up the timer to trigger when it ends
        private ActiveSound ResumeActiveSound(ActiveSound activeSound, Timer queueTimer)
        {
            mixer.AddMixerInput(activeSound.Provider);
            // Get the remaining time from the Pausable dispose timer, and setup the given timer
            queueTimer.Interval = activeSound.DisposalTimer.RemainingAfterPause;
            Console.WriteLine("Resuming with interval " + queueTimer.Interval);
            queueTimer.Start();
            // Re-add it to Active
            ActiveSounds[activeSound.Key] = activeSound;
            // Resume its dispose timer
            activeSound.DisposalTimer.Resume();
            
            // Previously: If it's not already in QueuedNotifications and is a Notification type, put it in there
            // This apparently worked for my test cases but.  That's not right.

            // If it's a notification, always add it.  But add it at index 1 if another notification is already playing
            // Because the next Queue trigger will remove the 0th index
            // I think this is already handled by other methods, which is why I made this if - it probably never triggered
            // Going to comment it out and test later

            //if (activeSound.QueueType == QueueType.Notification && !QueuedNotifications.Any(q => q.Key == activeSound.Key))
            //{
            //    QueuedNotifications.Insert(0, activeSound);
            //}
            //else if (activeSound.QueueType == QueueType.Queued && !QueuedSounds.Any(q => q.Key == activeSound.Key))
            //{
            //    QueuedSounds.Insert(0, activeSound);
            //}
            return activeSound;
        }

        // Plays a sound as a Notification - reduces volume on all other sounds until playback is done
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
            // Play or resume the sound, and set the NotificationTimer to elapse when it ends
            var activeSound = ResumeAnySound(sound, NotificationTimer);
            activeSound.NotificationTimer = t;
        }
        #endregion

        #region Timer Elapsed Events
        private void QueueElapsed(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine($"Queue Elapsed - Removing: {QueuedSounds.FirstOrDefault()?.Key}");
            if (QueuedSounds.Count > 0) // Remove the QueuedSound that just finished playing
                QueuedSounds.RemoveAt(0);
            Console.WriteLine($"Remaining:");
            foreach (var s in QueuedSounds)
                Console.WriteLine(s.Key);
            // Play the next QueuedSound
            PlayQueuedSound();
        }

        // Plays the next queued sound (without removing)
        private void PlayQueuedSound()
        {
            if (QueuedSounds.Count > 0)
            {
                var queuedSound = QueuedSounds.First();
                // Play or resume the sound, and set the QueueTimer to elapse when it ends
                ResumeAnySound(queuedSound, QueueTimer);
            }
            else
                QueueTimer.Stop();
        }

        private void NotificationElapsed(object sender, ElapsedEventArgs e)
        {
            if (QueuedNotifications.Count > 0) // Remove the notification that just finished
                QueuedNotifications.RemoveAt(0);
            if (QueuedNotifications.Count > 0)
            {
                var queuedSound = QueuedNotifications.First();
                // Play/resume the next notification
                PlayNotification(queuedSound);
            }
            else
            {
                // If there are no more notifications, we can stop the timer
                NotificationTimer.Stop();
                // Check if any QueuedSounds are Active
                var activeQueued = ActiveSounds.Where(s => s.Value.QueueType == QueueType.Queued)?.Select(s => s.Value)?.FirstOrDefault();
                double remaining = 0;
                // Get remaining duration early, because it can sometimes be 0 or negative if there was a lot of lag or whatever
                if (activeQueued != null && activeQueued.DisposalTimer != null)
                    remaining = activeQueued.DisposalTimer.GetRemainingTime();
                // If there is no active QueuedSound, or there is but it has 0 or less remaining time to trigger, trigger it
                // Note that, when we started a Notification, we disabled the QueueTimer, but did not stop any current messages from the Queue
                // This is just to handle if a message from the Queue is still playing
                if (activeQueued == null || remaining <= 0)
                {
                    Console.WriteLine("Playing queued sound after notification elapsed");
                    PlayQueuedSound(); // Play the next QueuedSound (if there is one exists) and setup the QueueTimer to trigger when it ends, to remove it
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
        // Remove sounds matching the key from all collections and dispose/advance their timers
        // Relies on uniqueness of key across collections; whenever we start any sound, we stop/remove all other sounds with the same key first
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
                if (active.DisposalTimer != null)
                {
                    active.DisposalTimer.Dispose(); // We already cleaned up, it would just call this
                    active.DisposalTimer = null;
                }
                if (active.NotificationTimer != null)
                    active.NotificationTimer.Interval = 1;
            }
        }

        // Pauses a sound by key.  If the sound is a QueuedSound or Notification, it should immediately play the next
        public void PauseSound(string key)
        {
            if (ActiveSounds.ContainsKey(key))
            {
                var sound = ActiveSounds[key];
                mixer.RemoveMixerInput(sound.Provider);
                ActiveSounds.Remove(key, out _);
                // Store it so it can be resumed or stopped later
                PausedSounds[key] = sound;
                // Pause the timer for this key, if it has one
                if (sound.DisposalTimer != null)
                    sound.DisposalTimer.Pause();
                // Check if this is in either of the Queues.  If it is, remove it and trigger them
                if (sound.QueueType == QueueType.Queued && QueuedSounds.Any(s => s.Key == key))
                {
                    if (QueuedSounds.First().Key == key) // Trigger the queue if it's first
                        QueueElapsed(this, null); // There's a race condition here, if the timer triggered between reading the QueueType and now, this will trigger another sound...
                    else // If it's not first, just remove it, the queue will handle it
                        QueuedSounds.Remove(QueuedSounds.Where(s => s.Key == key).First());

                }
                else if (sound.QueueType == QueueType.Notification && QueuedNotifications.Any(s => s.Key == key))
                {
                    if (QueuedNotifications.First().Key == key)
                        NotificationElapsed(this, null); // Same race condition...
                    else
                        QueuedNotifications.Remove(QueuedNotifications.Where(s => s.Key == key).First());

                }

                if (sound.NotificationTimer != null)
                    sound.NotificationTimer.Interval = 1; // We want this to run to restore volumes to things it lowered when it was playing
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
                    // Immediately play it
                    mixer.AddMixerInput(PausedSounds[key].Provider);
                    ActiveSounds[key] = sound;
                    if (sound.DisposalTimer != null)
                        sound.DisposalTimer.Resume(); // Resume its disposal timer
                }
                else if (sound.QueueType == QueueType.Queued)
                {
                    Console.WriteLine("Resuming queued sound " + key);
                    QueueSound(sound, true); // Re-queue it at the front
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




        // Adds a new sound to the Notification Queue
        public void QueueNotification(PendingSound sound, bool front = false)
        {
            // Identify it for later
            sound.QueueType = QueueType.Notification;
            // Check if any sounds already have this key, and remove them
            StopSound(sound.Key);

            // If any queued sounds are already active, insert it at 1 instead of 0
            if (front)
                if (ActiveSounds.Any(a => a.Value.QueueType == QueueType.Notification))
                    QueuedNotifications.Insert(1, sound);
                else
                    QueuedNotifications.Insert(0, sound);
            else
                QueuedNotifications.Add(sound);
            if (QueuedNotifications.Count == 1)
            {
                // If this is the only one, the timer should be stopped, so we manually play it and start the timer again
                QueueTimer.Enabled = false;
                PlayNotification(sound);
            }
        }
        // Add a new sound to the standard queue
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
