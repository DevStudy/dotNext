﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;
using IServer = Microsoft.AspNetCore.Hosting.Server.IServer;

namespace DotNext.Net.Cluster.Consensus.Raft.Http.Hosting
{
    internal sealed class RaftHostedCluster : RaftHttpCluster
    {
        private sealed class WebHostConfigurer
        {
            private readonly RaftHostedClusterMemberConfiguration config;
            private readonly IDedicatedHostBuilder? hostBuilder;
            private readonly RequestDelegate raftProcessor;

            internal WebHostConfigurer(IServiceProvider services, out RaftHostedClusterMemberConfiguration config, RequestDelegate raftProcessor)
            {
                this.config = config = services.GetRequiredService<IOptions<RaftHostedClusterMemberConfiguration>>().Value;
                hostBuilder = services.GetService<IDedicatedHostBuilder>();
                this.raftProcessor = raftProcessor;
            }

            private void Configure(IApplicationBuilder app)
                => app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = RaftHttpConfigurator.WriteExceptionContent }).Run(raftProcessor);

            private void Configure(IWebHostBuilder webHost)
            {
                Action<IApplicationBuilder> appBuilder = Configure;
                if (hostBuilder is null)
                {
                    webHost.UseKestrel(config.ConfigureKestrel);
                }
                else
                {
                    hostBuilder.Configure(webHost);
                    appBuilder = hostBuilder.Configure + appBuilder;
                }
                webHost.Configure(appBuilder);
            }

            internal IHost BuildHost() => new HostBuilder().ConfigureWebHost(Configure).Build();
        }

        private static readonly Uri Root = new Uri("/", UriKind.Relative);
        private readonly IHost host;

        public RaftHostedCluster(IServiceProvider services)
            : base(services, out var members)
        {
            host = new WebHostConfigurer(services, out var config, ProcessRequest).BuildHost();
            config.SetupHostAddressHint(host.Services.GetRequiredService<IServer>().Features);
            foreach (var memberUri in config.Members)
                members.Add(CreateMember(memberUri));
        }

        private protected override RaftClusterMember CreateMember(Uri address)
        {
            var member = new RaftClusterMember(this, address, Root);
            ConfigureMember(member);
            return member;
        }

        public override async Task StartAsync(CancellationToken token)
        {
            await host.StartAsync(token).ConfigureAwait(false);
            await base.StartAsync(token).ConfigureAwait(false);
        }

        public override async Task StopAsync(CancellationToken token)
        {
            await host.StopAsync(token).ConfigureAwait(false);
            await base.StopAsync(token).ConfigureAwait(false);
        }

        private protected override Predicate<RaftClusterMember> LocalMemberFinder => host.Services.GetRequiredService<IServer>().GetHostingAddresses().Contains;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                host.Dispose();
            base.Dispose(disposing);
        }
    }
}
