using System.Runtime.InteropServices;
using System.Text;

namespace WinThemeStudio.Interop;

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName,
        [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

public sealed class ShellLinkHandle : IDisposable
{
    private const uint StgmReadWrite = 0x00000002;
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    private object? _comObject;
    private readonly IShellLinkW _link;
    private readonly IPersistFile _file;

    private ShellLinkHandle(object comObject, IShellLinkW link, IPersistFile file)
    {
        _comObject = comObject;
        _link = link;
        _file = file;
    }

    public static ShellLinkHandle Open(string path)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClsid)
            ?? throw new InvalidOperationException("Не удалось получить COM-класс ShellLink.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Не удалось создать COM-объект ShellLink.");

        var link = (IShellLinkW)instance;
        var file = (IPersistFile)instance;
        file.Load(path, StgmReadWrite);
        return new ShellLinkHandle(instance, link, file);
    }

    public string GetTargetPath()
    {
        var builder = new StringBuilder(1024);
        _link.GetPath(builder, builder.Capacity, IntPtr.Zero, 0);
        return builder.ToString();
    }

    public string GetIconLocation(out int index)
    {
        var builder = new StringBuilder(1024);
        _link.GetIconLocation(builder, builder.Capacity, out index);
        return builder.ToString();
    }

    public void SetIconLocation(string iconPath, int index)
    {
        _link.SetIconLocation(iconPath, index);
    }

    public void Save()
    {
        _file.Save(null, true);
    }

    public void Dispose()
    {
        var instance = _comObject;
        _comObject = null;
        if (instance is not null)
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }
}
