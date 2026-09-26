#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BiosOptimizer.Core.Implementations
{
    public class DxgiAdapterInfo
    {
        public string Description { get; set; } = "";
        public uint VendorId { get; set; }
        public uint DeviceId { get; set; }
        public ulong DedicatedVideoMemoryBytes { get; set; }
        public ulong DedicatedSystemMemoryBytes { get; set; }
        public ulong SharedSystemMemoryBytes { get; set; }
        public uint Flags { get; set; }
        public bool IsSoftwareAdapter { get; set; }
        public bool IsDedicatedGpu { get; set; }
        public string DedicatedVramFormatted { get; set; } = "N/A";
        public string SharedMemoryFormatted { get; set; } = "Dynamic";
        public string ReservedMemoryFormatted { get; set; } = "";
        public string AdapterTypeString => IsDedicatedGpu ? "DEDICATED GPU" : "INTEGRATED GPU";
    }

    public static class DxgiGpuReader
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags; // DXGI_ADAPTER_FLAG_SOFTWARE = 2
        }

        [ComImport]
        [Guid("29038f61-3839-4626-91fd-086879011a05")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

            // IDXGIAdapter
            [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(IntPtr pDesc);
            [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);

            // IDXGIAdapter1
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        }

        [ComImport]
        [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

            // IDXGIFactory
            [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
            [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);

            // IDXGIFactory1
            [PreserveSig] int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
            [PreserveSig] int IsCurrent();
        }

        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

        public static List<DxgiAdapterInfo> EnumerateAdapters()
        {
            var results = new List<DxgiAdapterInfo>();

            try
            {
                var factoryGuid = typeof(IDXGIFactory1).GUID;
                int hr = CreateDXGIFactory1(ref factoryGuid, out var factory);
                if (hr != 0 || factory == null) return results;

                uint adapterIndex = 0;
                while (true)
                {
                    hr = factory.EnumAdapters1(adapterIndex, out var adapter);
                    if (hr != 0 || adapter == null) break;

                    try
                    {
                        if (adapter.GetDesc1(out var desc) == 0)
                        {
                            bool isSoftware = (desc.Flags & 2) != 0 || 
                                              desc.VendorId == 0x1414 || // Microsoft Basic Render Driver
                                              desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                                              desc.Description.Contains("Software Adapter", StringComparison.OrdinalIgnoreCase);

                            if (!isSoftware && !string.IsNullOrWhiteSpace(desc.Description))
                            {
                                ulong dedicatedVideo = (ulong)desc.DedicatedVideoMemory.ToUInt64();
                                ulong dedicatedSystem = (ulong)desc.DedicatedSystemMemory.ToUInt64();
                                ulong sharedSystem = (ulong)desc.SharedSystemMemory.ToUInt64();

                                var info = new DxgiAdapterInfo
                                {
                                    Description = desc.Description.Trim(),
                                    VendorId = desc.VendorId,
                                    DeviceId = desc.DeviceId,
                                    DedicatedVideoMemoryBytes = dedicatedVideo,
                                    DedicatedSystemMemoryBytes = dedicatedSystem,
                                    SharedSystemMemoryBytes = sharedSystem,
                                    Flags = desc.Flags,
                                    IsSoftwareAdapter = isSoftware
                                };

                                ClassifyGpuMemory(info);
                                results.Add(info);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }

                    adapterIndex++;
                }

                Marshal.ReleaseComObject(factory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DxgiGpuReader] Exception enumerating DXGI adapters: {ex}");
            }

            return results;
        }

        private static void ClassifyGpuMemory(DxgiAdapterInfo info)
        {
            // Vendor IDs:
            // 0x10DE = NVIDIA (Discrete GPU)
            // 0x1002 = AMD (Discrete RX series or Integrated APU)
            // 0x8086 = Intel (Integrated UHD/Iris or Discrete Arc)

            string name = info.Description.ToLowerInvariant();
            ulong dedicated = info.DedicatedVideoMemoryBytes;
            ulong shared = info.SharedSystemMemoryBytes;
            ulong reserved = info.DedicatedSystemMemoryBytes;

            bool isExplicitDedicatedVendor = info.VendorId == 0x10DE || // NVIDIA is discrete
                                            name.Contains("geforce") || 
                                            name.Contains("rtx") || 
                                            name.Contains("gtx") ||
                                            name.Contains("quadro") ||
                                            name.Contains("arc a") || 
                                            (name.Contains("radeon rx") && !name.Contains("radeon(tm)"));

            bool isExplicitIntegratedName = name.Contains("intel") && !name.Contains("arc") ||
                                           name.Contains("uhd graphics") ||
                                           name.Contains("iris") ||
                                           name.Contains("hd graphics") ||
                                           name.Contains("radeon(tm) graphics") ||
                                           name.Contains("radeon graphics") ||
                                           name.Contains("vega");

            // Decision Matrix:
            // If DedicatedVideoMemory >= 1 GB (1073741824 bytes), and not an explicit Intel iGPU driver stub, it is true dedicated VRAM.
            if (dedicated >= 1024 * 1024 * 1024L && (isExplicitDedicatedVendor || !isExplicitIntegratedName))
            {
                info.IsDedicatedGpu = true;
                double gb = dedicated / (1024.0 * 1024.0 * 1024.0);
                double roundGb = Math.Round(gb);
                if (Math.Abs(gb - roundGb) <= 0.35)
                {
                    info.DedicatedVramFormatted = $"{roundGb:F0} GB";
                }
                else
                {
                    info.DedicatedVramFormatted = $"{gb:F1} GB";
                }
            }
            else
            {
                // Integrated GPU Architecture
                info.IsDedicatedGpu = false;
                info.DedicatedVramFormatted = "N/A";

                if (dedicated > 0 && dedicated < 1024 * 1024 * 1024L)
                {
                    info.ReservedMemoryFormatted = $"{dedicated / (1024 * 1024)} MB";
                }
                else if (reserved > 0)
                {
                    info.ReservedMemoryFormatted = $"{reserved / (1024 * 1024)} MB";
                }
            }

            // Shared GPU Memory calculation
            if (shared > 0)
            {
                double sharedGb = shared / (1024.0 * 1024.0 * 1024.0);
                info.SharedMemoryFormatted = $"{sharedGb:F1} GB";
            }
            else
            {
                info.SharedMemoryFormatted = "Dynamic";
            }
        }
    }
}
