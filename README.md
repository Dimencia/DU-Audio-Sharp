# DU-Audio-Sharp
A copy of ZarTaen's logfile based audio framework, but in C# - easier setup and finer control

ZarTaen's framework found here: https://github.com/ZarTaen/DU_logfile_audioframework


Currently not available for the public except as source

# Common Users
Once available, download and run the .exe in the Releases tab (or compile it yourself from the source).  Place soundpacks into the Soundpacks folder
The soundpacks folder is generated when the application first runs, or you can create it yourself

It is advised to rename your soundpacks to something unique, and input that name into your scripts (if available), so that the birds at the market can't spam you with noises from the filepaths that they think you have


# Lua Scripters
This works much like ZarTaen's framework, for the most part.  You only need to send: 
`system.logInfo("playsound|path_to/sound_file.mp3|Optional ID")`
The path may not include ../ or ..\
The ID is used so that a. New sounds played with the same ID will stop previous sounds with that ID, and b. Sounds may be paused/stopped/resumed via ID

Available commands and formats:
`playsound|filename|ID (optional)` -- Play a sound without queuing - play overtop of any existing sounds

`qsound|soundpackFolder|filename|ID (optional)` -- Play a queued sound - plays queued sounds in order from when they were called, waiting for previous ones to finish first

`stopsound|ID` -- Stops the sound with the given ID

`pausesound|ID` -- Pauses the sound with the given ID

`resumesound|ID` -- Resumes the sound with the given ID
