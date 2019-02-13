﻿using AlphaOmega.Debug;
using Splat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FFXIV_GameSense
{
    static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, out uint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        internal static extern int CloseHandle(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]

        internal static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string lpModuleName);

        // CreateRemoteThread, since ThreadProc is in remote process, we must use a raw function-pointer.
        [DllImport("kernel32.dll")]
        internal static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, IntPtr dwStackSize,
          IntPtr lpStartAddress, // raw Pointer into remote process
          IntPtr lpParameter,
          uint dwCreationFlags,
          out uint lpThreadId
        );

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("shell32.dll", SetLastError = true)]
        internal static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);
        [DllImport("kernel32.dll")]
        private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll")]
        private static extern uint GetSystemDefaultLCID();

        internal static CultureInfo GetSystemDefaultCultureInfo() => new CultureInfo((int)GetSystemDefaultLCID(), true);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        internal static IntPtr GetWindowLongPtr3264(IntPtr hWnd, int nIndex)
        {
            if (Environment.Is64BitOperatingSystem)
                return GetWindowLongPtr(hWnd, nIndex);
            else
                return GetWindowLong(hWnd, nIndex);
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        internal static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong)
        {
            if (Environment.Is64BitOperatingSystem)
                return SetWindowLongPtr(hWnd, nIndex, new IntPtr(dwNewLong));
            else
                return new IntPtr(SetWindowLong(hWnd, nIndex, (int)dwNewLong));
        }

        internal static void InjectDLL(System.Diagnostics.Process Process, string DLLName, bool x86proc)
        {
            IntPtr hProcess = Process.Handle;
            // Length of string containing the DLL file name +1 byte padding 
            IntPtr LenWrite = new IntPtr(DLLName.Length + 1);
            // Allocate memory within the virtual address space of the target process 
            IntPtr AllocMem = VirtualAllocEx(hProcess, (IntPtr)null, LenWrite, AllocationType.Commit, MemoryProtection.ExecuteReadWrite);
            
            // Write DLL file name to allocated memory in target process 
            WriteProcessMemory(hProcess, AllocMem, Encoding.Default.GetBytes(DLLName), LenWrite, out uint bytesout);
            // Function pointer "Injector" 
            IntPtr Injector;
            if (Environment.Is64BitProcess && x86proc)//WOW64 case
            {
                MODULEENTRY32 k32mod = GetModules((uint)Process.Id).SingleOrDefault(x => x.szModule.Equals("kernel32.dll", StringComparison.InvariantCultureIgnoreCase));
                using (PEFile pe = new PEFile(StreamLoader.FromFile(k32mod.szExePath)))//alternatively ReadProcessMemory... but source name?
                {
                    Version winver = Environment.OSVersion.Version;
                    string LoadLibraryVariant = winver.Major <= 6 && winver.Minor <= 1 ? "LoadLibraryA" : "LoadLibraryExA";
                    Injector = IntPtr.Add(k32mod.hModule, (int)pe.Export.GetExportFunctions().SingleOrDefault(x => x.Name.Equals(LoadLibraryVariant)).Address);
                }
            }
            else
                Injector = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");

            if (Injector == null)
            {
                LogHost.Default.Error("Injector Error!");
                // return failed 
                return;
            }

            // Create thread in target process, and store handle in hThread 
            IntPtr hThread = CreateRemoteThread(hProcess, (IntPtr)null, IntPtr.Zero, Injector, AllocMem, 0, out bytesout);
            // Make sure thread handle is valid 
            if (hThread == null)
            {
                //incorrect thread handle ... return failed 
                LogHost.Default.Error("hThread [ 1 ] Error!");
                return;
            }
            // Time-out is 10 seconds... 
            uint Result = WaitForSingleObject(hThread, 10 * 1000);
            // Check whether thread timed out... 
            if (Result == 0x00000080L || Result == 0x00000102L || Result == 0xFFFFFFFF)
            {
                /* Thread timed out... */
                LogHost.Default.Error("hThread [ 2 ] Error!");
                // Make sure thread handle is valid before closing... prevents crashes. 
                if (hThread != null)
                {
                    //Close thread in target process 
                    CloseHandle(hThread);
                }
                return;
            }
            // Sleep for up to a second until named pipe client is connected
            for (int w = 0; !PersistentNamedPipeServer.Instance.IsConnected && w < 1000; w += 10)
                Thread.Sleep(10);
            // Clear up allocated space ( Allocmem ) 
            VirtualFreeEx(hProcess, AllocMem, (UIntPtr)0, 0x8000);
            // Make sure thread handle is valid before closing... prevents crashes. 
            if (hThread != null)
            {
                //Close thread in target process 
                CloseHandle(hThread);
            }
            // return succeeded 
            return;
        }

        private static List<MODULEENTRY32> GetModules(uint pid)
        {
            List<MODULEENTRY32> modules = new List<MODULEENTRY32>();
            IntPtr hModule = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPMODULE | SnapshotFlags.TH32CS_SNAPMODULE32, pid);
            MODULEENTRY32 mEntry = new MODULEENTRY32();
            mEntry.dwSize = (uint)Marshal.SizeOf(mEntry);
            while (Module32Next(hModule, ref mEntry))
            {
                modules.Add(mEntry);
                mEntry = new MODULEENTRY32();
                mEntry.dwSize = (uint)Marshal.SizeOf(mEntry);
            }
            return modules;
        }

        [Flags]
        private enum SnapshotFlags : uint
        {
            TH32CS_SNAPHEAPLIST = 0x00000001,
            TH32CS_SNAPPROCESS = 0x00000002,
            TH32CS_SNAPTHREAD = 0x00000004,
            TH32CS_SNAPMODULE = 0x00000008,
            TH32CS_SNAPMODULE32 = 0x00000010,
            TH32CS_INHERIT = 0x80000000,
            TH32CS_SNAPALL = TH32CS_SNAPHEAPLIST | TH32CS_SNAPMODULE | TH32CS_SNAPPROCESS | TH32CS_SNAPTHREAD,
            NoHeaps = 0x40000000
        }

        [Flags]
        public enum AllocationType
        {
            Commit = 0x1000,
            Reserve = 0x2000,
            Unknown = 0x3000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        public enum MemoryProtection
        {
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            ///<summary>x position of upper-left corner</summary>
            public int Left;
            ///<summary>y position of upper-left corner</summary>
            public int Top;
            ///<summary>x position of lower-right corner</summary>
            public int Right;
            ///<summary>y position of lower-right corner</summary>
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            /// <summary>
            /// The size of the structure in bytes.
            /// </summary>
            public uint cbSize;
            /// <summary>
            /// A Handle to the Window to be Flashed. The window can be either opened or minimized.
            /// </summary>
            public IntPtr hwnd;
            /// <summary>
            /// The Flash Status.
            /// </summary>
            public uint dwFlags;
            /// <summary>
            /// The number of times to Flash the window.
            /// </summary>
            public uint uCount;
            /// <summary>
            /// The rate at which the Window is to be flashed, in milliseconds. If Zero, the function uses the default cursor blink rate.
            /// </summary>
            public uint dwTimeout;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MODULEENTRY32
        {
            internal uint dwSize;
            internal uint th32ModuleID;
            internal uint th32ProcessID;
            internal uint GlblcntUsage;
            internal uint ProccntUsage;
            internal IntPtr modBaseAddr;
            internal uint modBaseSize;
            internal IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string szExePath;
        }

        private const uint FLASHW_STOP = 0;// Stop flashing. The system restores the window to its original stae.
        private const uint FLASHW_CAPTION = 1;// Flash the window caption.
        private const uint FLASHW_TASKBAR = 2;// Flash the taskbar button.
        private const uint FLASHW_ALL = 3;// Flash both the window caption and taskbar button. This is equivalent to setting the FLASHW_CAPTION | FLASHW_TRAY flags.
        private const uint FLASHW_TIMER = 4;// Flash continuously, until the FLASHW_STOP flag is set.
        private const uint FLASHW_TIMERNOFG = 12;// Flash continuously until the window comes to the foreground.

        internal static bool FlashTaskbarIcon(System.Diagnostics.Process process, uint duration, bool stopOnFocus = false) => FlashWindowEx(process, duration, stopOnFocus ? FLASHW_TASKBAR | FLASHW_TIMERNOFG : FLASHW_TASKBAR);
        internal static bool FlashTaskbarIcon(System.Diagnostics.Process process) => FlashWindowEx(process, uint.MaxValue, FLASHW_TASKBAR | FLASHW_TIMERNOFG);
        internal static bool StopFlashWindowEx(System.Diagnostics.Process process) => FlashWindowEx(process, 0, FLASHW_STOP);

        private static bool FlashWindowEx(System.Diagnostics.Process process, uint duration, uint flags)
        {
            var pwfi = new FLASHWINFO();
            pwfi.cbSize = Convert.ToUInt32(Marshal.SizeOf(pwfi));
            pwfi.hwnd = process.MainWindowHandle;
            pwfi.dwFlags = flags;
            pwfi.uCount = duration;
            pwfi.dwTimeout = 0;
            return FlashWindowEx(ref pwfi);
        }
    }

    public static class ApplicationRunningHelper
    {
        public static bool AlreadyRunning()
        {
            try
            {
                const int swRestore = 9;
                var me = System.Diagnostics.Process.GetCurrentProcess();
                var arrProcesses = System.Diagnostics.Process.GetProcessesByName(me.ProcessName);
                for (int i = 0; i < arrProcesses.Length; i++)
                {
                    if (arrProcesses[i].MainModule.FileName == me.MainModule.FileName && arrProcesses[i].Id != me.Id)
                    {
                        // get the window handle
                        IntPtr hWnd = arrProcesses[i].MainWindowHandle;
                        // if iconic, we need to restore the window
                        if (NativeMethods.IsIconic(hWnd))
                        {
                            NativeMethods.ShowWindowAsync(hWnd, swRestore);
                        }
                        // bring it to the foreground
                        NativeMethods.SetForegroundWindow(hWnd);
                        return true;
                    }
                }
                return false;
            }
            catch { return true; }
        }
    }
}
