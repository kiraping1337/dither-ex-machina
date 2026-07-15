using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public static class ImageOps
    {
        //Box blur (byte)
        public static byte[] BoxBlur(byte[] src, int width, int height, int radius)
        {
            if (radius <= 0) return src;

            byte[] temp = new byte[src.Length];
            byte[] result = new byte[src.Length];
            int windowSize = radius * 2 + 1;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                int sum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int xx = Math.Clamp(k, 0, width - 1);
                    sum += src[rowOffset + xx];
                }
                temp[rowOffset] = (byte)(sum / windowSize);

                for (int x = 1; x < width; x++)
                {
                    int addX = Math.Clamp(x + radius, 0, width - 1);
                    int removeX = Math.Clamp(x - radius - 1, 0, width - 1);
                    sum += src[rowOffset + addX] - src[rowOffset + removeX];
                    temp[rowOffset + x] = (byte)(sum / windowSize);
                }
            });

            Parallel.For(0, width, x =>
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int yy = Math.Clamp(k, 0, height - 1);
                    sum += temp[yy * width + x];
                }
                result[x] = (byte)(sum / windowSize);

                for (int y = 1; y < height; y++)
                {
                    int addY = Math.Clamp(y + radius, 0, height - 1);
                    int removeY = Math.Clamp(y - radius - 1, 0, height - 1);
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

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                double sum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int xx = Math.Clamp(k, 0, width - 1);
                    sum += src[rowOffset + xx];
                }
                temp[rowOffset] = sum / windowSize;

                for (int x = 1; x < width; x++)
                {
                    int addX = Math.Clamp(x + radius, 0, width - 1);
                    int removeX = Math.Clamp(x - radius - 1, 0, width - 1);
                    sum += src[rowOffset + addX] - src[rowOffset + removeX];
                    temp[rowOffset + x] = sum / windowSize;
                }
            });

            Parallel.For(0, width, x =>
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int yy = Math.Clamp(k, 0, height - 1);
                    sum += temp[yy * width + x];
                }
                result[x] = sum / windowSize;

                for (int y = 1; y < height; y++)
                {
                    int addY = Math.Clamp(y + radius, 0, height - 1);
                    int removeY = Math.Clamp(y - radius - 1, 0, height - 1);
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

            Parallel.For(0, height,
                () => new byte[windowSize],
                (y, state, buffer) =>
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int n = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = Math.Clamp(x + k, 0, width - 1);
                            buffer[n++] = src[rowOffset + xx];
                        }
                        temp[rowOffset + x] = MedianOfSmallArray(buffer);
                    }
                    return buffer;
                },
                _ => { });

            Parallel.For(0, width,
                () => new byte[windowSize],
                (x, state, buffer) =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        int n = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = Math.Clamp(y + k, 0, height - 1);
                            buffer[n++] = temp[yy * width + x];
                        }
                        result[y * width + x] = MedianOfSmallArray(buffer);
                    }
                    return buffer;
                },
                _ => { });

            return result;
        }

        private static byte MedianOfSmallArray(byte[] arr)
        {
            for (int i = 1; i < arr.Length; i++)
            {
                byte key = arr[i];
                int j = i - 1;
                while (j >= 0 && arr[j] > key)
                {
                    arr[j + 1] = arr[j];
                    j--;
                }
                arr[j + 1] = key;
            }
            return arr[arr.Length / 2];
        }

        //билинейный апсемплинг
        public static double[] UpsampleBilinear(byte[] gray, int width, int height, int sw, int sh)
        {
            double[] upsampled = new double[sw * sh];

            Parallel.For(0, sh, sy =>
            {
                double srcY = (double)sy * height / sh;
                int y0 = (int)srcY;
                int y1 = Math.Min(y0 + 1, height - 1);
                double fy = srcY - y0;

                for (int sx = 0; sx < sw; sx++)
                {
                    double srcX = (double)sx * width / sw;
                    int x0 = (int)srcX;
                    int x1 = Math.Min(x0 + 1, width - 1);
                    double fx = srcX - x0;

                    double b00 = gray[y0 * width + x0];
                    double b10 = gray[y0 * width + x1];
                    double b01 = gray[y1 * width + x0];
                    double b11 = gray[y1 * width + x1];

                    double top = b00 + (b10 - b00) * fx;
                    double bottom = b01 + (b11 - b01) * fx;
                    upsampled[sy * sw + sx] = (top + (bottom - top) * fy) / 255.0;
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
            byte[] output = new byte[width * height];

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    int count = supersample * supersample;

                    for (int dy = 0; dy < supersample; dy++)
                    {
                        int sy = y * supersample + dy;
                        int rowBase = sy * sw;
                        for (int dx = 0; dx < supersample; dx++)
                        {
                            int sx = x * supersample + dx;
                            sum += hiRes[rowBase + sx];
                        }
                    }

                    output[y * width + x] = (byte)(sum / count);
                }
            });

            return output;
        }

        //простое размножение серого в BGRA
        public static byte[] GrayToBgra(byte[] gray, int width, int height)
        {
            byte[] output = new byte[width * height * 4];

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte v = gray[idx];
                    int p = idx * 4;
                    output[p] = v;
                    output[p + 1] = v;
                    output[p + 2] = v;
                    output[p + 3] = 255;
                }
            });

            return output;
        }

        //раскраска по Gradient Map
        public static byte[] ApplyGradientLut(byte[] gray, int width, int height,
            byte[] lutR, byte[] lutG, byte[] lutB)
        {
            byte[] output = new byte[width * height * 4];

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte v = gray[idx];
                    int p = idx * 4;

                    output[p] = lutB[v];     // B
                    output[p + 1] = lutG[v]; // G
                    output[p + 2] = lutR[v]; // R
                    output[p + 3] = 255;
                }
            });

            return output;
        }
    }
}