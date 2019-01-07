namespace WebGatewayService
{
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Samples.DeviceMonitor;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using WebGatewayService;

    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    public sealed class GatewayService : StatelessService
    {
        volatile private GatewayState state;
        internal  IGatewayMonitor gatewayMonitor_;
        internal  CancellationTokenSource serviceDownToken;
        private TaskCompletionSource<bool>  monitorCreationTask;
        private readonly TimeSpan heartbeatInterval;
        internal readonly GatewayInfo myInfo;
        static HttpClient client = new HttpClient();

        private readonly Uri monitorServiceAddress;
        private readonly ServicePartitionKey monitorServicePartitionKey;

        public GatewayService(StatelessServiceContext context)
            : base(context)
        {
            this.state = GatewayState.Created;
            this.myInfo = CreateMyInfo(context);
            serviceDownToken = new CancellationTokenSource();
            this.heartbeatInterval = GetHeartbeatDuration(context.CodePackageActivationContext);
            this.monitorServiceAddress = GetMonitorServiceAddress(context.CodePackageActivationContext);
            this.monitorServicePartitionKey = new ServicePartitionKey(this.myInfo.Id.Name);
            this.monitorCreationTask = new TaskCompletionSource<bool>();

        }

        protected override async Task OnOpenAsync(CancellationToken cancellationToken)
        {
            var NodeId = FabricRuntime.GetNodeContext().NodeId.ToString();

            gatewayMonitor_ = await GetGatewaySupervisorServiceAsync(NodeId,true);
            this.monitorCreationTask.TrySetResult(true);
        }



        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services =>{
                                        services
                                            .AddSingleton<StatelessServiceContext>(serviceContext);
                                            services.AddSingleton<GatewayService>(this);
                                            } )
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }

        protected override Task OnCloseAsync(CancellationToken cancellationToken)
        {
            serviceDownToken.Cancel();
            return base.OnCloseAsync(cancellationToken);
        }
        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await InitializeMonitoring(cancellationToken);
            var firsttym = true;
            Task subsribetask = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                await SendHeartbeatAsync(cancellationToken);
                if (firsttym)
                {
                    firsttym = false;
                     subsribetask = Task.Run(async () => { await SubscribeDevices(cancellationToken); });
                }

                await Task.Delay(this.heartbeatInterval, cancellationToken);
            }
            await subsribetask;
        }

        private Task SubscribeDevices(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "Starting SubscribeDevices ");
            int requestCount = 0;
            while (true)
            {
                List<Task> tasks = new List<Task>();
                for(int i = 0; i < 50; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    var deviceId = Guid.NewGuid().ToString();
                    requestCount++;
                    tasks.Add(this.SubscribeDevice(deviceId));
                }

                Task.WaitAll(tasks.ToArray(), cancellationToken);
                if (requestCount > 100000)
                {
                    break;
                }
            }

            ServiceEventSource.Current.ServiceMessage(this.Context, "Completed SubscribeDevices for ", requestCount);
            return Task.CompletedTask;
        }
        private async Task InitializeMonitoring(CancellationToken cancellationToken)
        {
            await this.monitorCreationTask.Task;

            this.state = GatewayState.Initializing;

            try
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Initializing monitoring {0}", this.myInfo.Id.InstanceId);                

                await gatewayMonitor_.InitializeAsync(
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
            {var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    new CancellationTokenSource(heartbeatInterval).Token);
             
                await gatewayMonitor_.HeartbeatAsync(this.myInfo.Id, cts.Token);

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
                InstanceId = String.Concat(context.NodeContext.NodeId.ToString(),
                "+", Guid.NewGuid().ToString())
                
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
            return TimeSpan.FromSeconds(30);
        }

        private static Uri GetMonitorServiceAddress(ICodePackageActivationContext context)
        {
            return new Uri(new Uri(context.ApplicationName), "GatewayMonitorService");
        }

        internal static  async Task<IGatewayMonitor> GetGatewaySupervisorServiceAsync(string nodeId,bool createIfnotExist)
        {
            var appName = "fabric:/DeviceMonitorApp";
            var serviceName = $"{appName}/{nodeId}";
            var fabricClient = new FabricClient();
            var serviceAlreadyExists = false;
            try
            {
                var services = await fabricClient.QueryManager.GetServiceListAsync(new Uri(appName));
                serviceAlreadyExists = services.Select(s => s.ServiceName).Contains(new Uri(serviceName));
            }
            catch (FabricElementNotFoundException) { }
                
            if (!serviceAlreadyExists && createIfnotExist)
            {
                StatefulServiceDescription serviceDescription = new StatefulServiceDescription();
                serviceDescription.ApplicationName = new Uri(appName);
                serviceDescription.PartitionSchemeDescription = new SingletonPartitionSchemeDescription();
                serviceDescription.ServiceName = new Uri(serviceName);
                serviceDescription.ServiceTypeName = "GatewayMonitorServiceType";
                serviceDescription.HasPersistedState = true;
                serviceDescription.MinReplicaSetSize = 3;
                serviceDescription.TargetReplicaSetSize = 3;
                await fabricClient.ServiceManager.CreateServiceAsync(serviceDescription);
                //TODO:Call GetService and then get partition instead of delay
                await Task.Delay(TimeSpan.FromSeconds(30));                
               
            }
            else if(!serviceAlreadyExists)
            {
                throw new  ArgumentException("Invalid Arguments");
            }
            return ServiceProxy.Create<IGatewayMonitor>(new Uri(serviceName));
        }

         async Task SubscribeDevice(string deviceId)
        {
            HttpResponseMessage response = await client.PutAsJsonAsync(
            $"http://localhost:8469/api/device/{deviceId}", deviceId);
            response.EnsureSuccessStatusCode();
        }
    }
}
