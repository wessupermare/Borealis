using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Tags;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using TrainingServer.Extensibility;
using TrainingServer.Networking;

namespace Borealis.Data;

public class NetworkConnection
{
	public const float LAG_SECONDS = 1;

	public NetworkConnection() { }

	public Controller Controller
	{
		set
		{
			if (socket is null)
				return;

			_ = socket.SendAsync(_transcoder.SecurePack(_me, _server, new ControllerUpdate(_me, value)));
		}
	}

	private string _endpoint = "";
	private Guid _server = new();
	private Guid _me = new();
	private Transcoder _transcoder = new();
	private WebsocketMonitor? socket;

	public void SelectEndpoint(string endpoint) => _endpoint = endpoint;

	public async Task SelectServerAsync(Guid server)
	{
		_server = server;

		if (socket is not null)
		{
			await socket!.DisposeAsync(WebSocketCloseStatus.NormalClosure, "Good day!");
			socket = null;
			_transcoder = new();
		}

		ClientWebSocket sockClient = new();

		try
		{
			await sockClient.ConnectAsync(new($"ws://127.0.0.1:5031/connect/{_server}"), CancellationToken.None);
			socket = new(sockClient);
			_ = Task.Run(socket.MonitorAsync);

			string[] rsaParamElems = (await socket.InterceptNextTextAsync()).Split('|');
			if (rsaParamElems.Length != 3)
				return;

			_me = Guid.Parse(rsaParamElems[0]);
			_transcoder.LoadAsymmetricKey(new() { Modulus = Convert.FromBase64String(rsaParamElems[1]), Exponent = Convert.FromBase64String(rsaParamElems[2]) });

			byte[] symKey = Aes.Create().Key;
			_transcoder.RegisterKey(_me, symKey);
			_transcoder.RegisterSecondaryRecipient(_server, _me);

			byte[] symKeyCrypt = _transcoder.AsymmetricEncrypt(symKey);
			await socket.SendAsync(symKeyCrypt);

			if (_transcoder.SecureUnpack(await socket.InterceptNextTextAsync()).Data is not JsonArray ja || ja.Count != 0)
			{
				// Tunnel establishment handshake failed. Purge the server.
				await socket.DisposeAsync(WebSocketCloseStatus.ProtocolError, "Handshake failed.");
				return;
			}

			socket.OnTextMessageReceived += (string cryptedMessage) =>
			{
				DateTimeOffset sent; Guid recipient; JsonNode data;
				try
				{
					(sent, recipient, data) = _transcoder.SecureUnpack(cryptedMessage);
				}
				catch (ArgumentException) { return; }

				if (data.Deserialize<NetworkMessage>() is not NetworkMessage msg)
					return;

				_ = OnPacketReceivedAsync(sent, msg);
			};

			// Send the confirmation packet back to the hub.
			await Task.Delay(100);
			await socket.SendAsync(_transcoder.SecurePack(_me, _me, Array.Empty<object>()));

			// Present yourself to the server.
			await socket.SendAsync(_transcoder.SecurePack(_me, _server, new ControllerUpdate(_me, new(
				DateTimeOffset.Now,
				Metadata: new("KZLA", ControllerData.Level.CTR),
				Position: new([])
			))));
		}
		catch (WebSocketException) { }
	}

	public Task SendChannelTextAsync(decimal frequency, string message) =>
		socket!.SendAsync(_transcoder.SecurePack(_me, _server, new ChannelMessage(_me, frequency, message)));

	public Task SendTextAsync(Guid recipient, string message) =>
		socket!.SendAsync(_transcoder.SecurePack(_me, _server, new TextMessage(_me, recipient, message)));

	public Task SendKillAsync(Guid victim) =>
		socket!.SendAsync(_transcoder.SecurePack(_me, _server, new KillMessage(victim)));

	/// <summary>Updates a given recipient with all known data.</summary>
	private Task AuthoritativeUpdateAsync(Guid recipient) =>
		socket!.SendAsync(_transcoder.SecurePack(_me, recipient, _knownAircraft.Select(kvp => new AircraftUpdate(kvp.Key, kvp.Value)).Cast<UserUpdate>().Concat(_knownControllers.Select(kvp => new ControllerUpdate(kvp.Key, kvp.Value)))));

	private async Task OnPacketReceivedAsync(DateTimeOffset sent, NetworkMessage packet)
	{
		if (socket is null)
			return;

		switch (packet)
		{
			case AircraftUpdate acup when acup.Update.HasFlag(UserUpdate.UpdatedField.Delete):
				_knownAircraft.Remove(acup.Aircraft, out _);
				break;

			case AircraftUpdate acup:
				if (_knownAircraft.TryGetValue(acup.Aircraft, out var ac))
					_knownAircraft[acup.Aircraft] = acup.Apply(sent, ac);
				else
				{
					_transcoder.RegisterSecondaryRecipient(acup.Aircraft, _server);
					await AuthoritativeUpdateAsync(acup.Aircraft);

					_knownAircraft.TryAdd(acup.Aircraft, acup.Apply(sent, new(DateTimeOffset.MinValue, new(), new(), new())));
				}
				break;

			case ControllerUpdate cup when cup.Update.HasFlag(UserUpdate.UpdatedField.Delete):
				_knownControllers.Remove(cup.Controller, out _);
				break;

			case ControllerUpdate cup:
				if (_knownControllers.TryGetValue(cup.Controller, out var c))
					_knownControllers[cup.Controller] = cup.Apply(sent, c);
				else
				{
					_transcoder.RegisterSecondaryRecipient(cup.Controller, _server);
					await AuthoritativeUpdateAsync(cup.Controller);

					_knownControllers.TryAdd(cup.Controller, cup.Apply(sent, new(DateTimeOffset.MinValue, new(), new())));
				}
				break;

			case AuthoritativeUpdate aup:
				aup.Controllers.AsParallel().ForAll(update => 
					_knownControllers[update.Controller] = 
						_knownControllers.TryGetValue(update.Controller, out var c)
							? c + update
							: update.ToController());

				aup.Aircraft.AsParallel().ForAll(update =>
					_knownAircraft[update.Aircraft] = 
						_knownAircraft.TryGetValue(update.Aircraft, out var ac)
							? ac + update
							: update.ToAircraft());
				break;

			default:
				throw new NotImplementedException();
		}
	}

	public async Task<IEnumerable<ServerInfo>?> ListServersAsync()
	{
		HttpClient cli = new() { BaseAddress = new(_endpoint) };

		if (await cli.GetFromJsonAsync<ServerInfo[]>("/servers") is not ServerInfo[] servers)
			return null;

		return servers;
	}

	public async Task<IEnumerable<ICompleteOsmGeo>> GetOsmGeosAsync()
	{
		if (string.IsNullOrEmpty(_endpoint))
			throw new Exception("Cannot get geos before selecting a server!");

		HttpClient cli = new() { BaseAddress = new(_endpoint) };

		if (await cli.GetFromJsonAsync<NetworkNode[]>("/geos/nodes") is not NetworkNode[] netNodes || netNodes.Length == 0
		 || await cli.GetFromJsonAsync<NetworkWay[]>("/geos/ways") is not NetworkWay[] netWays
		 || await cli.GetFromJsonAsync<NetworkRelation[]>("/geos/relations") is not NetworkRelation[] netRelations)
		{
			await Task.Delay(1000);
			return await GetOsmGeosAsync();
		}

		ImmutableDictionary<long, Node> nodes = netNodes.AsParallel().AsUnordered().Select(n => new KeyValuePair<long, Node>(n.Id, Create(n))).ToImmutableDictionary();
		ImmutableDictionary<long, CompleteWay> ways = netWays.AsParallel().AsUnordered().Select(n => new KeyValuePair<long, CompleteWay>(n.Id, Create(n, nodes))).ToImmutableDictionary();
		ImmutableDictionary<long, CompleteRelation> relations = netRelations.Select(n => new KeyValuePair<long, CompleteRelation>(n.Id, Create(n, nodes, ways))).ToImmutableDictionary();

		return [..nodes.Values, ..ways.Values, ..relations.Values];
	}

	private static TagsCollection GetTags(Dictionary<string, string>? tags) =>
		new(tags?.Select(kvp => new Tag(kvp.Key, kvp.Value)) ?? []);

	private static Node Create(NetworkNode n) =>
		new() { Id = n.Id, Latitude = n.Latitude, Longitude = n.Longitude, Tags = GetTags(n.Tags) };

	private static CompleteWay Create(NetworkWay w, IDictionary<long, Node> nodes) =>
		new() { Id = w.Id, Nodes = w.Nodes.Select(id => nodes[id]).ToArray(), Tags = GetTags(w.Tags) };

	private static CompleteRelation Create(NetworkRelation r, IDictionary<long, Node> nodes, IDictionary<long, CompleteWay> ways) => new() { 
		Id = r.Id,
		Members = [..r.Members.Where(m => m.MemberType is 0 or 1).Select(m => new CompleteRelationMember() { Member = m.MemberType switch { 0 => nodes[m.Member], 1 => ways[m.Member], _ => throw new NotImplementedException() }, Role = m.Role })],
		Tags = GetTags(r.Tags)
	};

	private static ICompleteOsmGeo Create(NetworkGeo g, IDictionary<long, Node> nodes, IDictionary<long, CompleteWay> ways) => g switch {
		NetworkNode n => Create(n),
		NetworkWay w => Create(w, nodes),
		NetworkRelation r => Create(r, nodes, ways),
		_ => throw new NotImplementedException()
	};

	readonly ConcurrentDictionary<Guid, Aircraft> _knownAircraft = new();
	readonly ConcurrentDictionary<Guid, Controller> _knownControllers = new();

	public IEnumerable<Aircraft> GetAircraft() => _knownAircraft.Values.Select(ka => ka.Extrapolate(DateTimeOffset.Now));

	public IEnumerable<Controller> GetControllers() => _knownControllers.Values;

	public Guid[] GetGuidsFromCallsign(string callsign) => [
		.. _knownAircraft.Where(kvp => kvp.Value.Metadata.Callsign.Equals(callsign, StringComparison.InvariantCultureIgnoreCase)).Select(kvp => kvp.Key),
		.. _knownControllers.Where(kvp => kvp.Value.Metadata.Callsign.Equals(callsign, StringComparison.InvariantCultureIgnoreCase)).Select(kvp => kvp.Key)
	];
}

internal static class NetworkExtensions
{
	public static Aircraft Apply(this AircraftUpdate update, DateTimeOffset sent, Aircraft aircraft)
	{
		if (update.Update.HasFlag(UserUpdate.UpdatedField.Metadata))
			aircraft = aircraft with { Metadata = update.Metadata!.Value };

		if (update.Update.HasFlag(UserUpdate.UpdatedField.State))
			aircraft = aircraft with { Position = update.State!.Value };

		if (update.Update.HasFlag(UserUpdate.UpdatedField.Movement))
			aircraft = aircraft with { Movement = update.Movement!.Value };

		return aircraft with { Time = sent.AddSeconds(NetworkConnection.LAG_SECONDS) };
	}

	public static Controller Apply(this ControllerUpdate update, DateTimeOffset sent, Controller controller)
	{
		if (update.Update.HasFlag(UserUpdate.UpdatedField.Metadata))
			controller = controller with { Metadata = update.Metadata!.Value };

		if (update.Update.HasFlag(UserUpdate.UpdatedField.State))
			controller = controller with { Position = update.State!.Value };

		return controller with { Time = sent };
	}
}