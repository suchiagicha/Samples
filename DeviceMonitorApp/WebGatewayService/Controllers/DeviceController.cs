using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeviceActorService.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Samples.DeviceMonitor;
using Microsoft.ServiceFabric.Services.Remoting.Client;

namespace WebGatewayService.Controllers
{
    [Route("api/[controller]")]
    public class DeviceController : Controller
    {
        private  IGatewayMonitor gatewayMonitor_;
        private  string gatewayId;
        private  CancellationTokenSource _serviceDownToken;
        private ServiceContext context;
        public DeviceController(GatewayService gatewayService)
        {
            gatewayMonitor_ = gatewayService.gatewayMonitor_;
            _serviceDownToken = gatewayService.serviceDownToken;
            gatewayId = gatewayService.myInfo.Id.InstanceId;
            context = gatewayService.Context;
        }


        // Gets the List of  Connected Devices of this gateway 
        [HttpGet]
        public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
        {
            var result = await gatewayMonitor_.GetConnectedDevices(gatewayId,cancellationToken);
            if (result != null)
            {
                result.Add(this.gatewayId);
            }
            else
            {
                result = new List<string>() { this.gatewayId };
            }
            return Ok(result);
        }

        // Gets the List of  Connected Devices of  gateway Id specified
        [HttpGet("{id}")]
        // GET api/values
        public async Task<IActionResult> GetAsync(string id,CancellationToken cancellationToken)
        {
            var gatewayMonitor = GettheMonitorService(id);
              var result = await gatewayMonitor.GetConnectedDevices(id, cancellationToken);
            result.Add(id);
            return Ok(result);
        }

        // Gets the state of the device specified in deviceId.
        [HttpGet("state/{deviceId}")]

        public async Task<IActionResult> GetStateAsync(string deviceId, CancellationToken cancellationToken)
        {
            var proxy = ActorProxy.Create<IDeviceActorService>(new Microsoft.ServiceFabric.Actors.ActorId(deviceId), new Uri("fabric:/DeviceMonitorApp/DeviceActorServiceActorService"));
             var result = await proxy.GetState(cancellationToken);
            return Ok(result);
        }

        // Gets the time taken to disconnect this gateway.

        [HttpGet("perf/{gatewayId}")]

        public async Task<IActionResult> GetPerfAsync(string gatewayId, CancellationToken cancellationToken)
        {
            var gatewayMonitor = GettheMonitorService(gatewayId);
            var result = await gatewayMonitor.GetDisconnectedGatewayPerformance(gatewayId, cancellationToken); 
            return Ok(result);
        }

        // Connects the device to the current gateway
        [HttpPut("{Id}")]
        public async Task<ActionResult> PutAsync(string Id, CancellationToken cancellationToken) {
            try
            {
                await gatewayMonitor_.SubscribeAsync(Id, gatewayId, _serviceDownToken.Token);
            }catch(Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(context, "Exception while subscribing.{0}  {1}",e.StackTrace , e.InnerException);

            }
            return Ok();
        }



        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }


        private IGatewayMonitor GettheMonitorService(string gatewayId)
        {
            var nodeId = gatewayId.Substring(0,gatewayId.IndexOf("+"));
            return GatewayService.GetGatewaySupervisorServiceAsync(nodeId,false).Result;
        }

       

    }
}
