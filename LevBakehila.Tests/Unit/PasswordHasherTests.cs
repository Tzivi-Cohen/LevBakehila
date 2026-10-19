using LevBakehila.Service.Security;

namespace LevBakehila.Tests.Unit;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainPlainPassword_AndVerifiesCorrectly()
    {
        var hash = _hasher.Hash("Secret123!");

        Assert.DoesNotContain("Secret123!", hash);
        Assert.True(_hasher.Verify("Secret123!", hash));
        Assert.False(_hasher.Verify("Wrong123!", hash));
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        Assert.NotEqual(_hasher.Hash("Secret123!"), _hasher.Hash("Secret123!"));
    }
}
