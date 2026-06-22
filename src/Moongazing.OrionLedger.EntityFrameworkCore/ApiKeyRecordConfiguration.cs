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
/// <remarks>
/// Subject matching is defined by the contract as ordinal and case sensitive (see
/// <see cref="Storage.IApiKeyStore.FindBySubjectAsync"/>). <see cref="EfApiKeyStore{TContext}"/>
/// enforces that in code with a final ordinal filter, so bulk revoke is correct on every provider
/// regardless of the database collation. Collation names are provider specific, so this configuration
/// does not hard-code one; if you also query the subject column directly (outside the store) on a
/// database whose default collation is case insensitive, pass a provider-appropriate case-sensitive
/// collation through the constructor to make the column itself match ordinally (for example
/// <c>SQL_Latin1_General_CP1_CS_AS</c> on SQL Server, <c>C</c> on PostgreSQL; SQLite is already
/// case sensitive for ASCII).
/// </remarks>
public sealed class ApiKeyRecordConfiguration : IEntityTypeConfiguration<ApiKeyRecord>
{
    /// <summary>The default table name. Override by configuring the entity yourself if it clashes.</summary>
    public const string DefaultTableName = "OrionLedgerApiKeys";

    private static readonly JsonSerializerOptions ScopesJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string tableName;
    private readonly string? subjectCollation;

    /// <summary>Map to the default table name (<see cref="DefaultTableName"/>).</summary>
    public ApiKeyRecordConfiguration()
        : this(DefaultTableName)
    {
    }

    /// <summary>Map to a caller-supplied table name.</summary>
    /// <param name="tableName">The table to map the entity to. Must not be null or empty.</param>
    public ApiKeyRecordConfiguration(string tableName)
        : this(tableName, subjectCollation: null)
    {
    }

    /// <summary>Map to a caller-supplied table name and pin the subject column collation.</summary>
    /// <param name="tableName">The table to map the entity to. Must not be null or empty.</param>
    /// <param name="subjectCollation">
    /// A provider-specific case-sensitive collation to apply to the subject column, or null to leave it
    /// at the database default. The store enforces ordinal subject matching in code regardless; supply a
    /// collation only when you also query the subject column directly on a case-insensitive database and
    /// want the column itself to match case sensitively. Must be a collation the target provider knows,
    /// or model creation fails.
    /// </param>
    public ApiKeyRecordConfiguration(string tableName, string? subjectCollation)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        this.tableName = tableName;
        this.subjectCollation = subjectCollation;
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

        var subject = builder.Property(r => r.Subject)
            .HasMaxLength(256);

        // Pin a case-sensitive collation on the column when the caller supplied one for their provider.
        // The store's FindBySubjectAsync applies a final ordinal filter so bulk revoke is case sensitive
        // regardless of this, but a pinned collation also makes direct queries against the column match
        // ordinally on a database whose default collation is case insensitive. Collation names are
        // provider specific, so this is opt-in rather than a hard-coded default.
        if (!string.IsNullOrEmpty(subjectCollation))
        {
            subject.UseCollation(subjectCollation);
        }

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
