namespace QuickParrot.Core.Naming;

/// <summary>Protects secrets (e.g. the LiteLLM API key) at rest. Kept behind an interface so tests never touch DPAPI.</summary>
public interface IDpapiProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> and returns base64 ciphertext for storage in settings.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts base64 ciphertext produced by <see cref="Protect"/>. False means it couldn't be decrypted
    /// (e.g. the settings file was copied from another user/machine) and should be treated as unset.</summary>
    bool TryUnprotect(string base64Ciphertext, out string plaintext);
}
