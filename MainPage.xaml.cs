using Borealis.Layers;

using Microsoft.UI.Input;

namespace Borealis;

public partial class MainPage : ContentPage
{
	readonly Cursor _cursor;
	readonly QDM _qdm;

	public MainPage()
	{
		InitializeComponent();
		GvwScope.MoveHoverInteraction += GvwScope_MoveHoverInteraction;

		Scope scope = (Scope)GvwScope.Drawable;

		scope.Add(new Background(Color.FromRgb(0, 0, 0)));

		_qdm = new(Color.FromRgb(255, 255, 255));
		scope.Add(_qdm);

		_cursor = new(Color.FromRgb(255, 120, 200));
		scope.Add(_cursor);

		TapGestureRecognizer doubleClickRecognizer = new() { NumberOfTapsRequired = 2 };
		doubleClickRecognizer.Tapped += (_, _) => _qdm.DoubleClick();
		GvwScope.GestureRecognizers.Add(doubleClickRecognizer);
	}

	private void GvwScope_MoveHoverInteraction(object _, TouchEventArgs e)
	{
		if (GvwScope.Handler.PlatformView is Microsoft.UI.Xaml.Controls.Control c)
			c.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.Cross));

		_cursor.Position = e.Touches.FirstOrDefault();
		_qdm.CursorPosition = e.Touches.FirstOrDefault();
		GvwScope.Invalidate();
	}
}

