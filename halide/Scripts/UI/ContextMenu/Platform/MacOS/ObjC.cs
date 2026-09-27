using System;
using System.Runtime.InteropServices;

namespace Halide.Scripts.UI.ContextMenu.Platform.MacOS;

// the objective-c runtime surface the macos handler needs: classes, selectors and objc_msgSend in the
// few shapes appkit's menu api takes. every Send overload is the same symbol with a different signature
static class ObjC
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    public struct NSPoint
    {
        public double X, Y;
        public NSPoint(double x, double y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NSSize
    {
        public double Width, Height;
        public NSSize(double width, double height) { Width = width; Height = height; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ActionImp(nint self, nint sel, nint sender);

    [DllImport(Lib)] public static extern nint objc_getClass(string name);
    [DllImport(Lib)] public static extern nint sel_registerName(string name);
    [DllImport(Lib)] public static extern nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);
    [DllImport(Lib)] public static extern void objc_registerClassPair(nint cls);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool class_addMethod(nint cls, nint sel, nint imp, string types);

    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a, nint b, nint c);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, nint a, nuint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, long a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, [MarshalAs(UnmanagedType.I1)] bool a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, double a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint Send(nint receiver, nint sel, NSSize a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern long SendLong(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern double SendDouble(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern NSPoint SendPoint(nint receiver, nint sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SendBool(nint receiver, nint sel, nint a, NSPoint at, nint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoid(nint receiver, nint sel, nint a, nint b, [MarshalAs(UnmanagedType.I1)] bool c);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void SendVoid(nint receiver, nint sel, nint a, [MarshalAs(UnmanagedType.I1)] bool b);
    // keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:
    [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern nint SendKeyEvent(nint receiver, nint sel, nuint type, NSPoint location, nuint flags, double timestamp, long window, nint context, nint characters, nint charactersIgnoringModifiers, [MarshalAs(UnmanagedType.I1)] bool repeat, ushort keyCode);

    public static nint Class(string name) => objc_getClass(name);
    public static nint Sel(string name) => sel_registerName(name);

    // an autoreleased NSString
    public static nint NSString(string text)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(text ?? "");
        try { return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    // an NSData holding a copy of the bytes; release it
    public static nint NSData(byte[] bytes)
    {
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return Send(Send(Class("NSData"), Sel("alloc")), Sel("initWithBytes:length:"), buffer, (nuint)bytes.Length);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static void Release(nint obj)
    {
        if (obj != 0) Send(obj, Sel("release"));
    }
}
