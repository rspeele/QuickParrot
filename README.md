QuickParrot is open source soundboard software for gaming. Like existing soundboard alternatives such as Resanance, it
relies on a virtual audio cable (VB-Audio's VB-CABLE, or Muzychenko's Virtual Audio Cable) for microphone playback. What
it adds is:

* Mumble-style visual overlay with chord controls instead of a zillion keybinds
* Automatic push-to-talk control
* Instant-replay recording to grab sounds after you hear them
* Self-diagnosis and repair tool for virtual audio cable setup on Windows

# The visual overlay

QuickParrot displays its overlay over your game. The overlay shows nothing at all until you press and hold the *chord
hotkey*, B by default.

While you hold the chord key, your folders are displayed at the center of the screen, with numbers on each. By default
this is a numbered list, top to bottom, so the order is obvious at a glance; a ring layout is available in settings for
those who prefer it. E.g. "1: Trump", "2: Movies & TV Quotes", "3: Arnold Schwarzenegger". Folders are listed first, then
sound files, each alphabetically.

Still holding the chord key, you press the number to navigate into a folder which shows the same display. Once an
option is an audio file, rather than a folder, and you hit that key, it plays that audio file down both the virtual
cable and your output device.

Releasing B before navigating all the way to an audio file cancels the whole thing, and hitting B again starts over from
the beginning (top level folder).

B+shift+numbers *persistently* navigates into a folder. If you hold B+shift+1 then release B, you navigated into the
Trump folder and will start there the next time you hit B. If you use B+shift+numbers all the way to the audio file, it
both plays the file *and* persistently stays in the folder that contained that file.

B+0 goes up a folder. B+shift+(repeated 0 keypresses) can therefore return one persistently to the top folder. This
stops at the folder QuickParrot was originally pointed at. You can't navigate up beyond that to browse the broader
filesystem.

## Stopping and replacing clips

Pressing and releasing the chord key on its own, without pressing any number, stops the clip that's playing. Picking a
new clip while one is playing stops the first one; clips never overlap.

## Large folders

A folder with 10 or more entries is shown as a grid instead of a list or ring: columns of 9, filled in order, so adding a
file only shifts the entries after it. The first number zooms into a column, the second selects within it, and 0 backs
out of the zoomed column. Long names are truncated to fit.

That handles up to 81 entries per folder. Beyond that, make some subfolders: the desktop app warns about it, and the
overlay silently shows only the first 81.

## Favorites (F1–F12)

The clips you use constantly can live on the F-keys. B+F3 plays whatever is on F3, with the same push-to-talk and
replace-the-current-clip behavior as any other clip.

To assign, press B+shift+F3. The overlay switches to the favorites panel: all twelve slots across the top with F3
highlighted ("Assigning F3"), and your folders below.

* Pick a clip with the number keys as usual: it goes on F3 without playing.
* Press F3 again to put the last-played clip on it. (Heard something funny? B+shift+F3, F3.)
* Press Delete or Backspace to clear F3.
* Press a different F-key to switch which slot you're assigning, so you can edit several in one go.
* Release B to back out without changing anything. This also makes B+shift+F-key the way to just look at your favorites.

A setting lets the plain F-key play its favorite without holding B, for games that don't use the F-keys. To keep F5
refreshing your browser and F2 renaming files, plain F-keys only fire for slots that have a clip, and only while the
focused window covers the whole screen (i.e. you're in a fullscreen or borderless game). The default is to require B.

Favorites can also be managed from the desktop app. If a favorite's file is moved or deleted, its slot shows as missing.

If the chord key or push-to-talk key is itself an F-key, that F-key keeps its job and its slot shows as unavailable.
Like number keys, F-keys, Delete and Backspace are swallowed while the chord key is held.

## Choosing a chord key

The chord key is configurable, and QuickParrot fully owns it: the game never sees it. B is the default because the
number row is easy to reach with one hand, but it conflicts with some games (e.g. the Counter-Strike buy menu). A more
conservative choice is "-", which pairs well with the numpad for right-handed use. Keys are matched by physical position,
so the numpad works regardless of NumLock.

Number keys pressed while the chord key is held are also swallowed, so they don't switch weapons in-game.

# Automatic push to talk control

QuickParrot can be configured with your in-game push-to-talk key or mouse button, e.g. V. It will simulate a keypress,
like autohotkey, holding that key down for the duration of the audio file with a configurable (default: 500ms) margin on
each end to ensure the sound doesn't get cut off. This eliminates the need for you to jump onto your push-to-talk as
soon as you pick the file and hold it till it finishes playing. If one clip replaces another, the key stays held through
the switch, and QuickParrot won't release the key if you're physically holding it yourself.

It can also be configured to mute or attenuate your real microphone input while the soundboard clip is playing so your
keyboard mashing gameplay sounds don't interfere with the comedy. The original mic state is saved before changing it, so
it's restored even if QuickParrot crashes mid-clip.

# Recording tool

To build a library of fun sounds, you could spend an hour in Audacity, but building this in app makes it much easier.
QuickParrot works like "instant replay" in game capture tools: while it's running, it keeps the last 30 seconds of
whatever you hear (configurable up to 2 minutes) in memory. Hear something good — in a game, a YouTube video, a call — and
grab it after the fact. Nothing is written to disk until you grab, and the replay buffer can be switched off.

There are two ways to grab:

* In-game, press chord+Enter (B+Enter by default). The last 30 seconds are saved to a *pending grabs* list, and the
  overlay briefly confirms it. You can keep playing and deal with it later.
* Out of game, hit "Grab" in the app.

Opening a grab shows the waveform of the captured audio. You can scrub through it, replay, and click and drag to trim
the start/end down to the part you want. Clips are loudness-normalized so they're neither whisper-quiet nor ear-splitting
in voice chat, then saved into the library folder of your choice. Several sound bites can be snipped from one grab.

Noise removal (e.g. laugh tracks, wind) is out of scope for now.

If you configure QuickParrot with a LiteLLM endpoint, it will attempt to automatically name the clip based on
speech-to-text + AI summary. Otherwise you are prompted to enter the name.

The replay buffer records what plays through your output device, so it also catches other people's voices on calls and
QuickParrot's own clips. It stays in memory unless you grab it, but recording-consent rules vary by place.

# Self-diagnosis

QuickParrot is Windows-only software and can inspect the Windows audio setup to troubleshoot issues. The expected setup
is: your game or voice app uses the cable's output ("CABLE Output") as its microphone, and your real mic has "Listen to
this device" enabled with the cable's input as the playback target. That way your mic works through the cable even when
QuickParrot isn't running.

It's not unusual for Windows updates to break this, e.g. by disabling the "listen to this device" setting or changing
the default microphone/communication device. So some of this should be a check that happens silently on every startup.
It also checks for Windows communications ducking, which by default turns other sounds down by 80% when a voice app
opens the mic.

It can also detect if a virtual audio cable is not installed or enabled.

It may be able to offer a 1-click "repair" option that changes these settings to the correct setup. (sets default
devices, sets the "listen to this device" option)

Once it thinks everything is set up, it can also have a 1-click "test" function that records from the cable while
playing a test sound down it, then plays back. The user hits the test button, says something, and confirms that they
hear both their spoken input *and* the artificial soundboard test sound in the playback.

# Tech

QuickParrot is written in C#, runs on .NET 10.0, and has a simple XAML WPF desktop UI. Since it is highly tied to Windows
(virtual cable, mic settings) there is no need to use a cross-platform UI like Avalonia.

Everything runs in one process:

* The overlay is a separate always-on-top window over the game, not code injected into the game, so anti-cheat has
  nothing to object to. It never takes focus, ignores the mouse, and is hidden entirely when not in use. It's a WinForms
  layered window drawn with GDI+, on its own UI thread. It works with borderless windowed games but not true exclusive
  fullscreen.
* A low-level keyboard hook reads the chord and number keys, on its own thread.
* The chord navigation logic is a pure, unit-tested state machine with no Windows dependencies.
* If your game runs as administrator, QuickParrot must too, or Windows blocks its hotkeys and simulated push-to-talk.

# Future ideas

## Mouse gesture navigation

A third small-folder layout, alongside list and ring, that lets you pick clips without moving your hand off WASD+mouse.
It's modelled on Autodesk-style "marking menus":

* Hold the chord key: the ring appears with a virtual cursor in a small dead zone at its center. Mouse movement is
  swallowed while the chord is held, so the camera doesn't turn. Clicks still pass through.
* Moving out of the dead zone highlights the item in that direction.
* Crossing a folder's outer edge enters it, and its ring re-centers at the cursor. So one zigzag stroke ("up, then
  right") navigates into a folder and picks a clip within it. A reserved direction (e.g. straight down) goes back up.
* Releasing the chord key over a file plays it. Releasing back in the current ring's dead zone cancels. A bare tap
  (never leaving the first dead zone) still stops the playing clip. Shift+release could persist the folder like
  Shift+number does.
* Keyboard digits keep working in the same chord session.

Limits: 8 directions per ring (7 in subfolders, with one reserved for "back"), and accuracy drops past two levels deep,
so it suits a small curated set of favourites rather than a big library. Expert use becomes a single memorized flick,
no overlay-reading required. The low-level mouse hook would only be installed while the chord key is held, so it costs
nothing the rest of the time.
