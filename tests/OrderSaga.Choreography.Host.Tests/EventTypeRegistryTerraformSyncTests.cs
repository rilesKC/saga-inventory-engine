using System.Text.RegularExpressions;

namespace OrderSaga.Choreography.Host.Tests;

/// <summary>
/// EventTypeRegistry's known event types and Terraform's EventBridge rule list
/// (infra/modules/messaging/eventbridge.tf) are two independent, hand-maintained lists with no
/// shared source of truth -- add a type to one and forget the other, and PutEvents succeeds while
/// no rule matches it, silently dropping the event before it ever reaches SQS. This test is the
/// guardrail: it parses the Terraform list directly out of the .tf file and fails if it and
/// EventTypeRegistry's keys (the same list OutboundEventForwarder subscribes to) ever diverge.
/// </summary>
public class EventTypeRegistryTerraformSyncTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InventoryEngine.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (InventoryEngine.slnx) above " + AppContext.BaseDirectory);
    }

    private static IReadOnlySet<string> ReadTerraformEventTypes()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "modules", "messaging", "eventbridge.tf");
        var text = File.ReadAllText(path);

        var listMatch = Regex.Match(text, @"event_types\s*=\s*\[(?<items>[^\]]*)\]", RegexOptions.Singleline);
        Assert.True(listMatch.Success, $"Could not find a 'event_types = [...]' local in {path}.");

        return Regex.Matches(listMatch.Groups["items"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    [Fact]
    public void TerraformEventTypeList_MatchesEventTypeRegistry()
    {
        var terraformTypes = ReadTerraformEventTypes();
        var registryTypes = EventTypeRegistry.KnownEventTypeNames;

        Assert.NotEmpty(terraformTypes);
        Assert.Equal(registryTypes.OrderBy(t => t, StringComparer.Ordinal), terraformTypes.OrderBy(t => t, StringComparer.Ordinal));
    }
}
