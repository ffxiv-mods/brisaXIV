namespace BrisaXIV.Core;

// Finds the Japanese original of a text rendered in the client's language.
public interface ISourceTextResolver
{
    bool TryResolve(string display, string scope, out string japanese);
}
