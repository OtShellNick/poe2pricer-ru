using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenCvSharp;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Poe2Pricer.Core;

// A picture of one window by Windows.Graphics.Capture: the desktop compositor hands over the
// window's own content, nothing that lies over it, and sends the window no message for it
// (measured 23.10.2026 with a window that logged its messages: none during the capture, where
// PrintWindow sent it WM_NCPAINT, WM_ERASEBKGND and WM_PAINT for every picture). The frames
// stay on the GPU; a part is copied into a staging texture and read from there (about 2 ms for
// 400×300 of a 2560×1440 window). The capture border is switched off (the game would otherwise
// wear a yellow frame), which needs Windows 11 (GraphicsCaptureSession.IsBorderRequired, build
// 20348): where that is not possible, Open gives null and Unavailable says why, and the screen
// is read instead (GameCapture). Direct3D is reached through its vtables (no interop package).
public sealed class WindowCapture : IDisposable
{
    public nint Window { get; }
    // The window went away (its capture item closed): nothing comes any more.
    public bool Closed => closed;

    // Why window capture is not available on this machine, once known; null while it is.
    public static string? Unavailable { get; private set; }
    // Where to write what happened (the program's log).
    public static Action<string>? Log { get; set; }
    private static bool availabilityChecked;
    private static readonly object availabilityGate = new();

    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly GraphicsCaptureItem item;
    private readonly object gate = new();
    private Direct3D11CaptureFrame? latest;
    private SizeInt32 size;
    private nint staging;
    private int stagingWidth, stagingHeight;
    private volatile bool closed;

    private const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int Buffers = 2;

    // null: not available here (Unavailable), or this window cannot be captured right now.
    public static WindowCapture? Open(nint window)
    {
        // (The version check is Available()'s; it stands here again only for the platform
        // analyzer, which wants to see the guard before the constructor's calls.)
        if (window == 0 || !Available() || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            return null;
        try
        {
            return new WindowCapture(window);
        }
        catch (Exception e)
        {
            Log?.Invoke($"Window capture: {e.Message}");
            return null;
        }
    }

    private static bool Available()
    {
        lock (availabilityGate)
        {
            if (availabilityChecked)
                return Unavailable is null;
            availabilityChecked = true;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                Unavailable = "needs Windows 11 (build 20348 or later)";
            else if (!GraphicsCaptureSession.IsSupported())
                Unavailable = "Windows.Graphics.Capture is not supported here";
            else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                // Asked before the border is switched off, as the documentation wants; an
                // unpackaged program gets "Allowed" without a prompt (measured 23.10.2026 on
                // build 26300). Any other answer is not a refusal by itself: whether the border
                // stays is read back from the session.
                try
                {
                    var status = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().GetAwaiter().GetResult();
                    Log?.Invoke($"Window capture: borderless access {status}");
                }
                catch (Exception e)
                {
                    Log?.Invoke($"Window capture: borderless access not asked ({e.Message})");
                }
            }
            if (Unavailable is not null)
                Log?.Invoke($"Window capture: {Unavailable}");
            return Unavailable is null;
        }
    }

    [SupportedOSPlatform("windows10.0.20348")]
    private WindowCapture(nint window)
    {
        Window = window;
        item = ItemFor(window);
        size = item.Size;
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(Device.Value.Winrt, Format, Buffers, size);
        session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        session.IsBorderRequired = false;
        if (session.IsBorderRequired)
        {
            session.Dispose();
            pool.Dispose();
            lock (availabilityGate)
                Unavailable = "the capture border cannot be switched off";
            throw new InvalidOperationException(Unavailable);
        }
        item.Closed += (_, _) => closed = true;
        session.StartCapture();
        // The first frame comes within a few tens of milliseconds; without one there is nothing
        // to read, and the caller falls back.
        if (!Await(500))
        {
            Dispose();
            throw new InvalidOperationException("no picture from the window");
        }
    }

    // The newest frame into `latest`, waiting up to `milliseconds` for one when none has come.
    private bool Await(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do
        {
            while (pool.TryGetNextFrame() is { } frame)
            {
                latest?.Dispose();
                latest = frame;
            }
            if (latest is not null)
                return true;
            Thread.Sleep(5);
        } while (Environment.TickCount64 < until);
        return false;
    }

    // The part (in pixels of the window's client area, which for the borderless game is the
    // whole window) as a BGR picture; null when nothing comes (window gone, device lost).
    public Mat? Grab(Rect part)
    {
        lock (gate)
        {
            if (closed || !Await(0))
                return null;
            var content = latest!.ContentSize;
            if (content.Width != size.Width || content.Height != size.Height)
            {
                // The window changed size: the pool follows, and the next frame is the first at
                // the new size.
                size = content;
                pool.Recreate(Device.Value.Winrt, Format, Buffers, size);
                latest.Dispose();
                latest = null;
                if (!Await(200))
                    return null;
            }
            var cut = part.Intersect(new Rect(0, 0, size.Width, size.Height));
            if (cut.Width <= 0 || cut.Height <= 0)
                return null;
            return Read(latest!, cut);
        }
    }

    private unsafe Mat? Read(Direct3D11CaptureFrame frame, Rect cut)
    {
        var device = Device.Value;
        if (staging == 0 || stagingWidth != cut.Width || stagingHeight != cut.Height)
        {
            if (staging != 0)
                Marshal.Release(staging);
            staging = 0;
            var desc = new TEXTURE2D_DESC
            {
                Width = (uint)cut.Width, Height = (uint)cut.Height, MipLevels = 1, ArraySize = 1,
                Format = 87 /* DXGI_FORMAT_B8G8R8A8_UNORM */, SampleCount = 1,
                Usage = 3 /* D3D11_USAGE_STAGING */, CPUAccessFlags = 0x20000 /* D3D11_CPU_ACCESS_READ */,
            };
            nint created;
            var createTexture2D = (delegate* unmanaged[Stdcall]<nint, TEXTURE2D_DESC*, nint, nint*, int>)Slot(device.D3d, 5);
            Marshal.ThrowExceptionForHR(createTexture2D(device.D3d, &desc, 0, &created));
            staging = created;
            stagingWidth = cut.Width;
            stagingHeight = cut.Height;
        }
        nint surface = MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface);
        nint access = 0, texture = 0;
        try
        {
            var iidAccess = IID_IDirect3DDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surface, in iidAccess, out access));
            var iidTexture = IID_ID3D11Texture2D;
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(access, 3);
            nint got;
            Marshal.ThrowExceptionForHR(getInterface(access, &iidTexture, &got));
            texture = got;
            var box = new BOX { left = (uint)cut.X, top = (uint)cut.Y, front = 0, right = (uint)cut.Right, bottom = (uint)cut.Bottom, back = 1 };
            var copySubresourceRegion = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, BOX*, void>)Slot(device.Context, 46);
            copySubresourceRegion(device.Context, staging, 0, 0, 0, 0, texture, 0, &box);
            var map = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MAPPED*, int>)Slot(device.Context, 14);
            var unmap = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Slot(device.Context, 15);
            MAPPED mapped;
            Marshal.ThrowExceptionForHR(map(device.Context, staging, 0, 1 /* D3D11_MAP_READ */, 0, &mapped));
            try
            {
                using var bgra = new Mat(cut.Height, cut.Width, MatType.CV_8UC4);
                long row = cut.Width * 4L;
                for (int y = 0; y < cut.Height; y++)
                    Buffer.MemoryCopy((void*)(mapped.Data + y * (nint)mapped.RowPitch), (void*)(bgra.Data + y * bgra.Step()), row, row);
                var bgr = new Mat();
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                return bgr;
            }
            finally
            {
                unmap(device.Context, staging, 0);
            }
        }
        finally
        {
            if (texture != 0) Marshal.Release(texture);
            if (access != 0) Marshal.Release(access);
            Marshal.Release(surface);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            session.Dispose();
            pool.Dispose();
            latest?.Dispose();
            latest = null;
            if (staging != 0)
                Marshal.Release(staging);
            staging = 0;
        }
    }

    // The capture item of a window, from the interop factory of GraphicsCaptureItem
    // (IGraphicsCaptureItemInterop::CreateForWindow, vtable slot 3).
    private static unsafe GraphicsCaptureItem ItemFor(nint window)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(ItemClass, ItemClass.Length, out nint name));
        nint factory = 0, raw = 0;
        try
        {
            var iidInterop = IID_IGraphicsCaptureItemInterop;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in iidInterop, out factory));
            var iidItem = IID_IGraphicsCaptureItem;
            var createForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(factory, 3);
            nint created;
            Marshal.ThrowExceptionForHR(createForWindow(factory, window, &iidItem, &created));
            raw = created;
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(raw);
        }
        finally
        {
            if (raw != 0) Marshal.Release(raw);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(name);
        }
    }

    // One Direct3D 11 device for the process: the raw device and its immediate context for
    // the copies, and the WinRT device the frame pool wants.
    private sealed class D3dDevice
    {
        public nint D3d { get; }
        public nint Context { get; }
        public IDirect3DDevice Winrt { get; }

        public D3dDevice()
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1 /* D3D_DRIVER_TYPE_HARDWARE */, 0, 0x20 /* D3D11_CREATE_DEVICE_BGRA_SUPPORT */, 0, 0, 7 /* D3D11_SDK_VERSION */, out nint d3d, out _, out nint context));
            D3d = d3d;
            Context = context;
            var iidDxgi = IID_IDXGIDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in iidDxgi, out nint dxgi));
            nint inspectable = 0, winrt = 0;
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                var iidDevice = IID_IDirect3DDevice;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iidDevice, out winrt));
                Winrt = MarshalInterface<IDirect3DDevice>.FromAbi(winrt);
            }
            finally
            {
                if (winrt != 0) Marshal.Release(winrt);
                if (inspectable != 0) Marshal.Release(inspectable);
                Marshal.Release(dxgi);
            }
        }
    }

    private static readonly Lazy<D3dDevice> Device = new(() => new D3dDevice());

    private const string ItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_IDirect3DDevice = new("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    // The method at `index` of a COM object's vtable.
    private static unsafe nint Slot(nint com, int index) => (*(nint**)com)[index];

    [StructLayout(LayoutKind.Sequential)]
    private struct TEXTURE2D_DESC { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BOX { public uint left, top, front, right, bottom, back; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MAPPED { public nint Data; public uint RowPitch, DepthPitch; }

    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint levels, uint sdkVersion, out nint device, out int featureLevel, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out nint hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);
}
