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
        /// <summary>The game's light level 0-7 (GameLight), or -1 for the editor's full light.</summary>
        public int LightLevel = -1;
        public GameLight Light;
        public bool Darkness { get { return LightLevel >= 0; } }
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
        uint levelProg, spriteProg, lineProg, modelProg;
        uint levelVao, levelVbo, spriteVao, spriteVbo, lineVao, lineVbo, markVao, markVbo, modelVao, modelVbo, gridVao, gridVbo;
        uint levelTex, atlasTex, modelTex;
        int levelVertices, spriteVertices, lineVertices, markVertices, modelVertices, gridVertices;
        float[] pendingLevel, pendingSprites, pendingLines, pendingMarks, pendingModels, pendingGrid;
        List<RgbaImage> pendingModelLayers;
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
            modelProg = Program(h + Shaders.ModelVertex, h + Shaders.ModelFragment);
            levelVao = gl.GenVertexArray(); levelVbo = gl.GenBuffer();
            Layout(levelVao, levelVbo, new[] { 3, 2, 1, 1, 1 });
            spriteVao = gl.GenVertexArray(); spriteVbo = gl.GenBuffer();
            Layout(spriteVao, spriteVbo, new[] { 3, 2, 2, 1, 1 });
            lineVao = gl.GenVertexArray(); lineVbo = gl.GenBuffer();
            Layout(lineVao, lineVbo, new[] { 3, 4 });
            markVao = gl.GenVertexArray(); markVbo = gl.GenBuffer();
            Layout(markVao, markVbo, new[] { 3, 4 });
            modelVao = gl.GenVertexArray(); modelVbo = gl.GenBuffer();
            Layout(modelVao, modelVbo, new[] { 3, 2, 1, 3, 1 });
            gridVao = gl.GenVertexArray(); gridVbo = gl.GenBuffer();
            Layout(gridVao, gridVbo, new[] { 3, 4 });
            modelTex = gl.GenTexture();
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
                    float hw = s.Width / 2, hh = s.Height / 2;
                    // two triangles round the middle; the picture's top row (v0) at the top
                    Corner(f, s, -hw, -hh, u0, v1); Corner(f, s, hw, -hh, u1, v1); Corner(f, s, hw, hh, u1, v0);
                    Corner(f, s, -hw, -hh, u0, v1); Corner(f, s, hw, hh, u1, v0); Corner(f, s, -hw, hh, u0, v0);
                }
            }
            var arr = f.ToArray();
            lock (gate) pendingSprites = arr;
        }

        static void Corner(List<float> f, SpriteInstance s, float cx, float cy, float u, float v)
        {
            var c = s.Centre;
            f.Add(c.X); f.Add(c.Y); f.Add(c.Z); f.Add(cx); f.Add(cy); f.Add(u); f.Add(v); f.Add(s.Object); f.Add(s.Alpha);
        }

        /// <summary>The objects drawn as 3D models and the textures their faces use.</summary>
        public void SetModels(ModelMesh m)
        {
            var f = m == null ? new float[0] : m.ToArray();
            lock (gate) { pendingModels = f; pendingModelLayers = m == null ? new List<RgbaImage>() : m.Layers; }
        }

        /// <summary>The grid: lines like <see cref="SetLines"/>, but hidden behind walls.</summary>
        public void SetGrid(float[] lines) { lock (gate) pendingGrid = lines ?? new float[0]; }

        /// <summary>Lines as pairs of (x, y, z, r, g, b, a) points, drawn over everything.</summary>
        public void SetLines(float[] lines) { lock (gate) pendingLines = lines ?? new float[0]; }

        /// <summary>Solid markers: triangles of (x, y, z, r, g, b, a) points, hidden by walls, plus a faint copy seen through them.</summary>
        public void SetMarkers(float[] tris) { lock (gate) pendingMarks = tris ?? new float[0]; }

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
            if (pendingModels != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, modelVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingModels, BufferUsageARB.StaticDraw);
                modelVertices = pendingModels.Length / ModelVertex.Floats;
                pendingModels = null;
            }
            if (pendingModelLayers != null)
            {
                UploadArray(modelTex, pendingModelLayers, false);
                pendingModelLayers = null;
            }
            if (pendingGrid != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, gridVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingGrid, BufferUsageARB.DynamicDraw);
                gridVertices = pendingGrid.Length / 7;
                pendingGrid = null;
            }
            if (pendingLayers != null)
            {
                UploadArray(levelTex, pendingLayers, true);
                pendingLayers = null;
            }
            UploadRest();
        }

        /// <summary>64×64 layers into a texture array: repeating with mipmaps for the level,
        /// clamped and sharp for the models (each picture fits its face once).</summary>
        void UploadArray(uint tex, List<RgbaImage> layers, bool repeat)
        {
            int edge = 64, n = Math.Max(1, layers.Count);
            var all = new byte[edge * edge * 4 * n];
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null) Array.Copy(layers[i].Pixels, 0, all, i * edge * edge * 4, edge * edge * 4);
            gl.BindTexture(TextureTarget.Texture2DArray, tex);
            gl.TexImage3D<byte>(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8, (uint)edge, (uint)edge, (uint)n, 0, PixelFormat.Rgba, PixelType.UnsignedByte, all);
            if (repeat)
            {
                gl.GenerateMipmap(TextureTarget.Texture2DArray);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapLinear);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            }
            else
            {
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

        void UploadRest()
        {
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
            if (pendingMarks != null)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, markVbo);
                gl.BufferData<float>(BufferTargetARB.ArrayBuffer, pendingMarks, BufferUsageARB.DynamicDraw);
                markVertices = pendingMarks.Length / 7;
                pendingMarks = null;
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
                // the game's shading: smooth_div, smooth_lowpass, smooth_base, range; range < 0 is off
                var shading = new Vector4(0, 0, 0, -1);
                var factors = new float[16];
                bool mono = false;
                if (st.Darkness && st.Light != null)
                {
                    var row = st.Light.Rows[Math.Min(7, st.LightLevel)];
                    shading = new Vector4(row.Div, row.Lowpass, row.Base, row.Range);
                    mono = st.LightLevel == 5;
                    Array.Copy(mono ? st.Light.MonoFactors : st.Light.Factors, factors, 16);
                }
                factors[15] = 0;

                if (levelVertices > 0)
                {
                    gl.UseProgram(levelProg);
                    Matrix(levelProg, "uViewProj", vp);
                    gl.Uniform3(gl.GetUniformLocation(levelProg, "uEye"), eye.X, eye.Y, eye.Z);
                    gl.Uniform3(gl.GetUniformLocation(levelProg, "uFadeCentre"), focus.X, focus.Y, focus.Z);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uSelTile"), st.SelectedTile);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uHoverTile"), st.HoverTile);
                    Shading(levelProg, shading, factors, mono);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2DArray, levelTex);
                    gl.Uniform1(gl.GetUniformLocation(levelProg, "uTex"), 0);
                    gl.BindVertexArray(levelVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)levelVertices);
                }
                if (modelVertices > 0)
                {
                    gl.UseProgram(modelProg);
                    Matrix(modelProg, "uViewProj", vp);
                    gl.Uniform3(gl.GetUniformLocation(modelProg, "uFadeCentre"), focus.X, focus.Y, focus.Z);
                    gl.Uniform1(gl.GetUniformLocation(modelProg, "uSelObject"), st.SelectedObject);
                    gl.Uniform1(gl.GetUniformLocation(modelProg, "uHoverObject"), st.HoverObject);
                    Shading(modelProg, shading, factors, mono);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2DArray, modelTex);
                    gl.Uniform1(gl.GetUniformLocation(modelProg, "uTex"), 0);
                    gl.BindVertexArray(modelVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)modelVertices);
                }
                if (gridVertices > 0)
                {
                    // on the floors, hidden by walls in front of them
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    gl.DepthMask(false);
                    gl.UseProgram(lineProg);
                    Matrix(lineProg, "uViewProj", vp);
                    gl.BindVertexArray(gridVao);
                    gl.DrawArrays(PrimitiveType.Lines, 0, (uint)gridVertices);
                    gl.DepthMask(true);
                    gl.Disable(EnableCap.Blend);
                }
                if (spriteVertices > 0)
                {
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    gl.UseProgram(spriteProg);
                    Matrix(spriteProg, "uViewProj", vp);
                    var r = cam.Right;
                    gl.Uniform3(gl.GetUniformLocation(spriteProg, "uRight"), r.X, r.Y, r.Z);
                    var up = cam.Up;
                    gl.Uniform3(gl.GetUniformLocation(spriteProg, "uUp"), up.X, up.Y, up.Z);
                    gl.Uniform3(gl.GetUniformLocation(spriteProg, "uFadeCentre"), focus.X, focus.Y, focus.Z);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uSelObject"), st.SelectedObject);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uHoverObject"), st.HoverObject);
                    Shading(spriteProg, shading, factors, mono);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2D, atlasTex);
                    gl.Uniform1(gl.GetUniformLocation(spriteProg, "uAtlas"), 0);
                    gl.BindVertexArray(spriteVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)spriteVertices);
                    gl.Disable(EnableCap.Blend);
                }
                if (markVertices > 0)
                {
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    gl.UseProgram(lineProg);
                    Matrix(lineProg, "uViewProj", vp);
                    gl.BindVertexArray(markVao);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)markVertices);
                    // behind walls: a faint copy, so a marker in the next room still shows where it is
                    gl.DepthFunc(DepthFunction.Greater);
                    gl.DepthMask(false);
                    gl.BlendColor(0, 0, 0, 0.28f);
                    gl.BlendFunc(BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)markVertices);
                    gl.DepthMask(true);
                    gl.DepthFunc(DepthFunction.Lequal);
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

        void Shading(uint prog, Vector4 shading, float[] factors, bool mono)
        {
            gl.Uniform4(gl.GetUniformLocation(prog, "uShading"), shading.X, shading.Y, shading.Z, shading.W);
            fixed (float* f = factors) gl.Uniform1(gl.GetUniformLocation(prog, "uLight"), 16, f);
            gl.Uniform1(gl.GetUniformLocation(prog, "uMono"), mono ? 1f : 0f);
        }

        void Matrix(uint prog, string name, Matrix4x4 m)
        {
            int loc = gl.GetUniformLocation(prog, name);
            gl.UniformMatrix4(loc, 1, false, (float*)&m);
        }

        public void Dispose()
        {
            if (gl == null) return;
            gl.DeleteProgram(levelProg); gl.DeleteProgram(spriteProg); gl.DeleteProgram(lineProg); gl.DeleteProgram(modelProg);
            gl.DeleteVertexArray(modelVao); gl.DeleteVertexArray(gridVao); gl.DeleteBuffer(modelVbo); gl.DeleteBuffer(gridVbo); gl.DeleteTexture(modelTex);
            gl.DeleteVertexArray(levelVao); gl.DeleteVertexArray(spriteVao); gl.DeleteVertexArray(lineVao); gl.DeleteVertexArray(markVao); gl.DeleteBuffer(markVbo);
            gl.DeleteBuffer(levelVbo); gl.DeleteBuffer(spriteVbo); gl.DeleteBuffer(lineVbo);
            gl.DeleteTexture(levelTex); gl.DeleteTexture(atlasTex);
            gl = null;
            // a new context starts empty: nothing is drawn until it is sent again
            levelVertices = spriteVertices = lineVertices = markVertices = modelVertices = gridVertices = 0;
        }
    }
}
