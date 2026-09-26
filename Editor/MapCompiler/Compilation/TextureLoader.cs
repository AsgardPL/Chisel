using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MapCompiler
{
    /// <summary>
    /// Loads textures referenced by brush faces and pre-computes per-material
    /// average colors used for radiosity albedo.
    /// </summary>
    public static class TextureLoader
    {
        public static (System.Drawing.Bitmap[] textures, Color[] matColors) Load(string texturePath,Brush[] brushes)
        {
            var textures   = new System.Drawing.Bitmap[GlobalMapData.loadedMaterials.Length];
            var matColors  = new Color[GlobalMapData.loadedMaterials.Length];

            var usedTextures = brushes.SelectMany(b => b.faces).Select(f => f.surface).Distinct().ToArray();
            CompilerConsole.Stat("Unique textures", usedTextures.Length);

            foreach (int i in usedTextures)
                textures[i] = (System.Drawing.Bitmap)System.Drawing.Bitmap.FromFile(
                    $"{texturePath}/{GlobalMapData.loadedMaterials[i].texture}.png");

            Parallel.ForEach(usedTextures, i =>
                matColors[i] = GetDominantColor(textures[i]));

            return (textures, matColors);
        }

        /// <summary>
        /// Computes the average color of a bitmap by sampling every other pixel.
        /// </summary>
        public static Color GetDominantColor(System.Drawing.Bitmap bmp)
        {
            long r = 0, g = 0, b = 0, total = 0;

            for (int x = 0; x < bmp.Width; x += 2)
            for (int y = 0; y < bmp.Height; y += 2)
            {
                var c = bmp.GetPixel(x, y);
                r += c.R; g += c.G; b += c.B;
                total++;
            }

            return new Color((int)(r / total), (int)(g / total), (int)(b / total), 255);
        }
    }
}
