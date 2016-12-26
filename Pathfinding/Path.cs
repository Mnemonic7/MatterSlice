﻿/*
Copyright (c) 2015, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System.Text;

namespace Pathfinding
{
    public enum PathStatus
    {
        NOT_CALCULATED_YET,
        DESTINATION_UNREACHABLE,
        FOUND_GOAL,
        ALREADY_THERE
    }

    public struct Path<PathNodeType> where PathNodeType : IPathNode
    {
        public PathStatus status;
        public float pathLength;
        public PathNodeType[] nodes;
        public int pathSearchTestCount;

		public Path(PathNodeType[] pNodes, float pPathLength, PathStatus pStatus, int pPathSearchTestCount)
        {
            nodes = pNodes;
            pathLength = pPathLength;
            status = pStatus;
            pathSearchTestCount = pPathSearchTestCount;
        }

        public static Path<PathNodeType> EMPTY {
            get {
                return new Path<PathNodeType>(new PathNodeType[0], 0f, PathStatus.NOT_CALCULATED_YET, 0);
            }
        }
        
        public PathNodeType LastNode {
            get {
                return nodes[nodes.Length - 1];
            }
        }
        
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Path: \n[ ");
            
            foreach (IPathNode ipn in nodes) {
                sb.Append(ipn.ToString() + ",\n");
            }
            
            sb.Append("]");
            return sb.ToString();
        }

		public override bool Equals(object pOther)
		{
			if(!(pOther is Path<PathNodeType>)) return false;
			var other = (Path<PathNodeType>)pOther;
			if(status != other.status) return false;
			else if(pathLength != other.pathLength) return false;

			for(int i = 0; i < pathLength; i++) {
				if((System.IEquatable<PathNodeType>)nodes[i] != (System.IEquatable<PathNodeType>)other.nodes[i]) return false;
			}

			return true;
		}

		public static bool operator ==(Path<PathNodeType> a, Path<PathNodeType> b) {
			return a.Equals(b);
		}

		public static bool operator !=(Path<PathNodeType> a, Path<PathNodeType> b) {
			return !a.Equals(b);
		}
    }
}
