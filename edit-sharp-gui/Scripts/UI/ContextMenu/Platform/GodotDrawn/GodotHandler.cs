using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.GodotDrawn;

// shows a GodotMenuPopup. unlike the native handlers this one returns at once: picks and the close
// arrive through the model's signals as the popup reports them
public class GodotHandler : PlatformHandler
{
    GodotMenuPopup current;

    public override void HandleMenu(ContextMenu menu, Vector2? position = null)
    {
        current?.Hide();

        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        GodotMenuPopup popup = new(MenuModel.Flatten(menu.Elements));
        root.AddChild(popup);

        popup.Picked += item =>
        {
            bool hide = MenuModel.Activate(item.Entry) ? menu.HideOnCheckableItemSelect : menu.HideOnItemSelect;
            if (hide) popup.Hide();
            else popup.Refresh(MenuModel.Flatten(menu.Elements));
        };

        popup.PopupHide += () =>
        {
            if (current == popup) current = null;
            popup.QueueFree();
            menu.EmitSignal(ContextMenu.SignalName.Closed);
        };

        Vector2 at = position ?? root.GetMousePosition();
        if (!popup.IsEmbedded()) at = DisplayServer.WindowGetPosition(root.GetWindowId()) + root.GetScreenTransform() * at;

        current = popup;
        popup.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
        popup.KeepOnScreen();
    }

    // the popup keeps its rect current on the main thread, so this is safe from the preview thread
    public override Rect2I? OpenMenuRect() => current is { } popup && popup.LastScreenRect.Size != Vector2I.Zero ? popup.LastScreenRect : null;

    public override void Dismiss() => Callable.From(() => current?.Hide()).CallDeferred();

    public override void HighlightNext() => Callable.From(() => current?.FocusNext()).CallDeferred();

    public override void ActivateHighlighted() => Callable.From(() => current?.ActivateFocused()).CallDeferred();
}
