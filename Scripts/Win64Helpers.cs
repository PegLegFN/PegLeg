
using Godot;
using GdFileAccess = Godot.FileAccess;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;


#if GODOT_WINDOWS
using System.Windows;
#endif

static partial class Win64Helpers
{
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
    static IntPtr? currentClipImg = null;
    public static void ClipboardSetImage(Image image)
    {
#if GODOT_WINDOWS
        if (!OpenClipboard(MainWindowHandle))
            return;
        try
        {
            if (!EmptyClipboard())
                return;

            if (currentClipImg is not null)
            {
                Marshal.FreeHGlobal(currentClipImg.Value);
                currentClipImg = null;
            }

            //convert format
            int width = image.GetWidth();
            int height = image.GetHeight();
            RGBQUAD[] dibColours = new RGBQUAD[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var col = image.GetPixel(x, y);
                    dibColours[(y * width) + x] = new()
                    {
                        rgbRed = (byte)col.R8,
                        rgbGreen = (byte)col.G8,
                        rgbBlue = (byte)col.B8,
                        rgbReserved = (byte)col.A8,
                    };
                }
            }
            BITMAPINFOHEADER dibHeader = new()
            {
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = 0,
                biXPelsPerMeter = 250,
                biClrUsed = 0,
                biClrImportant = 0,
            };
            dibHeader.Init();
            BITMAPINFO dib = new()
            {
                bmiColors = dibColours,
                bmiHeader = dibHeader,
            };

            //write wraw data to memory
            try
            {
                var bSize = Marshal.SizeOf(typeof(BITMAPINFO));
                var ptr = Marshal.AllocHGlobal(bSize);
                Marshal.StructureToPtr(dib, ptr, false);

                SetClipboardData(CF_DIB, ptr);
            }
            catch (Exception e)
            {
                GD.Print("yup: " + e);
            }
        }
        finally
        {
            CloseClipboard();
        }
#endif
    }

#if GODOT_WINDOWS
    static readonly ITaskbarList3 taskbarList = (ITaskbarList3)new TaskbarInstance();

    #region API
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    static extern IntPtr SetClipboardData(int uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
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
