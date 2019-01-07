

namespace Microsoft.ServiceFabric.Samples.DeviceMonitor
{
    using Microsoft.ServiceFabric.Services.Remoting;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IGatewayMonitor : IService
    {
        /// <summary>
        /// Establish monitoring relationship through one way heartbeat messages to the monitor.
        /// </summary>
        /// <param name="gatewayInfo">Information about the gateway.</param>
        /// <param name="heartbeatInterval">Interval between the heartbeats. If the monitor does not receive the heartbeat it considers the gateway as being failed.</param>
        /// <param name="cancellationToken">Token to cancel outstanding operation.</param>
        /// <returns>Task representing the async operation.</returns>
        Task InitializeAsync(GatewayInfo gatewayInfo, TimeSpan leaseDuration, CancellationToken cancellationToken);


        /// <summary>
        /// Send heartbeat message to the monitor.
        /// </summary>
        /// <param name="gatewayId">Id of the gateway being monitored.</param>
        /// <param name="cancellationToken">Token to cancel outstanding operation.</param>
        /// <returns>Task representing the async operation.</returns>
        Task HeartbeatAsync(GatewayId gatewayId, CancellationToken cancellationToken);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        Task<bool> SubscribeAsync(string deviceId, string gatewayId,CancellationToken token);

        Task<List<string>> GetConnectedDevices(string gatewayId, CancellationToken token);

        Task<double> GetDisconnectedGatewayPerformance(string gatewayId, CancellationToken token);


    }
}
