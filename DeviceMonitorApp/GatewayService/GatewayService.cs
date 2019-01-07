namespace Microsoft.ServiceFabric.Samples.DeviceMonitor
{
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class GatewayService : StatelessService
    {
        volatile private GatewayState state;

        private readonly TimeSpan heartbeatInterval;
        private readonly GatewayInfo myInfo;

        private readonly Uri monitorServiceAddress;
        private readonly ServicePartitionKey monitorServicePartitionKey;

        public GatewayService(StatelessServiceContext context)
            : base(context)
        {
            this.state = GatewayState.Created;
            this.myInfo = CreateMyInfo(context);
            this.heartbeatInterval = GetHeartbeatDuration(context.CodePackageActivationContext);
            this.monitorServiceAddress = GetMonitorServiceAddress(context.CodePackageActivationContext);
            this.monitorServicePartitionKey = new ServicePartitionKey(this.myInfo.Id.Name);
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await InitializeMonitoring(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await SendHeartbeatAsync(cancellationToken);
                await Task.Delay(this.heartbeatInterval, cancellationToken);
            }
        }

        private async Task InitializeMonitoring(CancellationToken cancellationToken)
        {
            this.state = GatewayState.Initializing;

            try
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Initializing monitoring");

                var gatewayMonitor = ServiceProxy.Create<IGatewayMonitor>(
                    this.monitorServiceAddress,
                    this.monitorServicePartitionKey);

                await gatewayMonitor.InitializeAsync(
                    this.myInfo,
                    this.heartbeatInterval,
                    cancellationToken);

                ServiceEventSource.Current.ServiceMessage(this.Context, "Monitoring initialized");
                this.state = GatewayState.Monitored;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Failed to initialize monitoring - {0}", e.ToString());
                this.state = GatewayState.Failed;
                this.OnMonitoringFailed();
                throw e;
            }

        }

        private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "Sending heartbeat");

            try
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    new CancellationTokenSource(heartbeatInterval).Token);

                var gatewayMonitor = ServiceProxy.Create<IGatewayMonitor>(
                    this.monitorServiceAddress,
                    this.monitorServicePartitionKey);
                await gatewayMonitor.HeartbeatAsync(this.myInfo.Id, cts.Token);

                ServiceEventSource.Current.ServiceMessage(this.Context, "Heartbeat sent");
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Failed to send heartbeat - {0}", e.ToString());
                this.state = GatewayState.Failed;
                this.OnMonitoringFailed();
                throw e;
            }
        }

        public GatewayState GetState()
        {
            return this.state;
        }


        private static GatewayInfo CreateMyInfo(StatelessServiceContext context)
        {
            var gatewayId = new GatewayId()
            {
                Name = context.NodeContext.NodeName,
                InstanceId = context.InstanceId.ToString()
            };


            var gatewayInfo = new GatewayInfo()
            {
                Id = gatewayId,
                ServiceName = context.ServiceName,
                PartitionId = context.PartitionId,
            };

            return gatewayInfo;
        }

        private void OnMonitoringFailed()
        {
            // fail the process which shoud terminate all connections
            ServiceEventSource.Current.ServiceMessage(this.Context, "Monitoring failed, exiting.");
            Environment.Exit(-1);
        }

        private static TimeSpan GetHeartbeatDuration(ICodePackageActivationContext context)
        {
            // TBD: load from configuration
            return TimeSpan.FromSeconds(60);
        }

        private static Uri GetMonitorServiceAddress(ICodePackageActivationContext context)
        {
            return new Uri(new Uri(context.ApplicationName), "GatewayMonitorService");
        }
    }
}
