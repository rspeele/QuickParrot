QuickParrot is open source soundboard software for gaming. Like existing soundboard alternatives such as Resanance, it
relies on Virtual Audio Cable for microphone playback. What it adds is:

* Mumble-style visual overlay with chord controls instead of a zillion keybinds
* Automatic push-to-talk control
* Recording tool to easily add sounds
* Self-diagnosis and repair tool for Virtual Audio Cable setup on Windows

# The visual overlay

Like Mumble or Fraps, QuickParrot can hook into a 3d (DirectX, OpenGL?) game and display its overlay. The overlay shows
nothing at all until you press and hold the *chord hotkey*, B by default.

While you hold the chord key, your folders are displayed in a ring at the center of the screen, with numbers on each.
E.g. "1: Trump", "2: Movies & TV Quotes", "3: Arnold Schwarzenegger".

Still holding the chord key, you press the number to navigate into a folder which shows the same ring-display. Once an
option is an audio file, rather than a folder, and you hit that key, it plays that audio file down both the VAC and your
output device.

Releasing B before navigating all the way to an audio file cancels the whole thing, and hitting B again starts over from
the beginning (top level folder).

B+shift+numbers *persistently* navigates into a folder. If you hold B+shift+1 then release B, you navigated into the
Trump folder and will start there the next time you hit B. If you use B+shift+numbers all the way to the audio file, it
both plays the file *and* persistently stays in the folder that contained that file.

B+0 goes up a folder. B+shift+(repeated 0 keypresses) can therefore return one persistently to the top folder. This
stops at the folder QuickParrot was originally pointed at. You can't navigate up beyond that to browse the broader
filesystem.

# Automatic push to talk control

QuickParrot can be configured with your in-game push-to-talk key or mouse button, e.g. V. It will simulate a keypress,
like autohotkey, holding that key down for the duration of the audio file with a configurable (default: 500ms) margin on
each end to ensure the sound doesn't get cut off. This eliminates the need for you to jump onto your push-to-talk as
soon as you pick the file and hold it till it finishes playing.

It can also be configured to mute or attenuate your real microphone input while the soundboard clip is playing so your
keyboard mashing gameplay sounds don't interfere with the comedy.

# Recording tool

To build a library of fun sounds, you could spend an hour in Audacity, but building this in app makes it much easier.
Within QuickParrot, out-of-game, you can browse through your sound clip folders and in any of them, hit "record desktop
audio". This opens a window with stop/start capture buttons. Navigate to a YouTube or other video, pause it before your
intended capture, start capture, and play. Then stop capture once the clip you wanted to grab has finished playing.

QuickParrot will then display the wave of the captured audio. You can scrub through it, replay, and you can click and
drag to trim the start/end of the clip in case you didn't bracket it perfectly with your start/stop.

It may also be able to do some basic noise-removal on the captured clip to help remove e.g. a laugh track or background
wind noise. Possibly too ambitious.

If you configure QuickParrot with a LiteLLM endpoint, it will attempt to automatically name the capture based on
speech-to-text + AI summary. Otherwise you are prompted to enter the name.

Captured sound drops into the current folder and the workflow is ready to capture another so if you have multiple sound
bites you're going to grab from playing one Youtube video, you can get through them quite quickly.

# Self-diagnosis

QuickParrot is Windows-only software and can inspect the Windows audio setup to troubleshoot issues. It's not unusual
for Windows updates to break Virtual Audio Cable configuration, e.g. by disabling the "listen to this device" setting or
changing the default microphone/communication device. So some of this should be a check that happens silently on every
startup.

It can also detect if Virtual Audio Cable is not installed or enabled.

It may be able to offer a 1-click "repair" option that changes these settings to the correct setup. (sets default devices, sets the "listen to this device" option)

Once it thinks everything is set up, it can also have a 1-click "test" function that records from VAC while playing a
test sound down it, then plays back. The user hits the test button, says something, and confirms that they hear both their
spoken input *and* the artificial soundboard test sound in the playback.

# Tech

QuickParrot is written in C#, runs on .NET 10.0, and has a simple XAML WPF desktop UI. Since it is highly tied to Windows (VAC, mic settings) there is no need to use a cross-platform UI like Avalonia.