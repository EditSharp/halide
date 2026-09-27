using Halide.Scripts.UI.ContextMenu;
using Halide.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Halide.Scripts.UI.Dialogs.Platform.MacOS;

// macOS dialogs as sheets, shown by libeditsharp-dialogs.dylib in this process
public sealed class MacOSDialogHandler : DialogHandler
{
	// the library in the app bundle's Frameworks, beside the executable, or built in Tools/MacOS for a dev run
	public static string LibraryPath
	{
		get
		{
			string executable = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
			foreach (string path in new[]
			{
				Path.Combine(executable, "..", "Frameworks", "libeditsharp-dialogs.dylib"),
				Path.Combine(executable, "libeditsharp-dialogs.dylib"),
				ProjectSettings.GlobalizePath("res://Tools/MacOS/libeditsharp-dialogs.dylib"),
			})
				if (File.Exists(path)) return Path.GetFullPath(path);
			return null;
		}
	}

	public static bool Available => OS.GetName() == "macOS" && LibraryPath is not null;

	static readonly Dictionary<long, DialogSession> open = [];

	public override Task<string> ShowAsync(Dialog dialog, Window owner)
	{
		Library library = Library.Get();
		Window native = ContextMenus.NativeWindowOf(owner);
		nint ownerWindow = native is not null ? (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, native.GetWindowId()) : 0;
		bool dark = (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.Dark ?? false;

		long id = 0;
		DialogSession session = new(dialog, snapshot => library.Update(id, Json(snapshot)), problem => library.SetProblem(id, problem));

		id = library.Show(ownerWindow, dark ? 1 : 0, Json(DialogSnapshot.Of(dialog)));
		if (id == 0)
		{
			GD.PushError($"The dialog '{dialog.Title}' could not be shown.");
			session.Closed(null);
		}
		else open[id] = session;

		return session.Answer;
	}

	static string Json(DialogSnapshot snapshot) => JsonSerializer.Serialize(snapshot);

	// an event from the library; handled after the call that raised it, so the box never sees its own change mid-call
	static void OnEvent(nint json)
	{
		string text = Marshal.PtrToStringUTF8(json);
		Callable.From(() => Dispatch(text)).CallDeferred();
	}

	static void Dispatch(string text)
	{
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement e = document.RootElement;
		long id = e.GetProperty("dialog").GetInt64();
		if (!open.TryGetValue(id, out DialogSession session)) return;

		switch (e.GetProperty("event").GetString())
		{
			case "edited":
				JsonElement value = e.GetProperty("value");
				object plain = value.ValueKind switch
				{
					JsonValueKind.String => value.GetString(),
					JsonValueKind.Number => value.GetDouble(),
					JsonValueKind.True => true,
					JsonValueKind.False => false,
					_ => null,
				};
				session.Edited(e.GetProperty("index").GetInt32(), plain);
				break;

			case "list":
				session.ListPressed(e.GetProperty("index").GetInt32(), e.GetProperty("row").GetInt32(), e.GetProperty("button").GetInt32());
				break;

			case "browse":
				session.Browse(e.GetProperty("index").GetInt32());
				break;

			case "closed":
				open.Remove(id);
				JsonElement button = e.GetProperty("button");
				session.Closed(button.ValueKind == JsonValueKind.String ? button.GetString() : null);
				break;
		}
	}

	// the library's exports, loaded once
	internal sealed class Library
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void EventCallback(nint json);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetCallbackFn(nint callback);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate long ShowFn(nint owner, int dark, [MarshalAs(UnmanagedType.LPUTF8Str)] string json);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void UpdateFn(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string json);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TitleFn([MarshalAs(UnmanagedType.LPUTF8Str)] string title);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TitleTextFn([MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TypeFn([MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int KeyFn([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int key);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FrameFn([MarshalAs(UnmanagedType.LPUTF8Str)] string title, [Out] double[] rect);

		static Library instance;
		static readonly EventCallback events = OnEvent;

		public readonly Func<nint, int, string, long> Show;
		public readonly Action<long, string> Update;
		public readonly Action<long, string> SetProblem;

		// the probes' hands: a dialog worked by its title
		public readonly Func<string, int> TestShowing;
		public readonly Func<string, string, int> TestPress, TestEnabled, TestTextShown;
		public readonly Func<string, string, string, int> TestType;
		public readonly Func<string, int, int> TestKey;
		public readonly Func<string, double[], int> TestFrame;

		Library(nint handle)
		{
			T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

			Export<SetCallbackFn>("esd_set_callback")(Marshal.GetFunctionPointerForDelegate(events));
			Show = new Func<nint, int, string, long>(Export<ShowFn>("esd_show"));
			Update = new Action<long, string>(Export<UpdateFn>("esd_update"));
			SetProblem = new Action<long, string>(Export<UpdateFn>("esd_set_problem"));
			TestShowing = new Func<string, int>(Export<TitleFn>("esd_test_showing"));
			TestPress = new Func<string, string, int>(Export<TitleTextFn>("esd_test_press"));
			TestEnabled = new Func<string, string, int>(Export<TitleTextFn>("esd_test_enabled"));
			TestTextShown = new Func<string, string, int>(Export<TitleTextFn>("esd_test_text_shown"));
			TestType = new Func<string, string, string, int>(Export<TypeFn>("esd_test_type"));
			TestKey = new Func<string, int, int>(Export<KeyFn>("esd_test_key"));
			TestFrame = new Func<string, double[], int>(Export<FrameFn>("esd_test_frame"));
		}

		public static Library Get() => instance ??= new Library(NativeLibrary.Load(LibraryPath));
	}
}
