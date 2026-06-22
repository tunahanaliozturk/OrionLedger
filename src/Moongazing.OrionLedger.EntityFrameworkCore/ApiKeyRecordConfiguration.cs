namespace Moongazing.OrionLedger.EntityFrameworkCore;

using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Maps <see cref="ApiKeyRecord"/> to a relational table. Apply it from your own
/// <see cref="DbContext.OnModelCreating(ModelBuilder)"/> with
/// <c>modelBuilder.ApplyConfiguration(new ApiKeyRecordConfiguration())</c>, or use the bundled
/// <see cref="OrionLedgerDbContext"/>. The hash carries a unique index because it is the
/// verification lookup key; the subject carries a non-unique index because it backs bulk revoke.
/// </summary>
public sealed class ApiKeyRecordConfiguration : IEntityTypeConfiguration<ApiKeyRecord>
{
    /// <summary>The default table name. Override by configuring the entity yourself if it clashes.</summary>
    public const string DefaultTableName = "OrionLedgerApiKeys";

    private static readonly JsonSerializerOptions ScopesJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string tableName;

    /// <summary>Map to the default table name (<see cref="DefaultTableName"/>).</summary>
    public ApiKeyRecordConfiguration()
        : this(DefaultTableName)
    {
    }

    /// <summary>Map to a caller-supplied table name.</summary>
    /// <param name="tableName">The table to map the entity to. Must not be null or empty.</param>
    public ApiKeyRecordConfiguration(string tableName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        this.tableName = tableName;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApiKeyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(tableName);

        // The id is the primary key. It is an init-only opaque string assigned at issuance (a GUID
        // "N" by default), so it is never database-generated.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .ValueGeneratedNever()
            .HasMaxLength(128);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.Subject)
            .HasMaxLength(256);

        builder.Property(r => r.DisplayPrefix)
            .IsRequired()
            .HasMaxLength(128);

        // The hash is a 64-character lowercase hex SHA-256 digest. It is the verification lookup key,
        // so it is fixed-length, required, and uniquely indexed: two records can never share a hash,
        // and the lookup is an index seek rather than a scan.
        builder.Property(r => r.Hash)
            .IsRequired()
            .HasMaxLength(128);
        builder.HasIndex(r => r.Hash)
            .IsUnique();

        // Subject is nullable and non-unique; the index supports FindBySubjectAsync (bulk revoke) and
        // is filtered to non-null rows where the provider supports filtered indexes, so keys issued
        // without a subject do not bloat it.
        builder.HasIndex(r => r.Subject);

        ConfigureScopes(builder);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.ExpiresAt);
        builder.Property(r => r.RevokedAt);
        builder.Property(r => r.LastUsedAt);

        builder.Property(r => r.LastUsedCount)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(r => r.SupersededAt);
        builder.Property(r => r.SupersededById)
            .HasMaxLength(128);
        builder.Property(r => r.RetiresAt);
    }

    private static void ConfigureScopes(EntityTypeBuilder<ApiKeyRecord> builder)
    {
        // Scopes are an ordinal, case-sensitive set of arbitrary strings, so a delimiter-joined
        // column would be ambiguous if a scope contained the delimiter. JSON is collision free and
        // round-trips the exact set. A ValueComparer is mandatory for a converted collection: without
        // it EF Core cannot detect mutations or snapshot the value, and change tracking silently
        // misbehaves.
        var converter = new ValueConverter<IReadOnlySet<string>, string>(
            scopes => Serialize(scopes),
            json => Deserialize(json));

        var comparer = new ValueComparer<IReadOnlySet<string>>(
            (left, right) => SetEquals(left, right),
            scopes => HashSet(scopes),
            scopes => Snapshot(scopes));

        builder.Property(r => r.Scopes)
            .HasConversion(converter, comparer)
            .IsRequired();
    }

    private static string Serialize(IReadOnlySet<string> scopes) =>
        JsonSerializer.Serialize(scopes.OrderBy(s => s, StringComparer.Ordinal), ScopesJsonOptions);

    private static HashSet<string> Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var items = JsonSerializer.Deserialize<string[]>(json, ScopesJsonOptions) ?? [];
        return new HashSet<string>(items, StringComparer.Ordinal);
    }

    private static bool SetEquals(IReadOnlySet<string>? left, IReadOnlySet<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Count == right.Count && left.SetEquals(right);
    }

    private static int HashSet(IReadOnlySet<string> scopes)
    {
        // Order independent so two equal sets hash equally regardless of enumeration order.
        var hash = 0;
        foreach (var scope in scopes)
        {
            hash ^= StringComparer.Ordinal.GetHashCode(scope);
        }

        return hash;
    }

    private static HashSet<string> Snapshot(IReadOnlySet<string> scopes) =>
        new(scopes, StringComparer.Ordinal);
}
