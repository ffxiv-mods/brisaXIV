namespace BrisaXIV.Core;

// Incremental audit of every catalog original, including positions before the saved
// cursor. Cache writes only add entries; reloading the file restarts the audit.
public sealed class CatalogVerification(PreparationCatalog catalog, TranslationFile file)
{
    private readonly HashSet<int> missing = new();
    private long generation = -1;
    private int refreshCursor;
    private int repairCursor;
    public int Checked { get; private set; }
    public bool Ready => catalog.Ready && Checked == catalog.Ordered.Count && generation == file.Generation;
    public int MissingCount => missing.Count;
    public long MissingCharacters { get; private set; }
    public int ConfirmedCount => Math.Max(0, Checked - missing.Count);
    public long ConfirmedCharacters => catalog.ConfirmedCharacters(Checked) - MissingCharacters;
    public void Restart() { generation = file.Generation; Checked = 0; refreshCursor = repairCursor = 0; missing.Clear(); MissingCharacters = 0; }
    private long Characters(int index) => catalog.ConfirmedCharacters(index + 1) - catalog.ConfirmedCharacters(index);
    public void Pump(int budget = 500)
    {
        if (!catalog.Ready) return;
        if (generation != file.Generation) Restart();
        for (var n = 0; n < budget && Checked < catalog.Ordered.Count; n++, Checked++)
            if (!file.TryGet(catalog.Ordered[Checked].Text, out _)) { missing.Add(Checked); MissingCharacters += Characters(Checked); }
        // Cycle through positions rather than enumerate/allocate the missing set every frame.
        for (var n = 0; n < budget && Checked > 0; n++)
        {
            if (refreshCursor >= Checked) refreshCursor = 0;
            var index = refreshCursor++;
            if (missing.Contains(index) && file.TryGet(catalog.Ordered[index].Text, out _))
            { missing.Remove(index); MissingCharacters -= Characters(index); }
        }
    }
    public IEnumerable<int> MissingIndices(int maximum)
    {
        var result = new List<int>();
        for (var n = 0; n < Math.Min(5000, Checked) && result.Count < maximum; n++)
        {
            if (repairCursor >= Checked) repairCursor = 0;
            var index = repairCursor++;
            if (missing.Contains(index)) result.Add(index);
        }
        return result;
    }
}
