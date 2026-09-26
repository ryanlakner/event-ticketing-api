using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ticketing.Api.IntegrationTests;

/// <summary>
/// Keeps openapi/ticketing-api.v1.json identical to what the API serves. Clients (the web app)
/// generate their types from that file, so any contract change shows up as a reviewable diff.
/// </summary>
public sealed class OpenApiContractTests(TicketingApiFactory factory)
    : IClassFixture<TicketingApiFactory>
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task OpenApi_document_matches_the_committed_snapshot()
    {
        var served = await factory
            .CreateClient()
            .GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        var actual = Normalize(served);
        var snapshot = Path.Combine(RepositoryRoot(), "openapi", "ticketing-api.v1.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllTextAsync(snapshot, actual, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(snapshot)
            .ShouldBeTrue($"Missing {snapshot}. Run: {UpdateVariable}=1 dotnet test");
        var expected = Normalize(
            await File.ReadAllTextAsync(snapshot, TestContext.Current.CancellationToken)
        );
        actual.ShouldBe(
            expected,
            $"The API contract changed. If intended, run `{UpdateVariable}=1 dotnet test` and commit openapi/ticketing-api.v1.json."
        );
    }

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Ticketing.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
