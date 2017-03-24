// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Calculator.Service
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.ServiceModel.Description;
    using Calculator.Common;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Communication.Wcf;
    using Microsoft.ServiceFabric.Services.Communication.Wcf.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class Service : StatelessService
    {
        public Service(StatelessServiceContext context)
            : base(context)
        {
        }


        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(
                    this.CreateWCFListenerForAdd,
                    "Test1"),
                 new ServiceInstanceListener(
                    this.CreateWCFListenerForSubtract,
                    "Test2"),

            };
        }

        private ICommunicationListener CreateWCFListenerForAdd(StatelessServiceContext context)
        {
            var bindings = WcfUtility.CreateTcpListenerBinding();
            var listener = new WcfCommunicationListener<IAdd>(
                context,
                new AddService(context),
                bindings,
                new EndpointAddress("net.tcp://localhost:8085/Services/Tests1"));
            ServiceMetadataBehavior metaDataBehavior = new ServiceMetadataBehavior();
            listener.ServiceHost.Description.Behaviors.Add(metaDataBehavior);
            Binding mexBinding = MetadataExchangeBindings.CreateMexTcpBinding();
            listener.ServiceHost.AddServiceEndpoint(typeof(IMetadataExchange), mexBinding,
                "net.tcp://localhost:8086/Services/Tests1/mex", new Uri("net.tcp://localhost:8086/Services/Tests1/mex"));
            return listener;
        }

        private ICommunicationListener CreateWCFListenerForSubtract(ServiceContext context)
        {
            var bindings = WcfUtility.CreateTcpListenerBinding();
            var listener = new WcfCommunicationListener<ISubtract>(
                context,
                new SubtarctService(context),
                bindings,
               new EndpointAddress("net.tcp://localhost:8085/Services/Tests2"));
            
            ServiceMetadataBehavior metaDataBehavior = new ServiceMetadataBehavior();
            listener.ServiceHost.Description.Behaviors.Add(metaDataBehavior);
            Binding mexBinding = MetadataExchangeBindings.CreateMexTcpBinding();
            listener.ServiceHost.AddServiceEndpoint(typeof(IMetadataExchange), mexBinding,
                "net.tcp://localhost:8086/Services/Tests2/mex", new Uri("net.tcp://localhost:8086/Services/Tests2/mex"));
            return listener;
        }
    }
    
}