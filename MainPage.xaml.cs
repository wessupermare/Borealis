using Borealis.Data;
using Borealis.Layers;

using CommunityToolkit.Maui.Views;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

using NetTopologySuite.Geometries;

using OsmSharp.Complete;
using OsmSharp.Geo;
using OsmSharp.Streams;
using OsmSharp.Streams.Complete;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.RegularExpressions;

using TrainingServer.Networking;

using CIFP = CIFPReader.CIFP;

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
		string[] FIRs = new[] { "ZLA" };

		HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
		Whazzup whazzup = new(http);
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

		//Route[] borders = new[] { new Route("ZYZ") {
		//	new(43.8535551f, -82.1821771f), new(43.616667f, -82.125f), new(43f, -82.416667f), new(42.870833f, -82.466667f), new(42.829167f, -81.970833f), new(42.741667f, -81.220833f), new(42.779167f, -80.895833f), new(42.836111f, -79.844444f), new(42.95f, -79.058333f), new(43.197778f, -79.024444f), new(43.433333f, -78.75f), new(43.6f, -78.75f), new(43.633333f, -78.677778f), new(43.633333f, -76.791667f), new(44.0920867f, -76.4449558f), new(44.132983f, -76.3531025f), new(44.1982678f, -76.3128233f), new(44.2025951f, -76.2806128f), new(44.2374402f, -76.1946212f), new(44.7460604f, -76.2180651f), new(44.8177388f, -75.7532896f), new(44.8827552f, -75.4569735f), new(44.919408f, -75.3556016f), new(44.9746148f, -75.2564903f), new(45.0370639f, -75.1778825f), new(45.1269118f, -75.1135206f), new(45.216442f, -75.061588f), new(45.3070421f, -75.0603553f), new(45.4076116f, -75.0674255f), new(45.6261141f, -75.1663307f), new(45.8360396f, -76.2665354f), new(44.7459997f, -76.2180209f), new(45.8360528f, -76.2665431f), new(45.962258f, -76.9246569f), new(46.1340524f, -77.2503741f), new(46.9457957f, -77.2486692f), new(47.1014999f, -77.5332956f), new(47.5524628f, -78.1101128f), new(47.8334142f, -78.5814722f), new(47.8014542f, -78.6519573f), new(47.7770911f, -78.7393138f), new(47.7659425f, -78.8045529f), new(47.7566202f, -78.8759371f), new(47.7550478f, -78.9572252f), new(47.7602193f, -79.0370498f), new(47.7730405f, -79.1205071f), new(47.7960015f, -79.2012457f), new(47.8179012f, -79.2655653f), new(47.8508474f, -79.329744f), new(47.8940184f, -79.397667f), new(47.9353344f, -79.4492038f), new(47.9936922f, -79.4993339f), new(48.0613549f, -79.5354486f), new(48.1221048f, -79.5538734f), new(48.1828125f, -79.5595596f), new(48.2412016f, -79.5535512f), new(48.2973978f, -79.534769f), new(48.3424491f, -79.5097112f), new(48.392492f, -79.4678397f), new(48.4415903f, -79.4156321f), new(48.4837856f, -79.3557349f), new(48.5190916f, -79.2907213f), new(48.542876f, -79.2272738f), new(48.5610315f, -79.1624344f), new(48.5757537f, -79.0989369f), new(48.584042f, -79.0031909f), new(49.0117032f, -79.0002904f), new(53.4621807f, -80.0015757f), new(49.9103328f, -84.1700779f), new(48.7804853f, -85.3469845f), new(47.0825668f, -86.9939808f), new(46.7507423f, -85.6335354f), new(46.3834612f, -84.5324857f), new(46.2499896f, -84.3571855f), new(45.8148608f, -83.5818105f), new(45.332982f, -82.4987071f), new(43.8535551f, -82.1821771f)
		//}};

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
		List<Type> layerTypes = new();
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

		// Perform heavy loading on another thread so the main UI can spawn.
		Task.Run(async () =>
		{
			PBFOsmStreamSource osmStream = new(File.OpenRead(@"C:\Users\westo\Downloads\aeroways.osm.pbf"));
			OsmStreamSource borderedStream = osmStream.FilterSpatial(new Polygon(new LinearRing(borders.First().Select(p => new GeoAPI.Geometries.Coordinate(p.Point.Longitude, p.Point.Latitude)).Select(i => { colorEditor.IncrementLoadCount(); return i; }).ToArray())));

			Dictionary<Type, object[]> injectionItems = new() {
				{ typeof(HttpClient), new[] { http } },
				{ typeof(Whazzup), new[] { whazzup } },
				{ typeof(CIFP), new[] { _cifp } },
				{ typeof(Scope), new[] { _scope } },
				{ typeof(Route), borders },
				{ typeof(Colorscheme), new object[] { colors, labelColors } },
				{ typeof(OsmStreamSource), new[] { borderedStream, osmStream } },
				{ typeof(OsmCompleteStreamSource), new[] { borderedStream.ToComplete(), osmStream.ToComplete() } },
				{ typeof(NetworkConnection), new[] { selector.Connection } }
			};

			injectionItems.Add(typeof(ICompleteOsmGeo[]), new ICompleteOsmGeo[][] { ((OsmCompleteStreamSource)injectionItems[typeof(OsmCompleteStreamSource)].First()).ToArray() });

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
					Dictionary<Type, int> counts = new();
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

					List<KeyboardAccelerator> addAccels = new();
					if (cArgs.Any(a => a is KeyboardAccelerator))
						addAccels.AddRange(cArgs.Where(a => a is KeyboardAccelerator).Cast<KeyboardAccelerator>());

					if (newLayer.GetType().GetMethod("GetAccelerator") is MethodInfo mi && mi.ReturnType == typeof(KeyboardAccelerator) && !mi.GetParameters().Any())
						addAccels.Add(await MainThread.InvokeOnMainThreadAsync(() => (KeyboardAccelerator)mi.Invoke(newLayer, null)!));

					foreach (var accel in addAccels)
					{
						_accelerators.Add(accel);

						if (scopeControl is not null)
							await MainThread.InvokeOnMainThreadAsync(() => scopeControl.KeyboardAccelerators.Add(accel));
					}

					layerBuffer[layerIdx] = newLayer;

					if (injectionItems.TryGetValue(newLayer.GetType(), out object[]? old))
						injectionItems[newLayer.GetType()] = old.Append(newLayer).ToArray();
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

	readonly List<KeyboardAccelerator> _accelerators = new();
	private IEnumerable<KeyboardAccelerator> GetAccelerators()
	{
		foreach (var accel in _accelerators)
			yield return accel;

		KeyboardAccelerator locker = new() { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.M };
		locker.Invoked += (_, _) => _scope.MagVarLocked = !_scope.MagVarLocked;
		yield return locker;

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

	private ObservableCollection<SearchResult> QueryResults { get; } = new(new() { new("No results found", null) });
	private record SearchResult(string Name, Coordinate? Centerpoint) { }

	void Search(string query)
	{
		Regex iq = new("^" + query, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
			  rq = new(query, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

		var results = _scope.SelectMany(l => l.Find(iq)).OrderBy(v => _scope.Centerpoint.DistanceTo(v.Centerpoint)).Concat(_scope.SelectMany(l => l.Find(rq)).OrderBy(v => _scope.Centerpoint.DistanceTo(v.Centerpoint))).Distinct().Take(50).ToArray();
		QueryResults.Clear();

		if (!results.Any())
			QueryResults.Add(new("No results found", null));
		else
			foreach (var (name, centerpoint) in results)
				QueryResults.Add(new(name, centerpoint));
	}

	internal void ScopeClicked(ILayer.ClickType clickType, PointF position)
	{
		Coordinate worldPos = new();

		if (_scope.LastTransform is Transformer t)
			worldPos = t.LocalToWorldPoint(position);

		_ = _scope.Reverse().Where(l => l.Active).TakeWhile(l => !l.Interact(position, worldPos, clickType)).ToArray();
	}
}

