using System.Text.Json;

namespace TCFModManagement.Core.Serialization;

// Shared JSON options for the sp-mod.com API, mapping PascalCase properties to snake_case fields.
public static class SpModJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
