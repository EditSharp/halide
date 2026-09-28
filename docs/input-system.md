# Input system guide

The input system has one owner for shared state and a small set of focused
objects. UI components read that state; they should not each reconstruct press,
drag, release, or shortcut routing from Godot events.

## Mouse path

1. `InputManager` is an autoload (`halide/project.godot`). Its `_Input` method
   feeds each root-window event into `Mouse` before a control's `_GuiInput`.
2. `Mouse` updates the current global position, per-event motion/scroll values,
   button state, and the action for the current event.
3. A control handles the action in `_GuiInput` and claims a drag through the
   button's capture API. `MouseButtonState` keeps the captor available through
   the release event so it can finish the interaction.
4. `IDragCancellable.CancelDrag` lets the owner unwind visual/model state if a
   release is lost. Focus loss and OS button-mask reconciliation release stuck
   buttons.

`MouseButtonClickState` describes the across-frame state (`Released`,
`Clicking`, `Dragging`). `MouseAction` describes the current event (`Press`,
`DragStart`, `DragMove`, `Click`, `DragEnd`, and so on). Use `GetDragStepDelta`
for incremental movement; `GetDragDelta` is the total displacement from press.
Wheel and extra mouse buttons do not have a click/drag lifecycle.

## Keyboard path

1. Godot gives a key to the focused GUI control first. Text entry and focused
   buttons therefore keep keys they consume.
2. An unused key reaches `InputManager._UnhandledKeyInput` and
   `Keyboard.Dispatch`.
3. Dispatch offers the action to the focused node and its ancestors, then the
   last captured view and its ancestors, then the remaining registered handlers.
   A handler sets `ShortcutEventArgs.Handled` to stop routing.
4. `ShortcutMap` maps action IDs to one or more `KeyCombo`s. Defaults are in
   `Keyboard.cs`; user overrides are stored at `user://shortcuts.json`.

`ShortcutRelay` forwards unhandled keyboard events from non-embedded OS windows
to the shared keyboard router. The root manager also attaches relays to windows
that appear later. `Modifiers` normalizes the platform's command/control key and
is latched on mouse-down so a gesture uses the modifiers held at its start.

## Where to make a change

| Change | Start here |
| --- | --- |
| Mouse press/drag/release semantics | `halide/Scripts/Input/Mouse.cs`, `MouseButtonState.cs` |
| Shortcut IDs/defaults and persistence | `halide/Scripts/Input/Keyboard.cs` |
| Global event ordering, focus loss, new windows | `halide/Scripts/Input/InputManager.cs` |
| Keyboard forwarding between native windows | `halide/Scripts/Input/ShortcutRelay.cs` |
| Platform modifier interpretation | `halide/Scripts/Input/Modifiers.cs` |
| A UI gesture's effect | The owning control/view; read `InputManager.Singleton.Mouse` and handle input there |
| A command invoked by a shortcut | Register an action in `Shortcuts`, then route it through the owning view/app command handler |

## Rules that preserve the design

- Do not mark an event handled from `_Input`; that prevents the central mouse
  tracker from seeing it and can strand a release. Consume GUI input in the GUI
  phase, after the manager has observed the event.
- Capture a button only when a control owns the gesture. Use the capture's
  cancellation callback to undo transient drag state.
- Treat `MouseAction` as valid for one input event. The event ID prevents stale
  actions from being mistaken for the next event.
- Register and unregister shortcut handlers with node lifetime. Prefer action
  IDs to direct key checks so users can rebind the action.
- Keep parsing/state logic testable at the API level, and test user-visible
  click/drag/key behavior through the real-input fixtures listed in the test
  matrix.

## Relevant tests

- API-level routing/map behavior: `tests/godot/Suite/KeyboardChainTests.cs`, `ShortcutTests.cs`.
- Real input and focus behavior: `tests/godot/Suite/NativeKeyboardChainTests.cs` and UI fixtures in
  `tests/godot/test_matrix.csv`.
