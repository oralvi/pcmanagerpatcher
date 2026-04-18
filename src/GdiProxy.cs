/*
 * msimg32.dll 复现 - C# P/Invoke 版本
 * 
 * 使用 .NET P/Invoke 调用 GDI32 函数的代理包装器
 * 特点: 易维护、快速原型、跨越托管/非托管边界
 * 
 * 用法:
 *   var gdi = new GdiProxy();
 *   bool result = gdi.AlphaBlend(hdcDest, ...);
 *
 * 注意: 这个版本没有真正的 Hook，而是提供了代理层
 *       可用于日志记录、性能监控或未来 Hook 的集成点
 */

using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PCManagerCompat.GdiHooking
{
    /// <summary>
    /// GDI32 函数的 P/Invoke 声明与代理包装
    /// </summary>
    public static class GdiNativeMethods
    {
        private const string GDI32_DLL = "gdi32.dll";
        private const string MSIMG32_DLL = "msimg32.dll";

        // ====================================================================
        // 类型定义
        // ====================================================================

        /// <summary>
        /// BLENDFUNCTION 结构体（用于 AlphaBlend）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;       // AC_SRC_OVER
            public byte BlendFlags;    // 保留，必须为 0
            public byte SourceConstantAlpha;  // 0-255
            public byte AlphaFormat;   // AC_SRC_ALPHA
        }

        /// <summary>
        /// TRIVERTEX 结构体（用于 GradientFill）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct TRIVERTEX
        {
            public int x;       // 顶点 x 坐标
            public int y;       // 顶点 y 坐标
            public ushort Red;  // 红色分量（0-0xFF00）
            public ushort Green; // 绿色分量
            public ushort Blue;  // 蓝色分量
            public ushort Alpha; // Alpha（保留）
        }

        // ====================================================================
        // P/Invoke 导入
        // ====================================================================

        /// <summary>
        /// AlphaBlend - 半透明混合两个位图
        /// </summary>
        [DllImport(MSIMG32_DLL, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AlphaBlend(
            IntPtr hdcDest,
            int xoriginDest,
            int yoriginDest,
            int wDest,
            int hDest,
            IntPtr hdcSrc,
            int xoriginSrc,
            int yoriginSrc,
            int wSrc,
            int hSrc,
            BLENDFUNCTION ftn
        );

        /// <summary>
        /// GradientFill - 绘制颜色渐变
        /// </summary>
        [DllImport(MSIMG32_DLL, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GradientFill(
            IntPtr hdc,
            IntPtr pVertex,  // TRIVERTEX*
            uint nVertex,
            IntPtr pMesh,    // GRADIENT_RECT* or GRADIENT_TRIANGLE*
            uint nMesh,
            uint ulMode      // GRADIENT_FILL_*
        );

        /// <summary>
        /// TransparentBlt - 透明色位图传输
        /// </summary>
        [DllImport(MSIMG32_DLL, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TransparentBlt(
            IntPtr hdcDest,
            int xoriginDest,
            int yoriginDest,
            int wDest,
            int hDest,
            IntPtr hdcSrc,
            int xoriginSrc,
            int yoriginSrc,
            int wSrc,
            int hSrc,
            uint crTransparent
        );
    }

    // ========================================================================
    // 代理类：GDI 操作的托管包装器
    // ========================================================================

    /// <summary>
    /// GDI32/msimg32 函数的代理包装器
    /// 提供性能监控、异常处理、日志记录的集成点
    /// </summary>
    public class GdiProxy : IDisposable
    {
        // 配置常量
        private const int AC_SRC_OVER = 0;
        private const int AC_SRC_ALPHA = 1;

        // 性能计数器
        private struct PerformanceStats
        {
            public int CallCount;
            public long TotalDurationMs;
            public int ErrorCount;
        }

        private PerformanceStats alphaBlendStats;
        private PerformanceStats gradientFillStats;
        private PerformanceStats transparentBltStats;

        private bool disposed = false;

        // ====================================================================
        // AlphaBlend 代理
        // ====================================================================

        /// <summary>
        /// 代理 AlphaBlend - 半透明混合
        /// </summary>
        public bool AlphaBlend(
            IntPtr hdcDest,
            int xoriginDest,
            int yoriginDest,
            int wDest,
            int hDest,
            IntPtr hdcSrc,
            int xoriginSrc,
            int yoriginSrc,
            int wSrc,
            int hSrc,
            byte sourceAlpha = 255,
            bool useAlphaChannel = false)
        {
            // [Hook 点] 前置日志
            //Debug.WriteLine($"[GdiProxy.AlphaBlend] Called: dest({wDest}x{hDest}) src({wSrc}x{hSrc})");

            var sw = Stopwatch.StartNew();

            try
            {
                // 构建 BLENDFUNCTION
                var blendFunc = new GdiNativeMethods.BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = sourceAlpha,
                    AlphaFormat = useAlphaChannel ? (byte)AC_SRC_ALPHA : (byte)0
                };

                // 调用原始函数
                bool result = GdiNativeMethods.AlphaBlend(
                    hdcDest, xoriginDest, yoriginDest, wDest, hDest,
                    hdcSrc, xoriginSrc, yoriginSrc, wSrc, hSrc,
                    blendFunc
                );

                sw.Stop();
                alphaBlendStats.CallCount++;
                alphaBlendStats.TotalDurationMs += sw.ElapsedMilliseconds;

                if (!result)
                {
                    int err = Marshal.GetLastWin32Error();
                    alphaBlendStats.ErrorCount++;
                    Debug.WriteLine($"[GdiProxy.AlphaBlend] ERROR: Win32Error={err}");
                }

                // [Hook 点] 后置日志
                //Debug.WriteLine($"[GdiProxy.AlphaBlend] Result={result}, Time={sw.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                alphaBlendStats.ErrorCount++;
                Debug.WriteLine($"[GdiProxy.AlphaBlend] Exception: {ex.Message}");
                throw;
            }
        }

        // ====================================================================
        // GradientFill 代理
        // ====================================================================

        /// <summary>
        /// 代理 GradientFill - 颜色渐变填充
        /// </summary>
        public bool GradientFill(
            IntPtr hdc,
            GdiNativeMethods.TRIVERTEX[] vertices,
            IntPtr meshData,
            uint meshCount,
            uint mode)
        {
            if (vertices == null || vertices.Length == 0)
            {
                throw new ArgumentException("vertices cannot be null or empty");
            }

            var sw = Stopwatch.StartNew();

            try
            {
                // 将顶点数组固定在内存中
                GCHandle vertexHandle = GCHandle.Alloc(vertices, GCHandleType.Pinned);

                try
                {
                    bool result = GdiNativeMethods.GradientFill(
                        hdc,
                        vertexHandle.AddrOfPinnedObject(),
                        (uint)vertices.Length,
                        meshData,
                        meshCount,
                        mode
                    );

                    sw.Stop();
                    gradientFillStats.CallCount++;
                    gradientFillStats.TotalDurationMs += sw.ElapsedMilliseconds;

                    if (!result)
                    {
                        gradientFillStats.ErrorCount++;
                    }

                    return result;
                }
                finally
                {
                    if (vertexHandle.IsAllocated)
                    {
                        vertexHandle.Free();
                    }
                }
            }
            catch (Exception ex)
            {
                gradientFillStats.ErrorCount++;
                Debug.WriteLine($"[GdiProxy.GradientFill] Exception: {ex.Message}");
                throw;
            }
        }

        // ====================================================================
        // TransparentBlt 代理
        // ====================================================================

        /// <summary>
        /// 代理 TransparentBlt - 透明色位图传输
        /// </summary>
        public bool TransparentBlt(
            IntPtr hdcDest,
            int xoriginDest,
            int yoriginDest,
            int wDest,
            int hDest,
            IntPtr hdcSrc,
            int xoriginSrc,
            int yoriginSrc,
            int wSrc,
            int hSrc,
            uint crTransparent)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                bool result = GdiNativeMethods.TransparentBlt(
                    hdcDest, xoriginDest, yoriginDest, wDest, hDest,
                    hdcSrc, xoriginSrc, yoriginSrc, wSrc, hSrc,
                    crTransparent
                );

                sw.Stop();
                transparentBltStats.CallCount++;
                transparentBltStats.TotalDurationMs += sw.ElapsedMilliseconds;

                if (!result)
                {
                    transparentBltStats.ErrorCount++;
                }

                return result;
            }
            catch (Exception ex)
            {
                transparentBltStats.ErrorCount++;
                Debug.WriteLine($"[GdiProxy.TransparentBlt] Exception: {ex.Message}");
                throw;
            }
        }

        // ====================================================================
        // 性能统计
        // ====================================================================

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public void PrintStatistics()
        {
            Console.WriteLine("=== GDI Proxy Performance Statistics ===");
            
            PrintStats("AlphaBlend", alphaBlendStats);
            PrintStats("GradientFill", gradientFillStats);
            PrintStats("TransparentBlt", transparentBltStats);
        }

        private void PrintStats(string funcName, PerformanceStats stats)
        {
            if (stats.CallCount == 0)
            {
                Console.WriteLine($"{funcName}: Not called");
                return;
            }

            double avgTime = (double)stats.TotalDurationMs / stats.CallCount;
            Console.WriteLine($"{funcName}:");
            Console.WriteLine($"  Calls: {stats.CallCount}");
            Console.WriteLine($"  Total Time: {stats.TotalDurationMs} ms");
            Console.WriteLine($"  Avg Time: {avgTime:F3} ms");
            Console.WriteLine($"  Errors: {stats.ErrorCount}");
        }

        // ====================================================================
        // IDisposable 实现
        // ====================================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    PrintStatistics();
                }
                disposed = true;
            }
        }

        ~GdiProxy()
        {
            Dispose(false);
        }
    }

    // ========================================================================
    // 使用示例
    // ========================================================================

    /*
    
    // 示例用法:
    
    public class Program
    {
        public static void Main()
        {
            using (var gdi = new GdiProxy())
            {
                // 使用代理进行 GDI 操作
                
                // 示例 1: AlphaBlend
                // IntPtr hdcDest = GetDC(IntPtr.Zero);
                // IntPtr hdcSrc = CreateCompatibleDC(hdcDest);
                // gdi.AlphaBlend(hdcDest, 0, 0, 100, 100, hdcSrc, 0, 0, 100, 100, 128, true);
                // ReleaseDC(IntPtr.Zero, hdcDest);
                // DeleteDC(hdcSrc);
                
                // 示例 2: TransparentBlt
                // IntPtr hdcDest = GetDC(IntPtr.Zero);
                // IntPtr hdcSrc = CreateCompatibleDC(hdcDest);
                // gdi.TransparentBlt(hdcDest, 0, 0, 100, 100, hdcSrc, 0, 0, 100, 100, 0xFF00FF);
                // ReleaseDC(IntPtr.Zero, hdcDest);
                // DeleteDC(hdcSrc);
            }
        }
    }
    
    */
}
