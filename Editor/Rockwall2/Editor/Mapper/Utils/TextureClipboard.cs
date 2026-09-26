using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

public static class TextureClipboard
{
    public static int SourceBrush { get; private set; } = -1;
    public static int SourceFace { get; private set; } = -1;
    public static bool HasSource => SourceBrush != -1 && SourceFace != -1;
    public static string MaterialName { get; private set; }
    public static int Surface { get; private set; }
    public static float TOffX { get; private set; }
    public static float TOffY { get; private set; }
    public static float TScaleX { get; private set; }
    public static float TScaleY { get; private set; }
    public static float UvRotation { get; private set; }
    public static float LuxelScale { get; private set; }
    public static UVProjectionMode UvProjectionMode { get; private set; }

    public static void LiftFromFace(int brushIndex, int faceIndex)
    {
        SourceBrush = brushIndex;
        SourceFace = faceIndex;

        var face = MapTools.ActiveMap.brushes[brushIndex].faces[faceIndex];
        MaterialName = face.materialName;
        Surface = face.surface;
        TOffX = face.tOffX;
        TOffY = face.tOffY;
        TScaleX = face.tScaleX;
        TScaleY = face.tScaleY;
        UvRotation = face.uvRotation;
        LuxelScale = face.luxelScale;
        UvProjectionMode = face.uvProjectionMode;

        TextureSettingsWindow.Instance?.SyncFromClipboard();
    }

    public static void StampOntoFace(int brushIndex, int faceIndex, bool includeMaterial = true)
    {
        if (!HasSource) return;

        ref var face = ref MapTools.ActiveMap.brushes[brushIndex].faces[faceIndex];
        if (includeMaterial)
        {
            face.materialName = MaterialName;
            face.surface = Surface;
        }
        face.tOffX = TOffX;
        face.tOffY = TOffY;
        face.tScaleX = TScaleX;
        face.tScaleY = TScaleY;
        face.uvRotation = UvRotation;
        face.luxelScale = LuxelScale;
        face.uvProjectionMode = UvProjectionMode;
    }

    public static void SyncFromWindow(
        float offX, float offY,
        float scaleX, float scaleY,
        float rotation, float luxelScale,
        UVProjectionMode projMode)
    {
        TOffX = offX;
        TOffY = offY;
        TScaleX = scaleX;
        TScaleY = scaleY;
        UvRotation = rotation;
        LuxelScale = luxelScale;
        UvProjectionMode = projMode;
    }
}