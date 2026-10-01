using System.Runtime.InteropServices;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QuickParrot.Core.Editing;

namespace QuickParrot.Audio;

/// <summary>
/// Saves clips as ~192 kbps MP3 with Windows Media Foundation's encoder, or as 16-bit WAV where that's unavailable
/// (e.g. Windows N editions without the Media Feature Pack). Writes a temp file, then renames it into place.
/// </summary>
public sealed class ClipEncoder : IClipEncoder
{
    private const int Mp3BitRate = 192_000;

    // The MP3 encoder takes 16-bit PCM at MPEG-1 rates, mono or stereo.
    private static readonly int[] Mp3SampleRates = [32000, 44100, 48000];

    public SavedClip Save(EditableAudio audio, string folder, string fileStem, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var tempPath = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp"); // not an audio extension, so the library ignores it
        try
        {
            string extension;
            string? warning = null;
            try
            {
                EncodeMp3(audio, tempPath);
                extension = ".mp3";
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EncodeWav(audio, tempPath);
                extension = ".wav";
                warning = $"MP3 encoding isn't available on this PC ({e.Message.Trim()}), so the clip was saved as WAV.";
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new SavedClip(MoveToUniqueName(tempPath, folder, fileStem, extension), warning);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void EncodeMp3(EditableAudio audio, string path)
    {
        MediaFoundationApi.Startup();
        var source = Mp3Input(audio);
        var pcm = new SampleToWaveProvider16(source);
        var mediaType = MediaFoundationEncoder.SelectMediaType(AudioSubtypes.MFAudioFormat_MP3, pcm.WaveFormat, Mp3BitRate)
            ?? throw new NotSupportedException("no MP3 encoder for this format");

        using var encoder = new MediaFoundationEncoder(mediaType);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        encoder.Encode(stream, pcm, TranscodeContainerTypes.MFTranscodeContainerType_MP3);
    }

    private static ISampleProvider Mp3Input(EditableAudio audio)
    {
        ISampleProvider source = new EditableAudioSampleProvider(audio, 0, audio.FrameCount);
        if (audio.Channels > 2)
            source = new ChannelMapSampleProvider(source, 2);
        if (!Mp3SampleRates.Contains(audio.SampleRate))
            source = new WdlResamplingSampleProvider(source, 48000);

        return source;
    }

    private static void EncodeWav(EditableAudio audio, string path)
    {
        TryDelete(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var writer = new WaveFileWriter(stream, new WaveFormat(audio.SampleRate, 16, audio.Channels));
        var pcm = new SampleToWaveProvider16(new EditableAudioSampleProvider(audio, 0, audio.FrameCount));
        var buffer = new byte[pcm.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = pcm.Read(buffer)) > 0)
            writer.Write(buffer, 0, read);
    }

    private static string MoveToUniqueName(string tempPath, string folder, string stem, string extension)
    {
        for (var attempt = 0; ; attempt++)
        {
            var name = ClipFileNames.UniqueFileName(stem, extension, n => File.Exists(Path.Combine(folder, n)));
            var path = Path.Combine(folder, name);
            try
            {
                File.Move(tempPath, path, overwrite: false);
                return path;
            }
            catch (IOException) when (File.Exists(path) && attempt < 10)
            {
                // Someone took the name between checking and moving; pick the next free one.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"QuickParrot: couldn't delete temp file: {e.Message}");
        }
    }
}
