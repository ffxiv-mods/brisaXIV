using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BrisaXIV;

// Windows DPAPI: the saved API key can only be decrypted under this Windows user.
internal static class KeyProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public nint Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);

    public static string Protect(string key) => string.IsNullOrWhiteSpace(key) ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(key.Trim()), true));
    public static string Unprotect(string saved) => string.IsNullOrEmpty(saved) ? "" : Encoding.UTF8.GetString(Transform(Convert.FromBase64String(saved), false));

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect
                ? CryptProtectData(ref input, "BrisaXIV API key", 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            Array.Clear(bytes);
            if (output.Data != 0) LocalFree(output.Data);
        }
    }
}
