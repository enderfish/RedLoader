# Alt.Json

`Libs/Alt.Json.dll` is [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) (MIT licence, © James Newton-King) with its namespaces renamed from `Newtonsoft.Json` to `Alt.Json`, so RedLoader and mods can use it without clashing with the game's own copy of Newtonsoft.Json.

It is built from source by `Build-AltJson.ps1`:

- Source: tag `13.0.4`, commit `4e13299d4b0ec96bd4df9954ef646bd2d1b5bf2a` (the script refuses any other commit).
- The only source change is the text rename `Newtonsoft.Json` → `Alt.Json`, done in a temporary working copy.
- The build uses Newtonsoft's own feature settings for its newest .NET target, compiled for `net10.0`.

## Rebuild and verify

Requires git and the .NET 10 SDK. From the repository root:

```
pwsh build/AltJson/Build-AltJson.ps1
```

The build is deterministic. With the same SDK version it reproduces `Libs/Alt.Json.dll` byte for byte, and the script prints the SHA-256 to compare. The committed DLL was built with SDK 10.0.401 and has SHA-256 `4c0d3793bd00e65b26bb3c609105e340c4b01ba9e870cf45a6eb8dc5773c8311`.

## Compatibility with the previous Alt.Json

The previous `Alt.Json.dll` was built by hand from Newtonsoft.Json source between 13.0.3 and 13.0.4. Its public API is kept, with one fix: its `JsonSerializer.Error` / `JsonSerializerSettings.Error` events used `System.IO.ErrorEventArgs` by mistake. They now use `Alt.Json.Serialization.ErrorEventArgs`, as in Newtonsoft.Json. A mod compiled against the old, wrong signature would need a rebuild if it uses those events.
