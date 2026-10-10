namespace Stillframe.Core;

public sealed class Playlist
{
    private readonly Random random;
    private List<string> items = [];
    private int index = -1;
    public Playlist(int? seed = null) => random = seed.HasValue ? new(seed.Value) : new();
    public int Count => items.Count;
    public int Position => index < 0 ? 0 : index + 1;
    public string? Current => index >= 0 && index < items.Count ? items[index] : null;
    public string? Next => items.Count == 0 ? null : items[(index + 1) % items.Count];
    public IReadOnlyList<string> Items => items;

    public void Replace(IReadOnlyList<PhotoFile> files, ImageOrder order, string? preserve = null)
    {
        items = order == ImageOrder.ModifiedDate
            ? files.OrderBy(x => x.Modified).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.Path).ToList()
            : files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.Path).ToList();
        if (order == ImageOrder.Shuffle) random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));
        index = preserve is null ? -1 : items.FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x, preserve));
    }

    public string? Move(int direction)
    {
        if (items.Count == 0) return null;
        index = index < 0 ? (direction < 0 ? items.Count - 1 : 0) : (index + direction % items.Count + items.Count) % items.Count;
        return Current;
    }
}
