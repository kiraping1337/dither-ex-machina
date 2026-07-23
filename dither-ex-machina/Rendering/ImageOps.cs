using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace dither_ex_machina.Rendering
{
    public static class ImageOps
    {
        private static readonly ParallelOptions ParallelOptions = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

        //Box blur (byte)
        public static byte[] BoxBlur(byte[] src, int width, int height, int radius)
        {
            if (radius <= 0) return src;

            byte[] temp = new byte[src.Length];
            byte[] result = new byte[src.Length];
            int windowSize = radius * 2 + 1;
            int maxX = width - 1;
            int maxY = height - 1;

            Parallel.For(0, height, ParallelOptions, y =>
            {
                int rowOffset = y * width;
                int sum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int xx = k < 0 ? 0 : (k > maxX ? maxX : k);
                    sum += src[rowOffset + xx];
                }
                temp[rowOffset] = (byte)(sum / windowSize);

                for (int x = 1; x < width; x++)
                {
                    int addX = x + radius > maxX ? maxX : x + radius;
                    int removeX = x - radius - 1 < 0 ? 0 : x - radius - 1;
                    sum += src[rowOffset + addX] - src[rowOffset + removeX];
                    temp[rowOffset + x] = (byte)(sum / windowSize);
                }
            });

            Parallel.For(0, width, ParallelOptions, x =>
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int yy = k < 0 ? 0 : (k > maxY ? maxY : k);
                    sum += temp[yy * width + x];
                }
                result[x] = (byte)(sum / windowSize);

                for (int y = 1; y < height; y++)
                {
                    int addY = y + radius > maxY ? maxY : y + radius;
                    int removeY = y - radius - 1 < 0 ? 0 : y - radius - 1;
                    sum += temp[addY * width + x] - temp[removeY * width + x];
                    result[y * width + x] = (byte)(sum / windowSize);
                }
            });

            return result;
        }

        //Box blur (double)
        public static double[] BoxBlurDouble(double[] src, int width, int height, int radius)
        {
            if (radius <= 0) return src;

            double[] temp = new double[src.Length];
            double[] result = new double[src.Length];
            int windowSize = radius * 2 + 1;
            int maxX = width - 1;
            int maxY = height - 1;

            Parallel.For(0, height, ParallelOptions, y =>
            {
                int rowOffset = y * width;
                double sum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int xx = k < 0 ? 0 : (k > maxX ? maxX : k);
                    sum += src[rowOffset + xx];
                }
                temp[rowOffset] = sum / windowSize;

                for (int x = 1; x < width; x++)
                {
                    int addX = x + radius > maxX ? maxX : x + radius;
                    int removeX = x - radius - 1 < 0 ? 0 : x - radius - 1;
                    sum += src[rowOffset + addX] - src[rowOffset + removeX];
                    temp[rowOffset + x] = sum / windowSize;
                }
            });

            Parallel.For(0, width, ParallelOptions, x =>
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int yy = k < 0 ? 0 : (k > maxY ? maxY : k);
                    sum += temp[yy * width + x];
                }
                result[x] = sum / windowSize;

                for (int y = 1; y < height; y++)
                {
                    int addY = y + radius > maxY ? maxY : y + radius;
                    int removeY = y - radius - 1 < 0 ? 0 : y - radius - 1;
                    sum += temp[addY * width + x] - temp[removeY * width + x];
                    result[y * width + x] = sum / windowSize;
                }
            });

            return result;
        }

        //медианный фильтр
        public static byte[] MedianFilter(byte[] src, int width, int height, int radius)
        {
            if (radius <= 0) return src;

            byte[] temp = new byte[src.Length];
            byte[] result = new byte[src.Length];
            int windowSize = radius * 2 + 1;
            int maxX = width - 1;

            Parallel.For(0, height, ParallelOptions,
                () => new int[256],
                (y, state, histogram) =>
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        Array.Clear(histogram, 0, 256);
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = x + k < 0 ? 0 : (x + k > maxX ? maxX : x + k);
                            histogram[src[rowOffset + xx]]++;
                        }
                        int medianPos = windowSize / 2;
                        int count = 0;
                        for (int v = 0; v < 256; v++)
                        {
                            count += histogram[v];
                            if (count > medianPos)
                            {
                                temp[rowOffset + x] = (byte)v;
                                break;
                            }
                        }
                    }
                    return histogram;
                },
                _ => { });

            int maxY = height - 1;
            Parallel.For(0, width, ParallelOptions,
                () => new int[256],
                (x, state, histogram) =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        Array.Clear(histogram, 0, 256);
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = y + k < 0 ? 0 : (y + k > maxY ? maxY : y + k);
                            histogram[temp[yy * width + x]]++;
                        }
                        int medianPos = windowSize / 2;
                        int count = 0;
                        for (int v = 0; v < 256; v++)
                        {
                            count += histogram[v];
                            if (count > medianPos)
                            {
                                result[y * width + x] = (byte)v;
                                break;
                            }
                        }
                    }
                    return histogram;
                },
                _ => { });

            return result;
        }

        //билинейный апсемплинг
        public static double[] UpsampleBilinear(byte[] gray, int width, int height, int sw, int sh)
        {
            double[] upsampled = new double[sw * sh];
            double invWidth = 1.0 / width;
            double invHeight = 1.0 / height;
            double invSw = 1.0 / sw;
            double invSh = 1.0 / sh;
            double scaleX = width * invSw;
            double scaleY = height * invSh;

            Parallel.For(0, sh, ParallelOptions, sy =>
            {
                double srcY = sy * scaleY;
                int y0 = (int)srcY;
                if (y0 >= height - 1) y0 = height - 2;
                int y1 = y0 + 1;
                double fy = srcY - y0;

                int row0 = y0 * width;
                int row1 = y1 * width;

                for (int sx = 0; sx < sw; sx++)
                {
                    double srcX = sx * scaleX;
                    int x0 = (int)srcX;
                    if (x0 >= width - 1) x0 = width - 2;
                    int x1 = x0 + 1;
                    double fx = srcX - x0;

                    double b00 = gray[row0 + x0];
                    double b10 = gray[row0 + x1];
                    double b01 = gray[row1 + x0];
                    double b11 = gray[row1 + x1];

                    double top = b00 + (b10 - b00) * fx;
                    double bottom = b01 + (b11 - b01) * fx;
                    upsampled[sy * sw + sx] = (top + (bottom - top) * fy) * (1.0 / 255.0);
                }
            });

            return upsampled;
        }

        //контрастная сигмоида
        public static double ApplyContrastCurve(double x, double center, double steepness)
        {
            double Sigmoid(double v) => 1.0 / (1.0 + Math.Exp(-v));

            double lo = Sigmoid((0.0 - center) * steepness);
            double hi = Sigmoid((1.0 - center) * steepness);
            double raw = Sigmoid((x - center) * steepness);

            double range = hi - lo;
            if (range < 1e-9) return x;

            return (raw - lo) / range;
        }

        //даунсемплинг с усреднением 
        public static byte[] DownsampleGray(byte[] hiRes, int sw, int sh, int width, int height, int supersample)
        {
            return DownsampleGray(hiRes, sw, sh, width, height, supersample, useParallel: true);
        }

        //Non-parallel версия для тайловой обработки чтобы избежать вложенного параллелизма
        internal static byte[] DownsampleGray(byte[] hiRes, int sw, int sh, int width, int height, int supersample, bool useParallel)
        {
            byte[] output = new byte[width * height];
            int totalPixels = width * height;
            int count = supersample * supersample;

            if (useParallel)
            {
                Parallel.For(0, totalPixels, ParallelOptions, idx =>
                {
                    int x = idx % width;
                    int y = idx / width;

                    int sum = 0;
                    int syStart = y * supersample;
                    int sxStart = x * supersample;

                    for (int dy = 0; dy < supersample; dy++)
                    {
                        int rowBase = (syStart + dy) * sw;
                        for (int dx = 0; dx < supersample; dx++)
                        {
                            sum += hiRes[rowBase + sxStart + dx];
                        }
                    }

                    output[idx] = (byte)(sum / count);
                });
            }
            else
            {
                for (int idx = 0; idx < totalPixels; idx++)
                {
                    int x = idx % width;
                    int y = idx / width;

                    int sum = 0;
                    int syStart = y * supersample;
                    int sxStart = x * supersample;

                    for (int dy = 0; dy < supersample; dy++)
                    {
                        int rowBase = (syStart + dy) * sw;
                        for (int dx = 0; dx < supersample; dx++)
                        {
                            sum += hiRes[rowBase + sxStart + dx];
                        }
                    }

                    output[idx] = (byte)(sum / count);
                }
            }

            return output;
        }

        //простое размножение серого в BGRA
        public static byte[] GrayToBgra(byte[] gray, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            int totalPixels = width * height;

            Parallel.For(0, totalPixels, ParallelOptions, idx =>
            {
                byte v = gray[idx];
                int p = idx * 4;
                output[p] = v;
                output[p + 1] = v;
                output[p + 2] = v;
                output[p + 3] = 255;
            });

            return output;
        }

        //раскраска по Gradient Map
        public static byte[] ApplyGradientLut(byte[] gray, int width, int height,
            byte[] lutR, byte[] lutG, byte[] lutB)
        {
            byte[] output = new byte[width * height * 4];
            int totalPixels = width * height;

            Parallel.For(0, totalPixels, ParallelOptions, idx =>
            {
                byte v = gray[idx];
                int p = idx * 4;
                output[p] = lutB[v];
                output[p + 1] = lutG[v];
                output[p + 2] = lutR[v];
                output[p + 3] = 255;
            });

            return output;
        }

        //эффект Glow
        public static byte[] ApplyGlow(byte[] gray, int width, int height, int radius, double threshold, double intensity)
        {
            if (radius <= 0 || intensity <= 0) return gray;

            byte[] blurred = BoxBlur(gray, width, height, radius);
            byte[] output = new byte[gray.Length];

            Parallel.For(0, gray.Length, ParallelOptions, i =>
            {
                byte b = blurred[i];
                double diff = b / 255.0 - threshold;
                if (diff > 0)
                {
                    double add = diff * intensity * 255;
                    int v = gray[i] + (int)add;
                    output[i] = (byte)(v > 255 ? 255 : v);
                }
                else
                {
                    output[i] = gray[i];
                }
            });

            return output;
        }
    }
}