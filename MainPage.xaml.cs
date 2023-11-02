using Borealis.Data;
using Borealis.Layers;

using CommunityToolkit.Maui.Views;

using Microsoft.UI.Input;

using NetTopologySuite.Geometries;

using OsmSharp.Complete;
using OsmSharp.Geo;
using OsmSharp.Streams;
using OsmSharp.Streams.Complete;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.RegularExpressions;

using CIFP = CIFPReader.CIFP;
using KeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;

namespace Borealis;

// TODO: Flightplan popup & route analyser
// TODO: Color editor
// TODO: Insets

public partial class MainPage : ContentPage
{
	readonly CIFP _cifp;

	readonly Scope _scope;

	bool _isClickHeld = false;

	public MainPage()
	{
		string[] FIRs = ["ZLA"];

		HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
		InitializeComponent();

		Colorscheme colors = new(
			Aircraft: new(0x00, 0xFF, 0x00),

			Route: new(0xFF, 0xAA, 0xAA),
			Departure: new(0, 0xCC, 0xFF),
			Arrival: new(0, 0xCC, 0xFF),
			Approach: new(0, 0xCC, 0xFF),

			ClassB: new(0, 0, 0xFF),
			ClassC: new(0xCC, 0, 0x88),
			ClassD: new(0, 0, 0xFF),
			ClassE: new(0xCC, 0, 0x88),

			Airport: new(0xFF / 255f),

			Runway: new(0x66 / 255f),
			Taxiway: new(0xDD / 255f),
			Taxilane: new(0xDD / 255f),
			Gate: new(0xFF / 255f),
			Apron: new(0x99 / 255f),
			Building: new(0xCC, 0x44, 0x44),

			FirBoundary: new(0x00, 0xCC, 0xFF),
			Coastline: new(0xFF / 255f),

			Cursor: Color.FromRgb(0xFF, 0x77, 0xCC),
			QDM: new(0xFF / 255f),
			RangeRings: new(0xFF / 255f),
			Background: new(0x00 / 255f)
		);

		Colorscheme labelColors = colors with {
			ClassB = colors.ClassB.MultiplyAlpha(0.13f),
			ClassC = colors.ClassC.MultiplyAlpha(0.13f),
			ClassD = colors.ClassD.MultiplyAlpha(0.13f),
			ClassE = colors.ClassE.MultiplyAlpha(0.13f),

			Runway = new(0xFF / 255f),
			Taxiway = new(0xFF / 255f),
			Taxilane = new(0xFF / 255f),
		};

		string[][] artccBoundaryData = http.GetStringAsync("https://aeronav.faa.gov/Upload_313-d/ERAM_ARTCC_Boundaries/Ground_Level_ARTCC_Boundary_Data_2023-10-05.csv").Result.Split(Environment.NewLine).Skip(1).Select(l => l.Split(',')).ToArray();
		var allPoints = FIRs.Select(fir => (fir, artccBoundaryData.Where(l => l[0] == fir).Select(l => (new Coordinate($"{l[3][^1]}{l[3][..^1]}{l[4][^1]}{l[4][..^1]}"), FIRs.Contains(l[^1]) ? null : l[^1]))));
		Route[] borders = allPoints.Select(v => new Route(v.fir, v.Item2.Append(v.Item2.First()).ToArray())).ToArray();

		GvwScope.MoveHoverInteraction += GvwScope_MoveHoverInteraction;
		GvwScope.StartInteraction += (_, _) => _isClickHeld = true;
		GvwScope.EndInteraction += (_, _) => _isClickHeld = false;

		_scope = (Scope)GvwScope.Drawable;
		_scope.SetScale(_zoom);

		string layerConfig = Path.Combine(FileSystem.AppDataDirectory, "layers.conf");
		if (!File.Exists(layerConfig))
			File.WriteAllText(layerConfig, @".:
	Background
	RangeRings
	FirBoundary
	Coastline
	Buildings
	Aprons
	Gates
	Taxiways
	Runways
	Airspace
	IAPs
	Departures
	Arrivals
	LiveRoutes
	TrackHistory
	Airports
	Datablocks
	Aircraft
	QDM
	Cursor");

		Assembly? assembly = null;
		List<Type> layerTypes = [];
		foreach (string l in File.ReadAllLines(layerConfig))
		{
			string line = l.Trim();

			if (line.EndsWith(':')) // Specify an assembly.
			{
				line = line[..^1].TrimEnd();

				if (line == ".")
					assembly = Assembly.GetExecutingAssembly();
				else
				{
					if (!File.Exists(line))
						throw new Exception($"Cannot find assembly {line}.");

					assembly = Assembly.LoadFile(line);
				}
			}
			else if (string.IsNullOrEmpty(line))
				continue;
			else if (assembly is null)
				throw new Exception("You must specify an assembly before specifying layers.");
			else if (assembly.GetTypes().Where(t => t.Name.Equals(line, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault() is Type t)   // Specify a layer.
			{
				if (!t.GetInterfaces().Contains(typeof(ILayer)))
					throw new Exception($"{line} does not implement ILayer!");

				layerTypes.Add(t);
			}
			else
				throw new Exception($"Couldn't find layer type {line}.");
		}

		_cifp = _scope.Cifp;
		ColorEditor colorEditor = new(_scope, colors, labelColors);
		colorEditor.ColorsChanged += c => (colors, labelColors) = (c.Primary, c.Labels);

		ServerSelector selector = new(_scope, @"http://localhost:5031/");
		_scope.Add(selector);
		_scope.Add(colorEditor);

		// Perform heavy loading on another thread so the main UI can spawn.
		Task.Run(async () =>
		{
			OsmCompleteStreamSource osmStream = await selector.GetOsmGeosAsync();
			OsmEnumerableStreamSource simpleStream = new(osmStream.Select(ig => ig switch { OsmSharp.Node n => n, CompleteOsmGeo g => g.ToSimple(), _ => throw new NotImplementedException() }));
			HashSet<ICompleteOsmGeo> boundedGeos = [];

			foreach (var border in borders)
			{
				Polygon boundary = new(new LinearRing(border.Select(p => new GeoAPI.Geometries.Coordinate(p.Point.Longitude, p.Point.Latitude)).ToArray()));
				boundedGeos.UnionWith(simpleStream.FilterSpatial(boundary, true).ToComplete().Select(i => { colorEditor.IncrementLoadCount(); return i; }));
			}

			Dictionary<Type, object[]> injectionItems = new() {
				{ typeof(HttpClient), [http] },
				{ typeof(CIFP), [_cifp] },
				{ typeof(Scope), [_scope] },
				{ typeof(Route), borders },
				{ typeof(Colorscheme), new object[] { colors, labelColors } },
				{ typeof(OsmCompleteStreamSource), [new OsmCompleteEnumerableStreamSource(boundedGeos), osmStream] },
				{ typeof(NetworkConnection), [selector.Connection] },
				{ typeof(ICompleteOsmGeo[]), new ICompleteOsmGeo[][] { [..boundedGeos] } }
			};

			ILayer?[] layerBuffer = new ILayer?[layerTypes.Count];
			for (int iteration = 0; layerBuffer.Any(l => l is null) && iteration < layerBuffer.Length; ++iteration)
				for (int layerIdx = 0; layerIdx < layerBuffer.Length; ++layerIdx)
				{
					if (layerBuffer[layerIdx] is not null)
						continue;

					Type layerType = layerTypes[layerIdx];

					var constructor =
						layerType
							.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
							.OrderByDescending(c => c.GetParameters().Length)
							.FirstOrDefault(c =>
								c.GetParameters().All(p =>
									injectionItems.ContainsKey(p.ParameterType)
								 || (p.ParameterType.IsArray && injectionItems.ContainsKey(p.ParameterType.GetElementType() ?? typeof(void)))
								 || (p.ParameterType.IsByRef && p.ParameterType.GetElementType() == typeof(KeyboardAccelerator))));

					if (constructor is null)
						continue;

					object?[] cArgs = new object?[constructor.GetParameters().Length];
					Dictionary<Type, int> counts = [];
					foreach (var t in constructor.GetParameters().Select(p => injectionItems.ContainsKey(p.ParameterType) ? p.ParameterType : p.ParameterType.GetElementType() ?? typeof(void)))
						counts[t] = 0;

					int paramCntr = 0;
					foreach (var param in constructor.GetParameters())
					{
						if (injectionItems.TryGetValue(param.ParameterType, out object[]? v))   // Basic type. Give them the next item from the list.
							cArgs[paramCntr++] = v[counts[param.ParameterType]++ % v.Length];
						else if (injectionItems.TryGetValue(param.ParameterType.GetElementType() ?? typeof(void), out object[]? vA))    // Array type. Give them all!
							cArgs[paramCntr++] = vA;
						else if (param.ParameterType.IsByRef && param.ParameterType.GetElementType() == typeof(KeyboardAccelerator))
							cArgs[paramCntr++] = null;
						else
							throw new Exception("Impossible?");
					}

					ILayer newLayer = (ILayer)constructor.Invoke(cArgs);

					List<KeyboardAccelerator> addAccels = [];
					if (cArgs.Any(a => a is KeyboardAccelerator))
						addAccels.AddRange(cArgs.Where(a => a is KeyboardAccelerator).Cast<KeyboardAccelerator>());

					if (newLayer.GetType().GetMethod("GetAccelerator") is MethodInfo mi && mi.ReturnType == typeof(KeyboardAccelerator) && mi.GetParameters().Length == 0)
						addAccels.Add(await MainThread.InvokeOnMainThreadAsync(() => (KeyboardAccelerator)mi.Invoke(newLayer, null)!));

					foreach (var accel in addAccels)
					{
						_accelerators.Add(accel);

						if (scopeControl is not null)
							await MainThread.InvokeOnMainThreadAsync(() => scopeControl.KeyboardAccelerators.Add(accel));
					}

					layerBuffer[layerIdx] = newLayer;

					if (injectionItems.TryGetValue(newLayer.GetType(), out object[]? old))
						injectionItems[newLayer.GetType()] = [.. old, newLayer];
					else
						injectionItems.Add(newLayer.GetType(), new[] { newLayer });
				}

			if (layerBuffer.Any(l => l is null))
				throw new Exception($"Unable to load {layerTypes[layerBuffer.TakeWhile(l => l is not null).Count()].Name}. Are you sure you've loaded all the layers you need?");

			foreach (ILayer layer in layerBuffer.Cast<ILayer>())
				_scope.Add(layer);

			foreach (ILayer layer in _scope)
				layer.OnInvalidating += GvwScope.Invalidate;
		});

		TapGestureRecognizer leftClickRecognizer = new() { NumberOfTapsRequired = 1, Buttons = ButtonsMask.Primary };
		leftClickRecognizer.Tapped += (_, e) =>
			ScopeClicked(ILayer.ClickType.Single | ILayer.ClickType.Left, e.GetPosition(GvwScope)!.Value);
		GvwScope.GestureRecognizers.Add(leftClickRecognizer);

		TapGestureRecognizer rightClickRecognizer = new() { NumberOfTapsRequired = 1, Buttons = ButtonsMask.Secondary };
		rightClickRecognizer.Tapped += (_, e) =>
			ScopeClicked(ILayer.ClickType.Single | ILayer.ClickType.Right, e.GetPosition(GvwScope)!.Value);
		GvwScope.GestureRecognizers.Add(rightClickRecognizer);

		TapGestureRecognizer doubleClickRecognizer = new() { NumberOfTapsRequired = 2 };
		doubleClickRecognizer.Tapped += (_, e) =>
			ScopeClicked(ILayer.ClickType.Double | ILayer.ClickType.Left, e.GetPosition(GvwScope)!.Value);
		GvwScope.GestureRecognizers.Add(doubleClickRecognizer);

		// Invalidate regularly to permit a consistent refresh.
		Task.Run(async () =>
		{
			await Task.Delay(2500);
			while (true)
			{
				GvwScope.Invalidate();
				await Task.Delay(500);
			}
		});
	}

	Microsoft.UI.Xaml.Controls.Control? scopeControl;
	float _zoom = 0.005f;
	PointF _cursorPos = new();

	private void GvwScope_MoveHoverInteraction(object? _, TouchEventArgs e)
	{
		if (scopeControl is null && GvwScope.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Control c)
		{
			scopeControl = c;
			foreach (var accel in GetAccelerators())
				c.KeyboardAccelerators.Add(accel);

			c.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.Cross));
			c.PointerWheelChanged += (_, e) => { _zoom = (float)Math.Clamp(_zoom + (_zoom * -e.GetCurrentPoint(c).Properties.MouseWheelDelta / 1000f), 1E-05, 0.08); _scope.SetScale(_zoom); GvwScope.Invalidate(); };
		}

		PointF newPos = e.Touches.FirstOrDefault();

		if (_isClickHeld)
		{
			_scope.Drag(newPos - _cursorPos);
			ScopeClicked(ILayer.ClickType.Hover | ILayer.ClickType.Left, newPos);
		}
		else
			ScopeClicked(ILayer.ClickType.Hover, newPos);

		_cursorPos = newPos;

		GvwScope.Invalidate();
	}

	readonly List<KeyboardAccelerator> _accelerators = [];
	private IEnumerable<KeyboardAccelerator> GetAccelerators()
	{
		foreach (var accel in _accelerators)
			yield return accel;

		KeyboardAccelerator locker = new() { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.M };
		locker.Invoked += (_, _) => _scope.MagVarLocked = !_scope.MagVarLocked;
		yield return locker;

		KeyboardAccelerator texter = new() { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.Tab };
		texter.Invoked += (_, _) =>
			Task.Run(async () =>
			{
				Entry frequency = new() { Text = "123.45", Keyboard = Keyboard.Numeric };
				Entry input = new() { Placeholder = "Message", HorizontalOptions = LayoutOptions.Fill };

				Grid g = new() {
					RowDefinitions = {
						new RowDefinition()
					},
					ColumnDefinitions = {
						new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
						new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) }
					},
					ColumnSpacing = 6,
					HorizontalOptions = LayoutOptions.Fill
				};

				g.Add(frequency, 0);
				g.Add(input, 1);

				Popup p = new() {
					Content = g
				};

				frequency.Completed += (_, _) => input.Focus();
				input.Completed += (_, _) => p.Close((frequency.Text, input.Text));

				if (await MainThread.InvokeOnMainThreadAsync(async () => await this.ShowPopupAsync(p)) is (string freq, string text))
				{
					var conn = ((ServerSelector)_scope.First(l => l is ServerSelector)).Connection;

					if (decimal.TryParse(freq, out var f))
						await conn.SendChannelTextAsync(f, text);
					else if (freq.ToUpperInvariant() is "KILL" or "!" && conn.GetGuidsFromCallsign(text) is Guid[] victims && victims.Length > 0)
						await Task.WhenAll(victims.Select(g => conn.SendKillAsync(g)));
					else if (conn.GetGuidsFromCallsign(freq) is Guid[] recipients && recipients.Length > 0)
						await Task.WhenAll(recipients.Select(r => conn.SendTextAsync(r, text)));
				}
			});
		yield return texter;

		KeyboardAccelerator finder = new() { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.F };
		finder.Invoked += (_, _) =>
			Task.Run(async () =>
			{
				Entry searchBox = new() { Placeholder = "Query" };
				DataTemplate resultTemplate = new();

				Action<object?>? kill = null;

				ListView results = new() {
					ItemsSource = QueryResults,

					ItemTemplate = new DataTemplate(() =>
					{
						TextCell retval = new();
						retval.SetBinding(TextCell.TextProperty, "Name");
						retval.SetBinding(TextCell.DetailProperty, "Centerpoint");
						retval.Tapped += (_, _) => kill?.Invoke(((SearchResult)retval.BindingContext).Centerpoint);

						return retval;
					}),

					HorizontalScrollBarVisibility = ScrollBarVisibility.Always
				};

				searchBox.TextChanged += (_, e) => { results.BeginRefresh(); Search(e.NewTextValue); results.EndRefresh(); };

				Popup p = new() {
					Content = new ScrollView() {
						Content = new VerticalStackLayout() {
							searchBox,
							results
						}
					}
				};
				kill = p.Close;

				var res = await MainThread.InvokeOnMainThreadAsync(async () => await this.ShowPopupAsync(p));

				if (res is Coordinate c)
					_scope.Teleport(c);
			});
		yield return finder;
	}

	private ObservableCollection<SearchResult> QueryResults { get; } = new([new("No results found", null)]);
	private record SearchResult(string Name, Coordinate? Centerpoint) { }

	void Search(string query)
	{
		try
		{
			Regex iq = new("^" + query, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
				  rq = new(query, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

			var results = _scope.SelectMany(l => l.Find(iq)).OrderBy(v => _scope.Centerpoint.DistanceTo(v.Centerpoint)).Concat(_scope.SelectMany(l => l.Find(rq)).OrderBy(v => _scope.Centerpoint.DistanceTo(v.Centerpoint))).Distinct().Take(50).ToArray();
			QueryResults.Clear();

			if (results.Length == 0)
				QueryResults.Add(new("No results found", null));
			else
				foreach (var (name, centerpoint) in results)
					QueryResults.Add(new(name, centerpoint));
		}
		catch (RegexParseException)
		{
			QueryResults.Clear();
			QueryResults.Add(new("Invalid regex", null));
		}
	}

	internal void ScopeClicked(ILayer.ClickType clickType, PointF position)
	{
		Coordinate worldPos = new();

		if (_scope.LastTransform is Transformer t)
			worldPos = t.LocalToWorldPoint(position);

		_ = _scope.Reverse().Where(l => l.Active).TakeWhile(l => !l.Interact(position, worldPos, clickType)).ToArray();
	}
}

internal static class GeoExtensions
{
	public static GeoAPI.Geometries.IPoint ToPoint(this OsmSharp.Node n) => new NetTopologySuite.Geometries.Point(n.Longitude ?? 0, n.Latitude ?? 0);
}