using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Remoting.Client;
using Microsoft.ServiceFabric.Actors.Client;
using Actor1.Interfaces;
using System.Threading;
using Actor1;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;

namespace ConsoleApp1
{
    using Microsoft.ServiceFabric.Actors.Remoting.V2;
    using Microsoft.ServiceFabric.Actors.Remoting.V2.FabricTransport.Client;
    using Microsoft.ServiceFabric.Services.Remoting.V1;

    class Program
    {
        static  void Main(string[] args)
        {
            ActorId actorId = ActorId.CreateRandom();

            var proxyFactory = new ActorProxyFactory((c) =>
            {
                return new FabricTransportActorRemotingClientFactory(
                    fabricTransportRemotingSettings: null,
                    callbackMessageHandler: c,
                    serializationProvider: new ServiceRemotingJsonSerializationProvider());
            });

            var proxy = proxyFactory.CreateActorProxy<IActor1>(actorId, "fabric:/Application7");
            var msg = new BaseType();
            msg.j = 15;
            proxy.SetMessageAsync(msg, CancellationToken.None).GetAwaiter().GetResult();
            var res = proxy.GetMessageAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine(res.j);
        }
    }
}
