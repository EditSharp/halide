using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.GodotDrawn;

// shows a GodotMenuPopup. unlike the native handlers this one returns at once: picks and the close
// arrive through the model's signals as the popup reports them
public class GodotHandler : PlatformHandler
{
    GodotMenuPopup current;

    public override void HandleMenu(ContextMenu menu, Window owner, Vector2I? at)
    {
        current?.Hide();

        Window parent = owner ?? ((SceneTree)Engine.GetMainLoop()).Root;
        GodotMenuPopup popup = new(MenuModel.Flatten(menu.Elements));
        parent.AddChild(popup);

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

        // embedded popups take their embedder's pixels, native ones the screen's
        Vector2 point = at ?? DisplayServer.MouseGetPosition();
        if (popup.IsEmbedded() && ContextMenus.EmbedderOf(parent) is Window embedder)
            point = embedder.GetScreenTransform().AffineInverse() * (point - DisplayServer.WindowGetPosition(ContextMenus.NativeWindowOf(embedder).GetWindowId()));

        current = popup;
        popup.Popup(new Rect2I((Vector2I)point.Round(), Vector2I.Zero));
        popup.KeepOnScreen();
    }

    // the popup keeps its rect current on the main thread, so this is safe from the preview thread
    public override Rect2I? OpenMenuRect() => current is { } popup && popup.LastScreenRect.Size != Vector2I.Zero ? popup.LastScreenRect : null;

    public override void Dismiss() => Callable.From(() => current?.Hide()).CallDeferred();

    public override void HighlightNext() => Callable.From(() => current?.FocusNext()).CallDeferred();

    public override void ActivateHighlighted() => Callable.From(() => current?.ActivateFocused()).CallDeferred();
}
