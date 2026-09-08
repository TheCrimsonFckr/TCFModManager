using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// Reads and writes ModListEntryScope, across the two shapes it has had.
//
// It was a plain enum - Both = 0, Client = 1, Server = 2 - written as a name by the store's
// JsonStringEnumConverter and by ModListFile. It is now a flag set, and "Both" is not a member any
// more. Every list already exported and every mod_lists.json already on disk says "Both", so
// reading that has to keep working forever: this is not a format anyone can migrate centrally,
// because the files are on other people's machines and inside servers' config folders.
//
// Numbers are accepted too. Nothing this app wrote should carry one - the store and the share file
// have always used names - but settings and list files are offered for hand-editing, and a 1 that
// someone typed meaning "client" should not take the whole list down.
//
// UNRECOGNISED INPUT READS AS Everyone, on purpose. The alternative is zero, which means "no
// machine at all" and would make an entry vanish from every install silently. Fail open: the worst
// case is a mod installed somewhere it was not needed, which is visible and harmless, against a mod
// missing from the machine hosting the raid, which is neither.
//
// Typed on the NULLABLE enum because that is what ModListEntry.Scope is, and the attribute has to
// sit on that property: an options-level JsonStringEnumConverter - which both ModListFile and
// ModListStore register - outranks a converter named on the enum type, so a type-level attribute was
// quietly ignored and every legacy "Both" came back as a parse failure.
public sealed class ModListEntryScopeConverter : JsonConverter<ModListEntryScope?>
{
    public override ModListEntryScope? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            //
            // Zero is the one number that does not mean what it says: it was Both, the old default,
            // and reading it as the empty set would empty out every list written before the flags.
            //
            var number = reader.GetInt32();
            return number == 0 ? ModListEntryScope.Everyone : (ModListEntryScope)number;
        }

        if (reader.TokenType != JsonTokenType.String) return ModListEntryScope.Everyone;

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text)) return ModListEntryScope.Everyone;

        ModListEntryScope scope = 0;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            scope |= part.ToLowerInvariant() switch
            {
                "client" => ModListEntryScope.Client,
                "server" => ModListEntryScope.Server,
                "headless" => ModListEntryScope.Headless,

                // The old name for the whole set, and the new one.
                "both" or "everyone" => ModListEntryScope.Everyone,

                // Something a later version of this app knows about and this one does not. Ignored
                // rather than refused: the entry is still an entry, and the rest of the value still
                // says something true about it.
                _ => 0,
            };
        }

        return scope == 0 ? ModListEntryScope.Everyone : scope;
    }

    public override void Write(Utf8JsonWriter writer, ModListEntryScope? value, JsonSerializerOptions options)
    {
        //
        // Never reached with null in practice - the property is JsonIgnore'd when null - but a
        // converter that throws on a value its own type allows is a trap for whoever reuses it.
        //
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(Name(value.Value));
    }

    //
    // "Everyone" rather than "Client, Server, Headless" for the full set, so the common value reads
    // as the one word it is - these files get opened and edited by hand.
    //
    public static string Name(ModListEntryScope value)
    {
        if (value == ModListEntryScope.Everyone || value == 0) return nameof(ModListEntryScope.Everyone);

        var parts = new List<string>(3);

        if (value.HasFlag(ModListEntryScope.Client)) parts.Add(nameof(ModListEntryScope.Client));
        if (value.HasFlag(ModListEntryScope.Server)) parts.Add(nameof(ModListEntryScope.Server));
        if (value.HasFlag(ModListEntryScope.Headless)) parts.Add(nameof(ModListEntryScope.Headless));

        return string.Join(", ", parts);
    }
}
