using System.Net;
using System.Text;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class OsvDatabaseTests
{
    private static readonly SbomComponent[] Packages =
    [
        new("Newtonsoft.Json", "12.0.1", "pkg:nuget/Newtonsoft.Json@12.0.1", true, "MIT"),
        new("Markdig", "0.40.0", "pkg:nuget/Markdig@0.40.0", true, "BSD-2-Clause"),
    ];

    [Fact]
    public async Task AsksForEveryPackageByUrlAndReturnsTheAffectedOnes()
    {
        using var http = new FakeHttp().Answer(OsvDatabase.BatchEndpoint.AbsoluteUri, Encoding.UTF8.GetBytes("""
            { "results": [ { "vulns": [ { "id": "GHSA-5crp-9r3c-p9vr", "modified": "2024-01-01T00:00:00Z" } ] }, {} ] }
            """));
        using var client = new HttpClient(http);

        var affected = await new OsvDatabase(client).QueryAsync(Packages, TestContext.Current.CancellationToken);

        Assert.Equal("""{"queries":[{"package":{"purl":"pkg:nuget/Newtonsoft.Json@12.0.1"}},{"package":{"purl":"pkg:nuget/Markdig@0.40.0"}}]}""", Assert.Single(http.Requests).Body);
        var advisory = Assert.Single(affected);
        Assert.Equal(("Newtonsoft.Json", "12.0.1", "GHSA-5crp-9r3c-p9vr"), (advisory.Id, advisory.Version, Assert.Single(advisory.Advisories)));
    }

    [Fact]
    public async Task ReportsADatabaseItCannotAsk()
    {
        using var http = new FakeHttp().Answer(OsvDatabase.BatchEndpoint.AbsoluteUri, [], HttpStatusCode.InternalServerError);
        using var client = new HttpClient(http);

        await Assert.ThrowsAsync<ToolException>(() => new OsvDatabase(client).QueryAsync(Packages, TestContext.Current.CancellationToken));
    }
}
