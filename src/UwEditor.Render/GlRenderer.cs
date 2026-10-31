using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using UwEditor.Core;

namespace UwEditor.Render
{
    /// <summary>What the view should highlight and how it should look this frame.</summary>
    public sealed class ViewState
    {
        public int SelectedTile = -1, HoverTile = -1, SelectedObject = -1, HoverObject = -1;
        public bool Darkness;
        public float FadeStart = 1.5f, FadeEnd = 9;
    }

    /// <summary>
    /// Draws a level with OpenGL 3.3 / GLES 3.0. Knows nothing about the window toolkit: it gets
    /// a function loader, a framebuffer and a size. New data handed over with the Set methods is
    /// uploaded at the next <see cref="Render"/>, when the context is current.
    /// </summary>
    public sealed unsafe class GlRenderer : IDisposable
    {
        GL gl;
        bool gles;
        uint levelProg, spriteProg, lineProg;
        uint levelVao, levelVbo, spriteVao, spriteVbo, lineVao, lineVbo;
        uint levelTex, atlasTex;
        int levelVertices, spriteVertices, lineVertices;
        float[] pendingLevel, pendingSprites, pendingLines;
        List<RgbaImage> pendingLayers;
        RgbaImage pendingAtlas;
        readonly object gate = new object();
        public string Info { get; private set; }
        public string Error { get; private set; }

        public void Init(Func<string, IntPtr> getProc, bool isGles)
        {
            gles = isGles;
            gl = GL.GetApi(name => getProc(name));
            string h = Shaders.Header(gles);
            levelProg = Program(h + Shaders.LevelVertex, h + Shaders.LevelFragment);
            spriteProg = Program(h + Shaders.SpriteVertex, h + Shaders.SpriteFragment);
            lineProg = Program(h + Shaders.LineVertex, h + Shaders.LineFragment);
            levelVao = gl.GenVertexArray(); levelVbo = gl.GenBuffer();
            Layout(levelVao, levelVbo, new[] { 3, 2, 1, 1, 1 });
            spriteVao = gl.GenVertexArray(); spriteVbo = gl.GenBuffer();
            Layout(spriteVao, spriteVbo, new[] { 3, 2, 2, 1, 1 });
            lineVao = gl.GenVertexArray(); lineVbo = gl.GenBuffer();
            Layout(lineVao, lineVbo, new[] { 3, 4 });
            levelTex = gl.GenTexture();
            atlasTex = gl.GenTexture();
            Info = string.Format("{0} — {1}", gl.GetStringS(StringName.Version), gl.GetStringS(StringName.Renderer));
        }

        void Layout(uint vao, uint vbo, int[] sizes)
        {
            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            int stride = 0;
            foreach (var s in sizes) stride += s;
            int off = 0;
            for (uint i = 0; i < sizes.Length; i++)
            {
                gl.EnableVertexAttribArray(i);
                gl.VertexAttribPointer(i, sizes[i], VertexAttribPointerType.Float, false, (uint)(stride * 4), (void*)(off * 4));
                off += sizes[i];
            }
            gl.BindVertexArray(0);
        }

        uint Program(string vs, string fs)
        {
            uint v = Shader(ShaderType.VertexShader, vs), f = Shader(ShaderType.FragmentShader, fs);
            uint p = gl.CreateProgram();
            gl.AttachShader(p, v); gl.AttachShader(p, f);
            gl.LinkProgram(p);
            gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int ok);
            if (ok == 0) throw new InvalidOperationException("Shader link failed: " + gl.GetProgramInfoLog(p));
            gl.DeleteShader(v); gl.DeleteShader(f);
            return p;
        }

        uint Shader(ShaderType type, string src)
        {
            uint s = gl.CreateShader(type);
            gl.ShaderSource(s, src);
            gl.CompileShader(s);
            gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
            if (ok == 0) throw new InvalidOperationException(type + " failed: " + gl.GetShaderInfoLog(s));
            return s;
        }

        // ---- data, uploaded at the next frame

        public void SetLevel(LevelMesh mesh, List<RgbaImage> layers)
        {
            var f = mesh == null ? new float[0] : mesh.ToArray();
            lock (gate) { pendingLevel = f; pendingLayers = layers; }
        }

        public void SetAtlas(RgbaImage atlas) { lock (gate) pendingAtlas = atlas; }

        public void SetSprites(IList<SpriteInstance> sprites, SpriteAtlas atlas)
        {
            var f = new List<float>();
            if (sprites != null && atlas != null)
            {
                float aw = atlas.Image.Width, ah = atlas.Image.Height;
                foreach (var s in sprites)
                {
                    float u0 = s.Rect.X / aw, u1 = (s.Rect.X + s.Rect.W) / aw, v0 = s.Rect.Y / ah, v1 = (s.Rect.Y + s.Rect.H) / ah;
                    float hw = s.Width / 2;
                    // two triangles; the picture's top row (v0) at the top
                    Corner(f, s, -hw, 0, u0, v1); Corner(f, s, hw, 0, u1, v1); Corner(f, s, hw, s.Height, u1, v0);
                    Corner(f, s, -hw, 0, u0, v1); Corner(f, s, hw, s.Height, u1, v0); Corner(f, s, -hw, s.Height, u0, v0);
                }
            }
            var arr = f.ToArray();
            lock (gate) pendingSprites = arr;
        }

        static void Corner(List<float> f, SpriteInstance s, float cx, float cy, float u, float v)
        {
            f.Add(s.Base.X); f.Add(s.Base.Y); f.Add(s.Base.Z); f.Add(cx); f.Add(cy); f.Add(u); f.Add(v); f.Add(s.Object); f.Add(s.Alpha);
        }

        /// <summary>Lines as pairs of (x, y, z, r, g, b, a) points.</summary>
        public void SetLines(float[] lines) { lock (gate) pendingLines = lines ?? new float[0]; }

        void Upload()
        {
            lock (gate) UploadLocked();
        }

        void UploadLocked()
        {
            if (pendingLevel != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, levelVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingLevel, BufferUsageARB.StaticDraw);
                levelVertices = pendingLevel.Length / LevelVertex.Floats;
                pendingLevel = null;
            }
            if (pendingLayers != null)
            {
                int edge = 64, n = Math.Max(1, pendingLayers.Count);
                var all = new byte[edge * edge * 4 * n];
                for (int i = 0; i < pendingLayers.Count; i++)
                    if (pendingLayers[i] != null) Array.Copy(pendingLayers[i].Pixels, 0, all, i * edge * edge * 4, edge * edge * 4);
                gl.BindTexture(TextureTarget.Texture2DArray, levelTex);
                gl.TexImage3D<byte>(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8, (uint)edge, (uint)edge, (uint)n, 0, PixelFormat.Rgba, PixelType.UnsignedByte, all);
                gl.GenerateMipmap(TextureTarget.Texture2DArray);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapLinear);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                pendingLayers = null;
            }
            if (pendingAtlas != null)
            {
                gl.BindTexture(TextureTarget.Texture2D, atlasTex);
                gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)pendingAtlas.Width, (uint)pendingAtlas.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pendingAtlas.Pixels);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                pendingAtlas = null;
            }
            if (pendingSprites != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, spriteVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingSprites, BufferUsageARB.DynamicDraw);
                spriteVertices = pendingSprites.Length / 9;
                pendingSprites = null;
            }
            if (pendingLines != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, lineVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingLines, BufferUsageARB.DynamicDraw);
                lineVertices = pendingLines.Length / 7;
                pendingLines = null;
            }
        }

        public void Render(int framebuffer, int width, int height, OrbitCamera cam, ViewState st)
        {
            try
            {
                Upload();
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
                gl.Viewport(0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
                gl.ClearColor(0.055f, 0.055f, 0.07f, 1f);
                gl.ClearDepth(1);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                gl.Enable(EnableCap.DepthTest);
                gl.DepthFunc(DepthFunction.Lequal);
                gl.DepthMask(true);
                gl.Disable(EnableCap.CullFace);
                gl.Disable(EnableCap.Blend);

                var vp = cam.View * cam.Projection(width / (float)Math.Max(1, height));
                var eye = cam.Eye;
                // the darkness is a light at the point looked at, so it moves with the orbit's centre
                var focus = cam.Target;
                float fs = st.Darkness ? st.FadeStart : 1e5f, fe = st.Darkness ? st.FadeEnd : 2e5f;

                if (levelVertices > 0)
                {
                    gl.UseProgram(levelProg);
                    Matrix(levelProg, "uViewProj", vp);
                    gl.Uniform3(gl.GetUniformLocation(levelProg, "uEye"), eye.X, eye.Y, eye.Z);
                    gl.Uniform3(gl.GetUniformLocation(levelProg, "uFadeCentre"), focus.X, focus.Y, focus.Z);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uSelTile"), st.SelectedTile);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uHoverTile"), st.HoverTile);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uFadeStart"), fs);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uFadeEnd"), fe);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2DArray, levelTex);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uTex"), 0);
                    gl.BindVertexArray(levelVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)levelVertices);
                }
                if (spriteVertices > 0)
                {
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    gl.UseProgram(spriteProg);
                    Matrix(spriteProg, "uViewProj", vp);
                    var r = cam.Right;
                    gl.Uniform3(gl.GetUniformLocation(spriteProg, "uRight"), r.X, r.Y, r.Z);
                    gl.Uniform3(gl.GetUniformLocation(spriteProg, "uFadeCentre"), focus.X, focus.Y, focus.Z);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uSelObject"), st.SelectedObject);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uHoverObject"), st.HoverObject);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uFadeStart"), fs);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uFadeEnd"), fe);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2D, atlasTex);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uAtlas"), 0);
                    gl.BindVertexArray(spriteVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)spriteVertices);
                    gl.Disable(EnableCap.Blend);
                }
                if (lineVertices > 0)
                {
                    // drawn over everything, so a marker behind a wall still shows
                    gl.Disable(EnableCap.DepthTest);
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    gl.UseProgram(lineProg);
                    Matrix(lineProg, "uViewProj", vp);
                    gl.BindVertexArray(lineVao);
                    gl.DrawArrays(PrimitiveType.Lines, 0, (uint)lineVertices);
                    gl.Disable(EnableCap.Blend);
                    gl.Enable(EnableCap.DepthTest);
                }
                gl.BindVertexArray(0);
                gl.UseProgram(0);
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        void Matrix(uint prog, string name, Matrix4x4 m)
        {
            int loc = gl.GetUniformLocation(prog, name);
            gl.UniformMatrix4(loc, 1, false, (float*)&m);
        }

        public void Dispose()
        {
            if (gl == null) return;
            gl.DeleteProgram(levelProg); gl.DeleteProgram(spriteProg); gl.DeleteProgram(lineProg);
            gl.DeleteVertexArray(levelVao); gl.DeleteVertexArray(spriteVao); gl.DeleteVertexArray(lineVao);
            gl.DeleteBuffer(levelVbo); gl.DeleteBuffer(spriteVbo); gl.DeleteBuffer(lineVbo);
            gl.DeleteTexture(levelTex); gl.DeleteTexture(atlasTex);
            gl = null;
        }
    }
}
