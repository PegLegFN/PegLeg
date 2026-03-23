
using Godot;
using System;
using System.Runtime.InteropServices;
using System.Text;

#if GODOT_WINDOWS
using System.Windows;
using static Godot.Gradient;
#endif

static partial class Win64Helpers
{
#if GODOT_WINDOWS
    public const bool isWindows = true;
#else
    public const bool isWindows = false;
#endif

    static nint NativeWindowHandle(Window window) => 
        new(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId()));

    static nint MainWindowHandle =>
        NativeWindowHandle(((SceneTree)Engine.GetMainLoop()).Root);

    public enum TaskbarStates
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    public static void Win64SetVisible(this Window window, bool visible = true)
    {
#if GODOT_WINDOWS
        if (!window.Visible) 
            return;
        ShowWindow(NativeWindowHandle(window), visible ? 5 : 0);
#endif
    }

    public static void Win64AddToTaskbar(this Window window)
    {
#if GODOT_WINDOWS
        if (!window.Visible)
            return;
        taskbarList.AddTab(NativeWindowHandle(window));
#endif
    }

    public static void Win64RemoveFromTaskbar(this Window window)
    {
#if GODOT_WINDOWS
        if (!window.Visible)
            return;
        taskbarList.DeleteTab(NativeWindowHandle(window));
#endif
    }

    const int CF_DIB = 8;
    const int CF_DIBV5 = 17;
    const uint GMEM_MOVEABLE = 0x0002;
    public static void ClipboardSetImage(Image image)
    {
#if GODOT_WINDOWS
        if (!OpenClipboard(MainWindowHandle))
            return;
        GD.Print("cb open");
        try
        {
            uint pngFormat = RegisterClipboardFormatA("PNG");
            uint htmlFormat = RegisterClipboardFormatA("HTML Format");

            //debug: prints the format and contents of existing clipboard data

            //uint currentFormat = 0;
            //StringBuilder formatSB = new();
            //do
            //{
            //    currentFormat = EnumClipboardFormats(currentFormat);
            //    if (currentFormat == 0)
            //        break;
            //    if (currentFormat == 2)
            //    {
            //        GD.Print("Handle to bitmap");
            //        continue;
            //    }
            //    bool utf8 = currentFormat == htmlFormat;
            //    if (GetClipboardFormatNameA(currentFormat, formatSB, 16) != 0)
            //    {
            //        var formatString = formatSB.ToString();
            //        formatSB.Clear();
            //        GD.Print(formatString);
            //    }
            //    else
            //    {
            //        GD.Print("Unknown format: " + currentFormat);
            //    }
            //    try
            //    {
            //        var curPtr = GetClipboardData(currentFormat);
            //        var size = GlobalSize(curPtr);
            //        var lockedPtr = GlobalLock(curPtr);
            //        try
            //        {
            //            byte[] bytes = new byte[size];
            //            for (int i = 0; i < bytes.Length; i++)
            //            {
            //                bytes[i] = Marshal.ReadByte(lockedPtr, i);
            //            }
            //            string content = utf8 ? Encoding.UTF8.GetString(bytes) : Convert.ToHexString(bytes);
            //            GD.Print(content);
            //        }
            //        finally
            //        {
            //            GlobalUnlock(curPtr);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        GD.PushError(ex);
            //    }
            //}
            //while (currentFormat != 0);

            //GD.Print("png available");
            if (!EmptyClipboard())
                return;
            GD.Print("clipboard emptied");

            GD.Print("copying PNG");
            var rawPNG = image.SavePngToBuffer();

            try
            {
                var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)rawPNG.Length);
                if (handle != default)
                {
                    var ptr = GlobalLock(handle);
                    Marshal.Copy(rawPNG, 0, ptr, rawPNG.Length);

                    SetClipboardData(pngFormat, ptr);

                    GlobalUnlock(handle);
                    GD.Print("PNG copied");
                }
            }
            catch (Exception e)
            {
                GD.PushError(e);
            }

            GD.Print("copying HTML");
            var htmlDoc = $"""
                Version:0.9
                StartHTML:0000000105
                EndHTML:0000000198
                StartFragment:0000000141
                EndFragment:0000000162
                <html>
                <body>
                <!--StartFragment--><img src="image.png"><!--EndFragment-->
                </body>
                </html>
                """;
            byte[] htmlBytes = Encoding.UTF8.GetBytes(htmlDoc);
            try
            {
                var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)htmlBytes.Length);
                if (handle != default)
                {
                    var ptr = GlobalLock(handle);
                    Marshal.Copy(htmlBytes, 0, ptr, htmlBytes.Length);

                    SetClipboardData(htmlFormat, ptr);

                    GlobalUnlock(handle);
                    GD.Print("HTML copied");
                }
            }
            catch (Exception e)
            {
                GD.PushError(e);
            }

            GD.Print("copying DIBV5");
            var imageSize = image.GetSize();
            var bitmapHeader = BitmapV5Header.Create(imageSize.X, imageSize.Y, 32);
            var pixCount = imageSize.X * imageSize.Y;
            byte[] bitmapBytes = new byte[pixCount * 4];
            for (int i = 0; i < pixCount; i++)
            {
                var pixel = image.GetPixel(i % imageSize.X, (imageSize.Y-1) - (i / imageSize.X));
                int byteStart = i * 4;
                bitmapBytes[byteStart  ] = (byte)pixel.B8;
                bitmapBytes[byteStart+1] = (byte)pixel.G8;
                bitmapBytes[byteStart+2] = (byte)pixel.R8;
                bitmapBytes[byteStart+3] = (byte)pixel.A8;
            }

            try
            {
                var headerSize = (int)bitmapHeader._biSize;
                var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)(headerSize + bitmapBytes.Length));
                if (handle != default)
                {
                    var ptr = GlobalLock(handle);
                    Marshal.StructureToPtr(bitmapHeader, ptr, false);
                    Marshal.Copy(bitmapBytes, 0, IntPtr.Add(ptr, headerSize), bitmapBytes.Length);

                    SetClipboardData(CF_DIBV5, ptr);

                    GlobalUnlock(handle);
                    GD.Print("DIBV5 copied");
                }
            }
            catch (Exception e)
            {
                GD.PushError(e);
            }
        }
        finally
        {
            CloseClipboard();
            GD.Print("cb closed");
        }
#endif
    }

#if GODOT_WINDOWS
    static readonly ITaskbarList3 taskbarList = (ITaskbarList3)new TaskbarInstance();

    #region API
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern uint RegisterClipboardFormatA(string lpszFormat);
    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    static extern int GetClipboardFormatNameA(uint format, [Out] StringBuilder lpszFormatName, int cchMaxCount);
    [DllImport("user32.dll")]
    static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();


    [DllImport("kernel32.dll")]
    static extern UIntPtr GlobalSize(IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalUnlock(IntPtr hMem);


    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD[] bmiColors;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public BitmapCompressionMode biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;

        public void Init()
        {
            biSize = (uint)Marshal.SizeOf(this);
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }
    public enum BitmapCompressionMode : uint
    {
        BI_RGB = 0,
        BI_RLE8 = 1,
        BI_RLE4 = 2,
        BI_BITFIELDS = 3,
        BI_JPEG = 4,
        BI_PNG = 5
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct BitmapV5Header
    {
        [FieldOffset(0)]
        public uint _biSize;
        [FieldOffset(4)]
        public int _biWidth;
        [FieldOffset(8)]
        public int _biHeight;
        [FieldOffset(12)]
        public ushort _biPlanes;
        [FieldOffset(14)]
        public ushort _biBitCount;
        [FieldOffset(16)]
        public BitmapCompressionMode _biCompression;
        [FieldOffset(20)]
        public uint _biSizeImage;
        [FieldOffset(24)]
        public int _biXPelsPerMeter;
        [FieldOffset(28)]
        public int _biYPelsPerMeter;
        [FieldOffset(32)]
        public uint _biClrUsed;
        [FieldOffset(36)]
        public uint _biClrImportant;
        [FieldOffset(40)]
        public uint _bV5RedMask;
        [FieldOffset(44)]
        public uint _bV5GreenMask;
        [FieldOffset(48)]
        public uint _bV5BlueMask;
        [FieldOffset(52)]
        public uint _bV5AlphaMask;
        [FieldOffset(56)]
        public BitmapColorSpace _bV5CSType;
        [FieldOffset(60)]
        public CieXyzTripple _bV5Endpoints;
        [FieldOffset(96)]
        public uint _bV5GammaRed;
        [FieldOffset(100)]
        public uint _bV5GammaGreen;
        [FieldOffset(104)]
        public uint _bV5GammaBlue;
        [FieldOffset(108)]
        public BitmapColorSpace _bV5Intent;
        [FieldOffset(112)]
        public uint _bV5ProfileData;
        [FieldOffset(116)]
        public uint _bV5ProfileSize;
        [FieldOffset(120)]
        public uint _bV5Reserved;
        public static BitmapV5Header Create(int width, int height, ushort bpp)
        {
            return new BitmapV5Header
            {
                _biSize = (uint)Marshal.SizeOf(typeof(BitmapV5Header)),
                _biPlanes = 1,
                _biCompression = BitmapCompressionMode.BI_RGB,
                _biWidth = width,
                _biHeight = height,
                _biBitCount = bpp,
                _biSizeImage = (uint)(width * height * (bpp >> 3)),
                _biXPelsPerMeter = 0,
                _biYPelsPerMeter = 0,
                _biClrUsed = 0,
                _biClrImportant = 0,

                // V5
                _bV5RedMask = (uint)255 << 16,
                _bV5GreenMask = (uint)255 << 8,
                _bV5BlueMask = 255,
                _bV5AlphaMask = (uint)255 << 24,
                _bV5CSType = BitmapColorSpace.LCS_sRGB,
                _bV5Endpoints = new CieXyzTripple
                {
                    _cieXyzRed = CieXyz.Create(0),
                    _cieXyzGreen = CieXyz.Create(0),
                    _cieXyzBlue = CieXyz.Create(0)
                },
                _bV5GammaRed = 0,
                _bV5GammaGreen = 0,
                _bV5GammaBlue = 0,
                _bV5Intent = BitmapColorSpace.LCS_GM_IMAGES,
                _bV5ProfileData = 0,
                _bV5ProfileSize = 0,
                _bV5Reserved = 0
            };
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CieXyzTripple
    {
        public CieXyz _cieXyzRed;
        public CieXyz _cieXyzGreen;
        public CieXyz _cieXyzBlue;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CieXyz
    {
        public uint ciexyzX;
        public uint ciexyzY;
        public uint ciexyzZ;
        public static CieXyz Create(uint fxPt2Dot30)
        {
            return new CieXyz
            {
                ciexyzX = fxPt2Dot30,
                ciexyzY = fxPt2Dot30,
                ciexyzZ = fxPt2Dot30
            };
        }
    }
    public enum BitmapColorSpace : uint
    {
        LCS_CALIBRATED_RGB = 0,
        LCS_GM_BUSINESS = 0x00000001,
        LCS_GM_GRAPHICS = 0x00000002,
        LCS_GM_IMAGES = 0x00000004,
        LCS_GM_ABS_COLORIMETRIC = 0x00000008,
        LCS_sRGB = 1934772034,
        LCS_WINDOWS_COLOR_SPACE = 1466527264,
        PROFILE_LINKED,
        PROFILE_EMBEDDED
    }

    [ComImport()]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig]
        void HrInit();
        [PreserveSig]
        void AddTab(IntPtr hwnd);
        [PreserveSig]
        void DeleteTab(IntPtr hwnd);
        [PreserveSig]
        void ActivateTab(IntPtr hwnd);
        [PreserveSig]
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig]
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig]
        void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
        [PreserveSig]
        void SetProgressState(IntPtr hwnd, TaskbarStates state);
    }

    [ComImport()]
    [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }
    #endregion
#endif
}
