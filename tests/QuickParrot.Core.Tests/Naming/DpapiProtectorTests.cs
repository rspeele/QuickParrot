using QuickParrot.Core.Naming;

namespace QuickParrot.Core.Tests.Naming;

public class DpapiProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        var protector = new DpapiProtector();

        var ciphertext = protector.Protect("sk-super-secret");

        Assert.True(protector.TryUnprotect(ciphertext, out var plaintext));
        Assert.Equal("sk-super-secret", plaintext);
    }

    [Fact]
    public void Protect_DoesNotStorePlaintext()
    {
        var protector = new DpapiProtector();

        var ciphertext = protector.Protect("sk-super-secret");

        Assert.DoesNotContain("sk-super-secret", ciphertext);
    }

    [Fact]
    public void TryUnprotect_GarbageInput_FailsCleanlyInsteadOfThrowing()
    {
        var protector = new DpapiProtector();

        Assert.False(protector.TryUnprotect("not-valid-base64!!", out var plaintext));
        Assert.Equal("", plaintext);
    }
}
