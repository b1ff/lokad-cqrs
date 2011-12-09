﻿using System;
using System.Collections.Concurrent;
using Lokad.Cqrs.Core.Inbox;

namespace Lokad.Cqrs.Core.Dispatch
{
    public sealed class MemoryQuarantine : IEnvelopeQuarantine
    {
        readonly ConcurrentDictionary<string,int> _failures = new ConcurrentDictionary<string, int>();
        public bool TryToQuarantine(EnvelopeTransportContext context, ImmutableEnvelope envelope, Exception ex)
        {
            // serialization problem
            if (envelope == null)
                return true;
            var current = _failures.AddOrUpdate(envelope.EnvelopeId, s => 1, (s1, i) => i + 1);
            if (current < 4)
            {
                return false;
            }
            // accept and forget
            int forget;
            _failures.TryRemove(envelope.EnvelopeId, out forget);
            return true;
        }

        public void TryRelease(ImmutableEnvelope context)
        {
            if (null != context)
            {
                int value;
                _failures.TryRemove(context.EnvelopeId, out value);
            }
        }
    }
}