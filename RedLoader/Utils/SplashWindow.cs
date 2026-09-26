using System;

namespace RedLoader.Utils;

/// <summary>
///     Formerly drove a native splash window (Splash.dll). That DLL had no published source, so it was removed and
///     these members are now no-ops kept only so that mods calling them keep loading.
/// </summary>
[Obsolete("The splash window was removed; these members do nothing.")]
public class SplashWindow
{
    public static int TotalProgressSteps = 100;

    public static void CreateWindow() { }

    public static void CloseWindow() { }

    public static void PrintToConsole(string str) { }

    public static void SetProgress(float progress) { }

    public static void SetProgressSteps(int step) { }

    public static void HookLog() { }

    public static void UnhookLog() { }
}
