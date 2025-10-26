using MelonLoader.Bootstrap.RuntimeHandlers.Il2Cpp;
using MelonLoader.Bootstrap.RuntimeHandlers.Mono;
using System.Runtime.InteropServices;

namespace MelonLoader.Bootstrap
{
    internal static partial class ModuleSymbolRedirect
    {
        private static bool _runtimeInitialised;

#if LINUX || OSX
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
#endif
#if WINDOWS
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
#endif
        private delegate nint DetourFn(nint handle, nint symbol);
        private static readonly DetourFn DetourDelegate = SymbolDetour;

        internal static void ApplyHook()
        {
            IntPtr detourPtr = Marshal.GetFunctionPointerForDelegate(DetourDelegate);

#if LINUX || OSX
            PltHook.InstallHooks
            ([
                ("dlsym", detourPtr)
            ]);
#endif

#if WINDOWS
            PltHook.InstallHooks
            ([
                ("GetProcAddress", detourPtr)
            ]);
#endif
        }

        private static nint SymbolDetour(nint handle, nint symbol)
        {
            // Herp: Using Prebuilt Interop classes for this caused weird crashing issues when attempting to marshal string to span
            // This works around the issue by manually importing the appropriate original export into a delegate and then calling original using that instead
#if WINDOWS
            nint originalSymbolAddress = GetProcAddress(handle, symbol);
#else
            nint originalSymbolAddress = dlsym(handle, symbol);
#endif

            string? symbolName = Marshal.PtrToStringAnsi(symbol);
            if (string.IsNullOrEmpty(symbolName)
                || string.IsNullOrWhiteSpace(symbolName))
                return originalSymbolAddress;

            //MelonDebug.Log($"Looking for Symbol {symbolName}");
            if (!MonoHandler.SymbolRedirects.TryGetValue(symbolName, out var redirect)
                && !Il2CppHandler.SymbolRedirects.TryGetValue(symbolName, out redirect))
                return originalSymbolAddress;

            if (!_runtimeInitialised)
            {
                MelonDebug.Log("Init");
                redirect.InitMethod(handle);
                if (!LoaderConfig.Current.Loader.CapturePlayerLogs)
                    ConsoleHandler.ResetHandles();
            }
            _runtimeInitialised = true;

            MelonDebug.Log($"Redirecting {symbolName}");
            return redirect.detourPtr;
        }

#if WINDOWS
        [DllImport("kernel32")]
        private static extern nint GetProcAddress(nint handle, nint symbol);
#elif LINUX
        [DllImport("libdl.so.2")]
        private static extern IntPtr dlsym(nint handle, nint symbol);
#elif OSX
        [DllImport("libSystem.B.dylib")]
        private static extern IntPtr dlsym(nint handle, nint symbol);
#endif
    }
}
