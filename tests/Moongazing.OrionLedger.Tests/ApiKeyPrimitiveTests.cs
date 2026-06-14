namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger.Keys;

using Xunit;

public sealed class ApiKeyPrimitiveTests
{
    [Fact]
    public void Generated_tokens_carry_the_prefix_and_are_unique()
    {
        var a = ApiKeyGenerator.Generate("ork_", 32);
        var b = ApiKeyGenerator.Generate("ork_", 32);

        Assert.StartsWith("ork_", a, StringComparison.Ordinal);
        Assert.StartsWith("ork_", b, StringComparison.Ordinal);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Generated_tokens_are_url_safe()
    {
        var token = ApiKeyGenerator.Generate("ork_", 32);
        var secret = token["ork_".Length..];

        Assert.DoesNotContain('+', secret);
        Assert.DoesNotContain('/', secret);
        Assert.DoesNotContain('=', secret);
    }

    [Fact]
    public void Display_prefix_is_the_leading_twelve_characters()
    {
        var token = "ork_AbCdEfGhIjKlMnOp";
        Assert.Equal("ork_AbCdEfGh", ApiKeyGenerator.DisplayPrefix(token));
    }

    [Fact]
    public void Hash_is_stable_and_distinct()
    {
        Assert.Equal(ApiKeyHasher.Hash("ork_token"), ApiKeyHasher.Hash("ork_token"));
        Assert.NotEqual(ApiKeyHasher.Hash("ork_a"), ApiKeyHasher.Hash("ork_b"));
        Assert.Equal(64, ApiKeyHasher.Hash("ork_token").Length);
    }

    [Fact]
    public void FixedTimeEquals_matches_value_equality()
    {
        var h = ApiKeyHasher.Hash("ork_token");
        Assert.True(ApiKeyHasher.FixedTimeEquals(h, h));
        Assert.False(ApiKeyHasher.FixedTimeEquals(h, ApiKeyHasher.Hash("ork_other")));
    }

    [Fact]
    public void Too_short_a_secret_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiKeyGenerator.Generate("ork_", 8));
    }
}
