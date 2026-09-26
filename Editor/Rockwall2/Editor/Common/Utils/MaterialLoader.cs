using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;

public static class MaterialLoader
{
    /// <summary>
    /// Mounts all .cmt material files from <paramref name="pathToRoot"/> and its subdirectories.
    /// </summary>
    /// <param name="pathToRoot">Path to the root of the materials folder.</param>
    public static void MountMaterials(string pathToRoot)
    {
        List<Material> materials = new List<Material>();
        Dictionary<string, int> matNames = GlobalMapData.materialNameToIndex?.ToDictionary() ?? new Dictionary<string, int>();

        int count = 0;
        foreach (var file in Directory.EnumerateFiles(pathToRoot, "*.cmt", SearchOption.AllDirectories))
        {
            var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
            materials.Add(mat);

            matNames.Add(mat.name, GlobalMapData.loadedMaterials?.Length ?? 0 + count);
            count++;
        }

        if (GlobalMapData.loadedMaterials == null) GlobalMapData.loadedMaterials = materials.ToArray();
        else GlobalMapData.loadedMaterials = GlobalMapData.loadedMaterials.Concat(materials).ToArray();

        GlobalMapData.materialNameToIndex = matNames;
    }
}