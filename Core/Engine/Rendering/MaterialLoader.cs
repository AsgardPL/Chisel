using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;
public static class MaterialLoader
{
    /// <summary>
    /// Mounts all .cmt material files from <paramref name="pathToRoot"/> and its subdirectories.
    /// </summary>
    /// <param name="pathToRoot">Path to the root of the materials folder.</param>
    public static void MountMaterials(string pathToRoot)
    {
        List<Material> materials = new List<Material>();
        Dictionary<string, int> matNames = GlobalMapData.materialNameToIndex?.ToDictionary() ?? [];

        int count = 0;
        foreach(var file in Directory.EnumerateFiles(pathToRoot,"*.cmt",SearchOption.AllDirectories))
        {
            var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
            materials.Add(mat);

            matNames.Add(mat.name,GlobalMapData.loadedMaterials?.Length ?? 0 + count);
            count++;
        }

        if (GlobalMapData.loadedMaterials == null) GlobalMapData.loadedMaterials = [.. materials];
        else                                       GlobalMapData.loadedMaterials = [.. GlobalMapData.loadedMaterials, .. materials];

        GlobalMapData.materialNameToIndex = matNames.ToImmutableDictionary();
    }
}

public static class MaterialExtensions
{
    public static bool GetFlag(this Material mat, string key, bool defaultValue = false)
    {
        if (mat.shaderFlags != null && mat.shaderFlags.TryGetValue(key, out bool val))
            return val;
        return defaultValue;
    }
    public static void SetFlag(this ref Material mat, string key, bool value)
    {
        mat.shaderFlags ??= new Dictionary<string, bool>();
        mat.shaderFlags[key] = value;
    }
}
