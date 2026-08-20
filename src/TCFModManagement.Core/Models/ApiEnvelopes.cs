namespace TCFModManagement.Core.Models;

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
    public int From { get; set; }
    public int LastPage { get; set; }
    public string? Path { get; set; }
    public int PerPage { get; set; }
    public int To { get; set; }
    public int Total { get; set; }
}

// Error envelope: <c>{ "success": false, "code": "...", "message": "..." }</c>.
public sealed class ApiErrorResponse
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
}
