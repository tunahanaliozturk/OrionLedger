namespace Moongazing.OrionLedger.Tests;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionLedger;

using Xunit;

/// <summary>
/// Option validation coverage. <see cref="ApiKeyOptions.Validate"/> is internal but reachable
/// through <c>AddOrionLedger</c>, which validates eagerly at registration time.
/// </summary>
public sealed class ApiKeyOptionsTests
{
    private static void Configure(Action<ApiKeyOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddOrionLedger(configure);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        var ex = Record.Exception(() => Configure(_ => { }));
        Assert.Null(ex);
    }

    [Fact]
    public void Default_prefix_and_secret_length_match_the_documented_values()
    {
        var options = new ApiKeyOptions();
        Assert.Equal("ork_", options.Prefix);
        Assert.Equal(32, options.SecretByteLength);
        Assert.Null(options.DefaultLifetime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_prefix_is_rejected(string? prefix)
    {
        // ThrowIfNullOrEmpty raises ArgumentNullException for null, ArgumentException for empty.
        Assert.ThrowsAny<ArgumentException>(() => Configure(o => o.Prefix = prefix!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(15)]
    public void A_secret_length_below_sixteen_is_rejected(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Configure(o => o.SecretByteLength = length));
    }

    [Fact]
    public void A_secret_length_of_exactly_sixteen_is_accepted()
    {
        var ex = Record.Exception(() => Configure(o => o.SecretByteLength = 16));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void A_non_positive_default_lifetime_is_rejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Configure(o => o.DefaultLifetime = TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void A_positive_default_lifetime_is_accepted()
    {
        var ex = Record.Exception(() => Configure(o => o.DefaultLifetime = TimeSpan.FromMinutes(1)));
        Assert.Null(ex);
    }

    [Fact]
    public void A_custom_prefix_is_accepted()
    {
        var ex = Record.Exception(() => Configure(o => o.Prefix = "live_"));
        Assert.Null(ex);
    }
}
