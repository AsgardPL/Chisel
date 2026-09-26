using Engine.Rendering;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    [EntityDescriptor()]
    //[ExposeEntityProperty("Cubemap Projection Bounds Brush", Rockwall.EntityPropertyType.String, "When projecting cubemaps, use the brush boundaries of the brush entity with this target name.")]
    public class EnvCubemap : WorldEntity
    {
        public static List<EnvCubemap> Cubemaps = new List<EnvCubemap>();
        public static bool cubeRendering = false;

        public TextureCube[] diffusionMaps;
        static RenderTargetCube cubeTarget;
        public EnvCubemap()
        {
            Controller = new CubemapController();
            IsSimulated = false;
            IgnoreCollision = true;

            int size = 128;

            cubeTarget ??= new RenderTargetCube(MainEngine.Instance.GraphicsDevice, size, false, SurfaceFormat.Color, DepthFormat.Depth16);
        }

        internal class CubemapController : EntityController
        {
            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnBeforeRender(GameTime gameTime)
            {
                cubeRendering = true;
                var cube = entity as EnvCubemap;

                DrawCube();

                cubeRendering = false;

                EntityManager.DespawnEntity(entity);
            }
            void DrawCube()
            {
                var cube = entity as EnvCubemap;

                var oldp = RenderEngine.ProjectionMatrix;
                RenderEngine.ProjectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(90f), 1, 0.01f, 1000f);

                float yaw = 0, pitch = 0;

                MainEngine.Instance.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                var prevFrustum = RenderEngine.CameraBoundingFrustum;
                for (int i = 0; i < 6; i++)
                {
                    switch (i)
                    {
                        case 0:
                            pitch = 0;
                            yaw = 90;
                            break;
                        case 1:
                            pitch = 0;
                            yaw = -90;
                            break;
                        case 2:
                            pitch = 90;
                            yaw = 180;
                            break;
                        case 3:
                            pitch = -90;
                            yaw = 180;
                            break;
                        case 4:
                            pitch = 0;
                            yaw = 180;
                            break;
                        case 5:
                            pitch = 0;
                            yaw = 0;
                            break;
                    }

                    MainEngine.Instance.GraphicsDevice.SetRenderTarget(cubeTarget, CubeMapFace.PositiveX + i);
                    MainEngine.Instance.GraphicsDevice.Clear(Color.White);

                    RenderEngine.WorldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
                    RenderEngine.ViewMatrix = Matrix.CreateTranslation(-cube.Position) * Matrix.CreateScale(-1,1,1) * Matrix.Invert(Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch), 0));
                    RenderEngine.CameraBoundingFrustum = new BoundingFrustum(RenderEngine.WorldMatrix * RenderEngine.ViewMatrix * RenderEngine.ProjectionMatrix);
                    
                    MainEngine.Instance.GraphicsDevice.RasterizerState = RasterizerState.CullClockwise;
                    
                    Skybox.Draw(RenderEngine.ViewMatrix, RenderEngine.ProjectionMatrix);
                    MainEngine.Instance.GraphicsDevice.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, MainEngine.Instance.GraphicsDevice.Viewport.MaxDepth, 1);
                    
                    MainEngine.Instance.GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                    MainEngine.Instance.GraphicsDevice.SamplerStates[0] = RenderEngine.WorldTextureSamplerState;
                    MainEngine.Instance.GraphicsDevice.SamplerStates[1] = RenderEngine.WorldTextureSamplerState;
                    RenderEngine.PrepareWorldShaders();
                    RenderEngine.RenderMap(false);
                }
                MainEngine.Instance.GraphicsDevice.SetRenderTarget(null);

                cube.diffusionMaps = new TextureCube[6];
                cube.diffusionMaps[0] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size);
                cube.diffusionMaps[1] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size>>1);
                cube.diffusionMaps[2] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size>>2);
                cube.diffusionMaps[3] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size>>3);
                cube.diffusionMaps[4] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size>>4);
                cube.diffusionMaps[5] = CubemapMipmapGenerator.ScaleCube(cubeTarget, cubeTarget.Size>>5);

                RenderEngine.CameraBoundingFrustum = prevFrustum;

                RenderEngine.ProjectionMatrix = oldp;
            }
            public override void OnSpawn()
            {
                Cubemaps.Add((EnvCubemap)entity);
            }

            public override void OnUpdate(GameTime gameTime)
            {
            }
        }
    }
}
