﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Threading;
using Svg;

namespace SignedDistanceFontGenerator
{
    class SignedDistanceFieldRenderer
    {
        public static Bitmap CreateTextureAtlasFromCachedBitmaps(Dictionary<char, Bitmap> dict, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0xff, 0xff, 0xff, 0xff));

                foreach (KeyValuePair<char, Bitmap> k in dict)
                {
                    int idx = (k.Key - 0x20);
                    int x = idx % (width / k.Value.Width);
                    int y = idx / (width / k.Value.Width);

                    g.CompositingMode = CompositingMode.SourceCopy;

                    g.DrawImage(
                        k.Value, 
                        x * k.Value.Width, 
                        y * k.Value.Height);
                }
            }

            return bmp;
        }

        

        public static char AsciiToChar(byte c)
        {
            byte[] input = { c };
            byte[] output = Encoding.Convert(Encoding.ASCII, Encoding.Unicode, input);
            char[] chars = new char[Encoding.Unicode.GetCharCount(output, 0, output.Length)];
            Encoding.Unicode.GetChars(output, 0, output.Length, chars, 0);
            Debug.Assert(chars.Length == 1); // one char in should be one out.

            return chars[0];
        }

        public static Bitmap RenderDistanceFieldForChar(char c, Font f, int width, int height, InterpolationMode interpolation)
        {
            uint[] buffer;

            Bitmap bmp = BitmapHelper.CreateNewManagedBitmap(width, height, out buffer);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawString(c.ToString(), f, Brushes.Black, new PointF(0, 0));
            }

            return CreateDistanceField(interpolation, width, height, 3, buffer);
        }

        public static Bitmap CreateDistanceField(InterpolationMode interpolation, int width, int height, int scale, uint[] buffer)
        {
            Grid g1, g2;
            Grid.FromBitmap(out g1, out g2, width, height, buffer);

            g1.Generate();
            g2.Generate();

            return Grid.ToBitmap(g1, g2, scale, interpolation);
        }

        /// <summary>
        /// Specific create and store method for huge distance fields, uses threading to speed up the process
        /// but the overhead might be to large for small field.
        /// </summary>
        /// <param name="interpolationMode"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="scale"></param>
        /// <param name="filename"></param>
        /// <param name="buffer"></param>
        public static void CreateAndStoreBigDistanceField(InterpolationMode interpolationMode, int width, int height, int scale, string filename, uint[] buffer, int spreadFactor = -1)        {
            ThreadPool.QueueUserWorkItem(new WaitCallback((object o) =>
            {
                Grid g1, g2;
                Grid.FromBitmap(out g1, out g2, width, height, buffer);

                int completionCounter = 0;
                ThreadPool.QueueUserWorkItem(new WaitCallback((object k) =>
                {
                    g1.Generate();
                    if (Interlocked.Add(ref completionCounter, 1) == 2)
                    {
                        Grid.ToBitmap(g1, g2, scale, interpolationMode, spreadFactor).Save(filename);
                    }
                }));

                ThreadPool.QueueUserWorkItem(new WaitCallback((object j) =>
                {
                    g2.Generate();
                    if (Interlocked.Add(ref completionCounter, 1) == 2)
                    {
                        Grid.ToBitmap(g1, g2, scale, interpolationMode, spreadFactor).Save(filename);
                    }
                }));
            }));
        }

        public static Bitmap CreateBigDistanceField(InterpolationMode interpolation, int width, int height, int scale, uint[] buffer, int spreadFactor = -1)
        {

           
            Bitmap output = new Bitmap(width>>scale,height>>scale);
            Grid g1, g2;
            Grid.FromBitmap(out g1, out g2, width, height, buffer);
            Debug.Print(
                   "Generating distance field: W:" + width.ToString() +
                   " H:" + height.ToString() +
                   " Spread: " + spreadFactor.ToString() +
                   " Scale Factor: " + scale.ToString()
                   ); 
            Thread g1t, g2t, bitmapT;
            g1t = new Thread(() =>
                {
                    g1.Generate();
                    Debug.Print("Grid 1 generated.");
                });
            g2t = new Thread(() =>
                {
                    g2.Generate();
                    Debug.Print("Grid 2 generated.");
                });

            g1t.Start();
            g2t.Start();
            g1t.Join();
            g2t.Join();
            output = Grid.ToBitmap(g1, g2, scale, interpolation, spreadFactor);
                     
            return output;

           
        }

        public static void RenderSvgToDistanceFieldToFile(string input, string output, int scaleFactor = 5, int spreadfactor = -1)
        {
            uint[] buffer;
            //TODO: make these a config setting
            int width = 4096;
            int height = 4096;

            Bitmap svg = RenderSvgToBitmapWithMaximumSize(input, width, height);
            Bitmap bmp = BitmapHelper.CreateNewManagedBitmap(width, height, out buffer);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(svg, 0, 0);
            }

            ConvertToMonochrome(buffer, width, height);
            CreateAndStoreBigDistanceField(InterpolationMode.HighQualityBicubic, width, height, scaleFactor, output, buffer, spreadfactor);
        }

        public static Bitmap RenderSvgToDistanceFieldToBMP(string input, int scaleFactor = 5, int spreadfactor = -1, int sourceWidth = 4096, int sourceHeight = 4096)
        {
            uint[] buffer;
            
            //TODO: make these a config setting
            int width = sourceWidth;
            int height = sourceHeight;

            Bitmap svg = RenderSvgToBitmapWithMaximumSize(input, width, height);
            Bitmap bmp = BitmapHelper.CreateNewManagedBitmap(width, height, out buffer);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.DrawImage(svg, 0, 0);
            }

            ConvertToMonochrome(buffer, width, height);
            return CreateBigDistanceField(InterpolationMode.HighQualityBicubic, width, height, scaleFactor, buffer, spreadfactor);
            //return CreateDistanceField(InterpolationMode.HighQualityBicubic, width, height, scaleFactor, buffer);
        }


        public static void ConvertToMonochrome(uint[] buffer, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = x + y * width;

                    uint r = (buffer[idx] & 0xff);
                    uint g = (buffer[idx] & 0xff00) >> 8;
                    uint b = (buffer[idx] & 0xff0000) >> 16;
                    uint a = (buffer[idx] & 0xff000000) >> 24;

                    uint gray = (r + g + b) / 3;

                    if (a == 0) // assume background
                    {
                        buffer[idx] = 0xffffffff;
                    }
                    else if (gray < 127)
                    {
                        buffer[idx] = 0xff000000;
                    }
                    else
                    {
                        buffer[idx] = 0xffffffff;
                    }
                }
            }
        }

        /// <summary>
        /// Renders SVG to a bitmap at specified size. 
        /// Feed desired aspect ratio corrected values to this method.
        /// </summary>
        /// <param name="input">File name of SVG file to render. </param>
        /// <param name="width">Width to render bitmap.</param>
        /// <param name="height">Height to render bitmap.</param>
        /// <returns></returns>
        public static Bitmap RenderSvgToBitmapWithMaximumSize(string input, int width, int height)
        {
            
            SvgDocument d = SvgDocument.Open(input);
            d.Width = new SvgUnit(SvgUnitType.Pixel, width);
            d.Height = new SvgUnit(SvgUnitType.Pixel, height);

            return d.Draw();
        }
    }
}
