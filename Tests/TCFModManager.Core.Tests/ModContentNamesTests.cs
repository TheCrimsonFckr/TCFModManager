using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModContentNamesTests : IDisposable
{
    private const string GunId = "6a0000000000000000000001";
    private const string ScopeId = "6a0000000000000000000002";
    private const string TraderId = "6a00000000000000000000aa";
    private const string CodeOnlyId = "6a0000000000000000000003";

    private readonly string _mod = Path.Combine(Path.GetTempPath(), "TcfModNames_" + Guid.NewGuid());

    public ModContentNamesTests() => Directory.CreateDirectory(_mod);

    public void Dispose()
    {
        if (Directory.Exists(_mod)) Directory.Delete(_mod, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string text)
    {
        var file = Path.Combine(_mod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private IReadOnlyDictionary<string, string> Find(string? language = null) =>
        ModContentNames.Find([_mod], new HashSet<string> { GunId, ScopeId, TraderId, CodeOnlyId }, language);

    [Fact]
    public void Locale_keys_name_items_and_traders_in_English_by_default()
    {
        Write("db/locales/en.json", $$"""{ "{{GunId}} Name": "Gun", "{{GunId}} ShortName": "G", "{{TraderId}} Nickname": "Trader Joe" }""");
        Write("db/locales/fr.json", $$"""{ "{{GunId}} Name": "Fusil" }""");

        var names = Find();

        Assert.Equal("Gun", names[GunId]);
        Assert.Equal("Trader Joe", names[TraderId]);
        Assert.False(names.ContainsKey(CodeOnlyId));
    }

    [Fact]
    public void The_app_language_wins_when_the_mod_has_it_and_German_reads_ge()
    {
        Write("locales/en.json", $$"""{ "{{GunId}} Name": "Gun", "{{ScopeId}} Name": "Scope" }""");
        Write("locales/ge.json", $$"""{ "{{GunId}} Name": "Gewehr" }""");

        var names = Find("de");

        Assert.Equal("Gewehr", names[GunId]);
        Assert.Equal("Scope", names[ScopeId]);
    }

    [Fact]
    public void A_locales_block_on_an_item_keyed_by_its_id_or_carrying_it_names_it()
    {
        Write("db/CustomItems/guns.json", $$"""
            {
              // a comment
              "{{GunId}}": { "itemTplToClone": "5447a9cd4bdc2dbd208b4567", "locales": { "en": { "name": "Gun", "shortName": "G" } } },
            }
            """);
        Write("items.json", $$"""[ { "newId": "{{ScopeId}}", "locales": { "ru": { "name": "Прицел" }, "en": { "name": "Scope" } } } ]""");

        var names = Find();

        Assert.Equal("Gun", names[GunId]);
        Assert.Equal("Scope", names[ScopeId]);
    }

    [Fact]
    public void A_trader_base_gives_its_nickname_and_an_item_template_its_internal_name_last()
    {
        Write("db/traders/base.json", $$"""{ "_id": "{{TraderId}}", "nickname": "Joe", "_name": "joe_trader" }""");
        Write("db/templates/items.json", $$"""
            { "{{GunId}}": { "_id": "{{GunId}}", "_name": "weapon_gun", "_props": { "Name": "Gun props", "Slots": [ { "_id": "6a0000000000000000000099", "_name": "mod_scope" } ] } } }
            """);
        Write("db/locales/en.json", $$"""{ "{{GunId}} Name": "Gun" }""");
        Write("db/templates/scope.json", $$"""{ "{{ScopeId}}": { "_id": "{{ScopeId}}", "_name": "scope_internal" } }""");

        var names = Find();

        Assert.Equal("Joe", names[TraderId]);
        Assert.Equal("Gun", names[GunId]);
        Assert.Equal("scope_internal", names[ScopeId]);
    }

    [Fact]
    public void A_broken_or_unrelated_file_is_passed_over()
    {
        Write("broken.json", $$"""{ "{{GunId}} Name": """);
        Write("other.json", """{ "x": "y" }""");
        Write("readme.txt", $$"""{ "{{GunId}} Name": "Not JSON" }""");

        Assert.Empty(Find());
    }
}
