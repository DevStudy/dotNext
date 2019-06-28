﻿using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace DotNext.Net.Cluster.Consensus.Raft.Http
{
    internal sealed class MetadataMessage : HttpMessage, IHttpMessageReader<MemberMetadata>, IHttpMessageWriter<MemberMetadata>
    {
        internal new const string MessageType = "Metadata";

        internal MetadataMessage(IPEndPoint sender)
            : base(MessageType, sender)
        {
        }

        internal MetadataMessage(HttpRequest request)
            : base(request)
        {
        }

        async Task<MemberMetadata> IHttpMessageReader<MemberMetadata>.ParseResponse(HttpResponseMessage response)
        {
            var serializer = new DataContractJsonSerializer(typeof(MemberMetadata));
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                return (MemberMetadata)serializer.ReadObject(stream);
        }

        public Task SaveResponse(HttpResponse response, MemberMetadata metadata)
        {
            response.StatusCode = StatusCodes.Status200OK;
            var serializer = new DataContractJsonSerializer(typeof(MemberMetadata));
            serializer.WriteObject(response.Body, metadata);
            return Task.CompletedTask;
        }
    }
}