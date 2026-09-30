using QuickParrot.Core.Devices;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Mic;

public class MicDuckerTests
{
    private const string Mic = "mic";

    private readonly List<string> _log = [];
    private readonly List<string> _warnings = [];
    private readonly FakeCaptureDeviceCatalog _devices = new();
    private readonly FakeMicVolumeControl _control;
    private readonly FakeMicRestoreStore _store;
    private readonly MicDucker _ducker;

    public MicDuckerTests()
    {
        _control = new FakeMicVolumeControl(_log);
        _store = new FakeMicRestoreStore(_log);
        _devices.Devices.Add(new CaptureDeviceInfo(Mic, "Microphone (USB)", AudioDeviceState.Active, true));
        _devices.Devices.Add(new CaptureDeviceInfo("cable", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Active, false));
        _devices.DefaultId = "cable";
        _control.Levels[Mic] = new MicLevel(false, 0.8f);
        _control.Levels["cable"] = new MicLevel(false, 1f);
        _ducker = new MicDucker(_control, _devices, _store);
        _ducker.Warning += _warnings.Add;
        _ducker.Configure(new MicDuckSettings(MicDuckMode.Mute, 20, null));
    }

    [Fact]
    public void Mute_SavesRecordBeforeMuting_AndUnmuteRestoresThenDeletes()
    {
        _ducker.Mute();

        Assert.Equal(["save:mic", "mute:mic=True"], _log);
        Assert.Equal(new MicRestoreRecord(Mic, "Microphone (USB)", MicDuckMode.Mute, false, 0.8f), _store.Record);
        Assert.True(_control.Levels[Mic].Muted);

        _ducker.Unmute();

        Assert.Equal(["save:mic", "mute:mic=True", "mute:mic=False", "delete"], _log);
        Assert.Equal(new MicLevel(false, 0.8f), _control.Levels[Mic]);
        Assert.Null(_store.Record);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AlreadyMutedByUser_StaysMutedAfterwards()
    {
        _control.Levels[Mic] = new MicLevel(true, 0.8f);

        _ducker.Mute();
        _ducker.Unmute();

        Assert.True(_store.Record is null);
        Assert.True(_control.Levels[Mic].Muted);
        Assert.Equal(["save:mic", "mute:mic=True", "mute:mic=True", "delete"], _log);
    }

    [Fact]
    public void Attenuate_ScalesVolume_AndRestoresItWithoutTouchingMute()
    {
        _ducker.Configure(new MicDuckSettings(MicDuckMode.Attenuate, 25, null));

        _ducker.Mute();
        Assert.Equal(0.2f, _control.Levels[Mic].Volume, 5);

        _ducker.Unmute();
        Assert.Equal(new MicLevel(false, 0.8f), _control.Levels[Mic]);
        Assert.DoesNotContain(_log, entry => entry.StartsWith("mute:"));
    }

    [Theory]
    [InlineData(0.8f, 20, 0.16f)]
    [InlineData(1f, 0, 0f)]
    [InlineData(0.5f, 100, 0.5f)]
    [InlineData(0f, 50, 0f)]
    public void AttenuatedVolume_IsPercentOfOriginal(float original, int percent, float expected)
    {
        Assert.Equal(expected, MicDucker.AttenuatedVolume(original, percent), 5);
    }

    [Fact]
    public void Off_DoesNothing()
    {
        _ducker.Configure(MicDuckSettings.Off);

        _ducker.Mute();
        _ducker.Unmute();

        Assert.Empty(_log);
    }

    [Fact]
    public void ConfiguredDevice_IsUsed()
    {
        _devices.Devices.Add(new CaptureDeviceInfo("cam", "Webcam", AudioDeviceState.Active, false));
        _control.Levels["cam"] = new MicLevel(false, 1f);
        _ducker.Configure(new MicDuckSettings(MicDuckMode.Mute, 20, "cam"));

        _ducker.Mute();

        Assert.True(_control.Levels["cam"].Muted);
        Assert.False(_control.Levels[Mic].Muted);
    }

    [Fact]
    public void ConfiguredDeviceMissing_FallsBackAndWarns()
    {
        _ducker.Configure(new MicDuckSettings(MicDuckMode.Mute, 20, "gone"));

        _ducker.Mute();

        Assert.True(_control.Levels[Mic].Muted);
        Assert.Contains("Microphone (USB) was used instead", Assert.Single(_warnings));
    }

    [Fact]
    public void NoSuitableMic_SkipsAndWarnsOnce()
    {
        _devices.Devices.RemoveAt(0);

        _ducker.Mute();
        _ducker.Unmute();
        _ducker.Mute();
        _ducker.Unmute();

        Assert.Empty(_log);
        Assert.Single(_warnings);
    }

    [Fact]
    public void ReadFails_SkipsWithoutSavingAndWarns()
    {
        _control.ThrowOnRead = true;

        _ducker.Mute();
        _ducker.Unmute();

        Assert.Empty(_log);
        Assert.Contains("read failed", Assert.Single(_warnings));
    }

    [Fact]
    public void SaveFails_LeavesMicAlone()
    {
        _store.ThrowOnSave = true;

        _ducker.Mute();
        _ducker.Unmute();

        Assert.Empty(_log);
        Assert.False(_control.Levels[Mic].Muted);
        Assert.Contains("disk full", Assert.Single(_warnings));
    }

    [Fact]
    public void ChangeFails_TriesToRestoreAndWarnsWithoutThrowing()
    {
        _control.ThrowOnChange = true;

        _ducker.Mute();

        Assert.Equal(["save:mic", "mute:mic=True", "mute:mic=False"], _log);
        Assert.Contains("change failed", Assert.Single(_warnings));
        Assert.NotNull(_store.Record); // kept for a later retry, since restoring failed too
    }

    [Fact]
    public void UnpluggedMidClip_KeepsRecord_AndRestoresBeforeNextClip()
    {
        _ducker.Mute();
        _devices.SetState(Mic, AudioDeviceState.Unplugged);

        _ducker.Unmute();

        Assert.NotNull(_store.Record);
        Assert.Single(_warnings);

        _devices.SetState(Mic, AudioDeviceState.Active);
        _log.Clear();
        _ducker.Mute();

        Assert.Equal(["mute:mic=False", "delete", "save:mic", "mute:mic=True"], _log);
        Assert.False(_store.Record!.OriginalMuted);
    }

    [Fact]
    public void LeftoverStillUnrestorable_SkipsDucking()
    {
        _ducker.Mute();
        _devices.SetState(Mic, AudioDeviceState.Unplugged);
        _ducker.Unmute();
        _log.Clear();

        _ducker.Mute();

        Assert.Empty(_log);
        Assert.Equal(2, _warnings.Count);
    }

    [Fact]
    public void UsbMicPulledMidClip_IsNotPresent_KeepsRecord()
    {
        _ducker.Mute();
        _devices.SetState(Mic, AudioDeviceState.NotPresent);

        _ducker.Unmute();

        Assert.NotNull(_store.Record);
    }

    [Fact]
    public void DeviceRemovedMidClip_ForgetsRecord()
    {
        _ducker.Mute();
        _devices.Devices.RemoveAt(0);

        _ducker.Unmute();

        Assert.Null(_store.Record);
    }

    [Fact]
    public void SettingsChangedMidClip_StillRestores()
    {
        _ducker.Mute();
        _ducker.Configure(new MicDuckSettings(MicDuckMode.Attenuate, 50, "cable"));

        _ducker.Unmute();

        Assert.Equal(new MicLevel(false, 0.8f), _control.Levels[Mic]);
        Assert.Equal(new MicLevel(false, 1f), _control.Levels["cable"]);
    }

    [Fact]
    public void RestoreAfterCrash_NoRecord_ReturnsNull()
    {
        Assert.Null(_ducker.RestoreAfterCrash());
        Assert.Empty(_log);
    }

    [Fact]
    public void RestoreAfterCrash_RestoresAndDeletesRecord()
    {
        _control.Levels[Mic] = new MicLevel(false, 0.1f);
        _store.Record = new MicRestoreRecord(Mic, "Microphone (USB)", MicDuckMode.Attenuate, false, 0.8f);

        var message = _ducker.RestoreAfterCrash();

        Assert.Contains("restored", message);
        Assert.Equal(new MicLevel(false, 0.8f), _control.Levels[Mic]);
        Assert.Null(_store.Record);
    }

    [Fact]
    public void RestoreAfterCrash_DeviceUnplugged_KeepsRecordForLater()
    {
        _control.Levels[Mic] = new MicLevel(true, 0.8f);
        _store.Record = new MicRestoreRecord(Mic, "Microphone (USB)", MicDuckMode.Mute, false, 0.8f);
        _devices.SetState(Mic, AudioDeviceState.Unplugged);

        Assert.Contains("try again", _ducker.RestoreAfterCrash());
        Assert.NotNull(_store.Record);

        _devices.SetState(Mic, AudioDeviceState.Active);
        _ducker.Mute();
        _ducker.Unmute();

        Assert.Equal(new MicLevel(false, 0.8f), _control.Levels[Mic]);
        Assert.Null(_store.Record);
    }

    [Fact]
    public void RestoreAfterCrash_DeviceGone_DeletesRecord()
    {
        _store.Record = new MicRestoreRecord("gone", "Old mic", MicDuckMode.Mute, false, 0.8f);

        Assert.Contains("is gone", _ducker.RestoreAfterCrash());
        Assert.Null(_store.Record);
    }

    private void LeaveRestorePending()
    {
        _ducker.Mute();
        _devices.SetState(Mic, AudioDeviceState.Unplugged);
        _ducker.Unmute();
        _warnings.Clear();
        _log.Clear();
    }

    [Fact]
    public void RetryRestore_RestoresOnceTheDeviceIsBack()
    {
        LeaveRestorePending();

        _ducker.RetryRestore();
        Assert.NotNull(_store.Record);
        Assert.Empty(_warnings);

        _devices.SetState(Mic, AudioDeviceState.Active);
        _ducker.RetryRestore();

        Assert.Equal(["mute:mic=False", "delete"], _log);
        Assert.Contains("restored", Assert.Single(_warnings));
    }

    [Fact]
    public void RetryRestore_NothingPending_DoesNothing()
    {
        _ducker.RetryRestore();

        Assert.Empty(_log);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void RetryRestore_MidClip_LeavesTheClipsChangeAlone()
    {
        _ducker.Mute();

        _ducker.RetryRestore();

        Assert.True(_control.Levels[Mic].Muted);
        Assert.NotNull(_store.Record);
    }

    [Fact]
    public void RetryRestore_DuringAClipThatCouldntDuck_RestoresTheLeftover()
    {
        LeaveRestorePending();
        _ducker.Mute(); // skipped: the leftover can't be restored yet

        _devices.SetState(Mic, AudioDeviceState.Active);
        _ducker.RetryRestore();

        Assert.False(_control.Levels[Mic].Muted);
        Assert.Null(_store.Record);
    }

    [Fact]
    public void RetryRestore_DeviceGone_ForgetsAndWarns()
    {
        LeaveRestorePending();
        _devices.Devices.RemoveAt(0);

        _ducker.RetryRestore();

        Assert.Null(_store.Record);
        Assert.Contains("is gone", Assert.Single(_warnings));
    }

    [Fact]
    public void ConfiguringOff_RetriesAPendingRestore()
    {
        LeaveRestorePending();
        _devices.SetState(Mic, AudioDeviceState.Active);

        _ducker.Configure(MicDuckSettings.Off);

        Assert.False(_control.Levels[Mic].Muted);
        Assert.Null(_store.Record);
    }

    [Fact]
    public void ConfiguringOffMidClip_RestoresAtTheEndOfTheClip()
    {
        _ducker.Mute();

        _ducker.Configure(MicDuckSettings.Off);
        Assert.True(_control.Levels[Mic].Muted);

        _ducker.Unmute();
        Assert.False(_control.Levels[Mic].Muted);
    }

    [Fact]
    public void EmergencyRestore_MidClip_RestoresDeletesAndStopsFurtherDucking()
    {
        _ducker.Mute();

        _ducker.EmergencyRestore(TimeSpan.Zero);

        Assert.False(_control.Levels[Mic].Muted);
        Assert.Null(_store.Record);

        _log.Clear();
        _ducker.Mute();
        _ducker.Unmute();
        Assert.Empty(_log);
    }

    [Fact]
    public void EmergencyRestore_NothingApplied_DoesNothing()
    {
        _ducker.EmergencyRestore(TimeSpan.Zero);

        Assert.Empty(_log);
    }

    [Fact]
    public void EmergencyRestore_Failing_KeepsTheRecordWithoutThrowing()
    {
        _ducker.Mute();
        _control.ThrowOnChange = true;

        _ducker.EmergencyRestore(TimeSpan.Zero);

        Assert.NotNull(_store.Record);
    }

    [Fact]
    public void EmergencyRestore_WhileACallIsStuck_RestoresButKeepsTheRecordForStartup()
    {
        using var stuck = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _control.BeforeSetMute = muted =>
        {
            if (muted)
            {
                stuck.Set();
                release.Wait();
            }
        };
        var worker = new Thread(_ducker.Mute);
        worker.Start();
        stuck.Wait();

        _ducker.EmergencyRestore(TimeSpan.FromMilliseconds(20));

        Assert.Contains("mute:mic=False", _log);
        Assert.NotNull(_store.Record);
        release.Set();
        worker.Join();
    }

    [Fact]
    public void ThrowingWarningHandler_DoesNotEscape()
    {
        _ducker.Warning += _ => throw new InvalidOperationException("handler");
        _control.ThrowOnRead = true;

        _ducker.Mute();

        Assert.Single(_warnings);
    }
}
