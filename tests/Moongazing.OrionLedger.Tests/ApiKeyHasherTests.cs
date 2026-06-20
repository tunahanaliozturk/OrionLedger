namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger.Keys;

using Xunit;

public sealed class ApiKeyHasherTests
{
    [Fact]
    public void Hash_matches_the_known_sha256_vector()
    {
        // SHA-256("abc") is a published NIST test vector.
        var expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        Assert.Equal(expected, ApiKeyHasher.Hash("abc"));
    }

    [Fact]
    public void Hash_is_deterministic_for_the_same_input()
    {
        Assert.Equal(ApiKeyHasher.Hash("ork_token"), ApiKeyHasher.Hash("ork_token"));
    }

    [Fact]
    public void Hash_differs_for_different_inputs()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("ork_a"), ApiKeyHasher.Hash("ork_b"));
    }

    [Fact]
    public void Hash_is_sensitive_to_a_single_character_change()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("ork_token"), ApiKeyHasher.Hash("ork_tokeN"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ork_token")]
    [InlineData("a-very-long-token-value-with-many-characters-0123456789")]
    public void Hash_is_a_64_character_lowercase_hex_digest(string token)
    {
        var hash = ApiKeyHasher.Hash(token);

        Assert.Equal(64, hash.Length);
        foreach (var c in hash)
        {
            var isLowerHex = char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f');
            Assert.True(isLowerHex, $"unexpected character '{c}' in digest");
        }
    }

    [Fact]
    public void Hash_of_a_long_token_uses_the_pooled_path_and_stays_deterministic()
    {
        // A token whose UTF-8 size exceeds the stack threshold takes the pooled-buffer branch. The pooled
        // path must produce the same digest as a fresh hash of the same input (and the buffer is cleared on
        // return so no secret material leaks to the next renter).
        var token = new string('k', 4096);

        var first = ApiKeyHasher.Hash(token);
        var second = ApiKeyHasher.Hash(token);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hash_rejects_a_null_or_empty_token(string? token)
    {
        // ThrowIfNullOrEmpty raises ArgumentNullException for null, ArgumentException for empty.
        Assert.ThrowsAny<ArgumentException>(() => ApiKeyHasher.Hash(token!));
    }

    [Fact]
    public void FixedTimeEquals_is_true_for_equal_hashes()
    {
        var h = ApiKeyHasher.Hash("ork_token");
        Assert.True(ApiKeyHasher.FixedTimeEquals(h, h));
    }

    [Fact]
    public void FixedTimeEquals_is_true_for_two_independent_hashes_of_the_same_input()
    {
        Assert.True(ApiKeyHasher.FixedTimeEquals(ApiKeyHasher.Hash("ork_token"), ApiKeyHasher.Hash("ork_token")));
    }

    [Fact]
    public void FixedTimeEquals_is_false_for_different_hashes()
    {
        var a = ApiKeyHasher.Hash("ork_token");
        var b = ApiKeyHasher.Hash("ork_other");
        Assert.False(ApiKeyHasher.FixedTimeEquals(a, b));
    }

    [Fact]
    public void FixedTimeEquals_is_false_for_strings_of_different_length()
    {
        // CryptographicOperations.FixedTimeEquals returns false for length mismatch rather than throwing.
        Assert.False(ApiKeyHasher.FixedTimeEquals("abc", "abcd"));
    }

    [Fact]
    public void FixedTimeEquals_is_true_for_two_empty_strings()
    {
        Assert.True(ApiKeyHasher.FixedTimeEquals("", ""));
    }

    [Theory]
    [InlineData(null, "abc")]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void FixedTimeEquals_rejects_a_null_argument(string? a, string? b)
    {
        Assert.Throws<ArgumentNullException>(() => ApiKeyHasher.FixedTimeEquals(a!, b!));
    }
}
