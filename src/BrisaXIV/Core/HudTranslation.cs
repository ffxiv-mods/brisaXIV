namespace BrisaXIV.Core;

// The debounce protects API calls, never a lookup in the already loaded TXT.
public static class HudTranslation
{
    public static TranslationView Get(TranslationRouter router, string source, string category, bool priority, long unchangedMilliseconds)
    {
        var cached = router.Get(source, false, category);
        return cached.Text != null || unchangedMilliseconds < 350
            ? cached : router.Get(source, true, category, priority);
    }
}
