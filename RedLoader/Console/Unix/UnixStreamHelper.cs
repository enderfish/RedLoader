using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RedLoader.Unix;

/// <summary>
///     libc stream helpers for the Unix console. Previously bound through MonoMod 22's <c>DynDllImport</c>,
///     which MonoMod 25 removed; these are plain P/Invokes with a resolver that tries the same library names.
/// </summary>
internal static class UnixStreamHelper
{
    private const string LibC = "libc";

    private static readonly string[] LibCCandidates =
    {
        "libc.so.6",                // Ubuntu glibc
        "libc",                     // Linux glibc
        "/usr/lib/libSystem.dylib", // OSX POSIX
    };

    static UnixStreamHelper()
    {
        NativeLibrary.SetDllImportResolver(typeof(UnixStreamHelper).Assembly, (name, assembly, searchPath) =>
        {
            if (name != LibC) return IntPtr.Zero;

            foreach (var candidate in LibCCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                    return handle;
            }

            return IntPtr.Zero;
        });
    }

    [DllImport(LibC, EntryPoint = "dup")]
    public static extern int dup(int fd);

    [DllImport(LibC, EntryPoint = "fdopen")]
    public static extern IntPtr fdopen(int fd, [MarshalAs(UnmanagedType.LPStr)] string mode);

    [DllImport(LibC, EntryPoint = "fread")]
    public static extern IntPtr fread(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport(LibC, EntryPoint = "fwrite")]
    public static extern int fwrite(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport(LibC, EntryPoint = "fclose")]
    public static extern int fclose(IntPtr stream);

    [DllImport(LibC, EntryPoint = "fflush")]
    public static extern int fflush(IntPtr stream);

    [DllImport(LibC, EntryPoint = "isatty")]
    public static extern int isatty(int fd);

    public static Stream CreateDuplicateStream(int fileDescriptor)
    {
        var newFd = dup(fileDescriptor);

        return new UnixStream(newFd, FileAccess.Write);
    }
}
