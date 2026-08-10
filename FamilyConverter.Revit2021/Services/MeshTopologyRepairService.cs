using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Services
{
    public class MeshTopologyRepairAnalysis
    {
        public MeshTopologyRepairAnalysis()
        {
            Triangles = new List<IList<XYZ>>();
            CapLoops = new List<IList<XYZ>>();
        }

        public IList<IList<XYZ>> Triangles { get; private set; }
        public IList<IList<XYZ>> CapLoops { get; private set; }
        public int BoundaryEdgeCount { get; set; }
        public int BoundaryLoopCount { get; set; }
        public int NonManifoldEdgeCount { get; set; }
        public int OrientationFlipCount { get; set; }
        public int OrientationConflictCount { get; set; }
        public int NonPlanarBoundaryLoopCount { get; set; }
        public int OpenBoundaryChainCount { get; set; }
        public string FailureReason { get; set; }

        public bool CanAttemptRepair
        {
            get
            {
                return string.IsNullOrWhiteSpace(FailureReason)
                    && NonManifoldEdgeCount == 0
                    && OrientationConflictCount == 0
                    && OpenBoundaryChainCount == 0
                    && NonPlanarBoundaryLoopCount == 0
                    && (OrientationFlipCount > 0 || CapLoops.Count > 0);
            }
        }
    }

    public class MeshTopologyRepairService
    {
        private struct VertexKey : IEquatable<VertexKey>, IComparable<VertexKey>
        {
            public long X;
            public long Y;
            public long Z;

            public bool Equals(VertexKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexKey && Equals((VertexKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + X.GetHashCode();
                    hash = (hash * 31) + Y.GetHashCode();
                    hash = (hash * 31) + Z.GetHashCode();
                    return hash;
                }
            }

            public int CompareTo(VertexKey other)
            {
                int result = X.CompareTo(other.X);
                if (result != 0)
                {
                    return result;
                }

                result = Y.CompareTo(other.Y);
                return result != 0 ? result : Z.CompareTo(other.Z);
            }
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            public VertexKey First;
            public VertexKey Second;

            public EdgeKey(VertexKey first, VertexKey second)
            {
                if (first.CompareTo(second) <= 0)
                {
                    First = first;
                    Second = second;
                }
                else
                {
                    First = second;
                    Second = first;
                }
            }

            public bool Equals(EdgeKey other)
            {
                return First.Equals(other.First) && Second.Equals(other.Second);
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey && Equals((EdgeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (First.GetHashCode() * 397) ^ Second.GetHashCode();
                }
            }
        }

        private class TriangleData
        {
            public IList<XYZ> Vertices { get; set; }
            public VertexKey[] Keys { get; set; }
        }

        private class EdgeUse
        {
            public int TriangleIndex { get; set; }
            public VertexKey From { get; set; }
            public VertexKey To { get; set; }
        }

        private class DirectedBoundaryEdge
        {
            public EdgeKey Key { get; set; }
            public VertexKey From { get; set; }
            public VertexKey To { get; set; }
        }

        private const double WeldToleranceFeet = 1e-7;
        private const double PlanarityToleranceFeet = 0.0008202099737532809; // 0.25 mm
        private const int MaximumBoundaryLoopCount = 256;
        private const int MaximumBoundaryVertices = 10000;

        public MeshTopologyRepairAnalysis Analyze(
            Mesh mesh,
            Transform transform,
            Func<Mesh, int, Transform, IList<XYZ>> triangleReader)
        {
            var analysis = new MeshTopologyRepairAnalysis();
            if (mesh == null || triangleReader == null)
            {
                analysis.FailureReason = "Mesh topology analysis did not receive source geometry.";
                return analysis;
            }

            var triangles = new List<TriangleData>();
            var pointByKey = new Dictionary<VertexKey, XYZ>();
            var edgeUses = new Dictionary<EdgeKey, List<EdgeUse>>();

            for (int triangleIndex = 0; triangleIndex < mesh.NumTriangles; triangleIndex++)
            {
                IList<XYZ> vertices = triangleReader(mesh, triangleIndex, transform);
                if (vertices == null || vertices.Count != 3)
                {
                    analysis.FailureReason = "Mesh contains an invalid triangle at index " + triangleIndex + ".";
                    return analysis;
                }

                var keys = new VertexKey[3];
                for (int vertexIndex = 0; vertexIndex < 3; vertexIndex++)
                {
                    keys[vertexIndex] = CreateVertexKey(vertices[vertexIndex]);
                    if (!pointByKey.ContainsKey(keys[vertexIndex]))
                    {
                        pointByKey.Add(keys[vertexIndex], vertices[vertexIndex]);
                    }
                }

                var triangle = new TriangleData
                {
                    Vertices = vertices,
                    Keys = keys
                };
                int storedTriangleIndex = triangles.Count;
                triangles.Add(triangle);

                AddEdgeUse(edgeUses, storedTriangleIndex, keys[0], keys[1]);
                AddEdgeUse(edgeUses, storedTriangleIndex, keys[1], keys[2]);
                AddEdgeUse(edgeUses, storedTriangleIndex, keys[2], keys[0]);
            }

            var adjacency = new Dictionary<int, List<KeyValuePair<int, bool>>>();
            foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in edgeUses)
            {
                List<EdgeUse> uses = pair.Value;
                if (uses.Count > 2)
                {
                    analysis.NonManifoldEdgeCount++;
                    continue;
                }

                if (uses.Count != 2)
                {
                    continue;
                }

                bool sameDirection = uses[0].From.Equals(uses[1].From)
                    && uses[0].To.Equals(uses[1].To);
                AddAdjacency(adjacency, uses[0].TriangleIndex, uses[1].TriangleIndex, sameDirection);
                AddAdjacency(adjacency, uses[1].TriangleIndex, uses[0].TriangleIndex, sameDirection);
            }

            bool?[] flips = OrientTriangles(triangles.Count, adjacency, analysis);
            if (analysis.OrientationConflictCount > 0)
            {
                analysis.FailureReason = "Mesh has conflicting triangle orientation and cannot be repaired safely.";
            }

            for (int index = 0; index < triangles.Count; index++)
            {
                TriangleData triangle = triangles[index];
                if (flips[index] == true)
                {
                    analysis.Triangles.Add(new List<XYZ>
                    {
                        triangle.Vertices[0],
                        triangle.Vertices[2],
                        triangle.Vertices[1]
                    });
                    analysis.OrientationFlipCount++;
                }
                else
                {
                    analysis.Triangles.Add(new List<XYZ>(triangle.Vertices));
                }
            }

            if (analysis.NonManifoldEdgeCount > 0)
            {
                analysis.FailureReason = "Mesh contains "
                    + analysis.NonManifoldEdgeCount
                    + " non-manifold edges.";
                return analysis;
            }

            IList<DirectedBoundaryEdge> boundaryEdges = BuildBoundaryEdges(edgeUses, flips);
            analysis.BoundaryEdgeCount = boundaryEdges.Count;
            if (boundaryEdges.Count == 0)
            {
                return analysis;
            }

            BuildCapLoops(boundaryEdges, pointByKey, analysis);
            return analysis;
        }

        private static VertexKey CreateVertexKey(XYZ point)
        {
            return new VertexKey
            {
                X = (long)Math.Round(point.X / WeldToleranceFeet),
                Y = (long)Math.Round(point.Y / WeldToleranceFeet),
                Z = (long)Math.Round(point.Z / WeldToleranceFeet)
            };
        }

        private static void AddEdgeUse(
            IDictionary<EdgeKey, List<EdgeUse>> edgeUses,
            int triangleIndex,
            VertexKey from,
            VertexKey to)
        {
            var key = new EdgeKey(from, to);
            List<EdgeUse> uses;
            if (!edgeUses.TryGetValue(key, out uses))
            {
                uses = new List<EdgeUse>();
                edgeUses.Add(key, uses);
            }

            uses.Add(new EdgeUse
            {
                TriangleIndex = triangleIndex,
                From = from,
                To = to
            });
        }

        private static void AddAdjacency(
            IDictionary<int, List<KeyValuePair<int, bool>>> adjacency,
            int fromTriangle,
            int toTriangle,
            bool requiresOppositeFlip)
        {
            List<KeyValuePair<int, bool>> values;
            if (!adjacency.TryGetValue(fromTriangle, out values))
            {
                values = new List<KeyValuePair<int, bool>>();
                adjacency.Add(fromTriangle, values);
            }

            values.Add(new KeyValuePair<int, bool>(toTriangle, requiresOppositeFlip));
        }

        private static bool?[] OrientTriangles(
            int triangleCount,
            IDictionary<int, List<KeyValuePair<int, bool>>> adjacency,
            MeshTopologyRepairAnalysis analysis)
        {
            var flips = new bool?[triangleCount];
            var queue = new Queue<int>();

            for (int start = 0; start < triangleCount; start++)
            {
                if (flips[start].HasValue)
                {
                    continue;
                }

                flips[start] = false;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<KeyValuePair<int, bool>> neighbours;
                    if (!adjacency.TryGetValue(current, out neighbours))
                    {
                        continue;
                    }

                    foreach (KeyValuePair<int, bool> neighbour in neighbours)
                    {
                        bool expectedFlip = flips[current].Value ^ neighbour.Value;
                        if (!flips[neighbour.Key].HasValue)
                        {
                            flips[neighbour.Key] = expectedFlip;
                            queue.Enqueue(neighbour.Key);
                        }
                        else if (flips[neighbour.Key].Value != expectedFlip)
                        {
                            analysis.OrientationConflictCount++;
                        }
                    }
                }
            }

            return flips;
        }

        private static IList<DirectedBoundaryEdge> BuildBoundaryEdges(
            IDictionary<EdgeKey, List<EdgeUse>> edgeUses,
            bool?[] flips)
        {
            var result = new List<DirectedBoundaryEdge>();
            foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in edgeUses)
            {
                if (pair.Value.Count != 1)
                {
                    continue;
                }

                EdgeUse use = pair.Value[0];
                bool flip = flips[use.TriangleIndex] == true;
                result.Add(new DirectedBoundaryEdge
                {
                    Key = pair.Key,
                    From = flip ? use.To : use.From,
                    To = flip ? use.From : use.To
                });
            }

            return result;
        }

        private static void BuildCapLoops(
            IList<DirectedBoundaryEdge> boundaryEdges,
            IDictionary<VertexKey, XYZ> pointByKey,
            MeshTopologyRepairAnalysis analysis)
        {
            var outgoing = new Dictionary<VertexKey, List<DirectedBoundaryEdge>>();
            var incomingCount = new Dictionary<VertexKey, int>();

            foreach (DirectedBoundaryEdge edge in boundaryEdges)
            {
                List<DirectedBoundaryEdge> values;
                if (!outgoing.TryGetValue(edge.From, out values))
                {
                    values = new List<DirectedBoundaryEdge>();
                    outgoing.Add(edge.From, values);
                }

                values.Add(edge);
                int count;
                incomingCount.TryGetValue(edge.To, out count);
                incomingCount[edge.To] = count + 1;
            }

            foreach (DirectedBoundaryEdge edge in boundaryEdges)
            {
                List<DirectedBoundaryEdge> outgoingEdges;
                int incoming;
                outgoing.TryGetValue(edge.From, out outgoingEdges);
                incomingCount.TryGetValue(edge.From, out incoming);
                if (outgoingEdges == null || outgoingEdges.Count != 1 || incoming != 1)
                {
                    analysis.OpenBoundaryChainCount++;
                }
            }

            if (analysis.OpenBoundaryChainCount > 0)
            {
                analysis.FailureReason = "Mesh boundary contains open or branching chains.";
                return;
            }

            var used = new HashSet<EdgeKey>();
            foreach (DirectedBoundaryEdge firstEdge in boundaryEdges)
            {
                if (used.Contains(firstEdge.Key))
                {
                    continue;
                }

                if (analysis.BoundaryLoopCount >= MaximumBoundaryLoopCount)
                {
                    analysis.FailureReason = "Mesh has too many boundary loops for safe automatic repair.";
                    return;
                }

                var keys = new List<VertexKey> { firstEdge.From };
                DirectedBoundaryEdge currentEdge = firstEdge;
                bool closed = false;

                while (keys.Count <= MaximumBoundaryVertices)
                {
                    if (!used.Add(currentEdge.Key))
                    {
                        break;
                    }

                    VertexKey currentVertex = currentEdge.To;
                    if (currentVertex.Equals(keys[0]))
                    {
                        closed = true;
                        break;
                    }

                    keys.Add(currentVertex);
                    List<DirectedBoundaryEdge> nextEdges;
                    if (!outgoing.TryGetValue(currentVertex, out nextEdges)
                        || nextEdges.Count != 1
                        || used.Contains(nextEdges[0].Key))
                    {
                        break;
                    }

                    currentEdge = nextEdges[0];
                }

                if (!closed || keys.Count < 3)
                {
                    analysis.OpenBoundaryChainCount++;
                    analysis.FailureReason = "Mesh boundary loop could not be closed safely.";
                    return;
                }

                analysis.BoundaryLoopCount++;
                var loop = new List<XYZ>();
                for (int index = keys.Count - 1; index >= 0; index--)
                {
                    loop.Add(pointByKey[keys[index]]);
                }

                if (!IsPlanar(loop))
                {
                    analysis.NonPlanarBoundaryLoopCount++;
                    continue;
                }

                analysis.CapLoops.Add(loop);
            }

            if (analysis.NonPlanarBoundaryLoopCount > 0)
            {
                analysis.FailureReason = "Mesh has "
                    + analysis.NonPlanarBoundaryLoopCount
                    + " non-planar boundary loops; partial sealing was skipped.";
            }
        }

        private static bool IsPlanar(IList<XYZ> points)
        {
            if (points == null || points.Count < 3)
            {
                return false;
            }

            XYZ origin = points[0];
            XYZ normal = null;
            for (int first = 1; first < points.Count - 1 && normal == null; first++)
            {
                for (int second = first + 1; second < points.Count; second++)
                {
                    XYZ candidate = points[first].Subtract(origin)
                        .CrossProduct(points[second].Subtract(origin));
                    if (candidate.GetLength() > 1e-10)
                    {
                        normal = candidate.Normalize();
                        break;
                    }
                }
            }

            if (normal == null)
            {
                return false;
            }

            foreach (XYZ point in points)
            {
                double distance = Math.Abs(point.Subtract(origin).DotProduct(normal));
                if (distance > PlanarityToleranceFeet)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
