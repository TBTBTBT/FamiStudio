﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using OpenTK.Graphics.OpenGL;

namespace FamiStudio
{
    public class GLFont : IDisposable
    {
        public class CharInfo
        {
            public int width;
            public int height;
            public int xoffset;
            public int yoffset;
            public int xadvance;
            public float u0;
            public float v0;
            public float u1;
            public float v1;
        }

        Dictionary<char, CharInfo> charMap = new Dictionary<char, CharInfo>();
        Dictionary<int, int> kerningPairs = new Dictionary<int, int>();

        public int Texture { get; private set; }
        public int OffsetY { get; private set; }
        public int Alignment { get; private set; }
        public bool Ellipsis { get; private set; }

        public GLFont(int tex, int offsetY, int alignment, bool ellipsis)
        {
            Texture = tex;
            OffsetY = offsetY;
            Alignment = alignment;
            Ellipsis = ellipsis;
        }

        public void Dispose()
        {
            GL.DeleteTexture(Texture);
        }

        public void AddChar(char c, CharInfo info)
        {
            charMap[c] = info;
        }

        public void AddKerningPair(int c0, int c1, int amount)
        {
            kerningPairs[c0 | (c1 << 8)] = amount;
        }

        public CharInfo GetCharInfo(char c)
        {
            if (charMap.TryGetValue(c, out CharInfo info))
            {
                return info;
            }
            else
            {
                return charMap[char.MaxValue];
            }
        }

        public int GetKerning(char c0, char c1)
        {
            int key = (int)c0 | ((int)c1 << 8);
            return kerningPairs.TryGetValue(key, out int amount) ? amount : 0;
        }

        public void MeasureString(string text, out int minX, out int maxX)
        {
            minX = 0;
            maxX = 0;

            int x = 0;

            for (int i = 0; i < text.Length; i++)
            {
                var c0 = text[i];
                var info = GetCharInfo(c0);

                int x0 = x + info.xoffset;
                int x1 = x0 + info.width;

                minX = Math.Min(minX, x0);
                maxX = Math.Max(maxX, x1);

                x += info.xadvance;
                if (i != text.Length - 1)
                {
                    char c1 = text[i + 1];
                    x += GetKerning(c0, c1);
                }
            }
        }
    }

    public class GLConvexPath
    {
        public Point[] Points { get; private set; }

        public GLConvexPath(Point[] points)
        {
            Points = points;
        }
    }

    public class GLBrush
    {
        public float GradientSizeX = 0.0f;
        public float GradientSizeY = 0.0f;
        public Color Color0;
        public Color Color1;

        public GLBrush(Color color)
        {
            Color0 = color;
        }

        public GLBrush(Color color0, Color color1, float sizeX, float sizeY)
        {
            Color0 = color0;
            Color1 = color1;
            GradientSizeX = sizeX;
            GradientSizeY = sizeY;
        }

        public bool IsGradient => GradientSizeX > 0 || GradientSizeY > 0;
    }

    public class GLBitmap : IDisposable
    {
        public int Id { get; private set; }
        public Size Size { get; private set; }

        public GLBitmap(int id, int width, int height)
        {
            Id = id;
            Size = new Size(width, height);
        }

        public void Dispose()
        {
            GL.DeleteTexture(Id);
        }
    }

    public class GLGraphics
    {
        private int windowSizeY;
        private GLControl control;
        private Rectangle scissor;
        private Point translation;
        private Stack<Rectangle> clipStack = new Stack<Rectangle>();
        private Stack<Point> translationStack = new Stack<Point>();
        private Dictionary<Tuple<Color, int>, GLBrush> verticalGradientCache = new Dictionary<Tuple<Color, int>, GLBrush>();

        public GLGraphics()
        {
        }

        public void BeginDraw(GLControl control, int windowSizeY)
        {
            this.windowSizeY = windowSizeY;
            this.control = control;

            var controlRect = FlipRectangleY(new Rectangle(control.Left, control.Top, control.Width, control.Height));

            GL.Viewport(controlRect.Left, controlRect.Top, controlRect.Width, controlRect.Height);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(0, control.Width, control.Height, 0, -1, 1);
            GL.Disable(EnableCap.CullFace);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Blend);

            translation = new Point(0, 0);
            scissor = controlRect;
            GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(scissor.Left, scissor.Top, scissor.Width, scissor.Height);
        }

        public void EndDraw()
        {
            control = null;
        }

        private Rectangle FlipRectangleY(Rectangle rc)
        {
            return new Rectangle(
                rc.Left,
                windowSizeY - rc.Top - rc.Height,
                rc.Width,
                rc.Height);
        }

        public bool AntiAliasing
        {
            get { return false; }
            set { }
        }

        public void PushTranslation(float x, float y)
        {
            translationStack.Push(translation);
            translation.X += (int)x;
            translation.Y += (int)y;

            GL.PushMatrix();
            GL.Translate(x, y, 0);
        }

        public void PopTransform()
        {
            GL.PopMatrix();

            translation = translationStack.Pop();
        }

        public void PushClip(int x0, int y0, int x1, int y1)
        {
            // OpenGL 1.1 doesnt support multiple scissor rects, but for
            // our purpose, simply intersecting the rects does the job.
            clipStack.Push(scissor);
            scissor = new Rectangle(
                translation.X + control.Left + x0,
                translation.Y + control.Top + y0,
                x1 - x0,
                y1 - y0);
            scissor = FlipRectangleY(scissor);
            scissor.Intersect(clipStack.Peek());
            GL.Scissor(scissor.Left, scissor.Top, scissor.Width, scissor.Height);
        }

        public void PopClip()
        {
            scissor = clipStack.Pop();
            GL.Scissor(scissor.Left, scissor.Top, scissor.Width, scissor.Height);
        }

        public void Clear(Color color)
        {
            GL.ClearColor(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        public void DrawBitmap(GLBitmap bmp, float x, float y, float opacity = 1.0f)
        {
            DrawBitmap(bmp, x, y, bmp.Size.Width, bmp.Size.Height, opacity);
        }

        public void DrawBitmap(GLBitmap bmp, float x, float y, float width, float height, float opacity)
        {
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, bmp.Id);
            GL.Color4(1.0f, 1.0f, 1.0f, opacity);

            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = (int)(x + width);
            int y1 = (int)(y + height);

            GL.Begin(BeginMode.Quads);
            GL.TexCoord2(0, 0); GL.Vertex2(x0, y0);
            GL.TexCoord2(1, 0); GL.Vertex2(x1, y0);
            GL.TexCoord2(1, 1); GL.Vertex2(x1, y1);
            GL.TexCoord2(0, 1); GL.Vertex2(x0, y1);
            GL.End();
            GL.Disable(EnableCap.Texture2D);
        }
        
        public void DrawText(string text, GLFont font, float startX, float startY, GLBrush brush, float width = 1000)
        {
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, font.Texture);
            GL.Color4(brush.Color0.R, brush.Color0.G, brush.Color0.B, (byte)255);
            GL.Begin(BeginMode.Quads);

            int alignmentOffsetX = 0;
            if (font.Alignment != 0)
            {
                font.MeasureString(text, out int minX, out int maxX);

                if (font.Alignment == 1)
                {
                    alignmentOffsetX -= minX;
                    alignmentOffsetX += ((int)width - maxX - minX) / 2;
                }
                else
                {
                    alignmentOffsetX -= minX;
                    alignmentOffsetX += ((int)width - maxX - minX);
                }
            }

            int x = (int)(startX + alignmentOffsetX);
            int y = (int)(startY + font.OffsetY);

            for (int i = 0; i < text.Length; i++)
            {
                var c0 = text[i];
                var info = font.GetCharInfo(c0);

                int x0 = x + info.xoffset;
                int y0 = y + info.yoffset;
                int x1 = x0 + info.width;
                int y1 = y0 + info.height;

                GL.TexCoord2(info.u0, info.v0); GL.Vertex2(x0, y0);
                GL.TexCoord2(info.u1, info.v0); GL.Vertex2(x1, y0);
                GL.TexCoord2(info.u1, info.v1); GL.Vertex2(x1, y1);
                GL.TexCoord2(info.u0, info.v1); GL.Vertex2(x0, y1);

                x += info.xadvance;
                if (i != text.Length - 1)
                {
                    char c1 = text[i + 1];
                    x += font.GetKerning(c0, c1);
                }
            }

            GL.End();
            GL.Disable(EnableCap.Texture2D);
        }

        public void DrawLine(float x0, float y0, float x1, float y1, GLBrush brush, float width = 1.0f)
        {
            GL.Color4(brush.Color0);
            GL.LineWidth(width);
            GL.Begin(BeginMode.Lines);
            GL.Vertex2(x0 + 0.5f, y0 + 0.5f);
            GL.Vertex2(x1 + 0.5f, y1 + 0.5f);
            GL.End();
        }

        public void DrawRectangle(RectangleF rect, GLBrush brush)
        {
            DrawRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom, brush);
        }

        public void DrawRectangle(float x0, float y0, float x1, float y1, GLBrush brush)
        {
            GL.Color4(brush.Color0);
            GL.Begin(BeginMode.LineLoop);
            GL.Vertex2(x0 + 0.5f, y0 + 0.5f);
            GL.Vertex2(x1 + 0.5f, y0 + 0.5f);
            GL.Vertex2(x1 + 0.5f, y1 + 0.5f);
            GL.Vertex2(x0 + 0.5f, y1 + 0.5f);
            GL.End();
        }

        public void FillRectangle(RectangleF rect, GLBrush brush)
        {
            FillRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom, brush);
        }

        public void FillRectangle(float x0, float y0, float x1, float y1, GLBrush brush)
        {
            if (!brush.IsGradient)
            {
                GL.Color4(brush.Color0);
                GL.Begin(BeginMode.Quads);
                GL.Vertex2(x0, y0);
                GL.Vertex2(x1, y0);
                GL.Vertex2(x1, y1);
                GL.Vertex2(x0, y1);
                GL.End();
            }
            else if (brush.GradientSizeX == (x1 - x0))
            {
                GL.Begin(BeginMode.Quads);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y1);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y1);
                GL.End();
            }
            else if (brush.GradientSizeY == (y1 - y0))
            {
                GL.Begin(BeginMode.Quads);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y0);
                GL.Color4(brush.Color0); GL.Vertex2(x1, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y1);
                GL.Color4(brush.Color1); GL.Vertex2(x0, y1);
                GL.End();
            }
            else if (brush.GradientSizeY == 0.0f)
            {
                float xm = x0 + brush.GradientSizeX;

                GL.Begin(BeginMode.Quads);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y0);
                GL.Color4(brush.Color1); GL.Vertex2(xm, y0);
                GL.Color4(brush.Color1); GL.Vertex2(xm, y1);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y1);
                GL.Color4(brush.Color1); GL.Vertex2(xm, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y1);
                GL.Color4(brush.Color1); GL.Vertex2(xm, y1);
                GL.End();
            }
            else if (brush.GradientSizeX == 0.0f)
            {
                float ym = y0 + brush.GradientSizeY;

                GL.Begin(BeginMode.Quads);
                GL.Color4(brush.Color0); GL.Vertex2(x0, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x0, ym);
                GL.Color4(brush.Color1); GL.Vertex2(x1, ym);
                GL.Color4(brush.Color0); GL.Vertex2(x1, y0);
                GL.Color4(brush.Color1); GL.Vertex2(x0, ym);
                GL.Color4(brush.Color1); GL.Vertex2(x0, y1);
                GL.Color4(brush.Color1); GL.Vertex2(x1, y1);
                GL.Color4(brush.Color1); GL.Vertex2(x1, ym);
                GL.End();
            }
        }

        public void FillAndDrawRectangle(float x0, float y0, float x1, float y1, GLBrush fillBrush, GLBrush lineBrush)
        {
            FillRectangle(x0, y0, x1, y1, fillBrush);
            DrawRectangle(x0, y0, x1, y1, lineBrush);
        }

        public GLConvexPath CreateConvexPath(Point[] points)
        {
            return new GLConvexPath(points);
        }

        public void FillConvexPath(GLConvexPath geo, GLBrush brush)
        {
            if (!brush.IsGradient)
            {
                GL.Color4(brush.Color0);
                GL.Begin(BeginMode.TriangleFan);
                foreach (var pt in geo.Points)
                    GL.Vertex2(pt.X + 0.5f, pt.Y + 0.5f);
                GL.End();
            }
            else
            {
                Debug.Assert(brush.GradientSizeX == 0.0f);

                GL.Begin(BeginMode.TriangleFan);
                foreach (var pt in geo.Points)
                {
                    float lerp = pt.Y / (float)brush.GradientSizeY;
                    byte r = (byte)(brush.Color0.R * (1.0f - lerp) + (brush.Color1.R * lerp));
                    byte g = (byte)(brush.Color0.G * (1.0f - lerp) + (brush.Color1.G * lerp));
                    byte b = (byte)(brush.Color0.B * (1.0f - lerp) + (brush.Color1.B * lerp));
                    byte a = (byte)(brush.Color0.A * (1.0f - lerp) + (brush.Color1.A * lerp));

                    GL.Color4(r, g, b, a);
                    GL.Vertex2(pt.X + 0.5f, pt.Y + 0.5f);
                }
                GL.End();
            }
        }

        public void DrawConvexPath(GLConvexPath geo, GLBrush brush)
        {
            GL.Enable(EnableCap.LineSmooth);
            GL.Color4(brush.Color0);
            GL.Begin(BeginMode.LineLoop);
            foreach (var pt in geo.Points)
                GL.Vertex2(pt.X + 0.5f, pt.Y + 0.5f);
            GL.End();
            GL.Disable(EnableCap.LineSmooth);
        }

        public unsafe GLBitmap CreateBitmap(int width, int height, uint[] data)
        {
            fixed (uint* ptr = &data[0])
            {
                int id = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, new IntPtr(ptr));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);
                return new GLBitmap(id, width, height);
            }
        }

        public GLBrush CreateSolidBrush(Color color)
        {
            return new GLBrush(color);
        }

        public GLBrush CreateHorizontalGradientBrush(float x0, float x1, Color color0, Color color1)
        {
            Debug.Assert(x0 == 0.0f);
            return new GLBrush(color0, color1, x1 - x0, 0.0f);
        }

        public GLBrush CreateVerticalGradientBrush(float y0, float y1, Color color0, Color color1)
        {
            Debug.Assert(y0 == 0.0f);
            return new GLBrush(color0, color1, 0.0f, y1 - y0);
        }

        public int CreateGLTexture(Gdk.Pixbuf pixbuf)
        {
            Debug.Assert(pixbuf.Rowstride == pixbuf.Width * 4);

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, pixbuf.Width, pixbuf.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixbuf.Pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);

            return id;
        }

        public unsafe GLBitmap CreateBitmapFromResource(string name)
        {
            string suffix = GLTheme.MainWindowScaling > 1 ? "@2x" : "";
            var assembly = Assembly.GetExecutingAssembly();

            Gdk.Pixbuf pixbuf = null;

            if (assembly.GetManifestResourceInfo($"FamiStudio.Resources.{name}{suffix}.png") != null)
                pixbuf = Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.{name}{suffix}.png");
            else
                pixbuf = Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.{name}.png");

            return new GLBitmap(CreateGLTexture(pixbuf), pixbuf.Width, pixbuf.Height);
        }

        public GLBrush GetVerticalGradientBrush(Color color1, int sizeY, float dimming)
        {
            var key = new Tuple<Color, int>(color1, sizeY);

            GLBrush brush;
            if (verticalGradientCache.TryGetValue(key, out brush))
                return brush;

            Color color2 = Color.FromArgb(
                Utils.Clamp((int)(color1.A), 0, 255),
                Utils.Clamp((int)(color1.R * dimming), 0, 255),
                Utils.Clamp((int)(color1.G * dimming), 0, 255),
                Utils.Clamp((int)(color1.B * dimming), 0, 255));

            brush = CreateVerticalGradientBrush(0, sizeY, color1, color2);
            verticalGradientCache[key] = brush;

            return brush;
        }

        private T ReadFontParam<T>(string[] values, string key)
        {
            for (int i = 1; i < values.Length; i += 2)
            {
                if (values[i] == key)
                {
                    return (T)Convert.ChangeType(values[i + 1], typeof(T));
                }
            }

            Debug.Assert(false);
            return default(T);
        }

        public GLFont CreateFont(Gdk.Pixbuf pixbuf, string[] def, int size, int alignment, bool ellipsis, int existingTexture = -1)
        {
            var font = (GLFont)null;
            var lines = def;

            int baseValue = 0;
            int texSizeX = 256;
            int texSizeY = 256;

            foreach (var line in lines)
            {
                var splits = line.Split(new[] { ' ', '=', '\"' }, StringSplitOptions.RemoveEmptyEntries);

                switch (splits[0])
                {
                    case "common":
                    {
                        baseValue = ReadFontParam<int>(splits, "base");
                        texSizeX  = ReadFontParam<int>(splits, "scaleW");
                        texSizeY  = ReadFontParam<int>(splits, "scaleH");

                        int glTex = existingTexture;
                        if (glTex == 0)
                            glTex = CreateGLTexture(pixbuf);

                        font = new GLFont(glTex, size - baseValue, alignment, ellipsis);
                        break;
                    }
                    case "char":
                    {
                        var charInfo = new GLFont.CharInfo();

                        int c = ReadFontParam<int>(splits, "id");
                        int x = ReadFontParam<int>(splits, "x");
                        int y = ReadFontParam<int>(splits, "y");

                        charInfo.width    = ReadFontParam<int>(splits, "width");
                        charInfo.height   = ReadFontParam<int>(splits, "height");
                        charInfo.xoffset  = ReadFontParam<int>(splits, "xoffset");
                        charInfo.yoffset  = ReadFontParam<int>(splits, "yoffset");
                        charInfo.xadvance = ReadFontParam<int>(splits, "xadvance");
                        charInfo.u0 = (x + 0.0f) / (float)texSizeX;
                        charInfo.v0 = (y + 0.0f) / (float)texSizeY;
                        charInfo.u1 = (x + 0.0f + charInfo.width) / (float)texSizeX;
                        charInfo.v1 = (y + 0.0f + charInfo.height) / (float)texSizeY;

                        font.AddChar((char)c, charInfo);

                        break;
                    }
                    case "kerning":
                    {
                        int c0 = ReadFontParam<int>(splits, "first");
                        int c1 = ReadFontParam<int>(splits, "second");
                        int amount = ReadFontParam<int>(splits, "amount");
                        font.AddKerningPair(c0, c1, amount);
                        break;
                    }
                }
            }

            return font;
        }
    };
}
