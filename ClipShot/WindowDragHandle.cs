using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;

// A separate hit target keeps moving the window away from clip selection.
public sealed class WindowDragHandle : Canvas
{
    public WindowDragHandle()=>ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
}
