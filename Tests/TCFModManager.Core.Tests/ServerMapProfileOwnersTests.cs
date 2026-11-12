using System.Text.Json;
using TCFModManager.Core.ServerMap;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapProfileOwnersTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tcfmm-owners-" + Guid.NewGuid().ToString("N"));

    public ServerMapProfileOwnersTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AssigningWritesTheModsFormatAndKeepsEveryoneElse()
    {
        File.WriteAllText(Path.Combine(_directory, ServerMapProfileOwners.FileName),
            """{ "schemaVersion": 1, "owners": { "6abee519842869498c34024b": "22222222-2222-2222-2222-222222222222" } }""");

        Assert.True(ServerMapProfileOwners.TryAssign(_directory, "6ac8acae28793133fcefe63e", "11111111-1111-1111-1111-111111111111"));

        var owners = ServerMapProfileOwners.Read(_directory);
        Assert.Equal("11111111-1111-1111-1111-111111111111", owners["6ac8acae28793133fcefe63e"]);
        Assert.Equal("22222222-2222-2222-2222-222222222222", owners["6abee519842869498c34024b"]);

        using var written = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, ServerMapProfileOwners.FileName)));
        Assert.Equal(1, written.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void GivingAProfileToNobodyRemovesItsOwner()
    {
        ServerMapProfileOwners.TryAssign(_directory, "6ac8acae28793133fcefe63e", "11111111-1111-1111-1111-111111111111");

        ServerMapProfileOwners.TryAssign(_directory, "6ac8acae28793133fcefe63e", null);

        Assert.Empty(ServerMapProfileOwners.Read(_directory));
    }

    [Fact]
    public void TheMachinesComeFromTheModsClientsFile()
    {
        File.WriteAllText(Path.Combine(_directory, ServerMapProfileOwners.ClientsFileName), """
            { "schemaVersion": 1, "clients": [
              { "clientId": "22222222-2222-2222-2222-222222222222", "displayName": "Zed's PC" },
              { "clientId": "11111111-1111-1111-1111-111111111111", "displayName": "Alpha" },
              { "clientId": "not-a-guid", "displayName": "Broken" } ] }
            """);

        var machines = ServerMapProfileOwners.Machines(_directory);

        Assert.Equal(["Alpha", "Zed's PC"], machines.Select(m => m.DisplayName));
    }
}
