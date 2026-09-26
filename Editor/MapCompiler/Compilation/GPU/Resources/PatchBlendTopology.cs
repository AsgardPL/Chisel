using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU.Resources;
public sealed class PatchBlendTopology : IDisposable
{
    private readonly GpuBuffer texelHomePatch;
    private readonly GpuBuffer texelNeighborOffsets;
    private readonly GpuBuffer texelNeighborPatches;

    public PatchBlendTopology(GL gl, int[] texelHomePatchArr, int[] offsets, int[] flat)
    {
        texelHomePatch = new GpuBuffer(gl);
        texelHomePatch.Upload<int>(texelHomePatchArr);

        texelNeighborOffsets = new GpuBuffer(gl);
        texelNeighborOffsets.Upload<int>(offsets);

        texelNeighborPatches = new GpuBuffer(gl);
        texelNeighborPatches.Upload<int>(flat);
    }

    public void Bind()
    {
        texelHomePatch.BindBase(GpuBindings.TexelHomePatch);
        texelNeighborOffsets.BindBase(GpuBindings.TexelNeighborOffsets);
        texelNeighborPatches.BindBase(GpuBindings.TexelNeighborPatches);
    }

    public void Dispose()
    {
        texelHomePatch.Dispose();
        texelNeighborOffsets.Dispose();
        texelNeighborPatches.Dispose();
    }
}