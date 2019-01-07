using System;

namespace Microsoft.ServiceFabric.Samples.DeviceMonitor
{
    public class GatewayInfo
    {
        public GatewayId Id { get; set; }
        //TODO: Not Sure why we need ServiceName
        public Uri ServiceName { get; set; }
        //TODO: Not Sure why we need PartitionId
        public Guid PartitionId { get; set; }

        public string IpAddress { get; set; }
    }
}
