using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WindowSnapper.Platforms.Windows;

internal static class Direct3D11Helper
{
    private static readonly Guid Texture2DId = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(in Guid iid, out IntPtr result);
    }

    [DllImport("d3d11.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateWinRtDevice(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.IsDisposed || device.NativePointer == IntPtr.Zero)
            throw new InvalidOperationException("The Direct3D device is not valid.");

        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
        var result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var devicePtr);
        Marshal.ThrowExceptionForHR(result);

        if (devicePtr == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the WinRT Direct3D device.");

        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr);
        }
        finally
        {
            Marshal.Release(devicePtr);
        }
    }

    public static Texture2D GetTexture(IDirect3DSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var result = access.GetInterface(Texture2DId, out var texturePtr);
        Marshal.ThrowExceptionForHR(result);

        if (texturePtr == IntPtr.Zero)
            throw new InvalidOperationException("The capture frame does not expose ID3D11Texture2D.");

        return new Texture2D(texturePtr);
    }
}
