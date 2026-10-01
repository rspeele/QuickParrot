using QuickParrot.Core.Editing;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Naming;

/// <summary>Builds the clip editor's "Suggest name" function from the LiteLLM settings.</summary>
public static class LiteLlmNaming
{
    /// <summary>
    /// Null when LiteLLM naming isn't configured; otherwise a namer that resolves <paramref name="settings"/> fresh on
    /// every call, so a mid-session change takes effect without reopening the editor.
    /// </summary>
    /// <param name="warn">Told when the stored API key can't be decrypted; called on the namer's calling thread.</param>
    public static Func<EditableAudio, CancellationToken, Task<NameSuggestion>>? CreateSuggester(
        Func<AppSettings> settings, IDpapiProtector protector, HttpClient httpClient, Action<string> warn)
    {
        var (options, _) = LiteLlmOptionsResolver.Resolve(settings(), protector);
        if (options is null)
            return null;

        return async (audio, ct) =>
        {
            var (resolved, warning) = LiteLlmOptionsResolver.Resolve(settings(), protector);
            if (warning is not null)
                warn(warning);
            if (resolved is null)
                return new NameSuggestion(null);

            var namer = new LiteLlmClipNamer(httpClient, resolved);
            return await namer.SuggestAsync(audio.Samples, audio.SampleRate, audio.Channels, ct);
        };
    }
}
