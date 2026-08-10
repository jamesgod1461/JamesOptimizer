using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

class Program {
    static void Main() {
        string source = @"C:\Users\adity\.gemini\antigravity-ide\brain\e365d85d-e7de-443e-89c7-a7ef13bf4cb0\media__1783557101897.jpg";
        string target = @"c:\Users\adity\source\repos\optimizer\OptimizerUI\app_icon.ico";
        int[] sizes = { 256, 128, 64, 48, 32, 16 };
        
        using (Bitmap bmp = new Bitmap(source))
        using (FileStream fs = new FileStream(target, FileMode.Create)) {
            // write ICO header
            fs.WriteByte(0); fs.WriteByte(0);
            fs.WriteByte(1); fs.WriteByte(0);
            fs.Write(BitConverter.GetBytes((short)sizes.Length), 0, 2);
            
            int offset = 6 + 16 * sizes.Length;
            
            // write directories
            foreach (int size in sizes) {
                int w = size >= 256 ? 0 : size;
                fs.WriteByte((byte)w); fs.WriteByte((byte)w);
                fs.WriteByte(0); fs.WriteByte(0);
                fs.WriteByte(1); fs.WriteByte(0);
                fs.WriteByte(32); fs.WriteByte(0);
                
                int bytesInRes;
                if (size == 256) {
                    using (Bitmap resized = new Bitmap(bmp, size, size))
                    using (MemoryStream ms = new MemoryStream()) {
                        resized.Save(ms, ImageFormat.Png);
                        bytesInRes = (int)ms.Length;
                    }
                } else {
                    int maskRowBytes = ((size * 1 + 31) & ~31) / 8;
                    bytesInRes = 40 + (size * size * 4) + (maskRowBytes * size);
                }
                
                fs.Write(BitConverter.GetBytes(bytesInRes), 0, 4);
                fs.Write(BitConverter.GetBytes(offset), 0, 4);
                offset += bytesInRes;
            }
            
            // write image data
            foreach (int size in sizes) {
                using (Bitmap resized = new Bitmap(bmp, size, size)) {
                    if (size == 256) {
                        resized.Save(fs, ImageFormat.Png);
                    } else {
                        // BITMAPINFOHEADER
                        fs.Write(BitConverter.GetBytes(40), 0, 4);
                        fs.Write(BitConverter.GetBytes(size), 0, 4);
                        fs.Write(BitConverter.GetBytes(size * 2), 0, 4); // Height is doubled for mask
                        fs.Write(BitConverter.GetBytes((short)1), 0, 2);
                        fs.Write(BitConverter.GetBytes((short)32), 0, 2);
                        fs.Write(new byte[24], 0, 24); // Rest of header is 0
                        
                        // Pixel data (bottom-up, BGRA)
                        for (int y = size - 1; y >= 0; y--) {
                            for (int x = 0; x < size; x++) {
                                Color c = resized.GetPixel(x, y);
                                fs.WriteByte(c.B);
                                fs.WriteByte(c.G);
                                fs.WriteByte(c.R);
                                fs.WriteByte(255); // Alpha channel opaque
                            }
                        }
                        
                        // AND mask (all 0 for no transparency)
                        int maskRowBytes = ((size * 1 + 31) & ~31) / 8;
                        fs.Write(new byte[maskRowBytes * size], 0, maskRowBytes * size);
                    }
                }
            }
        }
        Console.WriteLine("Icon generated successfully!");
    }
}
