using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Remoting.FabricTransport;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.Builder;
using System.Runtime.Serialization;

[assembly: FabricTransportActorRemotingProvider(RemotingListenerVersion =RemotingListenerVersion.V2_1,RemotingClientVersion =RemotingClientVersion.V2_1)]
//[assembly:CodeBuilder(EnableDebugging =true)]
namespace Actor1.Interfaces
{
    /// <summary>
    /// This interface defines the methods exposed by an actor.
    /// Clients use this interface to interact with the actor that implements it.
    /// </summary>
    /// 
    public interface IActor1 : IActor
    {
        /// <summary>
        /// TODO: Replace with your own actor method.
        /// </summary>
        /// <returns></returns>
        Task<BaseType> GetMessageAsync(CancellationToken cancellationToken);

        /// <summary>
        /// TODO: Replace with your own actor method.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        Task SetMessageAsync(BaseType message, CancellationToken cancellationToken);


    }

    public class BaseType
    {
        public int j = 50;
    }
}
