﻿using System.Collections.Generic;
using System.Diagnostics;

namespace PackageHelper.Replay
{
    [DebuggerDisplay("{HitIndex}: {StartRequest.Url,nq}")]
    class RequestNode
    {
        public RequestNode(int hitIndex, StartRequest startRequest, HashSet<RequestNode> dependsOn)
        {
            HitIndex = hitIndex;
            StartRequest = startRequest;
            Dependencies = new HashSet<RequestNode>(dependsOn, HitIndexAndRequestComparer.Instance);
        }

        public int HitIndex { get; }
        public StartRequest StartRequest { get; }
        public EndRequest EndRequest { get; set; }
        public HashSet<RequestNode> Dependencies { get; set; }
    }
}
