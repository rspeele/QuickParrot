using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// Pure checks of the audio setup QuickParrot expects: the game's mic is the cable's recording side, and the real mic
/// reaches the cable through Windows' "Listen to this device". Anything unknown is skipped rather than reported.
/// </summary>
public static class AudioSetupDiagnoser
{
    public const float LowVolumeThreshold = 0.10f;

    private static readonly DeviceRoles[] EachRole = [DeviceRoles.Console, DeviceRoles.Multimedia, DeviceRoles.Communications];

    public static class Ids
    {
        public const string DevicesUnreadable = "devices-unreadable";
        public const string CableMissing = "cable-missing";
        public const string CableInputMissing = "cable-input-missing";
        public const string CableOutputMissing = "cable-output-missing";
        public const string MicMissing = "mic-missing";
        public const string ListenDisabled = "listen-disabled";
        public const string ListenWrongTarget = "listen-wrong-target";
        public const string DefaultPlaybackIsCable = "default-playback-is-cable";
        public const string CableInputLevel = "cable-input-level";
        public const string CableOutputLevel = "cable-output-level";
        public const string MicMuted = "mic-muted";
        public const string CommunicationsDucking = "communications-ducking";
        public const string DefaultRecordingNotCable = "default-recording-not-cable";
        public const string CommunicationsRecordingMismatch = "communications-recording-mismatch";
        public const string CommunicationsPlaybackMismatch = "communications-playback-mismatch";
    }

    /// <summary>Findings ordered by severity, then in the fixed order the checks run.</summary>
    public static IReadOnlyList<DiagnosticFinding> Diagnose(AudioSetupSnapshot snapshot)
    {
        if (!snapshot.DevicesReadable)
        {
            return [new DiagnosticFinding(Ids.DevicesUnreadable, DiagnosticSeverity.Advisory, "Couldn't check your audio devices",
                "Windows didn't let QuickParrot list your audio devices, so the setup couldn't be checked. Try Re-check.")];
        }

        var findings = new List<DiagnosticFinding>();
        var cable = OutputDeviceSelector.SelectCable(snapshot.RenderDevices, snapshot.Configured.CableId);
        var cableCapture = CableCaptureSelector.Select(snapshot.CaptureDevices, cable);
        var mic = SelectRealMic(snapshot, cable);

        CheckCableInstalled(findings, cable, cableCapture);
        if (mic is null)
        {
            findings.Add(new DiagnosticFinding(Ids.MicMissing, DiagnosticSeverity.Error, "No microphone found",
                "No working microphone other than the virtual cable is connected, so there's no voice to send to your game. " +
                "Plug in your mic, or enable it in Windows Sound settings."));
        }
        else if (cable is not null)
        {
            CheckListen(findings, snapshot, mic, cable);
        }

        if (cable is not null)
            CheckDefaultPlayback(findings, snapshot, cable);

        CheckCableLevels(findings, snapshot, cable, cableCapture);
        if (mic is not null)
            CheckMicMuted(findings, snapshot, mic);

        CheckDucking(findings, snapshot);
        CheckCommunicationsRecordingMismatch(findings, snapshot, cableCapture);
        if (cableCapture is not null)
            CheckDefaultRecording(findings, snapshot, cableCapture);

        CheckCommunicationsPlaybackMismatch(findings, snapshot, cable);

        return findings.OrderBy(f => f.Severity).ToList();
    }

    /// <summary>The real mic the diagnoser checks: as QuickParrot would pick it, but never the cable itself.</summary>
    public static CaptureDeviceInfo? SelectRealMic(AudioSetupSnapshot snapshot, AudioDeviceInfo? cable)
    {
        var defaults = snapshot.Defaults ?? DefaultEndpoints.None;
        var device = MicDeviceSelector.Select(
            snapshot.CaptureDevices, snapshot.Configured.MicId, defaults.CaptureMultimedia, defaults.CaptureCommunications).Device;
        if (device is { IsCable: true })
            device = MicDeviceSelector.Select(snapshot.CaptureDevices, null, defaults.CaptureMultimedia, defaults.CaptureCommunications).Device;

        // The selector finds nothing when the defaults are the cable and Listen is off, which is exactly what to report.
        // A mic whose Listen still targets the cable is most likely the one that was switched off.
        var candidates = snapshot.CaptureDevices.Where(d => d.IsActive && !d.IsCable).ToList();
        return device
            ?? candidates.FirstOrDefault(d => cable is not null && snapshot.Listen.TryGetValue(d.Id, out var listen)
                && listen.TargetId is { } target && SameId(target, cable.Id))
            ?? candidates.FirstOrDefault();
    }

    private static void CheckCableInstalled(List<DiagnosticFinding> findings, AudioDeviceInfo? cable, CaptureDeviceInfo? cableCapture)
    {
        var fix = new DiagnosticFix(FixKind.OpenCableDownloadPage, "Get VB-CABLE");
        if (cable is null && cableCapture is null)
        {
            findings.Add(new DiagnosticFinding(Ids.CableMissing, DiagnosticSeverity.Error, "No virtual audio cable found",
                "QuickParrot sends clips to your game through a virtual audio cable (VB-CABLE or Virtual Audio Cable), and " +
                "none is working. Install VB-CABLE, or enable the cable in Windows Sound settings if it's disabled.", fix));
        }
        else if (cable is null)
        {
            findings.Add(new DiagnosticFinding(Ids.CableInputMissing, DiagnosticSeverity.Error, "The cable's playback side is missing",
                $"{cableCapture!.Name} is there, but its playback side (CABLE Input) isn't working, so QuickParrot can't play " +
                "clips into it. Enable it in Windows Sound settings, or reinstall VB-CABLE.", fix));
        }
        else if (cableCapture is null)
        {
            findings.Add(new DiagnosticFinding(Ids.CableOutputMissing, DiagnosticSeverity.Error, "The cable's recording side is missing",
                $"{cable.Name} is there, but its recording side (CABLE Output) isn't working, so your game has no cable mic " +
                "to listen to. Enable it in Windows Sound settings, or reinstall VB-CABLE.", fix));
        }
    }

    private static void CheckListen(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, CaptureDeviceInfo mic, AudioDeviceInfo cable)
    {
        if (!snapshot.Listen.TryGetValue(mic.Id, out var listen))
            return;

        var fix = new DiagnosticFix(FixKind.EnableListen, $"Listen through {cable.Name}", mic.Id, cable.Id);
        if (!listen.Enabled)
        {
            findings.Add(new DiagnosticFinding(Ids.ListenDisabled, DiagnosticSeverity.Error, "Your voice isn't reaching the cable",
                $"\"Listen to this device\" is off for {mic.Name}. With your game using the cable as its mic, it only hears " +
                $"your voice when your mic plays into {cable.Name} through Listen.", fix));
            return;
        }

        var target = string.IsNullOrEmpty(listen.TargetId) ? snapshot.Defaults?.RenderConsole : listen.TargetId;
        if (target is null || SameId(target, cable.Id))
            return;

        var targetName = snapshot.RenderDevices.FirstOrDefault(d => SameId(d.Id, target))?.Name ?? "another device";
        var via = string.IsNullOrEmpty(listen.TargetId) ? $"your default playback device ({targetName})" : targetName;
        findings.Add(new DiagnosticFinding(Ids.ListenWrongTarget, DiagnosticSeverity.Error, "Your voice is going to the wrong place",
            $"\"Listen to this device\" is on for {mic.Name}, but it plays to {via} instead of {cable.Name}, so your game " +
            "won't hear you through the cable.", fix));
    }

    private static void CheckDefaultPlayback(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, AudioDeviceInfo cable)
    {
        if (snapshot.Defaults is not { } defaults)
            return;

        var roles = RolesWhere(role => defaults.Render(role) is { } id && SameId(id, cable.Id));
        if (roles == DeviceRoles.None)
            return;

        var target = OutputDeviceSelector.SelectRealPlaybackDevice(snapshot.RenderDevices, cable, snapshot.Configured.MonitorId);
        var fix = target is null ? null : new DiagnosticFix(FixKind.SetDefaultPlayback, $"Play through {target.Name}", target.Id, Roles: roles);
        if ((roles & (DeviceRoles.Console | DeviceRoles.Multimedia)) != 0)
        {
            findings.Add(new DiagnosticFinding(Ids.DefaultPlaybackIsCable, DiagnosticSeverity.Error, "Windows is playing sound into the cable",
                $"{cable.Name} is your default playback device, so you won't hear your games or apps, and they'll be sent " +
                "to your game's mic instead." + FixHint(target), fix));
        }
        else
        {
            findings.Add(new DiagnosticFinding(Ids.DefaultPlaybackIsCable, DiagnosticSeverity.Warning, "Voice chat is playing into the cable",
                $"{cable.Name} is your default communications playback device, so voice apps using it play your teammates " +
                "into your own mic instead of your headphones." + FixHint(target), fix));
        }
    }

    private static string FixHint(AudioDeviceInfo? target) =>
        target is null ? " Pick your speakers or headphones as the default in Windows Sound settings." : "";

    private static void CheckCableLevels(
        List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, AudioDeviceInfo? cable, CaptureDeviceInfo? cableCapture)
    {
        if (cable is not null && LevelProblem(snapshot, cable.Id, cable.Name) is { } inputProblem)
        {
            findings.Add(new DiagnosticFinding(Ids.CableInputLevel, DiagnosticSeverity.Warning, inputProblem,
                $"QuickParrot plays clips into {cable.Name}, so they'll be quiet or silent for everyone else until it's " +
                "unmuted and turned up.", new DiagnosticFix(FixKind.RestoreCableLevel, "Unmute and set to 100%", cable.Id)));
        }

        if (cableCapture is not null && LevelProblem(snapshot, cableCapture.Id, cableCapture.Name) is { } outputProblem)
        {
            findings.Add(new DiagnosticFinding(Ids.CableOutputLevel, DiagnosticSeverity.Warning, outputProblem,
                $"Your game hears your voice and clips through {cableCapture.Name}, so they'll be quiet or silent until it's " +
                "unmuted and turned up.", new DiagnosticFix(FixKind.RestoreCableLevel, "Unmute and set to 100%", cableCapture.Id)));
        }
    }

    private static string? LevelProblem(AudioSetupSnapshot snapshot, string id, string name)
    {
        if (!snapshot.Levels.TryGetValue(id, out var level))
            return null;
        if (level.Muted)
            return $"{name} is muted";

        return level.Volume < LowVolumeThreshold ? $"{name} is turned down to {Math.Round(level.Volume * 100)}%" : null;
    }

    private static void CheckMicMuted(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, CaptureDeviceInfo mic)
    {
        if (snapshot.MicRestorePending || !snapshot.Levels.TryGetValue(mic.Id, out var level) || !level.Muted)
            return;

        findings.Add(new DiagnosticFinding(Ids.MicMuted, DiagnosticSeverity.Warning, "Your microphone is muted",
            $"{mic.Name} is muted in Windows, so nobody can hear you.", new DiagnosticFix(FixKind.UnmuteMic, "Unmute", mic.Id)));
    }

    private static void CheckDucking(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot)
    {
        var effect = snapshot.CommunicationsDucking switch
        {
            CommunicationsDucking.MuteOthers => "mutes all other sounds",
            CommunicationsDucking.ReduceBy80Percent => "turns other sounds down by 80%",
            CommunicationsDucking.ReduceBy50Percent => "turns other sounds down by 50%",
            _ => null,
        };
        if (effect is null)
            return;

        findings.Add(new DiagnosticFinding(Ids.CommunicationsDucking, DiagnosticSeverity.Warning, "Windows turns sounds down during voice chat",
            $"Whenever a voice app opens a mic, Windows {effect}. That can include the clips QuickParrot plays into the " +
            "cable, so others hear them faintly, and your game goes quiet while voice chat is on.",
            new DiagnosticFix(FixKind.DisableCommunicationsDucking, "Set to \"Do nothing\"")));
    }

    private static void CheckDefaultRecording(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, CaptureDeviceInfo cableCapture)
    {
        if (snapshot.Defaults is not { } defaults)
            return;

        var roles = RolesWhere(role => defaults.Capture(role) is { } id && !SameId(id, cableCapture.Id));
        if (roles == DeviceRoles.None)
            return;

        var which = roles == DeviceRoles.Communications ? "default communications recording device" : "default recording device";
        findings.Add(new DiagnosticFinding(Ids.DefaultRecordingNotCable, DiagnosticSeverity.Advisory,
            $"{cableCapture.Name} isn't your {which}",
            $"Apps that use the Windows {which} won't hear your soundboard clips. That's fine as long as you pick " +
            $"{cableCapture.Name} as the microphone in each game or voice app.",
            new DiagnosticFix(FixKind.SetDefaultRecording, $"Make {cableCapture.Name} the default", cableCapture.Id, Roles: roles)));
    }

    // Skipped when communications is the cable: plausibly intentional, and this fix must never move it off the cable
    // (so it can't fight CheckDefaultRecording, whose fix points every role at the cable).
    private static void CheckCommunicationsRecordingMismatch(
        List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, CaptureDeviceInfo? cableCapture)
    {
        if (snapshot.Defaults is not { } defaults)
            return;

        var console = FindActiveCapture(snapshot, defaults.CaptureConsole);
        var comms = FindActiveCapture(snapshot, defaults.CaptureCommunications);
        if (console is null || comms is null || SameId(console.Id, comms.Id))
            return;

        var commsIsCable = cableCapture is not null && SameId(comms.Id, cableCapture.Id);
        if (commsIsCable)
            return;

        var consoleIsCable = cableCapture is not null && SameId(console.Id, cableCapture.Id);
        var explanation = consoleIsCable
            ? $"Most apps use {console.Name} (the one chosen in Windows Settings), which is how QuickParrot reaches your game with " +
              $"soundboard clips, but voice chat and some games use {comms.Name} instead, so they currently won't hear those clips."
            : $"Most apps use {console.Name} (the one chosen in Windows Settings), but voice chat and some games use {comms.Name} " +
              "instead.";
        explanation += " Steam also has its own mic choice (Steam → Settings → Voice) that QuickParrot can't change.";

        findings.Add(new DiagnosticFinding(Ids.CommunicationsRecordingMismatch, DiagnosticSeverity.Warning,
            "Your two default microphones don't match", explanation,
            new DiagnosticFix(FixKind.SetDefaultRecording, $"Also use {console.Name} for voice chat", console.Id,
                Roles: DeviceRoles.Communications)));
    }

    // Skipped when the cable is either default: CheckDefaultPlayback already offers a fix for that role.
    private static void CheckCommunicationsPlaybackMismatch(List<DiagnosticFinding> findings, AudioSetupSnapshot snapshot, AudioDeviceInfo? cable)
    {
        if (snapshot.Defaults is not { } defaults)
            return;

        if (cable is not null && (IsId(defaults.RenderConsole, cable.Id) || IsId(defaults.RenderCommunications, cable.Id)))
            return;

        var console = FindActiveRender(snapshot, defaults.RenderConsole);
        var comms = FindActiveRender(snapshot, defaults.RenderCommunications);
        if (console is null || comms is null || SameId(console.Id, comms.Id))
            return;

        findings.Add(new DiagnosticFinding(Ids.CommunicationsPlaybackMismatch, DiagnosticSeverity.Advisory,
            "Voice chat plays through a different output",
            $"Most apps play through {console.Name} (the one chosen in Windows Settings), but voice chat and some " +
            $"games play through {comms.Name} instead, so you might not hear teammates the way you expect.",
            new DiagnosticFix(FixKind.SetDefaultPlayback, $"Also use {console.Name} for voice chat", console.Id,
                Roles: DeviceRoles.Communications)));
    }

    private static AudioDeviceInfo? FindActiveRender(AudioSetupSnapshot snapshot, string? id) =>
        id is null ? null : snapshot.RenderDevices.FirstOrDefault(d => d.IsActive && SameId(d.Id, id));

    private static CaptureDeviceInfo? FindActiveCapture(AudioSetupSnapshot snapshot, string? id) =>
        id is null ? null : snapshot.CaptureDevices.FirstOrDefault(d => d.IsActive && SameId(d.Id, id));

    private static bool IsId(string? candidate, string id) => candidate is not null && SameId(candidate, id);

    private static DeviceRoles RolesWhere(Func<DeviceRoles, bool> predicate) =>
        EachRole.Where(predicate).Aggregate(DeviceRoles.None, (all, role) => all | role);

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
