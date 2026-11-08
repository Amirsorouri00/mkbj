using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sourcing;

/// <summary>
/// The import side's guarantees that live in the database.
/// </summary>
/// <remarks>
/// The fingerprint index is the one that matters most. "One listing, one record" is enforced by
/// the aggregate on the path the pipeline takes, but two workers racing on the same listing both
/// find nothing and both insert — so the index is what actually decides, and nothing above it
/// can be trusted to.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class SourcingSchemaTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static long _next = DateTimeOffset.UtcNow.Ticks;

    private static long NextId() => Interlocked.Increment(ref _next);

    [Fact]
    public async Task One_Fingerprint_Is_One_Record_Whatever_Races()
    {
        using var factory = fixture.CreateFactory();
        var fingerprint = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())
            .PadRight(64, '0')[..64];

        await InsertRecordAsync(factory, fingerprint);

        // The second worker's insert, which the aggregate could not have refused: it looked
        // first, found nothing, and lost the race.
        var act = () => InsertRecordAsync(factory, fingerprint);

        (await Refused(act)).Should().Be("ux_staged_records_fingerprint");
    }

    [Fact]
    public async Task One_Policy_Per_Domain()
    {
        using var factory = fixture.CreateFactory();
        var domain = $"shop-{NextId()}.example.com";

        await InsertPolicyAsync(factory, domain);

        // Two rows for one supplier would make "may we use this?" depend on which is read first.
        (await Refused(() => InsertPolicyAsync(factory, domain)))
            .Should().Be("ux_source_policies_domain");
    }

    [Fact]
    public async Task A_Price_Observation_Must_Be_Above_Zero()
    {
        using var factory = fixture.CreateFactory();
        var (listingId, observationId) = await InsertListingAsync(factory);

        (await Refused(() => ExecuteAsync(
            "UPDATE sourcing.price_observations SET amount_minor = 0 WHERE id = @id",
            ("id", observationId))))
            .Should().Be("ck_price_observations_amount");

        _ = listingId;
    }

    [Fact]
    public async Task An_Import_Jobs_Limits_Are_Bounded_In_The_Database_Too()
    {
        using var factory = fixture.CreateFactory();
        var jobId = await InsertJobAsync(factory);

        // The aggregate refuses this already. The constraint is what survives a hand-written
        // UPDATE during an incident.
        (await Refused(() => ExecuteAsync(
            "UPDATE sourcing.import_jobs SET max_pages = 99999 WHERE id = @id", ("id", jobId))))
            .Should().Be("ck_import_jobs_limits");
    }

    [Fact]
    public async Task A_Staged_Records_Issues_And_Match_Survive_A_Round_Trip()
    {
        using var factory = fixture.CreateFactory();
        var fingerprint = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())
            .PadRight(64, '0')[..64];

        var recordId = await InsertRecordAsync(factory, fingerprint, record =>
            record.RecordProcessing(
                """{"name":"A kettle"}""",
                ["staging/1/a.jpg"],
                new MatchResult(MatchKind.Fuzzy, null, "title", [new FuzzyCandidateRef(9, "t", 0.72)]),
                [new ValidationIssue(ValidationCodes.RightsUnknown, IssueSeverity.Review, "no policy")],
                DateTimeOffset.UtcNow));

        using var scope = factory.Services.CreateScope();
        var reloaded = await scope.ServiceProvider.GetRequiredService<IStagedRecordRepository>()
            .GetAsync(recordId, Ct);

        // jsonb columns that deserialize to something subtly different are the quiet kind of
        // bug: everything works until a reviewer opens the queue.
        reloaded!.Status.Should().Be(StagedRecordStatus.NeedsReview);
        reloaded.Match.Kind.Should().Be(MatchKind.Fuzzy);
        reloaded.Match.Candidates.Should().ContainSingle()
            .Which.Similarity.Should().BeApproximately(0.72, 0.0001);
        reloaded.Issues.Should().ContainSingle()
            .Which.Should().Be(new ValidationIssue(
                ValidationCodes.RightsUnknown, IssueSeverity.Review, "no policy"));
        reloaded.ImageKeys.Should().Equal("staging/1/a.jpg");
    }

    private static async Task<long> InsertRecordAsync(
        WebApplicationFactory<Program> factory, string fingerprint,
        Action<StagedRecord>? process = null)
    {
        using var scope = factory.Services.CreateScope();

        var record = StagedRecord.Stage(
            NextId(), NextId(), new Fingerprint(fingerprint), Listing(), "{}", DateTimeOffset.UtcNow);

        process?.Invoke(record);

        await scope.ServiceProvider.GetRequiredService<IStagedRecordRepository>()
            .AddAsync(record, Ct);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

        return record.Id;
    }

    private static async Task InsertPolicyAsync(
        WebApplicationFactory<Program> factory, string domain)
    {
        using var scope = factory.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ISourcePolicyRepository>()
            .AddAsync(
                SourcePolicy.Create(NextId(), domain, true, true, true, null, DateTimeOffset.UtcNow),
                Ct);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
    }

    private static async Task<(long ListingId, long ObservationId)> InsertListingAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        var listing = SourceListing.Create(
            NextId(), $"https://shop.example.com/p/{NextId()}", "shop.example.com", null,
            "A kettle", [], "policy:shop.example.com", NextId(), DateTimeOffset.UtcNow);

        var observation = listing.Observe(
            NextId(), "sig", 1_999, "SGD", true, DateTimeOffset.UtcNow);

        await scope.ServiceProvider.GetRequiredService<ISourceListingRepository>()
            .AddAsync(listing, Ct);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

        return (listing.Id, observation.Id);
    }

    private static async Task<long> InsertJobAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        var job = ImportJob.Create(
            NextId(),
            new CreateImportJobSpec(
                ImportMode.Crawl,
                ["https://shop.example.com/catalogue"],
                [],
                null,
                [],
                [],
                ImportLimits.Default,
                PublishPolicy.AutoPublishValidated,
                false),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        await scope.ServiceProvider.GetRequiredService<IImportJobRepository>().AddAsync(job, Ct);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

        return job.Id;
    }

    private static RawListing Listing() => new(
        "https://shop.example.com/p/1",
        "shop.example.com",
        "SKU-1",
        DateTimeOffset.UtcNow,
        ExtractionMethod.JsonLd,
        new System.Text.Json.Nodes.JsonObject { ["name"] = "A kettle" },
        null,
        [],
        null,
        "cursor");

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>The constraint that refused, or null if nothing did.</summary>
    private static async Task<string?> Refused(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (Exception exception) when (Postgres(exception) is { } postgres)
        {
            return postgres.ConstraintName;
        }
    }

    private static PostgresException? Postgres(Exception exception) => exception switch
    {
        PostgresException postgres => postgres,
        { InnerException: { } inner } => Postgres(inner),
        _ => null,
    };
}
