using System.Text.Json;
using System.Collections.Concurrent;

namespace BrisaXIV.Core;

public sealed record DeferredTranslation(string Text, string Group, int Expansion, int Failures,
    DateTimeOffset Due, string Error, bool Single, int AfterSavedCount = 0, DateTimeOffset NotBefore = default);

public sealed class TranslationRetryPolicy
{
    public DateTimeOffset Now => clock();
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<int, TimeSpan> delay;
    public TranslationRetryPolicy() : this(() => DateTimeOffset.UtcNow,
        attempt => TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(4, Math.Min(5, attempt - 1))))) { }
    internal TranslationRetryPolicy(Func<DateTimeOffset> clock, Func<int, TimeSpan> delay)
    { this.clock = clock; this.delay = delay; }
    public DateTimeOffset Schedule(int attempt, TimeSpan? minimum)
    {
        var wait = delay(attempt);
        if (minimum > wait) wait = minimum.Value;
        return Now + wait;
    }
    public static int TranslationGap(int attempt) => Math.Min(500, 50 * (1 << Math.Min(4, Math.Max(0, attempt - 1))));
}

// Persist failures only, never credentials or raw responses. Records remain in this
// journal while a retry is in flight, and are removed only after a confirmed TXT write.
internal sealed class TranslationRetryJournal
{
    private sealed class JournalSlot { public readonly object Gate = new(); public TranslationRetryJournal? Owner; }
    private static readonly ConcurrentDictionary<string, JournalSlot> slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly JournalSlot? slot;
    private readonly Dictionary<string, DeferredTranslation> entries = new(StringComparer.Ordinal);
    private readonly string? path;
    private long revision, savedRevision;
    public string? Warning { get; private set; }
    public TranslationRetryJournal(string? path)
    {
        this.path = path == null ? null : Path.GetFullPath(path);
        if (path == null) return;
        slot = slots.GetOrAdd(this.path!, _ => new());
        lock (slot.Gate)
        {
            // Reapplying a translator cancels its old workers asynchronously. Flush
            // their snapshot before takeover, then prevent late old cleanup writes
            // from overwriting the new queue's pending records.
            slot.Owner?.Flush(); slot.Owner = this;
            try
            {
                if (!File.Exists(this.path)) return;
                foreach (var item in JsonSerializer.Deserialize<DeferredTranslation[]>(File.ReadAllText(this.path)) ?? [])
                    if (item != null && !string.IsNullOrWhiteSpace(item.Text) && item.Text.Length <= 1_000_000 && item.Failures >= 0)
                        entries[TextRules.Normalize(item.Text)] = item with { Text = TextRules.Normalize(item.Text) };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            { Warning = "Não foi possível ler as pendências. A conferência do catálogo recuperará textos ausentes no TXT."; }
        }
    }
    public DeferredTranslation[] Snapshot() { lock (gate) return entries.Values.ToArray(); }
    public void Put(DeferredTranslation item) { lock (gate) { entries[item.Text] = item; revision++; } }
    public void Remove(string text) { lock (gate) if (entries.Remove(text)) revision++; }
    public void Flush()
    {
        if (path == null || slot == null) return;
        lock (slot.Gate)
        {
            if (!ReferenceEquals(slot.Owner, this)) return;
            DeferredTranslation[] snapshot; long current;
            lock (gate) { current = revision; if (current == savedRevision) return; snapshot = entries.Values.ToArray(); }
            var temporary = path + "." + Guid.NewGuid() + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot)); File.Move(temporary, path, true);
                lock (gate) savedRevision = current;
                Warning = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Warning = "Falha ao salvar pendências de nova tentativa. Elas continuam na memória; o catálogo será conferido contra o TXT ao reiniciar."; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
