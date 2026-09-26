# RedLoader on .NET 10: porting notes

This branch moves RedLoader 0.8.6 from .NET 6 to .NET 10. It boots Sons of the Forest (Unity 2022.2.16f1, game version 2.0.0) on the .NET 10.0.12 runtime, the chainloader completes, and mods compiled for net10.0 load and patch normally. Verified on Windows x64 on 2026-09-25.

## What changed and why

### Target frameworks
- `RedLoader`, `SonsSdk`, `GLTF`: `net6`/`net6.0` to `$(LoaderTfm)`, which `Directory.Build.props` sets to `net10.0`.
- `Dependencies/Il2CppInterop`: `Il2CppInterop.Runtime` and `Il2CppInterop.HarmonySupport` to `net10.0`. `Il2CppInterop.Generator` dropped its `net472` target (nothing here consumes it and it needs a .NET Framework targeting pack).
- `Directory.Build.props` also gained `LoaderNetFolder` (`net10`), the name of the loader's assembly folder under `_Redloader`. `LoaderEnvironment.cs`, both Doorstop configs, and the csproj copy targets use it instead of the hardcoded `net6`.

### Il2CppInterop source
The upstream repo pointed `Dependencies/Il2CppInterop` at commit `8663e8997a9e5cef0c8f8b9f7b082cdeb3304e89` of BepInEx/Il2CppInterop (version 1.4.6, January 2025) through a gitlink with no `.gitmodules` entry, so a plain clone left the folder empty. The source at that commit is now vendored directly in the tree. Only the three csproj files above were modified.

### Harmony / MonoMod stack
- HarmonyX 2.10.1 to 2.16.1 and MonoMod.RuntimeDetour 22.7.31.1 to 25.3.6. The MonoMod 22 detour engine predates .NET 7's JIT changes; MonoMod 25 ("MonoMod.Core") is the supported line for modern runtimes and is what actually hooks the JIT on .NET 10 (the log shows `Core60Runtime.CompileMethodHook` in use).
- Il2Cpp method patches were never routed through MonoMod's managed detours. `Il2CppInterop.HarmonySupport` builds the patched method with `DynamicMethodDefinition` + `HarmonyManipulator` and installs a native detour through RedLoader's `IDetourProvider` (Dobby). That code compiled unchanged against HarmonyX 2.16.
- MonoMod 25 removed a handful of MonoMod.Utils APIs RedLoader used. Each has a small replacement:
  - `IDetour` base interface and `DetourHelper.GenerateNativeProxy`: `INativeDetour` now declares its own members (`Hook/INativeDetour.cs`) and `BaseNativeDetour.GenerateTrampoline(MethodBase)` no longer builds a managed proxy (nothing consumed it; all callers use the generic overload that marshals a delegate from the trampoline pointer).
  - `Platform` enum, `PlatformHelper`, `IntPtr.AsDelegate<T>()`: re-created in `Utils/MonoModCompat.cs` on top of MonoMod 25's `PlatformDetection`. `PlatformUtils.SetPlatform()` still sets `PlatformHelper.Current` during preload.
  - `ReflectionHelper.IsCore`: replaced by `PlatformDetection.Runtime == RuntimeKind.CoreCLR` in `WindowsConsoleDriver.cs`.
  - `DynDllImport` bindings in `Console/Unix/UnixStreamHelper.cs`: replaced with plain `DllImport("libc")` externs plus a `NativeLibrary` resolver that tries the same library names as before.
  - `FastReflectionDelegate` / `GetFastDelegate()` in SonsSdk: now `FastReflectionHelper.FastInvoker` / `GetFastInvoker()`.

### Conditionals that silently targeted the wrong branch
`RLog.cs` and `Utils/LoaderUtils.cs` used `#if !NET6_0` to select the legacy MelonLoader/Mono code path. On any target other than exactly net6.0 that compiled the wrong branch. Both are now `#if !NET` (defined for .NET 5 and later).

### Runtime bundle
The release used BepInEx's trimmed "mini-coreclr" 6.0.7 build in `_Redloader/dotnet`. Doorstop only needs `coreclr.dll` plus a folder of framework assemblies for `corlib_dir`, so the official Microsoft runtime archive works as a drop-in: `_Redloader/dotnet` is now the contents of `shared/Microsoft.NETCore.App/10.0.12` from `dotnet-runtime-10.0.12-win-x64.zip`. Two consequences:
- The mini-coreclr bundle happened to include `Microsoft.Extensions.Logging*.dll`, which Il2CppInterop needs. The official runtime does not, so those PackageReferences in `RedLoader.csproj` are no longer compile-only and the DLLs ship in `_Redloader/net10`.
- The bundle is about 77 MB instead of 67 MB. Trimming it (as BepInEx did) is a possible follow-up.

### Doorstop
Updated from 4.3.0 to 4.5.0 (`version.dll` is Doorstop's `winhttp.dll` for x64, renamed as before). Config keys are unchanged.

### Packaging script (`build/Program.cs`)
Constants updated (`DotnetRuntimeVersion` 10.0.12, `DoorstopVersion` 4.5.0, new `LoaderNetFolder`), the runtime download now points at Microsoft's per-RID archive, and the dist step flattens `shared/Microsoft.NETCore.App/<version>` into `_Redloader/dotnet`. The Cake/Nuke pipeline itself was not run for this port; the dist in `bin/dist/Unity.IL2CPP-win-x64` was assembled by hand following the same layout.

### Misc
- Hardcoded personal paths (`F:\SteamLibrary\...`, the `E:\...Dedicated Server` copy) were removed. Post-build copies now go to `$(GamePath)\_Redloader\$(LoaderNetFolder)` only when that folder exists. Override `GamePath` on the command line or in `Directory.Build.props`.
- `SonsSdk.csproj` no longer references `iTween.dll` (the game no longer generates it and the only usage was commented out).

## Building

```
dotnet build RedLoader/RedLoader.csproj -c Release -p:SolutionDir="<repo root>\"
dotnet build SonsSdk/SonsSdk.csproj   -c Release -p:SolutionDir="<repo root>\"
```

Requirements: .NET 10 SDK, and an existing `_Redloader\Game` folder in `GamePath` (SonsSdk references the generated game assemblies). Run the game once with any RedLoader build to generate it. `SolutionDir` matters because `Directory.Build.props` derives `BuildDir` from it; without it the output path is wrong.

Loader output lands in `bin/Unity.IL2CPP`. That folder plus `GLTF.dll` (from `GLTF/bin/Release/net10.0`) and the native `dobby.dll` is what goes in `_Redloader/net10`.

## Changes merged after the port (cheerfulnut, 2026-09-26)
- #1 `Libs/Splash.dll` (native, no source) and `Resources/bg.png` are gone; the loading window is now built in `Utils/SplashWindow.cs` from user32/comctl32/gdi32 calls, toggled by the existing `hide_status_window` preference. Nothing to copy into the dist for it any more.
- #2 If interop generation fails, `IL2CPPChainloader` starts the game unmodded with a warning box instead of dying inside the JIT hook (`Il2CppInteropManager.GenerationFailed`).
- #3 The Unity base-libraries download is verified against a pinned SHA-256 per Unity version (`KnownUnityBaseLibraryHashes`). After a game update that bumps Unity, add the new hash there or players get the #2 fallback until you do.

Note for developers: `RedLoader.csproj` and `SonsSdk.csproj` copy their output into `$(GamePath)\_Redloader\$(LoaderNetFolder)` after every build when that folder exists, so building replaces the installed loader.

## Not done / worth checking next
- Only Windows x64 was tested. Linux/macOS Doorstop paths and the Unix console rewrite are untested.
- `Microsoft.Extensions.Logging` is still the 6.0.x package line; bumping to 10.0.x should be safe but was left alone to keep the diff small.
- Cpp2IL is still `2022.1.0-pre-release.19`; pre-release.21 exists.
- Interop assembly generation was not exercised on this run because `_Redloader/Game` already existed. Delete that folder and run once to confirm the generator path on .NET 10.
- The mini-coreclr trimming that BepInEx applied is gone; if size matters, trim `_Redloader/dotnet` to what the TPA actually needs.
