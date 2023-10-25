using System.Net.Http.Json;

using TrainingServer.Networking;

namespace Borealis.Data;

public class NetworkConnection
{
	public NetworkConnection() { }

	private string _endpoint = "";
	private Guid _server = new();

	public void SelectEndpoint(string endpoint) => _endpoint = endpoint;

	public async Task SelectServerAsync(Guid server)
	{
		_server = server;
	}

	public async Task<IEnumerable<ServerInfo>?> ListServersAsync()
	{
		HttpClient cli = new() { BaseAddress = new(_endpoint) };

		if (await cli.GetFromJsonAsync<ServerInfo[]>("/servers") is not ServerInfo[] servers)
			return null;

		return servers;
	}
}
