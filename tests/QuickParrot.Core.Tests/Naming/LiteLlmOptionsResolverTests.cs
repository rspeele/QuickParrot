using QuickParrot.Core.Naming;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Naming;

public class LiteLlmOptionsResolverTests
{
    private sealed class FakeProtector : IDpapiProtector
    {
        public bool ShouldFail { get; set; }

        public string Protect(string plaintext) => $"encrypted:{plaintext}";

        public bool TryUnprotect(string base64Ciphertext, out string plaintext)
        {
            if (ShouldFail || !base64Ciphertext.StartsWith("encrypted:"))
            {
                plaintext = "";
                return false;
            }

            plaintext = base64Ciphertext["encrypted:".Length..];
            return true;
        }
    }

    [Fact]
    public void NoBaseUrl_FeatureIsOff()
    {
        var (options, warning) = LiteLlmOptionsResolver.Resolve(new AppSettings(), new FakeProtector());

        Assert.Null(options);
        Assert.Null(warning);
    }

    [Fact]
    public void BaseUrlAndKeySet_DecryptsKey_NoWarning()
    {
        var protector = new FakeProtector();
        var settings = new AppSettings
        {
            LiteLlmBaseUrl = "https://litellm.example.com",
            LiteLlmApiKeyEncrypted = protector.Protect("sk-test"),
        };

        var (options, warning) = LiteLlmOptionsResolver.Resolve(settings, protector);

        Assert.NotNull(options);
        Assert.Equal("sk-test", options!.ApiKey);
        Assert.Null(warning);
    }

    [Fact]
    public void KeyFailsToDecrypt_TreatedAsUnset_WithWarning()
    {
        var settings = new AppSettings
        {
            LiteLlmBaseUrl = "https://litellm.example.com",
            LiteLlmApiKeyEncrypted = "garbage-from-another-machine",
        };

        var (options, warning) = LiteLlmOptionsResolver.Resolve(settings, new FakeProtector());

        Assert.NotNull(options);
        Assert.Null(options!.ApiKey);
        Assert.NotNull(warning);
    }

    [Fact]
    public void NoKeyConfigured_ApiKeyIsNull_NoWarning()
    {
        var settings = new AppSettings { LiteLlmBaseUrl = "https://litellm.example.com" };

        var (options, warning) = LiteLlmOptionsResolver.Resolve(settings, new FakeProtector());

        Assert.NotNull(options);
        Assert.Null(options!.ApiKey);
        Assert.Null(warning);
    }

    [Fact]
    public void ToString_RedactsTheApiKey()
    {
        var options = new LiteLlmOptions("https://litellm.example.com", "sk-super-secret", "whisper-1", "gpt-4o-mini");

        var text = options.ToString();

        Assert.DoesNotContain("sk-super-secret", text);
        Assert.Contains("ApiKey = ***", text);
        Assert.Contains("https://litellm.example.com", text);
    }

    [Fact]
    public void ToString_NoKey_ShowsNull()
    {
        var options = new LiteLlmOptions("https://litellm.example.com", null, "whisper-1", "gpt-4o-mini");

        Assert.Contains("ApiKey = null", options.ToString());
    }

    [Fact]
    public void ModelNames_FallBackToDefaultsWhenBlank()
    {
        var settings = new AppSettings
        {
            LiteLlmBaseUrl = "https://litellm.example.com",
            LiteLlmTranscriptionModel = "",
            LiteLlmChatModel = "",
        };

        var (options, _) = LiteLlmOptionsResolver.Resolve(settings, new FakeProtector());

        Assert.Equal("whisper-1", options!.TranscriptionModel);
        Assert.Equal("gpt-4o-mini", options.ChatModel);
    }
}
