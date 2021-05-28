# DU-Audio-Sharp
A copy of ZarTaen's logfile based audio framework, but in C# - easier setup and finer control

ZarTaen's framework found here: https://github.com/ZarTaen/DU_logfile_audioframework


Autobuilds from source in the releases tab.

Should be working at this time - please report any issues or inconsistencies between this and ZarTaen's Framework - they should both support all the same functionality.  This project is mostly for simpler installs, no config files and only a single executable.

# Common Users
Once available, download and run the .exe in the Releases tab (or compile it yourself from the source).

The audiopacks folder is generated when the application first runs, or you can create it yourself.  This folder is not enforced, so some lua scripts may not use it, but it is recommended for scripters to put their audiopacks in this folder

It is advised to rename your audiopacks to something unique, and input that name into your scripts (if available), so that the birds at the market can't spam you with noises from the filepaths that they think you have.  Hopefully, the lua scripts will have an export variable where you can enter the filepath to the soundpack you want to use with that script.  ArchHUD is a good example - it will not play sound unless the parameter for the name of the audiopack has been changed from default.


# Lua Scripters
This works much like ZarTaen's framework, using our new standardized format.  In lua, for example, `system.logInfo("sound_play|some_thing.mp3|uniqueID|50")` per the parameters and commands below

The path can be an absolute or relative path (to the executable) - usage of Windows sounds is encouraged

The ID is used so that a. New sounds played with the same ID will stop previous sounds with that ID, and b. Sounds may be paused/stopped/resumed via ID

## Available commands and formats:

`sound_play|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)` -- Plays a concurrent sound

`sound_notification|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)` -- Lowers volume on all other sounds for its duration, and plays overtop

`sound_q|path_to/the.mp3(string)|ID(string)|Optional Volume(int 0-100)` -- Plays a sound after all other queued sounds finish

-- The following use the IDs that were specified in the previous three

`sound_volume|ID(string)|Volume(int 0-100)`

`sound_pause|Optional ID(string)` -- If no ID is specified, pauses all sounds

`sound_stop|Optional ID(string)` -- If no ID is specified, stops all sounds

`sound_resume|Optional ID(string)` -- If no ID is specified, resumes all paused sounds


All ID's are strings, and may be omitted if you don't care to manipulate them afterwards, and don't care about the same audio file being played overtop itself
