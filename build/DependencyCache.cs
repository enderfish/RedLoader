using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cake.Core.IO;

readonly record struct DependencyCache(BuildContext Ctx, FilePath CacheFile)
{
    readonly IDictionary<string, string> cache =
        File.Exists(CacheFile.FullPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile.FullPath))
              ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();

    public void Refresh(string name, string key, Action process)
    {
        if (cache.TryGetValue(name, out var curKey) && curKey == key) return;
        process();
        cache[name] = key;
    }

    public void Save() =>
        File.WriteAllText(CacheFile.FullPath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
}
