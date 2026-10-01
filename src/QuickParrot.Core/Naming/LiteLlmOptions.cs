using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Naming;

/// <summary>Resolved, ready-to-use LiteLLM connection settings (API key already decrypted into memory).</summary>
public sealed record LiteLlmOptions(string BaseUrl, string? ApiKey, string TranscriptionModel, string ChatModel);

public static class LiteLlmOptionsResolver
{
    /// <summary>
    /// Builds <see cref="LiteLlmOptions"/> from <see cref="AppSettings"/>, decrypting the API key.
    /// Returns a null <c>Options</c> when the feature is off (no base URL). A non-null <c>Warning</c> means the
    /// stored key couldn't be decrypted (e.g. settings copied from another user) and was treated as unset.
    /// </summary>
    public static (LiteLlmOptions? Options, string? Warning) Resolve(AppSettings settings, IDpapiProtector protector)
    {
        if (string.IsNullOrWhiteSpace(settings.LiteLlmBaseUrl))
            return (null, null);

        string? apiKey = null;
        string? warning = null;
        if (!string.IsNullOrEmpty(settings.LiteLlmApiKeyEncrypted))
        {
            if (protector.TryUnprotect(settings.LiteLlmApiKeyEncrypted, out var plaintext))
                apiKey = plaintext;
            else
                warning = "Couldn't decrypt the stored LiteLLM API key (it may have been copied from another user or machine); continuing without it.";
        }

        var transcriptionModel = string.IsNullOrWhiteSpace(settings.LiteLlmTranscriptionModel)
            ? "whisper-1" : settings.LiteLlmTranscriptionModel;
        var chatModel = string.IsNullOrWhiteSpace(settings.LiteLlmChatModel)
            ? "gpt-4o-mini" : settings.LiteLlmChatModel;

        return (new LiteLlmOptions(settings.LiteLlmBaseUrl, apiKey, transcriptionModel, chatModel), warning);
    }
}
