using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace GrpcDotNetNamedPipes.Internal
{
    internal class MessageReader<TMessage> : IAsyncStreamReader<TMessage>
    {
        private readonly PayloadQueue _payloadQueue;
        private readonly Marshaller<TMessage> _marshaller;
        private readonly CancellationToken _callCancellationToken;
        private readonly Deadline _deadline;

        public MessageReader(PayloadQueue payloadQueue, Marshaller<TMessage> marshaller,
            CancellationToken callCancellationToken, Deadline deadline)
        {
            _payloadQueue = payloadQueue;
            _marshaller = marshaller;
            _callCancellationToken = callCancellationToken;
            _deadline = deadline;
        }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            try
            {
                var combined =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _callCancellationToken,
                        _deadline.Token);
                return await _payloadQueue.MoveNext(combined.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) 
            {
                if (_deadline.IsExpired)
                {
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded, ""));
                }
                else
                {
                    throw new RpcException(Status.DefaultCancelled);
                }
            }
        }

        public TMessage Current => _marshaller.Deserializer(_payloadQueue.Current);

        public Task<TMessage> ReadNextMessage()
        {
            return ReadNextMessage(CancellationToken.None);
        }

        public Task<TMessage> ReadNextMessage(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                if (!await MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Expected payload");
                }

                return Current;
            });
        }
    }
}