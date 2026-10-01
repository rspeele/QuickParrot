using System.Security.Cryptography;
using System.Text;

namespace QuickParrot.Core.Naming;

// CA1416: this project's TFM isn't "-windows" (it stays portable for testability), but QuickParrot itself is
// Windows-only (see README), so calling DPAPI here is safe despite the analyzer's platform-compat warning.
#pragma warning disable CA1416

/// <summary>Windows DPAPI, scoped to the current user, so the ciphertext only decrypts on the same Windows account.</summary>
public sealed class DpapiProtector : IDpapiProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QuickParrot.LiteLlmApiKey");

    public string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public bool TryUnprotect(string base64Ciphertext, out string plaintext)
    {
        try
        {
            var encrypted = Convert.FromBase64String(base64Ciphertext);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            plaintext = "";
            return false;
        }
    }
}

#pragma warning restore CA1416
