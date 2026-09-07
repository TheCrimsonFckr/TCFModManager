namespace TCFModManager.Core.Models;

// Envelope for single-resource endpoints: <c>{ "success": true, "data": {...} }</c>.
public sealed class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
}

// Envelope for list endpoints: <c>{ "success", "data": [...], "links", "meta" }</c>.
public sealed class PagedResult<T>
{
    public bool Success { get; set; }
    public List<T> Data { get; set; } = [];
    public PageLinks? Links { get; set; }
    public PageMeta? Meta { get; set; }
}

public sealed class PageLinks
{
    public string? First { get; set; }
    public string? Last { get; set; }
    public string? Prev { get; set; }
    public string? Next { get; set; }
}

public sealed class PageMeta
{
    public int CurrentPage { get; set; }

    //
    // NULLABLE, and that is not defensive tidiness - the API really sends null.
    //
    // These are the 1-based index of the first and last row ON THIS PAGE, and Laravel's paginator
    // has nothing to put in them when the page is empty, so it emits `"from": null, "to": null`
    // rather than 0. Declared as plain int, System.Text.Json does not shrug at that: it throws
    // "The JSON value could not be converted to System.Int32. Path: $.meta.from", which surfaces as
    // a failure of whatever asked - a mod list apply dying whole because ONE of its entries pinned a
    // version that is no longer published and the version filter matched nothing.
    //
    // Nothing reads either value; they are here to describe the envelope faithfully.
    //
    public int? From { get; set; }
    public int? To { get; set; }

    public int LastPage { get; set; }
    public string? Path { get; set; }
    public int PerPage { get; set; }
    public int Total { get; set; }
}

// Error envelope: <c>{ "success": false, "code": "...", "message": "..." }</c>.
public sealed class ApiErrorResponse
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
}
