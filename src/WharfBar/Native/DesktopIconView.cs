using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>A desktop icon read from the shell view: display name + view position (physical px).</summary>
internal readonly record struct DesktopIconPoint(string Name, int X, int Y);

/// <summary>
/// Read/write access to the desktop's shell view icon positions.
/// </summary>
/// <remarks>
/// The desktop icon layout is owned by explorer; the only documented way to read and write it
/// from another process is the shell's folder view object:
/// <c>Shell.Application</c> → <c>IShellWindows.FindWindowSW(SWC_DESKTOP)</c> →
/// <c>IServiceProvider.QueryService(SID_STopLevelBrowser)</c> → <c>IShellBrowser.QueryActiveShellView</c>
/// → <c>IFolderView</c>. No memory is read from or written to explorer's process.
///
/// The desktop view is recreated when explorer restarts, and the RCWs become stale: every call
/// reports failure instead of throwing, and the owner reopens the view. Must be used on an STA
/// thread only (the view is an apartment-threaded shell object).
/// </remarks>
internal sealed class DesktopIconView : IDisposable
{
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_ALLVIEW = 0x00000002;
    private const int SIGDN_DESKTOPABSOLUTEPARSING = unchecked((int)0x80028000);

    private static readonly Guid SidStopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    private object? _shellApp;
    private IShellWindows? _shellWindows;
    private object? _desktopDispatch;
    private IServiceProvider? _serviceProvider;
    private IShellBrowser? _shellBrowser;
    private IShellView? _shellView;
    private IFolderView? _folderView;
    private bool _disposed;

    private DesktopIconView()
    {
    }

    /// <summary>
    /// Opens the desktop folder view. False when the shell does not expose it (composited session,
    /// restricted token, explorer not ready) — callers must degrade quietly.
    /// </summary>
    internal static bool TryOpen(out DesktopIconView view)
    {
        view = new DesktopIconView();
        return view.TryConnect();
    }

    private bool TryConnect()
    {
        try
        {
            Type? appType = Type.GetTypeFromProgID("Shell.Application");
            if (appType is null)
            {
                return false;
            }

            _shellApp = Activator.CreateInstance(appType);
            if (_shellApp is null)
            {
                return false;
            }

            // ShellWindows has no CoCreatable class on Windows 11 25H2; the collection instance
            // comes off Shell.Application and FindWindowSW is still callable on it.
            object? windowsObject = appType.InvokeMember(
                "Windows",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                _shellApp,
                null);
            if (windowsObject is not IShellWindows windows)
            {
                return false;
            }

            _shellWindows = windows;
            if (!TryGetDesktopDispatch(out object? desktop) || desktop is null)
            {
                return false;
            }

            _desktopDispatch = desktop;
            if (desktop is not IServiceProvider serviceProvider)
            {
                return false;
            }

            _serviceProvider = serviceProvider;
            Guid sid = SidStopLevelBrowser;
            Guid iidShellBrowser = typeof(IShellBrowser).GUID;
            if (serviceProvider.QueryService(ref sid, ref iidShellBrowser, out IntPtr browserPtr) != 0
                || browserPtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                _shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
            }
            finally
            {
                Marshal.Release(browserPtr);
            }

            if (_shellBrowser.QueryActiveShellView(out IShellView? shellView) != 0 || shellView is null)
            {
                return false;
            }

            _shellView = shellView;
            _folderView = shellView as IFolderView;
            return _folderView is not null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon view open failed: {ex.Message}");
            return false;
        }
    }

    private bool TryGetDesktopDispatch(out object? dispatch)
    {
        dispatch = null;
        if (_shellWindows is null)
        {
            return false;
        }

        // FindWindowSW takes two VARIANT* arguments; VT_EMPTY is a zeroed VARIANT.
        IntPtr emptyLocation = Marshal.AllocHGlobal(24);
        IntPtr emptyRoot = Marshal.AllocHGlobal(24);
        try
        {
            for (int offset = 0; offset < 24; offset += 4)
            {
                Marshal.WriteInt32(emptyLocation, offset, 0);
                Marshal.WriteInt32(emptyRoot, offset, 0);
            }

            int hr = _shellWindows.FindWindowSW(
                emptyLocation,
                emptyRoot,
                SWC_DESKTOP,
                out _,
                SWFO_NEEDDISPATCH,
                out IntPtr desktopDispatch);
            if (hr != 0 || desktopDispatch == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                dispatch = Marshal.GetObjectForIUnknown(desktopDispatch);
            }
            finally
            {
                Marshal.Release(desktopDispatch);
            }

            return dispatch is not null;
        }
        finally
        {
            Marshal.FreeHGlobal(emptyLocation);
            Marshal.FreeHGlobal(emptyRoot);
        }
    }

    /// <summary>Reads every desktop icon position plus the current icon grid pitch.</summary>
    internal bool TryRead(out List<DesktopIconPoint> items, out int pitchX, out int pitchY)
    {
        items = new List<DesktopIconPoint>();
        pitchX = 0;
        pitchY = 0;
        IFolderView? view = _folderView;
        if (_disposed || view is null)
        {
            return false;
        }

        try
        {
            if (view.GetSpacing(out POINT spacing) == 0 && spacing.X > 0 && spacing.Y > 0)
            {
                pitchX = spacing.X;
                pitchY = spacing.Y;
            }

            if (view.ItemCount(SVGIO_ALLVIEW, out int count) != 0 || count < 0)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (view.Item(i, out IntPtr pidl) != 0 || pidl == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (view.GetItemPosition(pidl, out POINT position) != 0)
                    {
                        continue;
                    }

                    string? name = GetItemName(pidl);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    items.Add(new DesktopIconPoint(name, position.X, position.Y));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>True when the desktop view lets the shell arrange the icons itself.</summary>
    internal bool IsAutoArrange()
    {
        IFolderView? view = _folderView;
        if (_disposed || view is null)
        {
            return false;
        }

        try
        {
            // S_OK means auto-arrange is on; S_FALSE means the user owns the layout.
            return view.GetAutoArrange() == NativeMethods.S_OK;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop auto-arrange read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Moves the icons named in <paramref name="targets"/> to their positions in one shell call.
    /// Items without a target keep their current position. False when the shell rejected the write.
    /// </summary>
    internal bool TryWrite(IReadOnlyList<DesktopIconPoint> targets)
    {
        IFolderView? view = _folderView;
        if (_disposed || view is null || targets.Count == 0)
        {
            return false;
        }

        try
        {
            if (view.ItemCount(SVGIO_ALLVIEW, out int count) != 0 || count <= 0)
            {
                return false;
            }

            var wanted = new Dictionary<string, DesktopIconPoint>(targets.Count, StringComparer.OrdinalIgnoreCase);
            foreach (DesktopIconPoint target in targets)
            {
                wanted[target.Name] = target;
            }

            var pidls = new List<IntPtr>(count);
            var points = new List<POINT>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (view.Item(i, out IntPtr pidl) != 0 || pidl == IntPtr.Zero)
                    {
                        continue;
                    }

                    string? name = GetItemName(pidl);
                    if (name is null || !wanted.TryGetValue(name, out DesktopIconPoint target))
                    {
                        Marshal.FreeCoTaskMem(pidl);
                        continue;
                    }

                    pidls.Add(pidl);
                    points.Add(new POINT { X = target.X, Y = target.Y });
                }

                if (pidls.Count == 0)
                {
                    return false;
                }

                int hr = view.SelectAndPositionItems(
                    (uint)pidls.Count,
                    pidls.ToArray(),
                    points.ToArray(),
                    SVGIO_ALLVIEW);
                return hr == 0;
            }
            finally
            {
                foreach (IntPtr pidl in pidls)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon write failed: {ex.Message}");
            return false;
        }
    }

    private static string? GetItemName(IntPtr pidl)
    {
        int hr = SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out IntPtr namePtr);
        if (hr != 0 || namePtr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(namePtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePtr);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _folderView = null;
        ReleaseComObject(_shellView);
        _shellView = null;
        ReleaseComObject(_shellBrowser);
        _shellBrowser = null;
        ReleaseComObject(_serviceProvider);
        _serviceProvider = null;
        ReleaseComObject(_desktopDispatch);
        _desktopDispatch = null;
        ReleaseComObject(_shellWindows);
        _shellWindows = null;
        ReleaseComObject(_shellApp);
        _shellApp = null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            _ = Marshal.ReleaseComObject(value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon COM release failed: {ex.Message}");
        }
    }

    // ---- shell interop (documented shell interfaces) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        internal int X;
        internal int Y;
    }

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        [PreserveSig] int GetTypeInfoCount(out uint pctinfo);
        [PreserveSig] int GetTypeInfo(uint iTInfo, uint lcid, out IntPtr ppTInfo);
        [PreserveSig] int GetIDsOfNames(ref Guid riid, IntPtr[] rgszNames, uint cNames, uint lcid, IntPtr rgDispId);
        [PreserveSig] int Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort wFlags, IntPtr pDispParams, IntPtr pVarResult, IntPtr pExcepInfo, IntPtr puArgErr);
        [PreserveSig] int get__NewEnum(out IntPtr ppunk);
        [PreserveSig] int get_Item(object index, out IntPtr folder);
        [PreserveSig] int get_Count(out int count);
        [PreserveSig] int Register(IntPtr pid, int hwnd, int swClass, out int cookie);
        [PreserveSig] int RegisterPending(int threadId, IntPtr loc, IntPtr locRoot, int swClass, out int cookie);
        [PreserveSig] int Revoke(int cookie);
        [PreserveSig] int OnNavigate(int cookie, IntPtr loc);
        [PreserveSig] int OnActivated(int cookie, short active);
        [PreserveSig] int FindWindowSW(IntPtr loc, IntPtr locRoot, int swClass, out int hwnd, int options, out IntPtr dispatch);
        [PreserveSig] int OnCreated(int cookie);
        [PreserveSig] int ProcessAttachDetach(short attach);
    }

    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr ppv);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);
        [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool fEnable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        [PreserveSig] int QueryActiveShellView(out IShellView? ppshv);
        [PreserveSig] int OnViewWindowActive(IShellView pshv);
        [PreserveSig] int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
        [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
        [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);
        [PreserveSig] int UIActivate(uint uState);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IShellView psvPrevious, IntPtr pfs, IShellBrowser psb, IntPtr prcView, out IntPtr phWnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(IntPtr pfs);
        [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(out POINT ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] POINT[] apt,
            uint dwFlags);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, int sigdnName, out IntPtr ppszName);
}
