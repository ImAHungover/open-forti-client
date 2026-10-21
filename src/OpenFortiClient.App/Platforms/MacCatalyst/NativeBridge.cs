using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;

namespace OpenFortiClient.App;

/// <summary>
/// Loads libOFCNative.dylib (src/OpenFortiClient.Native: menu bar icon, privileged helper client, macOS VPN services) and
/// creates its Objective-C objects. The library is AppKit code built for macOS, which the Catalyst app
/// cannot link against, so it is loaded at runtime and talked to through selectors.
/// </summary>
public static class NativeBridge
{
    private const string LibraryName = "libOFCNative.dylib";
    private static readonly Lazy<string?> LoadError = new(Load);

    /// <summary>A new instance of the named class, or null with <paramref name="error"/> saying why.</summary>
    public static NSObject? Create(string className, out string? error)
    {
        error = LoadError.Value;
        if (error is not null)
        {
            return null;
        }

        var cls = Class.GetHandle(className);
        if (cls == IntPtr.Zero)
        {
            error = $"{className} not found in {LibraryName}.";
            return null;
        }

        return Runtime.GetNSObject(Send(Send(cls, "alloc"), "init"));
    }

    /// <summary>
    /// Calls a method that returns nothing. Not <c>PerformSelector</c>: that reads a return value, and for a
    /// void method it gets whatever was left in the register, which crashed the app (2026-10-18).
    /// </summary>
    /// A selector that takes an argument ("terminate:") always gets one, nil if <paramref name="argument"/> is
    /// null: leaving it out lets Swift read garbage from the register, which also crashed the app.
    public static void Call(NSObject target, string selector, NSObject? argument = null)
    {
        if (selector.EndsWith(':'))
        {
            VoidObjcMsgSend(target.Handle, Selector.GetHandle(selector), argument?.Handle ?? IntPtr.Zero);
        }
        else
        {
            VoidObjcMsgSend(target.Handle, Selector.GetHandle(selector));
        }

        GC.KeepAlive(target);
        GC.KeepAlive(argument);
    }

    /// <summary>Calls a method that returns an NSString (or nil).</summary>
    public static string? CallForString(NSObject target, string selector, NSObject? argument = null)
    {
        var result = selector.EndsWith(':')
            ? IntPtrObjcMsgSend(target.Handle, Selector.GetHandle(selector), argument?.Handle ?? IntPtr.Zero)
            : IntPtrObjcMsgSend(target.Handle, Selector.GetHandle(selector));
        GC.KeepAlive(target);
        GC.KeepAlive(argument);
        return result == IntPtr.Zero ? null : Runtime.GetNSObject<NSString>(result)?.ToString();
    }

    /// <summary>Calls a method that returns an object (an NSArray or NSDictionary), or nil.</summary>
    public static T? CallForObject<T>(NSObject target, string selector, NSObject? argument = null)
        where T : NSObject
    {
        var result = selector.EndsWith(':')
            ? IntPtrObjcMsgSend(target.Handle, Selector.GetHandle(selector), argument?.Handle ?? IntPtr.Zero)
            : IntPtrObjcMsgSend(target.Handle, Selector.GetHandle(selector));
        GC.KeepAlive(target);
        GC.KeepAlive(argument);
        return result == IntPtr.Zero ? null : Runtime.GetNSObject<T>(result);
    }

    private static string? Load()
    {
        var path = Path.Combine(NSBundle.MainBundle.BundlePath, "Contents", "MonoBundle", LibraryName);
        return Dlfcn.dlopen(path, 0) == IntPtr.Zero ? $"Could not load {path}: {Dlfcn.dlerror()}" : null;
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtrObjcMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtrObjcMsgSend(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void VoidObjcMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void VoidObjcMsgSend(IntPtr receiver, IntPtr selector, IntPtr argument);

    private static IntPtr Send(IntPtr receiver, string selector) => IntPtrObjcMsgSend(receiver, Selector.GetHandle(selector));
}
