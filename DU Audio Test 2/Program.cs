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

        private static Random random = new Random();
         
        static void Main(string[] args)
        {
            //var seatbelt = new PendingSound(new CachedSound("Ripley Galactic_KICS 4_Audio_KICS_SealtBeltSign.mp3"),100,"sb");
            //var problem = new PendingSound(new CachedSound("Ripley Galactic_KICS 4_Audio_KICS_WeHaveALittleProblem.mp3"),100,"prb");
            //var really = new PendingSound(new CachedSound("Ripley Galactic_KICS 4_Audio_Requests_YourShouldReally.mp3"),100,"rly");
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
            


            if (!Directory.Exists("audiopacks"))
                Directory.CreateDirectory("audiopacks");

            // We need to make an AudioPlaybackEngine start, which starts reading the logfiles and such
            if (AudioPlaybackEngine.Instance == null)
                return;

            //AudioPlaybackEngine.Instance.PlaySound(seatbelt);
            //AudioPlaybackEngine.Instance.QueueSound(problem);
            //AudioPlaybackEngine.Instance.QueueSound(really);
            //Thread.Sleep(1000);
            //AudioPlaybackEngine.Instance.QueueNotification(really);

            Console.WriteLine("Framework ready, waiting for sounds");
            Console.WriteLine("(Press Any Key to Exit)");

            while(true)
                Console.Read();
        }

        private static Dictionary<string, CachedSound> cachedFileMap = new Dictionary<string, CachedSound>();

        private static CachedSound GetCachedSound(string path)
        {
            if (File.Exists(path))
            {
                if (!cachedFileMap.ContainsKey(path))
                {
                    cachedFileMap[path] = new CachedSound(path);
                }
                return cachedFileMap[path];
            }
            else if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    var randomFile = files[random.Next(files.Length)];
                    if (!cachedFileMap.ContainsKey(randomFile))
                    {
                        cachedFileMap[randomFile] = new CachedSound(randomFile);
                    }
                    return cachedFileMap[randomFile];
                }
            }
            return null;
        }

        // Format: sound_play|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)
        public static void sound_play(string[] input)
        {
            string path = input[0];
            string Id = input[1];
            int volume = 100;
            if (input.Length > 2)
                volume = Math.Clamp(int.Parse(input[2]),0,100); // Throws an exception if invalid, which is good, gets caught outside
            Console.WriteLine("Trying to play file " + path);
            var sound = GetCachedSound(path);
            if (sound != null)
                AudioPlaybackEngine.Instance.PlaySound(new PendingSound(sound, volume, Id));
            else
                Console.WriteLine($"File Not Found: {path}");
        }

        // Format: sound_loop|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)
        public static void sound_loop(string[] input)
        {
            string path = input[0];
            string Id = input[1];
            int volume = 100;
            if (input.Length > 2)
                volume = Math.Clamp(int.Parse(input[2]), 0, 100); // Throws an exception if invalid, which is good, gets caught outside
            Console.WriteLine("Trying to loop file " + path);
            var sound = GetCachedSound(path);
            if (sound != null)
                AudioPlaybackEngine.Instance.LoopSound(new PendingSound(sound, volume, Id));
            else
                Console.WriteLine($"File Not Found: {path}");
        }

        // Format: sound_notification|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100) 
        // Lowers volume on all other currently playing sounds for its duration, and plays overtop
        public static void sound_notification(string[] input)
        {
            string path = input[0];
            string Id = input[1];
            int volume = 100;
            if (input.Length > 2)
                volume = Math.Clamp(int.Parse(input[2]), 0, 100);
            Console.WriteLine("Trying to notify file " + path);
            var sound = GetCachedSound(path);
            if (sound != null)
                AudioPlaybackEngine.Instance.QueueNotification(new PendingSound(sound, volume, Id));
            else
                Console.WriteLine($"File Not Found: {path}");
        }

        // Format: sound_q|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)
        public static void sound_q(string[] input)
        {
            string path = input[0];
            string Id = input[1];
            int volume = 100;
            if (input.Length > 2)
                volume = Math.Clamp(int.Parse(input[2]), 0, 100);
            Console.WriteLine("Trying to queue file " + path);
            var sound = GetCachedSound(path);
            if (sound != null)
                AudioPlaybackEngine.Instance.QueueSound(new PendingSound(sound, volume, Id));
            else
                Console.WriteLine($"File Not Found: {path}");
        }

        // Format: sound_volume|ID(string)|Volume(int 0-100)
        public static void sound_volume(string[] input)
        {
            string Id = input[0];
            int volume = Math.Clamp(int.Parse(input[1]), 0, 100);

            AudioPlaybackEngine.Instance.SetVolume(Id, volume);
        }

        // Format: sound_pause|Optional ID(string)
        // If no ID is specified, pauses all sounds
        public static void sound_pause(string[] input)
        {
            string Id = null;
            if (input.Length > 0)
                Id = input[0];

            AudioPlaybackEngine.Instance.PauseSound(Id);
        }

        // Format: sound_stop|Optional ID(string)
        // If no ID is specified, stops all sounds
        public static void sound_stop(string[] input)
        {
            string Id = null;
            if (input.Length > 0)
                Id = input[0];

            AudioPlaybackEngine.Instance.StopSound(Id);
        }

        // Format: sound_resume|Optional ID(string)
        // If no ID is specified, resumes all paused sounds
        public static void sound_resume(string[] input)
        {
            string Id = null;
            if (input.Length > 0)
                Id = input[0];

            AudioPlaybackEngine.Instance.ResumeSound(Id);
        }


        
    }
}
