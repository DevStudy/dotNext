﻿using Microsoft.AspNetCore.Http;
using System.Net;

namespace DotNext.Net.Cluster.Consensus.Raft.Http
{
    internal sealed class HeartbeatMessage : RaftHttpMessage
    {
        internal new const string MessageType = "Heartbeat";

        internal HeartbeatMessage(IPEndPoint sender, long term)
            : base(MessageType, sender, term)
        {
        }

        internal HeartbeatMessage(HttpRequest request)
            : base(request)
        {
        }

        internal static void CreateResponse(HttpResponse response)
        {
            response.StatusCode = StatusCodes.Status200OK;
        }
    }
}