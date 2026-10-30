using System.Runtime.InteropServices;
using WinMarchy.Core.Backup;

namespace WinMarchy.Core.Platform;

/// <summary>
/// The lock screen picture of the signed-in user, through Windows.System.UserProfile.LockScreen. Needs no
/// admin rights. A policy (Personalization → LockScreenImage, NoChangingLockScreen) wins over it; the
/// policy check on the log page shows that.
///
/// WinMarchy has no WinRT projection (it would add 25 MB), so the three calls go straight through
/// the COM interfaces. IDs and slot order are those of the Windows SDK metadata.
/// </summary>
public sealed class WindowsLockScreen : ILockScreenAccess
{
    const string ClassName = "Windows.System.UserProfile.LockScreen";
    static readonly Guid ILockScreenStatics = new("3ee9d3ad-b607-40ae-b426-7631d9821269");
    static readonly Guid IRandomAccessStream = new("905a0fe1-bc53-11df-8c49-001e4fc686da");
    static readonly Guid IAsyncInfo = new("00000036-0000-0000-c000-000000000046");
    static readonly Guid IStream = new("0000000c-0000-0000-c000-000000000046");

    // Slots after IUnknown (0–2) and IInspectable (3–5).
    const int GetImageStreamSlot = 7, SetImageStreamAsyncSlot = 9;
    const int AsyncStatusSlot = 7, AsyncErrorCodeSlot = 8;
    const int StreamReadSlot = 3;

    /// <summary>The picture shown now, or null if Windows does not hand it out (Spotlight, policy).</summary>
    public byte[]? Current() => OnWinRtThread(() =>
    {
        var statics = Factory();
        IntPtr ra = IntPtr.Zero, stream = IntPtr.Zero;
        try
        {
            Check(Slot<OutFn>(statics, GetImageStreamSlot)(statics, out ra));
            if (ra == IntPtr.Zero) return null;
            var iid = IStream;
            Check(CreateStreamOverRandomAccessStream(ra, ref iid, out stream));
            return ReadAll(stream);
        }
        finally
        {
            Release(stream);
            Release(ra);
            Release(statics);
        }
    });

    /// <summary>Sets a JPEG, PNG or BMP file as the lock screen picture and waits until Windows took it.</summary>
    public void Set(string path) => OnWinRtThread<object?>(() =>
    {
        var statics = Factory();
        IntPtr ra = IntPtr.Zero, op = IntPtr.Zero;
        try
        {
            var iid = IRandomAccessStream;
            Check(CreateRandomAccessStreamOnFile(Path.GetFullPath(path), 0 /* FileAccessMode.Read */, ref iid, out ra));
            Check(Slot<InOutFn>(statics, SetImageStreamAsyncSlot)(statics, ra, out op));
            Wait(op);
            return null;
        }
        finally
        {
            Release(op);
            Release(ra);
            Release(statics);
        }
    });

    static IntPtr Factory()
    {
        Check(WindowsCreateString(ClassName, ClassName.Length, out var hstring));
        try
        {
            var iid = ILockScreenStatics;
            Check(RoGetActivationFactory(hstring, ref iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>Polls the IAsyncAction until it is done (at most 15 seconds).</summary>
    static void Wait(IntPtr operation)
    {
        var iid = IAsyncInfo;
        Check(Marshal.QueryInterface(operation, in iid, out var info));
        try
        {
            var status = Slot<IntOutFn>(info, AsyncStatusSlot);
            for (int i = 0; i < 300; i++)
            {
                Check(status(info, out var s));
                if (s == 1) return;                       // Completed
                if (s == 3)                               // Error
                {
                    Check(Slot<IntOutFn>(info, AsyncErrorCodeSlot)(info, out var hr));
                    Marshal.ThrowExceptionForHR(hr);
                }
                if (s == 2) throw new OperationCanceledException();
                Thread.Sleep(50);
            }
            throw new TimeoutException();
        }
        finally
        {
            Release(info);
        }
    }

    static byte[] ReadAll(IntPtr stream)
    {
        var read = Slot<ReadFn>(stream, StreamReadSlot);
        using var ms = new MemoryStream();
        var buffer = new byte[1 << 16];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            while (true)
            {
                Check(read(stream, handle.AddrOfPinnedObject(), buffer.Length, out var got));
                if (got <= 0) break;
                ms.Write(buffer, 0, got);
                if (ms.Length > 64L << 20) throw new InvalidDataException("lock screen image > 64 MB");
            }
        }
        finally
        {
            handle.Free();
        }
        return ms.ToArray();
    }

    /// <summary>WinRT wants an initialised thread; a short-lived MTA thread keeps the UI thread out of it.</summary>
    static T OnWinRtThread<T>(Func<T> work)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            // S_OK, S_FALSE (already) or RPC_E_CHANGED_MODE: each leaves the thread usable for WinRT.
            RoInitialize(1 /* RO_INIT_MULTITHREADED */);
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "WinMarchy lock screen" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20))) throw new TimeoutException();
        if (error != null) throw error;
        return result;
    }

    static T Slot<T>(IntPtr obj, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));

    static void Release(IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.Release(p);
    }

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutFn(IntPtr self, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int InOutFn(IntPtr self, IntPtr value, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntOutFn(IntPtr self, out int result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadFn(IntPtr self, IntPtr buffer, int size, out int read);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("shcore.dll", CharSet = CharSet.Unicode)]
    static extern int CreateRandomAccessStreamOnFile(string filePath, uint accessMode, ref Guid riid, out IntPtr stream);

    [DllImport("shcore.dll")]
    static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid riid, out IntPtr stream);
}
