namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger.Keys;

using Xunit;

public sealed class ApiKeyGeneratorTests
{
    [Theory]
    [InlineData("ork_")]
    [InlineData("live_")]
    [InlineData("test_")]
    [InlineData("x")]
    public void Generated_tokens_start_with_the_supplied_prefix(string prefix)
    {
        var token = ApiKeyGenerator.Generate(prefix, 32);
        Assert.StartsWith(prefix, token, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_tokens_are_unique_across_many_draws()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 1_000; i++)
        {
            Assert.True(seen.Add(ApiKeyGenerator.Generate("ork_", 32)), "duplicate token generated");
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void Secret_length_grows_with_the_requested_byte_count(int secretByteLength)
    {
        var token = ApiKeyGenerator.Generate("ork_", secretByteLength);
        var secret = token["ork_".Length..];

        // Base64url without padding encodes n bytes in ceil(n * 4 / 3) characters.
        var expected = (int)Math.Ceiling(secretByteLength * 4 / 3d);
        Assert.Equal(expected, secret.Length);
    }

    [Fact]
    public void Larger_secrets_produce_longer_tokens()
    {
        var small = ApiKeyGenerator.Generate("ork_", 16);
        var large = ApiKeyGenerator.Generate("ork_", 64);

        Assert.True(large.Length > small.Length);
    }

    [Fact]
    public void Generated_secret_is_url_safe_and_unpadded()
    {
        var token = ApiKeyGenerator.Generate("ork_", 48);
        var secret = token["ork_".Length..];

        Assert.DoesNotContain('+', secret);
        Assert.DoesNotContain('/', secret);
        Assert.DoesNotContain('=', secret);
    }

    [Fact]
    public void Generated_secret_uses_only_base64url_alphabet()
    {
        var token = ApiKeyGenerator.Generate("ork_", 32);
        var secret = token["ork_".Length..];

        foreach (var c in secret)
        {
            var allowed = char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';
            Assert.True(allowed, $"unexpected character '{c}' in secret");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_null_or_empty_prefix_is_rejected(string? prefix)
    {
        // ThrowIfNullOrEmpty throws ArgumentNullException for null and ArgumentException for empty;
        // ThrowsAny accepts the null subclass too.
        Assert.ThrowsAny<ArgumentException>(() => ApiKeyGenerator.Generate(prefix!, 32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(15)]
    public void A_secret_below_the_minimum_byte_count_is_rejected(int secretByteLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiKeyGenerator.Generate("ork_", secretByteLength));
    }

    [Fact]
    public void The_minimum_secret_byte_count_is_accepted()
    {
        var token = ApiKeyGenerator.Generate("ork_", 16);
        Assert.StartsWith("ork_", token, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_prefix_is_the_leading_twelve_characters()
    {
        var token = "ork_AbCdEfGhIjKlMnOp";
        Assert.Equal("ork_AbCdEfGh", ApiKeyGenerator.DisplayPrefix(token));
        Assert.Equal(ApiKeyGenerator.DisplayPrefixLength, ApiKeyGenerator.DisplayPrefix(token).Length);
    }

    [Fact]
    public void Display_prefix_returns_the_whole_token_when_it_is_short()
    {
        Assert.Equal("ork_short", ApiKeyGenerator.DisplayPrefix("ork_short"));
    }

    [Fact]
    public void Display_prefix_returns_the_whole_token_at_exactly_the_boundary_length()
    {
        var token = new string('a', ApiKeyGenerator.DisplayPrefixLength);
        Assert.Equal(token, ApiKeyGenerator.DisplayPrefix(token));
    }

    [Fact]
    public void Display_prefix_of_a_real_generated_token_is_non_secret_and_recognisable()
    {
        var token = ApiKeyGenerator.Generate("ork_", 32);
        var display = ApiKeyGenerator.DisplayPrefix(token);

        Assert.StartsWith("ork_", display, StringComparison.Ordinal);
        Assert.StartsWith(display, token, StringComparison.Ordinal);
        Assert.True(token.Length > display.Length, "the display prefix must not expose the full token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Display_prefix_rejects_a_null_or_empty_token(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(() => ApiKeyGenerator.DisplayPrefix(token!));
    }

    [Fact]
    public void Decoding_the_secret_recovers_the_requested_byte_count()
    {
        const int secretByteLength = 32;
        var token = ApiKeyGenerator.Generate("ork_", secretByteLength);
        var secret = token["ork_".Length..];

        // Reverse the base64url transform and re-pad so the entropy can be measured.
        var base64 = secret.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        var bytes = Convert.FromBase64String(base64);

        Assert.Equal(secretByteLength, bytes.Length);
    }
}
