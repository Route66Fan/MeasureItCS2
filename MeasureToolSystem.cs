using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Mathematics;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using GameMode = Game.GameMode;

namespace MeasureItCS2.Systems
{
    /// <summary>
    /// ECS replacement for CS1's MeasureTool.cs (a ToolBase MonoBehaviour). Handles:
    ///  - raycasting the cursor to a world position each frame (was SimulationStep()/
    ///    OnToolLateUpdate() in CS1),
    ///  - left click = add a point, right click = remove the last point (was
    ///    OnToolGUI()/AddPosition()/RemoveLastPosition()),
    ///  - drawing the point markers + connecting segments (was RenderOverlay()).
    ///
    /// Unlike CS1, which only ever tracked a Start/End pair, this keeps an ordered list
    /// of points so an arbitrary multi-point measurement chain can be drawn, per the
    /// "place multiple points" requirement. MeasureUISystem reads Points from here and
    /// pushes the segment/total math to the React panel.
    /// </summary>
    public partial class MeasureToolSystem : ToolBaseSystem
    {
        /// <summary>Reads the current color choice from settings each time it's used,
        /// so changing the dropdown in Options takes effect immediately without
        /// needing to reactivate the tool. Parses MeasureMath.ColorHex() rather than
        /// keeping a separate RGB list here, so this and the floating number labels
        /// (which read the same hex value on the React side) can never drift apart.</summary>
        private static Color PointColor
        {
            get
            {
                Color color = Color.yellow;
                ColorUtility.TryParseHtmlString(MeasureMath.ColorHex(Mod.Settings.MeasureColor), out color);
                color.a = 0.85f;
                return color;
            }
        }
        private const float PointRadius = 4f;

        // How far (in the XZ plane, meters) a click can be from a network node and
        // still snap to it. Wide enough to comfortably catch a buried pipe/tunnel
        // node when clicking on the surface roughly above it, tight enough not to
        // grab an unrelated node from a completely different street over.
        private const float NodeSnapRadius = 15f;

        // Wider than NodeSnapRadius so the player can see upcoming snap targets
        // slightly before the cursor is actually close enough to snap to them, not
        // just the instant snapping engages.
        private const float NodeVisibilityRadius = 80f;
        private const float NodeMarkerRadius = 10f;

        private readonly List<float3> m_NearbyNodePositions = new List<float3>();

        private OverlayRenderSystem m_OverlayRenderSystem;
        private EntityQuery m_NodeQuery;
        private EntityQuery m_EdgeQuery;

        private float3 m_CursorPosition;
        private bool m_HasCursorPosition;
        private Entity m_CursorNodeEntity;

        // A scratch buffer reused every frame for the live preview segment's path
        // (last placed point -> cursor), to avoid allocating a new List every frame
        // just to throw it away. Segments that get actually placed each get their
        // own permanent List in m_SegmentPaths instead (see AddPoint).
        private readonly List<float3> m_PreviewPathScratch = new List<float3>();

        /// <summary>Current raycast cursor world position and whether it's valid this
        /// frame - exposed so MeasureUISystem's "Add Point" keybind can add a point at
        /// the cursor without duplicating the raycast logic.</summary>
        public float3 CursorPosition => m_CursorPosition;
        public bool HasCursorPosition => m_HasCursorPosition;

        /// <summary>Ordered chain of measurement points. Public+readonly for MeasureUISystem.</summary>
        public IReadOnlyList<float3> Points => m_Points;
        private readonly List<float3> m_Points = new List<float3>();

        // Parallel to m_Points: which network node (if any) each point snapped to,
        // Entity.Null if that point wasn't snapped to a node at all. Needed so
        // consecutive points can be pathfound across the network graph between them.
        private readonly List<Entity> m_PointNodeEntities = new List<Entity>();

        // Parallel to the gaps between m_Points (index i = path between point i and
        // point i+1): the actual sequence of world positions tracing the network
        // path between those two points, computed once when the second point of the
        // pair is added (see AddPoint) rather than every frame. Null/empty means no
        // path was found (or one/both points weren't snapped to a node at all) - draw
        // a plain straight line for that segment instead.
        private readonly List<List<float3>> m_SegmentPaths = new List<List<float3>>();

        public override string toolID => "MeasureItCS2.MeasureTool";

        protected override void OnCreate()
        {
            base.OnCreate();

            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_NodeQuery = GetEntityQuery(ComponentType.ReadOnly<Node>());
            m_EdgeQuery = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>());

            // Confirmed via Game.dll's IL: a per-frame job in UndergroundViewSystem
            // does "undergroundOn = activeTool.requireUnderground" whenever any tool
            // is active - which is how OnStartRunning() forces tunnels/pipes visible
            // for the duration this tool is active. allowUnderground is a separate
            // property that turned out to be necessary too (empirically - removing it
            // broke requireUnderground's effect, even though the only confirmed IL
            // usage of it elsewhere, in ObjectToolSystem, is for something unrelated).
            allowUnderground = true;

            Enabled = false;
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // ToolBaseSystem.SetActions() (called by base.OnStartRunning()) only wires
            // up an internal event listener via SetInteraction() - it does NOT actually
            // enable applyAction/secondaryApplyAction itself (UpdateActions() is a
            // no-op by default). Compare to ResetActions(), which explicitly disables
            // them on stop. So the tool is responsible for enabling its own actions
            // here. This is safe (unlike the earlier crash) because applyAction/
            // secondaryApplyAction are the tool-scoped default actions inherited from
            // ToolBaseSystem, not the raw shared built-in ones from
            // InputManager.FindAction() - enabling THOSE directly is what threw
            // "Built-in actions can not be enabled directly".
            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;

            // Confirmed via Game.dll's IL: UndergroundViewSystem runs a per-frame job
            // that does "undergroundOn = activeTool.requireUnderground" whenever any
            // tool is active. Rather than trying to preserve/mirror whatever state the
            // player had it in (which turned out not to work reliably), this simply
            // forces Underground View on for the duration this tool is active, so
            // tunnels/pipes are always visible for reference while measuring. Once the
            // tool deactivates and control reverts to the game's own default tool
            // (whose requireUnderground is false), that same job naturally sets
            // undergroundOn back to false on its own - no explicit handling needed in
            // OnStopRunning().
            //
            // Only done in-game. In the editor this tool should behave like any other
            // editor tool and leave Underground View alone - forcing it on there would
            // be an unrequested side effect, and it's not something that's been
            // verified to make sense in the editor's views. ToolSystem.actionMode is
            // just the GameMode (Game/Editor) set when the scene loads (confirmed via
            // IL). Assigned explicitly either way (rather than only set to true in
            // game mode) so a stale value can never carry over if the same system
            // instance gets reused after switching between game and editor.
            requireUnderground = (m_ToolSystem.actionMode & GameMode.Game) != 0;
        }

        protected override void OnStopRunning()
        {
            applyAction.shouldBeEnabled = false;
            secondaryApplyAction.shouldBeEnabled = false;

            base.OnStopRunning();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            // Always hit-test Terrain, Net (roads/bridges/tunnels), and StaticObjects
            // (buildings etc.), so points land exactly on whatever's under the
            // cursor.
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Net | TypeMask.StaticObjects;
            m_ToolRaycastSystem.raycastFlags = RaycastFlags.Outside;

            // CollisionMask governs which vertical layer Net/StaticObjects entities
            // can be hit on. Raycast-based underground hit-testing was attempted and
            // removed - it never reliably worked (Underground View kept getting
            // force-disabled, and even after fixing that, terrain still occluded
            // tunnel hits) - so this stays at the normal ground-level/elevated set
            // only. Precise placement on buried/submerged network geometry is instead
            // handled by node-snapping (TryFindNearestNode), which reads each node's
            // actual 3D position directly from the ECS data rather than depending on
            // the raycast reaching it at all. Underground View itself is still
            // supported for visibility (forced on for the duration this tool is
            // active - see OnStartRunning()) so tunnels/pipes are always visible
            // while measuring - it just no longer changes what layers this tool's
            // own raycast can hit.
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;

            // ToolBaseSystem's own InitializeRaycast() defaults netLayerMask to
            // Layer.None entirely, meaning network surfaces (roads, tracks, bridges,
            // tunnels, etc.) are excluded from raycasts by default regardless of
            // typeMask including TypeMask.Net. Without this, clicks near a bridge or
            // tunnel would land on whatever terrain/water is underneath instead of the
            // network surface itself. Confirmed Layer.All is genuinely 0xFFFFFFFF (via
            // IL), so this covers every network type including subway/pipe layers.
            m_ToolRaycastSystem.netLayerMask = Layer.All;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (GetRaycastResult(out float3 hitPosition))
            {
                // "Select nodes along pathways": network nodes (Game.Net.Node,
                // confirmed via IL - a plain IComponentData struct with a real
                // m_Position float3) exist at their actual designed position
                // regardless of terrain occlusion, camera angle, or view mode. That
                // sidesteps the whole underground-raycasting problem entirely -
                // rather than trying to make the raycast itself reach buried/
                // submerged network geometry (which the removed Underground View
                // support kept fighting with), this does a plain nearest-node search
                // in the XZ plane around wherever the raycast landed (typically the
                // terrain surface above the actual pipe/tunnel) and, if one's close
                // enough, snaps to that node's true 3D position - depth and all.
                if (Mod.Settings.SnapToNodes)
                {
                    if (TryFindNearestNode(hitPosition, out float3 nodePosition, out Entity nodeEntity))
                    {
                        m_CursorPosition = nodePosition;
                        m_CursorNodeEntity = nodeEntity;
                    }
                    else
                    {
                        m_CursorPosition = hitPosition;
                        m_CursorNodeEntity = Entity.Null;
                    }
                }
                else
                {
                    m_CursorPosition = hitPosition;
                    m_CursorNodeEntity = Entity.Null;
                    m_NearbyNodePositions.Clear();
                }

                m_HasCursorPosition = true;
            }
            else
            {
                m_HasCursorPosition = false;
                m_CursorNodeEntity = Entity.Null;
                m_NearbyNodePositions.Clear();
            }

            // NOTE: InputManager.instance.mouseOverUI is the commonly-used guard in CS2
            // tool mods for "don't act on clicks that landed on a UI panel". If your SDK
            // version names this differently, adjust here.
            bool mouseOverUI = InputManager.instance.mouseOverUI;

            // applyAction/secondaryApplyAction are protected properties inherited from
            // ToolBaseSystem, already wired to this tool's own scoped "Apply"/
            // "Secondary Apply" built-in actions and explicitly enabled in
            // OnStartRunning() (see the comment there for why that's needed).
            if (applyAction.WasPressedThisFrame() && m_HasCursorPosition && !mouseOverUI)
            {
                AddPoint(m_CursorPosition);
            }
            else if (secondaryApplyAction.WasPressedThisFrame())
            {
                RemoveLastPoint();
            }

            DrawOverlay();

            return inputDeps;
        }

        /// <summary>Raycasts using the base ToolBaseSystem raycast pipeline and returns the world hit position.</summary>
        private bool GetRaycastResult(out float3 position)
        {
            if (GetRaycastResult(out _, out RaycastHit hit))
            {
                position = hit.m_HitPosition;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// Plain linear nearest-neighbor search over every Game.Net.Node in the city,
        /// comparing XZ distance only (so a node buried straight down from the search
        /// point is still found, regardless of how deep it is). This runs once per
        /// frame while the tool is active - not inside a hot per-entity job - so a
        /// simple EntityQuery + array scan is more than fast enough; a proper
        /// Burst/quadtree-based spatial query (Game.Net.SearchSystem's
        /// m_NetSearchTree) would scale better for very large cities but needs a
        /// custom IBurstQuadTreeIterator, which is a lot of extra complexity for a
        /// once-a-frame lookup like this.
        ///
        /// Also collects every node within the wider NodeVisibilityRadius into
        /// m_NearbyNodePositions for DrawOverlay to render as small markers - since
        /// this method already scans every node in the city for the snap search, this
        /// adds no real extra cost, just an extra distance check per node already
        /// being visited.
        ///
        /// Also outputs the actual node Entity (not just its position), so a placed
        /// point can be pathfound against its network neighbors later - see
        /// TryFindNetworkPath.
        /// </summary>
        private bool TryFindNearestNode(float3 searchPosition, out float3 nodePosition, out Entity nodeEntity)
        {
            m_NearbyNodePositions.Clear();

            NativeArray<Entity> nodeEntities = m_NodeQuery.ToEntityArray(Allocator.Temp);
            NativeArray<Node> nodes = m_NodeQuery.ToComponentDataArray<Node>(Allocator.Temp);

            bool found = false;
            float nearestDistanceSq = NodeSnapRadius * NodeSnapRadius;
            float visibilityDistanceSq = NodeVisibilityRadius * NodeVisibilityRadius;
            nodePosition = default;
            nodeEntity = Entity.Null;

            for (int i = 0; i < nodes.Length; i++)
            {
                float3 candidate = nodes[i].m_Position;
                float dx = candidate.x - searchPosition.x;
                float dz = candidate.z - searchPosition.z;
                float distanceSq = dx * dx + dz * dz;

                if (distanceSq <= visibilityDistanceSq)
                {
                    m_NearbyNodePositions.Add(candidate);
                }

                if (distanceSq <= nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nodePosition = candidate;
                    nodeEntity = nodeEntities[i];
                    found = true;
                }
            }

            nodeEntities.Dispose();
            nodes.Dispose();
            return found;
        }

        // How many nodes a network-path search will visit before giving up and
        // falling back to a straight line - keeps pathfinding bounded even in huge,
        // sprawling networks, at the cost of not finding very distant connections.
        private const int MaxPathfindNodesVisited = 3000;

        // How many points to sample along each edge's curve when tracing a found
        // path, for smooth rendering of that edge's actual shape.
        private const int PathEdgeSampleCount = 10;

        /// <summary>
        /// Breadth-first search across the Game.Net.Edge/Node graph from startNode to
        /// endNode, and if found, fills result with the actual world positions tracing
        /// that path (sampling each edge's real Curve.m_Bezier geometry in sequence,
        /// oriented the right way around depending on which direction the path
        /// crosses that edge). This is what makes the drawn line actually follow a
        /// buried pipe/subway line's real route between two points, instead of a
        /// straight line cutting through the ground between them.
        ///
        /// This is plain C# (Dictionary/Queue/HashSet) run on the main thread, not a
        /// Burst job - simplest to write correctly, and pathfinding only needs to run
        /// once per placed segment (see AddPoint) plus once per frame for the live
        /// preview segment, not for every entity in a hot loop. The real
        /// Game.Pathfind system exists for exactly this but is a much larger, more
        /// specialized subsystem than this tool needs.
        /// </summary>
        private bool TryFindNetworkPath(Entity startNode, Entity endNode, float3 startPosition, float3 endPosition, List<float3> result)
        {
            result.Clear();

            if (startNode == Entity.Null || endNode == Entity.Null || startNode == endNode)
            {
                return false;
            }

            NativeArray<Edge> edges = m_EdgeQuery.ToComponentDataArray<Edge>(Allocator.Temp);
            NativeArray<Curve> curves = m_EdgeQuery.ToComponentDataArray<Curve>(Allocator.Temp);

            var touchingEdges = new Dictionary<Entity, List<int>>();
            for (int i = 0; i < edges.Length; i++)
            {
                AddTouchingEdge(touchingEdges, edges[i].m_Start, i);
                AddTouchingEdge(touchingEdges, edges[i].m_End, i);
            }

            var cameFromEdge = new Dictionary<Entity, int>();
            var cameFromNode = new Dictionary<Entity, Entity>();
            var visited = new HashSet<Entity> { startNode };

            // Plain List used as a FIFO queue (read index advances instead of
            // removing from the front) rather than System.Collections.Generic.Queue<T>
            // - this project's .csproj references both System.dll and mscorlib.dll
            // directly, and Queue<T> exists identically in both, which the compiler
            // can't disambiguate (CS0433). A List with a manual head index gives the
            // same FIFO behavior without that conflict.
            var queue = new List<Entity> { startNode };
            int queueHead = 0;

            bool found = false;
            int visitedCount = 1;

            while (queueHead < queue.Count && visitedCount <= MaxPathfindNodesVisited)
            {
                Entity current = queue[queueHead];
                queueHead++;

                if (current == endNode)
                {
                    found = true;
                    break;
                }

                if (!touchingEdges.TryGetValue(current, out List<int> edgeIndices))
                {
                    continue;
                }

                for (int e = 0; e < edgeIndices.Count; e++)
                {
                    int edgeIndex = edgeIndices[e];
                    Edge edge = edges[edgeIndex];
                    Entity other = edge.m_Start == current ? edge.m_End : edge.m_Start;

                    if (visited.Contains(other))
                    {
                        continue;
                    }

                    visited.Add(other);
                    cameFromEdge[other] = edgeIndex;
                    cameFromNode[other] = current;
                    queue.Add(other);
                    visitedCount++;
                }
            }

            if (found)
            {
                // Walk backward from endNode to startNode via cameFromNode/Edge to
                // reconstruct the sequence of edges (and which direction each one is
                // crossed), then reverse it into start-to-end order.
                var edgeSequence = new List<(int edgeIndex, bool forward)>();
                Entity walk = endNode;

                while (walk != startNode)
                {
                    int edgeIndex = cameFromEdge[walk];
                    Entity previous = cameFromNode[walk];
                    bool forward = edges[edgeIndex].m_Start == previous;
                    edgeSequence.Add((edgeIndex, forward));
                    walk = previous;
                }

                edgeSequence.Reverse();

                for (int s = 0; s < edgeSequence.Count; s++)
                {
                    (int edgeIndex, bool forward) = edgeSequence[s];
                    Bezier4x3 bezier = curves[edgeIndex].m_Bezier;

                    for (int step = 0; step <= PathEdgeSampleCount; step++)
                    {
                        float t = step / (float)PathEdgeSampleCount;
                        if (!forward)
                        {
                            t = 1f - t;
                        }

                        // Two consecutive edges that share a node don't necessarily
                        // have their own bezier curves meeting at exactly the same
                        // point - e.g. a lane-count transition (3 lanes -> 2 lanes)
                        // shifts where each side's curve actually connects, even
                        // though both edges reference the same shared node entity.
                        // Concatenating each edge's raw samples end-to-end left a
                        // small visible "jog" at exactly that kind of boundary.
                        // Forcing this edge's very first sample to exactly match the
                        // previous edge's very last sample (already in result)
                        // eliminates that discontinuity - only this one shared
                        // boundary point is adjusted, the rest of each edge's shape
                        // is untouched.
                        if (step == 0 && s > 0)
                        {
                            continue;
                        }

                        result.Add(MathUtils.Position(bezier, t));
                    }
                }

                // A Node's own m_Position (what points snap to, and what's actually
                // drawn as the point marker) isn't necessarily the exact same point
                // as where its connected edge's curve endpoint sits (confirmed via
                // diagnostic logging - likely because a node represents an
                // intersection's logical center while an edge connects at the actual
                // lane/road-surface point, which is naturally offset at
                // intersections). Snapping the path's first/last sampled points to
                // match the actual drawn markers exactly avoids a visible gap/
                // discontinuity between the point circles and the line connecting
                // them - only these two endpoints are adjusted, every point in
                // between keeps the edge's real sampled shape.
                if (result.Count > 0)
                {
                    result[0] = startPosition;
                    result[result.Count - 1] = endPosition;
                }
            }

            edges.Dispose();
            curves.Dispose();

            return found && result.Count > 1;
        }

        private static void AddTouchingEdge(Dictionary<Entity, List<int>> touchingEdges, Entity node, int edgeIndex)
        {
            if (!touchingEdges.TryGetValue(node, out List<int> list))
            {
                list = new List<int>();
                touchingEdges[node] = list;
            }
            list.Add(edgeIndex);
        }

        public void AddPoint(float3 position)
        {
            m_Points.Add(position);
            m_PointNodeEntities.Add(m_CursorNodeEntity);

            if (m_Points.Count >= 2)
            {
                Entity fromNode = m_PointNodeEntities[m_PointNodeEntities.Count - 2];
                Entity toNode = m_PointNodeEntities[m_PointNodeEntities.Count - 1];
                float3 from = m_Points[m_Points.Count - 2];
                float3 to = m_Points[m_Points.Count - 1];

                var path = new List<float3>();
                TryFindNetworkPath(fromNode, toNode, from, to, path);
                m_SegmentPaths.Add(path);
            }
        }

        public void RemoveLastPoint()
        {
            if (m_Points.Count > 0)
            {
                m_Points.RemoveAt(m_Points.Count - 1);
                m_PointNodeEntities.RemoveAt(m_PointNodeEntities.Count - 1);

                if (m_SegmentPaths.Count > 0)
                {
                    m_SegmentPaths.RemoveAt(m_SegmentPaths.Count - 1);
                }
            }
        }

        public void ClearPoints()
        {
            m_Points.Clear();
            m_PointNodeEntities.Clear();
            m_SegmentPaths.Clear();
        }

        public override void GetAvailableSnapMask(out Snap onMask, out Snap offMask)
        {
            onMask = Snap.NetSide | Snap.NetNode | Snap.ObjectSide;
            offMask = Snap.None;
        }

        /// <summary>
        /// Draws a marker at every placed point, plus the connector lines between
        /// consecutive points and a preview line out to the live cursor position - this
        /// replaces RenderOverlay() from CS1's MeasureTool.cs.
        ///
        /// This grabs the overlay buffer and draws directly on the main thread rather
        /// than scheduling a Burst job: the point count here is tiny (a handful of
        /// clicks), so the synchronous Complete() is not a meaningful cost. If you want
        /// this Burst-scheduled instead, move the loop body into an IJob and schedule it
        /// against overlayDeps.
        /// </summary>
        private void DrawOverlay()
        {
            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle overlayDeps);
            overlayDeps.Complete();

            if (Mod.Settings.SnapToNodes)
            {
                // Small, faint markers at every network node within snapping range of
                // the cursor, so the player can see what's actually snappable -
                // especially useful for buried pipes/tunnel nodes that aren't
                // otherwise visible at all until Underground View is on. Deliberately
                // dimmer and smaller than the real measurement points/lines so they
                // read as background context rather than competing with them visually.
                Color nodeMarkerColor = PointColor;
                nodeMarkerColor.a *= 0.6f;

                for (int i = 0; i < m_NearbyNodePositions.Count; i++)
                {
                    buffer.DrawCircle(nodeMarkerColor, m_NearbyNodePositions[i], NodeMarkerRadius);
                }
            }

            for (int i = 0; i < m_Points.Count; i++)
            {
                buffer.DrawCircle(PointColor, m_Points[i], PointRadius);

                if (i > 0)
                {
                    DrawSegmentOrPath(buffer, m_Points[i - 1], m_Points[i], m_SegmentPaths[i - 1]);
                }
            }

            if (m_HasCursorPosition)
            {
                buffer.DrawCircle(PointColor, m_CursorPosition, PointRadius);

                if (m_Points.Count > 0)
                {
                    // Computed fresh every frame (not cached like committed segments)
                    // since the cursor moves constantly - this is real pathfinding
                    // work happening every frame while previewing, which is the
                    // accepted cost of a live "does this connect to the network"
                    // preview; MaxPathfindNodesVisited keeps it bounded.
                    Entity lastPointNode = m_PointNodeEntities[m_PointNodeEntities.Count - 1];
                    float3 lastPoint = m_Points[m_Points.Count - 1];
                    TryFindNetworkPath(lastPointNode, m_CursorNodeEntity, lastPoint, m_CursorPosition, m_PreviewPathScratch);
                    DrawSegmentOrPath(buffer, lastPoint, m_CursorPosition, m_PreviewPathScratch);
                }
            }
        }

        /// <summary>Draws a network path if one was found (a real sequence of points
        /// tracing the actual pipe/subway route), or a plain straight line between
        /// the two endpoints if not (no path found, or one/both endpoints weren't
        /// snapped to the network at all).</summary>
        private void DrawSegmentOrPath(OverlayRenderSystem.Buffer buffer, float3 from, float3 to, List<float3> path)
        {
            if (path != null && path.Count > 1)
            {
                for (int p = 1; p < path.Count; p++)
                {
                    buffer.DrawLine(PointColor, new Line3.Segment(path[p - 1], path[p]), PointRadius * 0.25f);
                }
            }
            else
            {
                buffer.DrawLine(PointColor, new Line3.Segment(from, to), PointRadius * 0.25f);
            }
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;
    }
}