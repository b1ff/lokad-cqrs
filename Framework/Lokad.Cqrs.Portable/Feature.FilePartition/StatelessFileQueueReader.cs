using System;
using System.IO;
using System.Linq;
using Lokad.Cqrs.Core.Inbox;
using Lokad.Cqrs.Core.Inbox.Events;

namespace Lokad.Cqrs.Feature.FilePartition
{
    public sealed class StatelessFileQueueReader
    {
        readonly IEnvelopeStreamer _streamer;
        readonly ISystemObserver _observer;

        readonly Lazy<DirectoryInfo> _posionQueue;
        readonly DirectoryInfo _queue;
        readonly string _queueName;

        public string Name
        {
            get { return _queueName; }
        }

        public StatelessFileQueueReader(IEnvelopeStreamer streamer, ISystemObserver observer, Lazy<DirectoryInfo> posionQueue, DirectoryInfo queue, string queueName)
        {
            _streamer = streamer;
            _observer = observer;
            _posionQueue = posionQueue;
            _queue = queue;
            _queueName = queueName;
        }

        public GetEnvelopeResult TryGetMessage()
        {
            FileInfo message;
            try
            {
                message = _queue.EnumerateFiles().FirstOrDefault();
            }
            catch (Exception ex)
            {
                _observer.Notify(new FailedToReadMessage(ex, _queueName));
                return GetEnvelopeResult.Error();
            }

            if (null == message)
            {
                return GetEnvelopeResult.Empty;
            }

            try
            {
                var buffer = File.ReadAllBytes(message.FullName);

                var unpacked = new EnvelopeTransportContext(message, buffer, _queueName);
                return GetEnvelopeResult.Success(unpacked);
            }
            catch (IOException ex)
            {
                // this is probably sharing violation, no need to 
                // scare people.
                if (!IsSharingViolation(ex))
                {
                    _observer.Notify(new FailedToAccessStorage(ex, _queue.Name, message.Name));
                }
                return GetEnvelopeResult.Retry;
            }
            catch (Exception ex)
            {
                _observer.Notify(new EnvelopeDeserializationFailed(ex, _queue.Name, message.Name));
                // new poison details
                var poisonFile = Path.Combine(_posionQueue.Value.FullName, message.Name);
                message.MoveTo(poisonFile);
                return GetEnvelopeResult.Retry;
            }
        }

        static bool IsSharingViolation(IOException ex)
        {
            // http://stackoverflow.com/questions/425956/how-do-i-determine-if-an-ioexception-is-thrown-because-of-a-sharing-violation
            // don't ask...
            var hResult = System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            const int sharingViolation = 32;
            return (hResult & 0xFFFF) == sharingViolation;
        }

        public void Initialize()
        {
            _queue.Create();
        }

        /// <summary>
        /// ACKs the message by deleting it from the queue.
        /// </summary>
        /// <param name="envelope">The message context to ACK.</param>
        public void AckMessage(EnvelopeTransportContext envelope)
        {
            if (envelope == null) throw new ArgumentNullException("message");
            ((FileInfo)envelope.TransportMessage).Delete();
        }
    }
}