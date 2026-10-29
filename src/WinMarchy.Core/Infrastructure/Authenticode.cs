using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace WinMarchy.Core;

/// <summary>Checks the Authenticode signature of a downloaded installer (WinVerifyTrust).</summary>
public static class Authenticode
{
    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>True if the file has a valid signature whose certificate subject contains "O=&lt;organization&gt;".</summary>
    public static bool IsSignedBy(string file, string organization) =>
        Subject(file) is { } subject && subject.Contains("O=" + organization, StringComparison.OrdinalIgnoreCase);

    /// <summary>Organization (O=) of a valid signature, else its common name (CN=); null when unsigned or invalid.</summary>
    public static string? Signer(string file)
    {
        var subject = Subject(file);
        if (subject == null) return null;
        string? Part(string key) =>
            subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim('"');
        return Part("O") ?? Part("CN") ?? subject;
    }

    /// <summary>Certificate subject of a valid Authenticode signature, or null.</summary>
    static string? Subject(string file)
    {
        if (!OperatingSystem.IsWindows() || !Verify(file)) return null;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    static bool Verify(string file)
    {
        var fileInfo = new WinTrustFileInfo { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = file };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UIChoice = 2,               // WTD_UI_NONE
                RevocationChecks = 0,       // WTD_REVOKE_NONE: no online check through the proxy
                UnionChoice = 1,            // WTD_CHOICE_FILE
                FileInfo = filePtr,
                ProvFlags = 0x1000,         // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData data);
}
