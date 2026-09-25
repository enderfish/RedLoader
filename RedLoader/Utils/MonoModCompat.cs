using System;
using System.Runtime.InteropServices;
using MonoMod.Utils;

namespace RedLoader;

/// <summary>
///     Platform flags as MonoMod 22 defined them. MonoMod 25 replaced <c>PlatformHelper</c>/<c>Platform</c> with
///     <see cref="PlatformDetection" />/<see cref="OSKind" />; this shim keeps RedLoader's call sites unchanged.
/// </summary>
[Flags]
public enum Platform
{
    OS = 1 << 0,
    Bits64 = 1 << 1,
    NT = 1 << 2,
    Unix = 1 << 3,
    ARM = 1 << 16,
    Wine = 1 << 17,

    Unknown = OS | (1 << 4),
    Windows = OS | NT | (1 << 5),
    MacOS = OS | Unix | (1 << 6),
    Linux = OS | Unix | (1 << 7),
    Android = Linux | (1 << 8),
    iOS = MacOS | (1 << 9),
}

/// <summary>
///     Drop-in replacement for MonoMod 22's <c>PlatformHelper</c>. <see cref="Current" /> is normally set by
///     <c>PlatformUtils.SetPlatform()</c> during preload; if read before that it is derived from MonoMod 25's detection.
/// </summary>
public static class PlatformHelper
{
    private static Platform? _current;

    public static Platform Current
    {
        get => _current ??= Detect();
        set => _current = value;
    }

    public static bool Is(Platform platform) => (Current & platform) == platform;

    public static string LibrarySuffix => Is(Platform.Windows) ? "dll" : Is(Platform.MacOS) ? "dylib" : "so";

    private static Platform Detect()
    {
        var os = PlatformDetection.OS;
        var current = os switch
        {
            OSKind.Windows or OSKind.Wine => Platform.Windows,
            OSKind.OSX => Platform.MacOS,
            OSKind.IOS => Platform.iOS,
            OSKind.Android => Platform.Android,
            OSKind.Linux or OSKind.BSD or OSKind.Posix => Platform.Linux,
            _ => Platform.Unknown,
        };

        if (os == OSKind.Wine) current |= Platform.Wine;
        if (Environment.Is64BitOperatingSystem) current |= Platform.Bits64;
        if (PlatformDetection.Architecture is ArchitectureKind.Arm or ArchitectureKind.Arm64) current |= Platform.ARM;

        return current;
    }
}

/// <summary>MonoMod 22 extension helpers that MonoMod 25 dropped.</summary>
public static class MonoModCompatExtensions
{
    /// <summary>Wraps a native function pointer in a delegate (was <c>MonoMod.Utils.Extensions.AsDelegate</c>).</summary>
    public static T AsDelegate<T>(this IntPtr ptr) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(ptr);
}
