using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Rockwall2.Editor.Common;

public static class GlobalEditorData
{
    public static string EDSFile = "", WorkingDirectory = "", ContentPath = "", CompileProgramPath = "";
    public static string[] RegisteredClassnames;
    public static Dictionary<string, EntityClassMetadata> RegisteredEntityMeta = new Dictionary<string, EntityClassMetadata>();
    public static EditorOverrides EditorOverrides;
    public static TextureItem[] TexturesAsImages;

    private static bool texturesLoaded = false;

    public static void LoadTex(bool forceReload = false)
    {
        if (texturesLoaded && !forceReload) return;
        if (!Directory.Exists(WorkingDirectory)) return;
        if (GlobalMapData.loadedMaterials == null) return;

        var relativePaths = new Dictionary<string, string>();
        string materialsRoot = Path.Combine(ContentPath, "Materials");

        if (Directory.Exists(materialsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(materialsRoot, "*.cmt", SearchOption.AllDirectories))
            {
                string name;
                try
                {
                    var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
                    name = mat.name;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name)) continue;

                string dir = Path.GetDirectoryName(file) ?? materialsRoot;
                string relDir = Path.GetRelativePath(materialsRoot, dir).Replace('\\', '/');
                if (relDir == ".") relDir = "";

                relativePaths[name] = relDir;
            }
        }

        const int thumbnailSize = 128;

        TexturesAsImages = new TextureItem[GlobalMapData.loadedMaterials.Length];
        for (int i = 0; i < GlobalMapData.loadedMaterials.Length; i++)
        {
            var mat = GlobalMapData.loadedMaterials[i];
            relativePaths.TryGetValue(mat.name, out string relPath);

            Bitmap bmp;
            using (var stream = File.OpenRead($"{WorkingDirectory}/{mat.texture}.png"))
            {
                bmp = Bitmap.DecodeToWidth(stream, thumbnailSize);
            }

            TexturesAsImages[i] = new TextureItem(bmp, mat.name, i, relPath ?? "");
        }

        texturesLoaded = true;
    }
}

public class TextureItem : INotifyPropertyChanged
{
    public Bitmap Image { get; }
    public string Name { get; }
    public int MaterialIdx { get; }
    public string RelativePath { get; }

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public TextureItem(Bitmap image, string name, int materialIdx, string relativePath)
    {
        Image = image;
        Name = name;
        MaterialIdx = materialIdx;
        RelativePath = relativePath;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

public class MaterialPickerState
{
    public double ScrollOffset { get; set; }
    public string NameFilter { get; set; } = "";
    public string PathFilter { get; set; } = "";
}