using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Resources;

namespace DotNext.Net.Cluster.Consensus.Raft.Http
{
    [SuppressMessage("Globalization", "CA1304", Justification = "This is culture-specific resource strings")]
    [SuppressMessage("Globalization", "CA1305", Justification = "This is culture-specific resource strings")]
    internal static class LogMessages
    {
        private static readonly ResourceManager Resources = new ResourceManager("DotNext.Net.Cluster.Consensus.Raft.Http.LogMessages", Assembly.GetExecutingAssembly());

        internal static void SendingRequestToMember(this ILogger logger, IPEndPoint member, string messageType)
            => logger.LogDebug(Resources.GetString("SendingRequestToMember"), messageType, member);

        internal static void MemberUnavailable(this ILogger logger, IPEndPoint member, Exception e)
            => logger.LogWarning(e, Resources.GetString("MemberUnavailable"), member);

        internal static void UnhandledException(this ILogger logger, Exception e)
            => logger.LogError(e, Resources.GetString("UnhandledException"));
    }
}
