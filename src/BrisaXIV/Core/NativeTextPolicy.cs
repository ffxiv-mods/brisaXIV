namespace BrisaXIV.Core;

public static class NativeTextPolicy
{
    // Visibility changes include the game's close/fade animation. They must not cause
    // our original text to flash back into the still-drawn window.
    public static bool ShouldRestore(bool restoreOnly, bool enabled, bool nativeHud, bool included)
        => restoreOnly || !enabled || !nativeHud || !included;

    public static bool MatchesAppliedText(bool identityMatches, string current, string original, string rendered)
        => identityMatches && (current == original || current == rendered);
}
