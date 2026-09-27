using EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;
using static EditSharpGUI.Scripts.UI.Dialogs.Platform.Windows.DialogWin32;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform.Windows;

// one dialog box on the menu thread, built from a snapshot of the resource out of
// win32's own controls, dark or light. what the user does comes back on godot's
// thread through the events, by element index; it never touches the resources
sealed class NativeDialog(nint owner, bool dark, nint iconWindow)
{
	// godot's thread, all of them
	public event Action<int, object> Edited;          // element, its new value: string, int, double or bool
	public event Action<int, int, int> ListPressed;   // list element, row or -1, list button or -1
	public event Action<int> BrowseRequested;         // path element
	public event Action<string> Closed;               // the button's id; null when dismissed

	// layout, in 96 dpi pixels
	const int Width = 480, Margin = 18, Gap = 12, RowHeight = 24, ButtonHeight = 27, Inner = 6, LabelGap = 12;
	const int ListRow = 36, ListStep = 42, MaxRows = 6, FooterPad = 12, MinButton = 90;

	public void Open(DialogSnapshot snapshot) => MenuThread.Post(() => Create(snapshot));

	// the dialog changed in code: the same shape updates in place, a new one is built again
	public void Refresh(DialogSnapshot snapshot) => MenuThread.Post(() =>
	{
		if (closed) return;
		if (KeyOf(snapshot) == layoutKey) Apply(snapshot);
		else Rebuild(snapshot);
	});

	// what Validate says now, after the user changed something
	public void SetProblem(string problem) => MenuThread.Post(() =>
	{
		if (closed) return;
		shown = shown with { Problem = problem };
		ApplyProblem();
		Layout();
	});

	// ---- menu thread from here on ----

	nint window;
	uint dpi = 96;
	DialogSnapshot shown;
	string layoutKey;
	bool applying, closed;

	nint font, headingFont, contentBrush, footerBrush, fieldBrush;
	int footerTop;

	readonly List<Part> parts = [];
	readonly List<nint> footer = [];
	readonly Dictionary<int, Action<int>> commands = [];
	readonly Dictionary<nint, uint> colors = [];
	nint problem;
	int nextId;

	sealed class Part
	{
		public ElementSnapshot Element;
		public nint Label, Control, Extra, Suffix, Panel;
		public readonly List<(nint Text, nint Detail, nint Button)> Rows = [];
		public readonly List<nint> Buttons = [];
		public double Number;
		public int Scroll, PanelHeight, ContentHeight, RowsWidth;
	}

	static readonly Dictionary<nint, NativeDialog> byWindow = [];
	static readonly List<NativeDialog> open = [];
	static NativeDialog creating;
	static readonly DialogProc dialogProc = Proc;
	static readonly WindowProc panelProc = PanelProc;
	static bool panelClass;

	static NativeDialog() => MenuThread.PreTranslate = Translate;

	uint Text => dark ? Rgb(255, 255, 255) : Rgb(26, 26, 26);
	uint Dim => dark ? Rgb(166, 166, 166) : Rgb(96, 96, 96);
	uint Error => dark ? Rgb(255, 153, 164) : Rgb(196, 43, 28);
	uint Content => dark ? Rgb(32, 32, 32) : Rgb(255, 255, 255);
	uint FooterColor => dark ? Rgb(43, 43, 43) : Rgb(240, 240, 240);
	uint Field => dark ? Rgb(45, 45, 45) : Rgb(255, 255, 255);

	int S(int pixels) => pixels * (int)dpi / 96;

	void Create(DialogSnapshot snapshot)
	{
		if (owner != 0 && GetDpiForWindow(owner) is uint ownerDpi and > 0) dpi = ownerDpi;

		nuint styles = EnterVisualStyles();
		try
		{
			nint template = Template(snapshot.Title);
			creating = this;
			window = CreateDialogIndirectParamW(GetModuleHandleW(null), template, owner, dialogProc, 0);
			creating = null;
			Marshal.FreeHGlobal(template);

			if (window == 0)
			{
				GD.PushError($"The dialog '{snapshot.Title}' could not be created: {Marshal.GetLastWin32Error()}");
				closed = true;
				Callable.From(() => Closed?.Invoke(null)).CallDeferred();
				return;
			}

			byWindow[window] = this;
			SetDialogDpiChangeBehavior(window, DDC_DISABLE_ALL, DDC_DISABLE_ALL);
			uint darkTitle = dark ? 1u : 0u;
			DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkTitle, 4);
			// the app's icon in the title bar, which also gives the title its usual spacing
			nint icon = SendMessageW(iconWindow, WM_GETICON, ICON_SMALL2, 0);
			if (icon == 0) icon = (nint)GetClassLongPtrW(iconWindow, GCLP_HICONSM);
			if (icon != 0) SendMessageW(window, WM_SETICON, ICON_SMALL, icon);

			uint round = DWMWCP_ROUND;
			DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);

			contentBrush = CreateSolidBrush(Content);
			footerBrush = CreateSolidBrush(FooterColor);
			fieldBrush = CreateSolidBrush(Field);
			MakeFonts();
			Build(snapshot);
		}
		finally
		{
			LeaveVisualStyles(styles);
		}

		Place();
		if (owner != 0) EnableWindow(owner, false);
		open.Add(this);
		ShowWindow(window, SW_SHOW);
		SetForegroundWindow(window);
		FocusFirst();
	}

	// a dialog frame with no controls; they're made after, in pixels
	static nint Template(string title)
	{
		byte[] name = Encoding.Unicode.GetBytes((title ?? "") + "\0");
		byte[] data = new byte[22 + name.Length];
		BitConverter.GetBytes(WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | WS_CLIPCHILDREN).CopyTo(data, 0);
		BitConverter.GetBytes(WS_EX_CONTROLPARENT).CopyTo(data, 4);
		name.CopyTo(data, 22);

		nint memory = Marshal.AllocHGlobal(data.Length);
		Marshal.Copy(data, 0, memory, data.Length);
		return memory;
	}

	void MakeFonts()
	{
		if (font != 0) DeleteObject(font);
		if (headingFont != 0) DeleteObject(headingFont);

		NONCLIENTMETRICSW metrics = new() { cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>() };
		SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0, dpi);
		font = CreateFontIndirectW(ref metrics.lfMessageFont);

		LOGFONTW heading = metrics.lfMessageFont;
		heading.lfHeight = heading.lfHeight * 4 / 3;
		headingFont = CreateFontIndirectW(ref heading);
	}

	// ---- building ----

	// what decides the controls; anything else changes in place
	static string KeyOf(DialogSnapshot s) =>
		string.Join("|", s.Elements.Select(e => $"{e.Kind};{e.Label.Length > 0};{string.Join(",", e.Options)};{e.Suffix.Length > 0};{string.Join(",", e.Rows.Select(r => r.ButtonText.Length > 0))};{e.ListButtons.Count}"))
		+ "#" + string.Join(",", s.Buttons.Select(b => $"{b.Role}"));

	static bool Labelled(ElementSnapshot e) => e.Label.Length > 0 && e.Kind is "field" or "path" or "dropdown" or "number";

	void Build(DialogSnapshot snapshot)
	{
		shown = snapshot;
		layoutKey = KeyOf(snapshot);
		applying = true;
		commands.Clear();
		colors.Clear();
		parts.Clear();
		footer.Clear();
		nextId = 100;

		foreach (ElementSnapshot element in snapshot.Elements) parts.Add(BuildPart(element));

		problem = Child("STATIC", "", SS_LEFT | SS_NOPREFIX | SS_EDITCONTROL);
		colors[problem] = Error;

		int defaultId = 0;
		foreach (ButtonSnapshot b in snapshot.Buttons)
		{
			string id = b.Id;
			bool isDefault = b.Role == DialogButtonRole.Default;
			nint button = Child("BUTTON", b.Text, (isDefault ? BS_DEFPUSHBUTTON : BS_PUSHBUTTON) | WS_TABSTOP, code =>
			{
				if (code == BN_CLICKED && (!isDefault || shown.Problem is null)) Close(id);
			});
			if (isDefault && defaultId == 0) defaultId = nextId - 1;
			footer.Add(button);
		}
		if (defaultId != 0) SendMessageW(window, DM_SETDEFID, defaultId, 0);

		applying = false;
		Apply(snapshot);
	}

	Part BuildPart(ElementSnapshot e)
	{
		Part part = new() { Element = e, Number = e.Number };
		int index = e.Index;

		if (Labelled(e)) part.Label = Child("STATIC", e.Label, SS_LEFT | SS_NOPREFIX | SS_CENTERIMAGE);

		switch (e.Kind)
		{
			case "text":
				part.Control = Child("STATIC", e.Text, SS_LEFT | SS_NOPREFIX | SS_EDITCONTROL);
				break;

			case "field":
				part.Control = Child("EDIT", e.Value, ES_AUTOHSCROLL | WS_TABSTOP, code => { if (code == EN_CHANGE) Raise(index, TextOf(part.Control)); }, WS_EX_CLIENTEDGE);
				break;

			case "path":
				part.Control = Child("EDIT", e.Value, ES_AUTOHSCROLL | WS_TABSTOP, code => { if (code == EN_CHANGE) Raise(index, TextOf(part.Control)); }, WS_EX_CLIENTEDGE);
				part.Extra = Child("BUTTON", "Browse...", BS_PUSHBUTTON | WS_TABSTOP, code => { if (code == BN_CLICKED) Raise(() => BrowseRequested?.Invoke(index)); });
				break;

			case "dropdown":
				part.Control = Child("COMBOBOX", "", CBS_DROPDOWNLIST | CBS_HASSTRINGS | WS_TABSTOP | WS_VSCROLL, code =>
				{
					if (code == CBN_SELCHANGE) Raise(index, (int)SendMessageW(part.Control, CB_GETCURSEL, 0, 0));
				});
				foreach (string option in e.Options) SendMessageW(part.Control, CB_ADDSTRING, 0, option);
				SendMessageW(part.Control, CB_SETMINVISIBLE, 10, 0);
				break;

			case "number":
				part.Control = Child("EDIT", Format(e.Number), ES_AUTOHSCROLL | WS_TABSTOP, code => NumberEdited(part, code), WS_EX_CLIENTEDGE);
				part.Extra = Child("msctls_updown32", "", UDS_ARROWKEYS | UDS_HOTTRACK);
				SendMessageW(part.Extra, UDM_SETRANGE32, 0, 100);
				SendMessageW(part.Extra, UDM_SETPOS32, 0, 50);
				if (e.Suffix.Length > 0) part.Suffix = Child("STATIC", e.Suffix, SS_LEFT | SS_NOPREFIX | SS_CENTERIMAGE);
				break;

			case "checkbox":
				// the words are a label of their own: a themed checkbox won't take a dark text colour
				part.Control = Child("BUTTON", "", BS_AUTOCHECKBOX | WS_TABSTOP, code => { if (code == BN_CLICKED) Raise(index, Checked(part.Control)); });
				part.Extra = Child("STATIC", e.Text, SS_LEFT | SS_NOPREFIX | SS_NOTIFY | SS_CENTERIMAGE, code =>
				{
					if (code != STN_CLICKED || !IsWindowEnabled(part.Control)) return;
					SendMessageW(part.Control, BM_SETCHECK, Checked(part.Control) ? 0 : 1, 0);
					Raise(index, Checked(part.Control));
				});
				break;

			case "list":
				BuildList(part);
				break;
		}

		return part;
	}

	void BuildList(Part part)
	{
		ElementSnapshot e = part.Element;
		int index = e.Index;

		if (e.Label.Length > 0) part.Label = Child("STATIC", e.Label, SS_LEFT | SS_NOPREFIX);

		nint parent = window;
		if (e.Rows.Count > MaxRows)
		{
			part.Panel = Panel();
			parent = part.Panel;
		}

		foreach (RowSnapshot row in e.Rows)
		{
			int r = row.Index;
			nint text = Child("STATIC", row.Text, SS_LEFT | SS_NOPREFIX | SS_ENDELLIPSIS, parent: parent);
			nint detail = Child("STATIC", row.Detail, SS_LEFT | SS_NOPREFIX | SS_PATHELLIPSIS, parent: parent);
			colors[detail] = Dim;
			nint button = row.ButtonText.Length > 0
				? Child("BUTTON", row.ButtonText, BS_PUSHBUTTON | WS_TABSTOP, code => { if (code == BN_CLICKED) Raise(() => ListPressed?.Invoke(index, r, -1)); }, parent: parent)
				: 0;
			part.Rows.Add((text, detail, button));
		}

		foreach (ButtonSnapshot b in e.ListButtons)
		{
			int bi = b.Index;
			part.Buttons.Add(Child("BUTTON", b.Text, BS_PUSHBUTTON | WS_TABSTOP, code => { if (code == BN_CLICKED) Raise(() => ListPressed?.Invoke(index, -1, bi)); }));
		}
	}

	// a control, in the dialog or a list's panel, with its command handler

	nint Child(string cls, string text, uint style, Action<int> command = null, uint exStyle = 0, nint parent = 0)
	{
		int id = nextId++;
		commands[id] = command ?? (_ => { });

		nint control = CreateWindowExW(exStyle, cls, text, WS_CHILD | WS_VISIBLE | style, 0, 0, 0, 0, parent == 0 ? window : parent, id, GetModuleHandleW(null), 0);
		SendMessageW(control, WM_SETFONT, font, 1);
		SetDialogControlDpiChangeBehavior(control, DCDC_DISABLE_FONTUPDATE | DCDC_DISABLE_RELAYOUT, DCDC_DISABLE_FONTUPDATE | DCDC_DISABLE_RELAYOUT);
		Theme(control, cls);
		return control;
	}

	void Theme(nint control, string cls)
	{
		if (!dark) return;
		try { AllowDarkModeForWindow(control, true); }
		catch (EntryPointNotFoundException) { }
		SetWindowTheme(control, cls is "EDIT" or "COMBOBOX" ? "DarkMode_CFD" : "DarkMode_Explorer", null);
	}

	// a scrolling box for a list longer than MaxRows
	nint Panel()
	{
		if (!panelClass)
		{
			WNDCLASSEXW cls = new()
			{
				cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
				lpfnWndProc = Marshal.GetFunctionPointerForDelegate(panelProc),
				hInstance = GetModuleHandleW(null),
				hCursor = LoadCursorW(0, IDC_ARROW),
				lpszClassName = "EditSharpDialogPanel",
			};
			RegisterClassExW(ref cls);
			panelClass = true;
		}

		nint panel = CreateWindowExW(WS_EX_CONTROLPARENT, "EditSharpDialogPanel", "", WS_CHILD | WS_VISIBLE | WS_VSCROLL | WS_CLIPCHILDREN, 0, 0, 0, 0, window, 0, GetModuleHandleW(null), 0);
		Theme(panel, "");
		return panel;
	}

	void Rebuild(DialogSnapshot snapshot)
	{
		for (nint child = GetWindow(window, GW_CHILD); child != 0;)
		{
			nint next = GetWindow(child, GW_HWNDNEXT);
			DestroyWindow(child);
			child = next;
		}

		nuint styles = EnterVisualStyles();
		try { Build(snapshot); }
		finally { LeaveVisualStyles(styles); }
		FocusFirst();
	}

	// ---- values ----

	// every value and state onto the controls, then laid out again
	void Apply(DialogSnapshot snapshot)
	{
		shown = snapshot;
		applying = true;

		SetText(window, snapshot.Title);

		for (int i = 0; i < parts.Count && i < snapshot.Elements.Count; i++)
		{
			Part part = parts[i];
			ElementSnapshot e = part.Element = snapshot.Elements[i];

			foreach (nint control in Controls(part))
			{
				ShowWindow(control, e.Visible ? SW_SHOWNA : SW_HIDE);
				EnableWindow(control, e.Enabled);
			}
			if (part.Label != 0) SetText(part.Label, e.Label);

			switch (e.Kind)
			{
				case "text":
					SetText(part.Control, e.Text);
					SendMessageW(part.Control, WM_SETFONT, e.TextStyle == (int)DialogText.TextStyle.Heading ? headingFont : font, 1);
					colors[part.Control] = e.TextStyle == (int)DialogText.TextStyle.Error ? Error : Text;
					break;

				case "field" or "path":
					SetEdit(part.Control, e.Value);
					SendMessageW(part.Control, EM_SETCUEBANNER, 1, e.Placeholder);
					break;

				case "dropdown":
					SendMessageW(part.Control, CB_SETCURSEL, e.Selected, 0);
					break;

				case "number":
					part.Number = e.Number;
					SetEdit(part.Control, Format(e.Number));
					if (part.Suffix != 0) SetText(part.Suffix, e.Suffix);
					break;

				case "checkbox":
					SendMessageW(part.Control, BM_SETCHECK, e.Checked ? 1 : 0, 0);
					SetText(part.Extra, e.Text);
					break;

				case "list":
					for (int r = 0; r < part.Rows.Count && r < e.Rows.Count; r++)
					{
						(nint text, nint detail, nint button) = part.Rows[r];
						SetText(text, e.Rows[r].Text);
						SetText(detail, e.Rows[r].Detail);
						if (button != 0)
						{
							SetText(button, e.Rows[r].ButtonText);
							EnableWindow(button, e.Enabled && e.Rows[r].ButtonEnabled);
						}
					}
					for (int b = 0; b < part.Buttons.Count && b < e.ListButtons.Count; b++) SetText(part.Buttons[b], e.ListButtons[b].Text);
					break;
			}
		}

		for (int b = 0; b < footer.Count && b < snapshot.Buttons.Count; b++) SetText(footer[b], snapshot.Buttons[b].Text);

		ApplyProblem();
		applying = false;
		Layout();
		InvalidateRect(window, 0, true);
	}

	void ApplyProblem()
	{
		SetText(problem, shown.Problem ?? "");
		ShowWindow(problem, shown.Problem is null ? SW_HIDE : SW_SHOWNA);

		for (int b = 0; b < footer.Count && b < shown.Buttons.Count; b++)
			if (shown.Buttons[b].Role == DialogButtonRole.Default) EnableWindow(footer[b], shown.Problem is null);
	}

	static IEnumerable<nint> Controls(Part part)
	{
		foreach (nint control in new[] { part.Label, part.Control, part.Extra, part.Suffix, part.Panel }) if (control != 0) yield return control;
		foreach ((nint text, nint detail, nint button) in part.Rows)
		{
			yield return text;
			yield return detail;
			if (button != 0) yield return button;
		}
		foreach (nint button in part.Buttons) yield return button;
	}

	static void SetText(nint control, string text)
	{
		if (TextOf(control) != text) SetWindowTextW(control, text);
	}

	// an edit box the user is typing in keeps what they typed
	static void SetEdit(nint edit, string text)
	{
		if (GetFocus() != edit) SetText(edit, text);
	}

	static bool Checked(nint box) => SendMessageW(box, BM_GETCHECK, 0, 0) == 1;

	static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

	void NumberEdited(Part part, int code)
	{
		ElementSnapshot e = part.Element;

		if (code == EN_CHANGE && double.TryParse(TextOf(part.Control), NumberStyles.Float, CultureInfo.CurrentCulture, out double typed))
		{
			part.Number = Math.Clamp(typed, e.Min, e.Max);
			Raise(e.Index, part.Number);
		}
		else if (code == EN_KILLFOCUS)
		{
			applying = true;
			SetText(part.Control, Format(part.Number));
			applying = false;
		}
	}

	void Step(Part part, int delta)
	{
		ElementSnapshot e = part.Element;
		part.Number = Math.Clamp(part.Number + delta * e.Step, e.Min, e.Max);

		applying = true;
		SetText(part.Control, Format(part.Number));
		applying = false;
		Raise(e.Index, part.Number);
	}

	// ---- layout ----

	void Layout()
	{
		int width = S(Width), margin = S(Margin), inner = width - 2 * margin, row = S(RowHeight), button = S(ButtonHeight);
		int labels = parts.Where(p => p.Label != 0 && Labelled(p.Element) && p.Element.Visible).Select(p => Measure(p.Element.Label, font).cx + S(LabelGap)).DefaultIfEmpty(0).Max();

		int y = margin;
		bool first = true;

		foreach (Part part in parts)
		{
			ElementSnapshot e = part.Element;
			if (!e.Visible) continue;
			if (!first) y += S(Gap);
			first = false;

			int x = margin + (part.Label != 0 && Labelled(e) ? labels : 0);
			int w = inner - (x - margin);
			if (part.Label != 0 && Labelled(e)) Move(part.Label, margin, y, labels, row);

			switch (e.Kind)
			{
				case "text":
					int height = Wrapped(e.Text, inner, e.TextStyle == (int)DialogText.TextStyle.Heading ? headingFont : font);
					Move(part.Control, margin, y, inner, height);
					y += height;
					break;

				case "field":
					Move(part.Control, x, y, w, row);
					y += row;
					break;

				case "path":
					int browse = ButtonWidth("Browse...");
					Move(part.Control, x, y, w - browse - S(Inner), row);
					Move(part.Extra, x + w - browse, y, browse, row);
					y += row;
					break;

				case "dropdown":
					SendMessageW(part.Control, CB_SETITEMHEIGHT, -1, row - S(6));
					Move(part.Control, x, y, w, row + S(RowHeight) * Math.Min(10, Math.Max(1, e.Options.Count)));
					y += row;
					break;

				case "number":
					Move(part.Control, x, y, S(84), row);
					Move(part.Extra, x + S(84), y, S(18), row);
					if (part.Suffix != 0) Move(part.Suffix, x + S(84 + 18 + Inner), y, w - S(84 + 18 + Inner), row);
					y += row;
					break;

				case "checkbox":
					int box = S(18);
					int left = margin + labels;
					Move(part.Control, left, y + (row - box) / 2, box, box);
					Move(part.Extra, left + box + S(Inner), y, inner - labels - box - S(Inner), row);
					y += row;
					break;

				case "list":
					y = LayoutList(part, y, margin, inner);
					break;
			}
		}

		if (shown.Problem is not null)
		{
			y += S(Gap);
			int height = Wrapped(shown.Problem, inner, font);
			Move(problem, margin, y, inner, height);
			y += height;
		}

		y += margin;
		footerTop = y;

		int right = width - margin;
		for (int b = footer.Count - 1; b >= 0; b--)
		{
			int w = ButtonWidth(shown.Buttons[b].Text);
			right -= w;
			Move(footer[b], right, y + S(FooterPad), w, button);
			right -= S(Inner);
		}

		int bottom = y + (footer.Count > 0 ? S(FooterPad) * 2 + button : 0);

		RECT outer = new(0, 0, width, bottom);
		AdjustWindowRectExForDpi(ref outer, WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME, false, WS_EX_CONTROLPARENT, dpi);
		SetWindowPos(window, 0, 0, 0, outer.Width, outer.Height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
		InvalidateRect(window, 0, true);
	}

	int LayoutList(Part part, int y, int margin, int inner)
	{
		ElementSnapshot e = part.Element;
		int line = S(18);

		if (part.Label != 0)
		{
			Move(part.Label, margin, y, inner, line);
			y += line + S(Inner);
		}

		if (part.Panel != 0)
		{
			part.PanelHeight = S(ListStep) * MaxRows - S(Inner);
			part.ContentHeight = S(ListStep) * part.Rows.Count - S(Inner);
			part.RowsWidth = inner - GetSystemMetricsForDpi(2, dpi);
			part.Scroll = Math.Clamp(part.Scroll, 0, part.ContentHeight - part.PanelHeight);
			Move(part.Panel, margin, y, inner, part.PanelHeight);
			LayoutRows(part, 0, part.RowsWidth, -part.Scroll);

			SCROLLINFO info = new() { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_RANGE | SIF_PAGE | SIF_POS, nMax = part.ContentHeight - 1, nPage = (uint)part.PanelHeight, nPos = part.Scroll };
			SetScrollInfo(part.Panel, SB_VERT, ref info, true);
			y += part.PanelHeight;
		}
		else if (part.Rows.Count > 0)
		{
			LayoutRows(part, margin, inner, y);
			y += S(ListStep) * part.Rows.Count - S(Inner);
		}

		if (part.Buttons.Count > 0)
		{
			if (part.Rows.Count > 0 || part.Label != 0) y += S(9);
			int x = margin;
			for (int b = 0; b < part.Buttons.Count; b++)
			{
				int w = ButtonWidth(e.ListButtons[b].Text);
				Move(part.Buttons[b], x, y, w, S(ButtonHeight));
				x += w + S(Inner);
			}
			y += S(ButtonHeight);
		}

		return y;
	}

	void LayoutRows(Part part, int x, int width, int top)
	{
		int height = S(ListRow), line = S(18), button = S(ButtonHeight);

		for (int r = 0; r < part.Rows.Count; r++)
		{
			(nint text, nint detail, nint press) = part.Rows[r];
			int y = top + r * S(ListStep);
			int w = press != 0 ? ButtonWidth(part.Element.Rows[r].ButtonText) : 0;
			int words = width - (w > 0 ? w + S(9) : 0);

			Move(text, x, y, words, line);
			Move(detail, x, y + line, words, line);
			if (press != 0) Move(press, x + width - w, y + (height - button) / 2, w, button);
		}
	}

	void ScrollPanel(nint panel, int request, int wheel)
	{
		if (parts.FirstOrDefault(p => p.Panel == panel) is not Part part) return;

		int step = S(ListStep), max = Math.Max(0, part.ContentHeight - part.PanelHeight);
		int to = part.Scroll;

		if (wheel != 0) to -= wheel * step / 120;
		else
		{
			switch (request)
			{
				case SB_LINEUP: to -= step; break;
				case SB_LINEDOWN: to += step; break;
				case SB_PAGEUP: to -= part.PanelHeight; break;
				case SB_PAGEDOWN: to += part.PanelHeight; break;
				case SB_TOP: to = 0; break;
				case SB_BOTTOM: to = max; break;
				case SB_THUMBTRACK:
					SCROLLINFO track = new() { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_TRACKPOS };
					GetScrollInfo(panel, SB_VERT, ref track);
					to = track.nTrackPos;
					break;
			}
		}

		to = Math.Clamp(to, 0, max);
		if (to == part.Scroll) return;

		part.Scroll = to;
		LayoutRows(part, 0, part.RowsWidth, -to);
		SCROLLINFO info = new() { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_POS, nPos = to };
		SetScrollInfo(panel, SB_VERT, ref info, true);
		InvalidateRect(panel, 0, true);
	}

	static void Move(nint control, int x, int y, int width, int height) => MoveWindow(control, x, y, Math.Max(0, width), Math.Max(0, height), true);

	int ButtonWidth(string text) => Math.Max(S(MinButton), Measure(text, font).cx + S(24));

	SIZE Measure(string text, nint withFont)
	{
		nint dc = GetDC(window);
		nint was = SelectObject(dc, withFont);
		GetTextExtentPoint32W(dc, text, text.Length, out SIZE size);
		SelectObject(dc, was);
		ReleaseDC(window, dc);
		return size;
	}

	int Wrapped(string text, int width, nint withFont)
	{
		if (string.IsNullOrEmpty(text)) return 0;

		nint dc = GetDC(window);
		nint was = SelectObject(dc, withFont);
		RECT rect = new(0, 0, width, 0);
		DrawTextW(dc, text, text.Length, ref rect, DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX | DT_EDITCONTROL);
		SelectObject(dc, was);
		ReleaseDC(window, dc);
		return rect.Height;
	}

	// over the middle of the owner, kept on its screen
	void Place()
	{
		GetWindowRect(window, out RECT me);
		nint reference = owner != 0 ? owner : window;
		GetWindowRect(reference, out RECT over);

		MONITORINFO monitor = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
		GetMonitorInfoW(MonitorFromWindow(reference, MONITOR_DEFAULTTONEAREST), ref monitor);
		if (owner == 0) over = monitor.rcWork;

		int x = over.left + (over.Width - me.Width) / 2;
		int y = over.top + (over.Height - me.Height) / 2;
		x = Math.Clamp(x, monitor.rcWork.left, Math.Max(monitor.rcWork.left, monitor.rcWork.right - me.Width));
		y = Math.Clamp(y, monitor.rcWork.top, Math.Max(monitor.rcWork.top, monitor.rcWork.bottom - me.Height));
		SetWindowPos(window, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
	}

	// the first thing to type into, or the Enter button
	void FocusFirst()
	{
		nint target = parts.Where(p => p.Element.Visible && p.Element.Enabled && p.Element.Kind is "field" or "path" or "number" or "dropdown").Select(p => p.Control).FirstOrDefault();
		if (target == 0)
		{
			int d = shown.Buttons.ToList().FindIndex(b => b.Role == DialogButtonRole.Default);
			if (d >= 0 && d < footer.Count) target = footer[d];
		}
		if (target != 0) SendMessageW(window, WM_NEXTDLGCTL, target, 1);
	}

	// ---- messages ----

	static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		NativeDialog dialog = byWindow.GetValueOrDefault(hwnd) ?? creating;
		if (dialog is null) return 0;

		try
		{
			return dialog.Handle(hwnd, msg, wParam, lParam);
		}
		catch (Exception e)
		{
			GD.PushError($"Dialog message {msg:X} failed: {e}");
			return 0;
		}
	}

	nint Handle(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		switch (msg)
		{
			case WM_INITDIALOG:
				return 1;

			case WM_ERASEBKGND:
				GetClientRect(hwnd, out RECT all);
				RECT top = all with { bottom = footerTop };
				RECT bottom = all with { top = footerTop };
				FillRect(wParam, ref top, contentBrush);
				FillRect(wParam, ref bottom, footerBrush);
				return Result(hwnd, 1);

			case WM_CTLCOLORDLG:
				return contentBrush;

			case WM_CTLCOLORSTATIC:
				SetTextColor(wParam, colors.GetValueOrDefault(lParam, Text));
				SetBkColor(wParam, Content);
				return contentBrush;

			case WM_CTLCOLOREDIT or WM_CTLCOLORLISTBOX:
				SetTextColor(wParam, Text);
				SetBkColor(wParam, Field);
				return fieldBrush;

			case WM_CTLCOLORBTN:
				return footer.Contains(lParam) ? footerBrush : contentBrush;

			case WM_COMMAND:
				Command((int)((long)wParam & 0xFFFF), (int)(((long)wParam >> 16) & 0xFFFF));
				return 1;

			case WM_NOTIFY:
				NMHDR header = Marshal.PtrToStructure<NMHDR>(lParam);
				if (header.code == UDN_DELTAPOS && parts.FirstOrDefault(p => p.Extra == header.hwndFrom) is Part number)
				{
					Step(number, Marshal.PtrToStructure<NMUPDOWN>(lParam).iDelta);
					return Result(hwnd, 1);
				}
				return 0;

			case WM_CLOSE:
				Close(null);
				return 1;

			case WM_DPICHANGED:
				dpi = (uint)(((long)wParam >> 16) & 0xFFFF);
				RECT suggested = Marshal.PtrToStructure<RECT>(lParam);
				MakeFonts();
				Rebuild(shown);
				SetWindowPos(window, 0, suggested.left, suggested.top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
				return Result(hwnd, 0);
		}

		return 0;
	}

	// a dialog procedure's answer for messages whose result isn't its return value
	static nint Result(nint hwnd, nint value)
	{
		SetWindowLongPtrW(hwnd, DWLP_MSGRESULT, value);
		return 1;
	}

	void Command(int id, int code)
	{
		if (id == IDOK) { Enter(); return; }
		if (id == IDCANCEL) { Close(null); return; }
		if (!applying && commands.TryGetValue(id, out Action<int> command)) command(code);
	}

	void Enter()
	{
		if (shown.Problem is not null) return;
		if (shown.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Default) is { } enter) Close(enter.Id);
	}

	static nint PanelProc(nint panel, uint msg, nint wParam, nint lParam)
	{
		NativeDialog dialog = byWindow.GetValueOrDefault(GetParent(panel));

		switch (msg)
		{
			case WM_COMMAND or WM_NOTIFY or WM_CTLCOLORSTATIC or WM_CTLCOLORBTN or WM_CTLCOLOREDIT:
				return SendMessageW(GetParent(panel), msg, wParam, lParam);

			case WM_ERASEBKGND when dialog is not null:
				GetClientRect(panel, out RECT all);
				FillRect(wParam, ref all, dialog.contentBrush);
				return 1;

			case WM_VSCROLL:
				dialog?.ScrollPanel(panel, (int)((long)wParam & 0xFFFF), 0);
				return 0;

			case WM_MOUSEWHEEL:
				dialog?.ScrollPanel(panel, 0, (short)(((long)wParam >> 16) & 0xFFFF));
				return 0;
		}

		return DefWindowProcW(panel, msg, wParam, lParam);
	}

	// tab, enter and escape for every open dialog
	static bool Translate(MenuThread.MSG msg)
	{
		foreach (NativeDialog dialog in open)
			if (IsDialogMessageW(dialog.window, ref msg)) return true;
		return false;
	}

	// ---- to godot's thread ----

	void Raise(int index, object value)
	{
		if (!applying) Callable.From(() => Edited?.Invoke(index, value)).CallDeferred();
	}

	static void Raise(Action action) => Callable.From(action).CallDeferred();

	void Close(string id)
	{
		if (closed) return;
		closed = true;
		open.Remove(this);

		// the owner first, so it's the one that comes back to the front
		if (owner != 0) EnableWindow(owner, true);
		DestroyWindow(window);
		byWindow.Remove(window);

		foreach (nint handle in new[] { font, headingFont, contentBrush, footerBrush, fieldBrush }) if (handle != 0) DeleteObject(handle);

		Raise(() => Closed?.Invoke(id));
	}
}
