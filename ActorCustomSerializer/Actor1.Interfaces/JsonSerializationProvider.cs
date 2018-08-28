using Microsoft.ServiceFabric.Services.Remoting.V2;
using Microsoft.ServiceFabric.Services.Remoting.V2.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Actor1
{
    public class ServiceRemotingJsonSerializationProvider : IServiceRemotingMessageSerializationProvider
    {
        public IServiceRemotingMessageBodyFactory CreateMessageBodyFactory()
        {
            return new JsonMessageFactory();
        }

        public IServiceRemotingRequestMessageBodySerializer CreateRequestMessageSerializer(Type serviceInterfaceType, IEnumerable<Type> requestWrappedType, IEnumerable<Type> requestBodyTypes = null)
        {
            return new ServiceRemotingRequestJsonMessageBodySerializer();
        }

        public IServiceRemotingResponseMessageBodySerializer CreateResponseMessageSerializer(Type serviceInterfaceType, IEnumerable<Type> responseWrappedType, IEnumerable<Type> responseBodyTypes = null)
        {
            return new ServiceRemotingResponseJsonMessageBodySerializer();
        }
    }
    class JsonMessageFactory : IServiceRemotingMessageBodyFactory
    {

        public IServiceRemotingRequestMessageBody CreateRequest(string interfaceName, string methodName, int numberOfParameters, object wrappedRequestObject)
        {
            return new JsonBody(wrappedRequestObject);
        }

        public IServiceRemotingResponseMessageBody CreateResponse(string interfaceName, string methodName, object wrappedRequestObject)
        {
            return new JsonBody(wrappedRequestObject);
        }
    }

    class ServiceRemotingRequestJsonMessageBodySerializer : IServiceRemotingRequestMessageBodySerializer
    {
        private JsonSerializer serializer;

        public ServiceRemotingRequestJsonMessageBodySerializer()
        {
            serializer = JsonSerializer.Create(new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }

        public IOutgoingMessageBody Serialize(IServiceRemotingRequestMessageBody serviceRemotingRequestMessageBody)
        {
            if (serviceRemotingRequestMessageBody == null)
            {
                return null;
            }
            using (var writeStream = new MemoryStream())
            {
                using (var jsonWriter = new JsonTextWriter(new StreamWriter(writeStream)))
                {
                    serializer.Serialize(jsonWriter, serviceRemotingRequestMessageBody);
                    jsonWriter.Flush();
                    var bytes = writeStream.ToArray();
                    var segment = new ArraySegment<byte>(bytes);
                    var segments = new List<ArraySegment<byte>> { segment };
                    return new OutgoingMessageBody(segments);
                }
            }
        }

        public IServiceRemotingRequestMessageBody Deserialize(IIncomingMessageBody messageBody)
        {
            using (var sr = new StreamReader(messageBody.GetReceivedBuffer()))
            {
                using (JsonReader reader = new JsonTextReader(sr))
                {
                    var ob = serializer.Deserialize<JsonBody>(reader);
                    return ob;
                }
            }
        }
    }
    class ServiceRemotingResponseJsonMessageBodySerializer : IServiceRemotingResponseMessageBodySerializer
    {
        private JsonSerializer serializer;

        public ServiceRemotingResponseJsonMessageBodySerializer()
        {
            serializer = JsonSerializer.Create(new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }

        public IOutgoingMessageBody Serialize(IServiceRemotingResponseMessageBody responseMessageBody)
        {
            if (responseMessageBody == null)
            {
                return null;
            }

            using (var writeStream = new MemoryStream())
            {
                using (var jsonWriter = new JsonTextWriter(new StreamWriter(writeStream)))
                {
                    serializer.Serialize(jsonWriter, responseMessageBody);
                    jsonWriter.Flush();
                    var bytes = writeStream.ToArray();
                    var segment = new ArraySegment<byte>(bytes);
                    var segments = new List<ArraySegment<byte>> { segment };
                    return new OutgoingMessageBody(segments);
                }
            }
        }

        public IServiceRemotingResponseMessageBody Deserialize(IIncomingMessageBody messageBody)
        {

            using (var sr = new StreamReader(messageBody.GetReceivedBuffer()))
            {
                using (var reader = new JsonTextReader(sr))
                {
                    var obj = serializer.Deserialize<JsonBody>(reader);
                    return obj;
                }
            }
        }
    }

    class JsonBody : WrappedMessage, IServiceRemotingRequestMessageBody, IServiceRemotingResponseMessageBody
    {
        public JsonBody(object wrapped)
        {
            this.Value = wrapped;
        }

        public void SetParameter(int position, string parameName, object parameter)
        {  //Not Needed if you are using WrappedMessage
            throw new NotImplementedException();
        }

        public object GetParameter(int position, string parameName, Type paramType)
        {
            //Not Needed if you are using WrappedMessage
            throw new NotImplementedException();
        }

        public void Set(object response)
        { //Not Needed if you are using WrappedMessage
            throw new NotImplementedException();
        }

        public object Get(Type paramType)
        {  //Not Needed if you are using WrappedMessage
            throw new NotImplementedException();
        }
    }
}
