namespace Misty.Application.DTOs.Common;

public record CursorPagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}
