﻿using Fluxor;
using Microsoft.AspNetCore.Components;
using Nebula.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Lyra.Core.API;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace Nebula.Store.NodeViewUseCase
{
	public class NodeViewActionEffect : Effect<NodeViewAction>
	{
		private readonly LyraRestClient client;
		private readonly IConfiguration config;

		public NodeViewActionEffect(LyraRestClient lyraClient, IConfiguration configuration)
		{
			client = lyraClient;
			config = configuration;
		}

		protected override async Task HandleAsync(NodeViewAction action, IDispatcher dispatcher)
		{
			var bb = await client.GetBillBoardAsync();

			var bag = new ConcurrentDictionary<string, GetSyncStateAPIResult>();
			var tasks = bb.AllNodes
				//.Where(a => bb.PrimaryAuthorizers.Contains(a.Key))
				.Select(b => b.Value)
				.Select(async node =>
			{
				var lcx = LyraRestClient.Create(config["network"], Environment.OSVersion.ToString(), "Nebula", "1.4", $"http://{node.IPAddress}:4505/api/Node/");
				try
                {
					var syncState = await lcx.GetSyncState();
					bag.TryAdd(node.AccountID, syncState);
				}
				catch(Exception ex)
                {
					bag.TryAdd(node.AccountID, null);
                }
			});
			await Task.WhenAll(tasks);

			dispatcher.Dispatch(new NodeViewResultAction(bb, bag));
		}
	}
}