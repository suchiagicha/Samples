using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Samples.DeviceMonitor;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using DeviceActorService.Interfaces;
using System.Diagnostics;

namespace GatewayMonitorService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class GatewayMonitorService : StatefulService, IGatewayMonitor
    {
        private static string GATEWAYINFODIC = "GATEWAYINFODIC";
        private static string GATEWAYPERFDIC = "GATEWAYPERFDIC";

        private static string GATEWAY_PING_DIC = "GATEWAY_PING_DIC";
        private static long PINGINTERVAL = TimeSpan.FromMinutes(2).Ticks;
        private static  string DEVICEINFO = "DEVICEINFO";
        private ConcurrentDictionary<string, Task> disconnectedGateway;
        private ConcurrentDictionary<string, GatewayMemoryInfo> gatewayMemoryInfoStore;
        private TaskCompletionSource<bool> dctaskafterFailover;
        private TaskCompletionSource<bool> updategatewayInfo;
        //String is immutable so we dont need interlock here but there can be a case where we are reading old value.Which is fine because in our case same gateway wont be up again.
        private string gatewayRunningId;

        public GatewayMonitorService(StatefulServiceContext context)
            : base(context)
        {
            this.gatewayRunningId = string.Empty;
            this.disconnectedGateway = new ConcurrentDictionary<string, Task>();
            this.gatewayMemoryInfoStore = new ConcurrentDictionary<string, GatewayMemoryInfo>();
            this.dctaskafterFailover = new TaskCompletionSource<bool>();
            this.updategatewayInfo = new TaskCompletionSource<bool>();
        }

        public async Task HeartbeatAsync(GatewayId gatewayId, CancellationToken cancellationToken)
        {
            //Check if Gateway is Running. If not send an Error Msg. Error in Gateway should mean it should go down.(ReportFault api can be used for Gateway to go down.)
            await EnsureGatewayRunningAsync(gatewayId.InstanceId,true);
            //If yes Adds Gateway Info in RD
            //Get Dictionary
            //Add GatewayInformation.
            var stateDictionary = await GetGatewayPingStatusDictionaryAsync();
            var infodic = await this.GetGatewayInformationDictionaryAsync();
            using (var tx = StateManager.CreateTransaction())
            {
                var ifexists = await infodic.ContainsKeyAsync(tx, gatewayId.InstanceId);
                if (!ifexists)
                {   //Dispose call tx abort
                    throw new InvalidOperationException("GatewayInfo has not been Initialized");
                }
                var time = DateTime.UtcNow.Ticks;

                await stateDictionary.AddOrUpdateAsync(tx, gatewayId.InstanceId, time, (k, v) => time);
                await tx.CommitAsync();
            }
            
            this.gatewayRunningId = gatewayId.InstanceId;
        }

        private async Task EnsureGatewayRunningAsync(string gatewayId,bool isHeartBeatRequest)
        {
            if (string.IsNullOrEmpty(this.gatewayRunningId))
            {
                HasDisconnectedTaskFinish();
            }

            //It is not a running gateway
            if (!gatewayId.Equals(gatewayRunningId))
            {
                if (isHeartBeatRequest)
                {
                    //This check should be performed for first heartbeat request for that gateway.or after the failover.
                    //check if it is part of dc gateway. This is to make sure that its not a new gateway instance
                    //first check if dc task has been completed at least once after failover;                
                    HasDisconnectedTaskFinish();
                    if (this.disconnectedGateway.ContainsKey(gatewayId))
                    {
                        throw new InvalidOperationException("Gateway is already down");
                    }
                }
                else
                {
                    
                    throw new InvalidOperationException("Gateway is already down");
                }
          
            }
        }

        private void HasDisconnectedTaskFinish()
        {
            if (!this.dctaskafterFailover.Task.IsCompleted)
            {
                throw new FabricTransientException("Service Is Not yet Ready");
            }
        }


        public async Task InitializeAsync(GatewayInfo gatewayInfo, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            //Add GatewayInfo in RD with key as GatewayID
            var stateDictionary = await GetGatewayInformationDictionaryAsync().ConfigureAwait(false);
            using (var tx = StateManager.CreateTransaction())
            {
                await stateDictionary.AddOrUpdateAsync(tx, gatewayInfo.Id.InstanceId, gatewayInfo, (k, v) => gatewayInfo);
                await tx.CommitAsync();
            }
            //update  after gateway info store is updated.
            EnsureServiceIsReady();

            var currentVal = this.gatewayMemoryInfoStore.GetOrAdd(gatewayInfo.Id.InstanceId, (key) =>
            {
                var info = new GatewayMemoryInfo(0, 0);
                return info;
            });
            currentVal.resetEvent.TrySetResult(true);
        }

        private void EnsureServiceIsReady()
        {
            if (!IsServiceReady)
            {
                throw new FabricTransientException("Service Is Not Yet Ready");
            }
        }

        public bool IsServiceReady
        {
            get { return (this.updategatewayInfo != null && this.updategatewayInfo.Task.IsCompleted); }
        }


        public async Task<bool> SubscribeAsync(string deviceId,string gatewayId, CancellationToken token)
        {
            //Add Device to Store.
            //Check if Recovery Task is not going on to get the last item from RD
            //1 . Gte the last key
            // 2 Increment the key
            //3  Add or Update Async  with increment key. Add Retry Policy 
            //4 

            //4
            await EnsureGatewayRunningAsync(gatewayId,false);

            var lastKey = await this.GetNextKeyToBeAdded(gatewayId);
            //Add the data.
            var devicedic =  await this.GetDeviceDictionaryAsync(gatewayId);
            using (var tx = StateManager.CreateTransaction())
            {
                await devicedic.TryAddAsync(tx, lastKey, deviceId);
                var proxy = ActorProxy.Create<IDeviceActorService>(new Microsoft.ServiceFabric.Actors.ActorId(deviceId),new Uri("fabric:/DeviceMonitorApp/DeviceActorServiceActorService"));
                await proxy.OnConnect(CancellationToken.None);
                await tx.CommitAsync();
            }
            return true; ;
        }


        public async Task<List<string>> GetConnectedDevices(string gatewayId, CancellationToken token)
        {
            List<string> devices = new List<string>();
            var devicedic = await this.GetDeviceDictionaryAsync(gatewayId);
            using (var tx = base.StateManager.CreateTransaction())
            {
                var enumerable = await devicedic.CreateEnumerableAsync(tx).ConfigureAwait(false);
                var asyncEnumerable = enumerable.GetAsyncEnumerator();

                while (await asyncEnumerable.MoveNextAsync(token))
                {
                    devices.Add(asyncEnumerable.Current.Value);
                }
            }
            return devices; 
        }


        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        ///Notes :

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
           try
            {
                List<Task> tasks = new List<Task>();

                cancellationToken.ThrowIfCancellationRequested();
                var updateGateWayTask = UpdateGateWayInfo(cancellationToken).ContinueWith(t => this.updategatewayInfo.TrySetResult(true));
                //start the task for checking if there is an dc gateway.
                tasks.Add(updateGateWayTask);
                var heartbeattask = this.SuperviceGatewayHeartBeatAsync(cancellationToken);
                tasks.Add(heartbeattask);
                Task.WaitAll(tasks.ToArray(), cancellationToken);
            }
            catch(Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Exception win RunAsync {0}", e.StackTrace);

            }


        }

        private async Task UpdateGateWayInfo(CancellationToken cancellationToken)
        {
            var myDictionary = await this.GetGatewayInformationDictionaryAsync();
            List<Task> tasks = new List<Task>();

            using (var tx = base.StateManager.CreateTransaction())
            {
                var enumerable = await myDictionary.CreateKeyEnumerableAsync(tx).ConfigureAwait(false);
                var asyncEnumerable = enumerable.GetAsyncEnumerator();

                while (await asyncEnumerable.MoveNextAsync(cancellationToken))
                {
                    var task = Task.Run(() => this.FillUpdateKeyInfoAsync(asyncEnumerable.Current, cancellationToken));
                    tasks.Add(task);
                }
            }

            Task.WaitAll (tasks.ToArray(),cancellationToken);
        }

        private async Task FillUpdateKeyInfoAsync(string gatewayId, CancellationToken cancellationToken = default(CancellationToken))
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "FillUpdateKeyInfoAsync for gateway {0}",gatewayId);

            var myDictionary = await this.GetDeviceDictionaryAsync(gatewayId);
            bool firstKey = true;
            long startKey = 0;
            long lastKey = 0;

            using (var tx = base.StateManager.CreateTransaction())
            {
                var enumerable = await myDictionary.CreateKeyEnumerableAsync(tx).ConfigureAwait(false);
                var asyncEnumerable = enumerable.GetAsyncEnumerator();

                while (await asyncEnumerable.MoveNextAsync(cancellationToken))
                {
                    if (firstKey)
                    {
                        startKey = asyncEnumerable.Current-1;
                        lastKey = asyncEnumerable.Current;
                        firstKey = false;
                    }
                    else
                    {
                        lastKey = asyncEnumerable.Current;
                    }
                }
               var currentVal = this.gatewayMemoryInfoStore.GetOrAdd(gatewayId, (key) =>
                {
                    var info = new GatewayMemoryInfo(lastKey,startKey);
                    return info;
                });
                currentVal.resetEvent.TrySetResult(true);
                ServiceEventSource.Current.ServiceMessage(this.Context, "FillUpdateKeyInfoAsync completed for gateway {0} with First and LastKey {1}  {2}", gatewayId, startKey, lastKey);
            }
        }

        private async Task SuperviceGatewayHeartBeatAsync(CancellationToken cancellationToken)
        {
            //1 Iterate through HeartBeat Dic and check if there is any DC gateway.
            ServiceEventSource.Current.ServiceMessage(this.Context, "SuperviceGatewayHeartBeatAsync started");
            while (true)
            {
                var heartbeatdic = await this.GetGatewayPingStatusDictionaryAsync();
                using (var tx = base.StateManager.CreateTransaction())
                {
                    var enumerable = await heartbeatdic.CreateEnumerableAsync(tx).ConfigureAwait(false);
                    var asyncEnumerable = enumerable.GetAsyncEnumerator();
                    while (await asyncEnumerable.MoveNextAsync(cancellationToken))
                    {
                        var lastPing = asyncEnumerable.Current.Value;

                        if (lastPing == null)
                        {
                            continue;
                        }

                        var now = DateTime.UtcNow.Ticks;
                        var timeBetweenPings = now - lastPing;
                        string gatewayId = asyncEnumerable.Current.Key;
                        if (timeBetweenPings > PINGINTERVAL)
                        {
                            //Dc the gateway
                            //if yes..add that info in DC gateway dic.
                            await AddGatewayToDisconnectedGateWayStoreAsync(gatewayId, cancellationToken);
                            if (this.gatewayRunningId.Equals(gatewayId))
                            {
                                this.gatewayRunningId = string.Empty;
                            }

                            ServiceEventSource.Current.ServiceMessage(this.Context, " Disconnected the  Gateway {0}", gatewayId);
                        }
                        else
                        {//We are assuming there will be only 1 Connected Gateway.
                            this.gatewayRunningId = gatewayId;
                            ServiceEventSource.Current.ServiceMessage(this.Context, "Connected gateway {0}", gatewayId);

                        }
                    }
                }
                //This check is to not trysetResult true multiple times;
                if (!this.dctaskafterFailover.Task.IsCompleted)
                {
                    this.dctaskafterFailover.TrySetResult(true);
                }
                
                //TODO: Check Supervision Interval From HoneyWell

                await Task.Delay(TimeSpan.FromMinutes(2));
            }

        }

        private async Task AddGatewayToDisconnectedGateWayStoreAsync(string gatewayId,CancellationToken cancellationToken)
        {
            //2  Start the task for each dc gateway which is not in-memory dc and then add in inmory.
            await this.disconnectedGateway.GetOrAdd(gatewayId, (Id)=> { return this.CreateTask(Id, cancellationToken); });
        }

        private Task CreateTask(string gatewayId,CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                bool completedsuccessfully = false;
                while (!completedsuccessfully)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Cancelling Disconnect Task {0}", gatewayId);

                        return;
                    }
                    try
                    {
                        await DisconnectDevicesAsync(gatewayId,cancellationToken);
                        completedsuccessfully = true;
                    }
                    catch (Exception e)
                    {
                        //We are retrying on all Exceptions 
                        completedsuccessfully = false;
                        //Reset the KeystoProcess
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Exception while disconnect {0} {1}", gatewayId, e.StackTrace);
                        await UpdateTheFirstKeyAsync(gatewayId,cancellationToken);

                    }
                }
            }
            );
        }

        async Task UpdateTheFirstKeyAsync(string gatewayId,CancellationToken cancellationToken)
        {
            var firstKey = await GettheFirstKey(gatewayId, cancellationToken);
            GatewayMemoryInfo memoryInfo;
            this.gatewayMemoryInfoStore.TryGetValue(gatewayId, out memoryInfo);
            ServiceEventSource.Current.ServiceMessage(this.Context, "Updating FirstKey for Gateway {0} {1}", gatewayId, firstKey);
            memoryInfo.UpdateLastKeyProcessed(firstKey);
        }

        private async Task DisconnectDevicesAsync(string gatewayId, CancellationToken cancellationToken = default(CancellationToken))
        {

            ServiceEventSource.Current.ServiceMessage(this.Context, "Running Disconnect Gateway Logic for {0}",gatewayId);
            var stopwatch = Stopwatch.StartNew();
            //Disconnect Device
            //1  Get the last Key processed and increment it
            //2 check if  the Item is in RD . If item not found increment the key and check. 
            //3 .  Process the Item. 
            //4 Remove from the RD
            //Create 35 task.
            List<Task> tasks = new List<Task>();
            for(int i = 0; i < 70; i++)
            {
                tasks.Add(Task.Run(async ()=>{
                    try
                    {
                        await DisconnectDevice(gatewayId,cancellationToken);
                    }
                    catch(Exception e)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "Exception while disconnect Task  {0} {1}", gatewayId, e.StackTrace);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray(), cancellationToken);
            stopwatch.Stop();
            //Add check to see if deviceDic is empty
            var isEmpty = await this.CheckIfDeviceDicIsEmpty(gatewayId);
            if (!isEmpty)
            {
                var lastKey = await this.GettheLastKey(gatewayId,cancellationToken);
                //Get the lastKey
                ServiceEventSource.Current.ServiceMessage(this.Context, "Disconnected logic  had issue  gateway {0} , lastKey : {1} ", gatewayId, lastKey);
                //Higher layer will restart it again.
                throw new InvalidOperationException();
            }

            //Remove the gateway from heartbeat and info dic
            var heartbeatdic = await this.GetGatewayPingStatusDictionaryAsync();
            var infodic = await this.GetGatewayInformationDictionaryAsync();
            using (var tx = base.StateManager.CreateTransaction())
            {
                await heartbeatdic.TryRemoveAsync(tx, gatewayId);
                await infodic.TryRemoveAsync(tx, gatewayId);
                await tx.CommitAsync();
            }
            var totaltime = stopwatch.Elapsed;
            var perfdic = await this.GetDisconnectedGatewayPerformanceAsync();
            bool updateres = false;
            using (var tx = base.StateManager.CreateTransaction())
            {
                 updateres = await perfdic.TryAddAsync(tx, gatewayId, totaltime.TotalMilliseconds);
                await tx.CommitAsync();
            }
            ServiceEventSource.Current.ServiceMessage(this.Context, "Completed Disconnect Gateway Logic for {0} , Time Taken : {1} , UpdateResult {2}", gatewayId,totaltime.TotalMilliseconds, updateres);

        }

        private async Task DisconnectDevice(string gatewayId,CancellationToken cancellationToken)
        {
            int retryCount = 0;
            //Each Task work
            while (true)
            {
                var KeyToProcess = await this.GetLastKeyProcessedUnderLock(gatewayId);
                var deviceId = await GetDeviceId(gatewayId, KeyToProcess);
                if (!string.IsNullOrEmpty(deviceId))
                {
                    var proxy = ActorProxy.Create<IDeviceActorService>(new Microsoft.ServiceFabric.Actors.ActorId(deviceId), new Uri("fabric:/DeviceMonitorApp/DeviceActorServiceActorService"));
                    await proxy.OnDisconnect(CancellationToken.None);
                    //remove from RD after call succeds
                    var devicedic = await this.GetDeviceDictionaryAsync(gatewayId);
                    using (var tx = base.StateManager.CreateTransaction())
                    {
                        var success = await devicedic.TryRemoveAsync(tx, KeyToProcess);
                        if (!success.HasValue)
                        {
                            //Retry Policy to remove it again.
                        }
                        await tx.CommitAsync();
                    }
                }
                else
                {
                    //check if Device Dic has items.
                    var res = await this.CheckIfDeviceDicIsEmpty(gatewayId);
                    if (res)
                    {
                        //break the loop 
                        break;
                    }
                    else
                    {//Increment and check for next item.
                        retryCount++;
                        if(retryCount> 3)
                        {
                            var lastKey = await GettheLastKey(gatewayId,cancellationToken);
                            if (KeyToProcess > lastKey)
                            {//This wil happen if the remianing keys are being processed by other threads.
                                break;
                            }
                        }
                        continue;
                    }
                }
            }
        }

        private async Task<long> GettheLastKey(string gatewayId,CancellationToken cancellationToken)
        {
            var myDictionary = await this.GetDeviceDictionaryAsync(gatewayId);
         
            long lastKey = 0;

            using (var tx = base.StateManager.CreateTransaction())
            {
                var enumerable = await myDictionary.CreateKeyEnumerableAsync(tx).ConfigureAwait(false);
                var asyncEnumerable = enumerable.GetAsyncEnumerator();

                while (await asyncEnumerable.MoveNextAsync(cancellationToken))
                {
                 lastKey = asyncEnumerable.Current;
                }
            }

            return lastKey;
        }

        private async Task<long> GettheFirstKey(string gatewayId,CancellationToken cancellationToken)
        {
            var myDictionary = await this.GetDeviceDictionaryAsync(gatewayId);

            long firstKey = 0;

            using (var tx = base.StateManager.CreateTransaction())
            {
                var enumerable = await myDictionary.CreateKeyEnumerableAsync(tx).ConfigureAwait(false);
                var asyncEnumerable = enumerable.GetAsyncEnumerator();

                if(await asyncEnumerable.MoveNextAsync(cancellationToken))
                {
                    firstKey = asyncEnumerable.Current;
                }
            }

            return firstKey;
        }

        private async Task<bool> CheckIfDeviceDicIsEmpty(string gatewayId)
        {
            var devicedic = await this.GetDeviceDictionaryAsync(gatewayId);
            return devicedic.Count <=0;            
        }

        private async Task<string> GetDeviceId(string gatewayId, long key)
        {
            var devicedic = await this.GetDeviceDictionaryAsync(gatewayId);
            using (var tx = base.StateManager.CreateTransaction())
            {
                var val = await devicedic.TryGetValueAsync(tx, key);
                if (val.HasValue)
                {
                    return val.Value;
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        private async Task<long> GetLastKeyProcessedUnderLock(string gatewayId)
        {
            EnsureServiceIsReady();
            GatewayMemoryInfo memoryInfo;
            this.gatewayMemoryInfoStore.TryGetValue(gatewayId, out memoryInfo);
            await memoryInfo.resetEvent.Task;
            return memoryInfo.IncrementNextKeyToProcess();
        }

        private async Task<long> GetNextKeyToBeAdded(string gatewayId)
        {
            EnsureServiceIsReady();
            GatewayMemoryInfo memoryInfo;
            this.gatewayMemoryInfoStore.TryGetValue(gatewayId, out memoryInfo);
            await memoryInfo.resetEvent.Task;
            return memoryInfo.IncrementNextKeyToBeAdded();
        }


        private async Task<IReliableDictionary2<string, GatewayInfo>> GetGatewayInformationDictionaryAsync()
        {
             return await StateManager.GetOrAddAsync
                <IReliableDictionary2<string, GatewayInfo>>(GATEWAYINFODIC)
                .ConfigureAwait(false);
        }

        private async Task<IReliableDictionary2<string, long>> GetGatewayPingStatusDictionaryAsync()
        {
            return await StateManager.GetOrAddAsync
                <IReliableDictionary2<string, long>>(GATEWAY_PING_DIC)
                .ConfigureAwait(false);
        }
        //Key,DeviceId
        private async Task<IReliableDictionary2<long, string>> GetDeviceDictionaryAsync(string gatewayId)
        {
            var dicname = String.Concat(DEVICEINFO, gatewayId);
            return await StateManager.GetOrAddAsync
                <IReliableDictionary2<long, string>>(dicname)
                .ConfigureAwait(false);
        }

        private async Task<IReliableDictionary2<string, double>> GetDisconnectedGatewayPerformanceAsync()
        {
            return await StateManager.GetOrAddAsync
               <IReliableDictionary2<string, double>>(GATEWAYPERFDIC)
               .ConfigureAwait(false);
        }

        public async Task<double> GetDisconnectedGatewayPerformance(string gatewayId, CancellationToken token)
        {
            var perfdic = await this.GetDisconnectedGatewayPerformanceAsync();
            using (var tx = StateManager.CreateTransaction())
            {
                var val =  await perfdic.TryGetValueAsync(tx, gatewayId);
                if (val.HasValue)
                {
                    return val.Value;
                }
                else
                {
                    return 0;
                }
            }
        }
    }
}

class GatewayMemoryInfo
{
    public GatewayMemoryInfo(
        long lastKeyAdded,
        long lastKeyProcessed)
    {
        this.lastKeyAdded = lastKeyAdded;
        this.lastKeyProcessed = lastKeyProcessed;
        this.resetEvent = new TaskCompletionSource<bool>();
    }

    private long lastKeyAdded;
    private long lastKeyProcessed;

    public long IncrementNextKeyToProcess()
    {
        return Interlocked.Increment(ref this.lastKeyProcessed);
    }

    public long UpdateLastKeyProcessed(long lastKeyProcessed)
    {
        return Interlocked.Exchange(ref this.lastKeyProcessed, lastKeyProcessed);
    }
    public long IncrementNextKeyToBeAdded()
    {
        return Interlocked.Increment(ref lastKeyAdded);
    }


    public TaskCompletionSource<bool> resetEvent;
}
