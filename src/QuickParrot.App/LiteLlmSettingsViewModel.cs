using System.Net.Http;
using QuickParrot.App.Mvvm;
using QuickParrot.Core.Naming;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>The LiteLLM clip-naming settings and their connection test.</summary>
public sealed class LiteLlmSettingsViewModel : ObservableObject
{
    private static readonly (string, Func<AppSettings, object?>)[] Properties =
    [
        (nameof(BaseUrl), s => s.LiteLlmBaseUrl),
        (nameof(TranscriptionModel), s => s.LiteLlmTranscriptionModel),
        (nameof(ChatModel), s => s.LiteLlmChatModel),
        (nameof(ApiKeyPlaceholder), s => s.LiteLlmApiKeyEncrypted is null),
    ];

    private readonly SettingsMirror _settings;
    private readonly StatusViewModel _status;
    private readonly HttpClient _httpClient;
    private readonly IDpapiProtector _protector;
    private bool _isTesting;
    private string _connectionTestResult = "";

    public LiteLlmSettingsViewModel(SettingsMirror settings, StatusViewModel status, HttpClient httpClient, IDpapiProtector protector)
    {
        _settings = settings;
        _status = status;
        _httpClient = httpClient;
        _protector = protector;
        _settings.Changed += (old, now) => OnPropertiesChanged(old, now, Properties);
    }

    public string BaseUrl
    {
        get => _settings.Current.LiteLlmBaseUrl ?? "";
        set => _settings.Update(s => s with { LiteLlmBaseUrl = value });
    }

    public string TranscriptionModel
    {
        get => _settings.Current.LiteLlmTranscriptionModel;
        set => _settings.Update(s => s with { LiteLlmTranscriptionModel = value });
    }

    public string ChatModel
    {
        get => _settings.Current.LiteLlmChatModel;
        set => _settings.Update(s => s with { LiteLlmChatModel = value });
    }

    public string ApiKeyPlaceholder =>
        _settings.Current.LiteLlmApiKeyEncrypted is null ? "(not set)" : "(saved — type to replace)";

    public bool CanTestConnection => !_isTesting;

    public string TestConnectionLabel => _isTesting ? "Testing…" : "Test connection";

    public string ConnectionTestResult
    {
        get => _connectionTestResult;
        private set => SetField(ref _connectionTestResult, value);
    }

    /// <summary>Called from the password box's PasswordChanged handler; never bound, so the plaintext never round-trips through XAML.</summary>
    public void SetApiKey(string plaintext)
    {
        var encrypted = string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);
        _settings.Update(s => s with { LiteLlmApiKeyEncrypted = encrypted });
    }

    public async Task TestConnectionAsync()
    {
        if (_isTesting)
            return;

        var (options, warning) = LiteLlmOptionsResolver.Resolve(_settings.Current, _protector);
        if (warning is not null)
            _status.Report(warning);

        if (options is null)
        {
            ConnectionTestResult = "Set a base URL first.";
            return;
        }

        SetTesting(true);
        try
        {
            var namer = new LiteLlmClipNamer(_httpClient, options);
            var result = await namer.TestConnectionAsync(CancellationToken.None);
            ConnectionTestResult = result.Message;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or UriFormatException)
        {
            ConnectionTestResult = $"That base URL isn't usable: {e.Message}";
        }
        finally
        {
            SetTesting(false);
        }
    }

    private void SetTesting(bool testing)
    {
        _isTesting = testing;
        OnPropertyChanged(nameof(CanTestConnection));
        OnPropertyChanged(nameof(TestConnectionLabel));
    }
}
