// Copyright (c) .NET Foundation.
// All Rights Reserved
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING
// WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF
// TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR
// NON-INFRINGEMENT.
// See the Apache 2 License for the specific language governing
// permissions and limitations under the License.

using Microsoft.AspNet.Routing.Template;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Framework.WebEncoders;

namespace Microsoft.AspNet.Routing.Logging
{
    [EventSource(Name = "Microsoft-AspNet-Routing")]
    /// <summary>
    /// Logger for hardcoded events that must go to ETW regardless of ILogger
    /// implementation or config.
    /// </summary>
    internal sealed class AspNetRoutingEventSource : EventSource
    {
        /// <summary>
        /// Identifiers for event types from this EventSource.
        /// </summary>
        private const int RequestRoutedId = 1;

        private static Lazy<AspNetRoutingEventSource> _lazyInstance = new Lazy<AspNetRoutingEventSource>(() => new AspNetRoutingEventSource());
        public static AspNetRoutingEventSource Log
        {
            get
            {
                return _lazyInstance.Value;
            }
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            base.OnEventCommand(command);

            // Cache keyword state here
        }

        /// <remarks>
        /// Values in this dictionary are not used, it just serves as a concurrent HashSet.
        /// </remarks>
        private Lazy<ConcurrentDictionary<string, byte>> _lazyRequestHandled = new
            Lazy<ConcurrentDictionary<string, byte>>(() => new ConcurrentDictionary<string, byte>());

        private AspNetRoutingEventSource() : base(false)
        {
        }

        [Event(RequestRoutedId)]
        private void RequestRouted(string path, string httpMethod, string requestId, string parameterString, string target, string pathBase)
        {
            WriteEvent(RequestRoutedId, httpMethod, path, requestId, parameterString, target, pathBase);
        }

        /// <summary>
        /// Log that request was handled by a router.
        /// </summary>
        /// <param name="context">RouteContext of this routing operation.</param>
        /// <param name="target">Router that handled request.</param>
        /// <param name="routeData">RouteData that was passed into handling router.</param>
        [NonEvent]
        public void RequestRouted(RouteContext context, IRouter target, RouteData routeData)
        {
            if (IsEnabled()) // Check (local copy of) keyword state here
            {
                // Ignore Routing framework glue.
                if (context != null && target != null &&
                    !(target is TemplateRoute) && !(target is RouteCollection))
                {
                    // We are only interested in the first routing node we see that reports handled.
                    var requestId = GetRequestIdFromContext(context);
                    if (!_lazyRequestHandled.Value.ContainsKey(requestId))
                    {
                        _lazyRequestHandled.Value.AddOrUpdate(requestId, 0, (s, b) => 0);
                        RequestRouted(context.HttpContext.Request.Path,
                            context.HttpContext.Request.Method,
                            requestId,
                            GetJsonArgumentsFromDictionary(routeData.Values),
                            target.GetType().Name,
                            context.HttpContext.Request.PathBase);
                    }
                }
            }
        }

        /// <summary>
        /// Log that this route operation is done, no more routers will be invoked.
        /// </summary>
        /// <param name="context">RouteContext of this routing operation.</param>
        [NonEvent]
        public void RoutingTraversalComplete(RouteContext context)
        {
            if (IsEnabled()) // Check (local copy of) keyword state here
            {
                if (context != null)
                {
                    var requestId = GetRequestIdFromContext(context);
                    byte b;

                    // Don't actually record any event at this time, but remove request ID
                    // from list of already-handled requests to avoid a leak.
                    _lazyRequestHandled.Value.TryRemove(requestId, out b);
                }
            }
        }

        [NonEvent]
        private static string GetRequestIdFromContext(RouteContext context)
        {
            var requestIdFeature = context.HttpContext.GetFeature<Http.Features.IHttpRequestIdentifierFeature>();
            return requestIdFeature != null ? requestIdFeature.TraceIdentifier : "";
        }

        // Internal for unit testing.
        [NonEvent]
        internal static string GetJsonArgumentsFromDictionary(IDictionary<string, object> dictionary)
        {
            if (dictionary != null)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.Append('{');
                var empty = true;

                foreach (var kvp in dictionary)
                {
                    // Format these with invariant culture if they are formattable.
                    var valueFormattable = kvp.Value as IFormattable;
                    var valueString = (valueFormattable == null) ?
                        kvp.Value.ToString() :
                        valueFormattable.ToString("G", CultureInfo.InvariantCulture);

                    if (!empty)
                    {
                        stringBuilder.Append(',');
                    }
                    stringBuilder.Append("\"");
                    stringBuilder.Append(JavaScriptStringEncoder.Default.JavaScriptStringEncode(kvp.Key));
                    stringBuilder.Append("\":\"");
                    stringBuilder.Append(JavaScriptStringEncoder.Default.JavaScriptStringEncode(valueString));
                    stringBuilder.Append("\"");

                    empty = false;
                }

                stringBuilder.Append('}');
                return stringBuilder.ToString();
            }
            else
            {
                return "";
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, params string[] args)
        {
            var dataDesc = stackalloc EventData[args.Length];
            var handles = stackalloc GCHandle[args.Length];
            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    handles[i] = GCHandle.Alloc(args[i], GCHandleType.Pinned);
                    dataDesc[i].DataPointer = handles[i].AddrOfPinnedObject();
                    dataDesc[i].Size = (args[i].Length + 1) * sizeof(char);
                }
                WriteEventCore(eventId, args.Length, dataDesc);
            }
            catch
            {
                // don't throw an exception for failure to generate ETW event
                Debug.Fail("Exception hit while generating ETW event");
            }
            finally
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if (handles[i].IsAllocated)
                    {
                        handles[i].Free();
                    }
                }
            }
        }
    }
}
