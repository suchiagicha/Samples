// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Calculator.Client
{
    using System;
    using System.ServiceModel;
    using System.Threading.Tasks;
    using Calculator.Common;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Communication.Wcf;
    using Microsoft.ServiceFabric.Services.Communication.Wcf.Client;
    using ServiceReference1;
    using ServiceReference2;
    internal class Program
    {
        private static void Main(string[] args)
        {
            var client = new AddClient();
            Console.WriteLine("Add Api Call" + client.AddAsync(2, 3).GetAwaiter().GetResult());
            var client2 = new SubtractClient();
            Console.WriteLine("Subtract Api Call" + client2.SubtractAsync(2, 3).GetAwaiter().GetResult());
            Console.WriteLine("Add Api Call" + client.AddAsync(2, 3).GetAwaiter().GetResult());
            Console.WriteLine("Subtract Api Call" + client2.SubtractAsync(2, 3).GetAwaiter().GetResult());
        }
    }

}