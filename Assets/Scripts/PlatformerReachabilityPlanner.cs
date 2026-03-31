using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

public class PlatformerReachabilityPlanner : MonoBehaviour
{
    [SerializeField] private PlayerMovement player;
    [SerializeField] private StateManager stateManager; // Link to the new StateManager
    [SerializeField] private Transform goal;

    [Header("Action Cost Settings")]
    [SerializeField] private float idleMultiplier = 0.5f;
    [SerializeField] private float moveMultiplier = 1.0f;
    [SerializeField] private float jumpMultiplier = 3.0f;
    [SerializeField] private float dashMultiplier = 3.0f;

    [Header("Search Settings")]
    [SerializeField] private int MaxExpansions = 5000;
    [SerializeField] private float StepDt = 0.02f;
    [SerializeField] private int StepsPerAction = 3;
    [SerializeField] private float GoalReachRadiusTolerance = 0.5f;
    
    [Header("Reachability Display Settings")]
    [SerializeField] private bool showClusteredHeatmap = true;
    [SerializeField] private float HeatMapGranularity = 2.0f;
    [SerializeField] private float nodeSize = 0.15f;
    [SerializeField] private Gradient distanceGradient;
    
    // Mesh Statistics Tracking
    private int cacheHits;
    private int cacheMisses;
    // State transition cache
    private struct SimulationOutcome
    {
        public GameStateSnapshot ResultState;
        public Vector2 ResultPosition;
    }
    private readonly Dictionary<GameStateSnapshot, Dictionary<PlayerMovement.MoveAction, SimulationOutcome>> simulationCache = new Dictionary<GameStateSnapshot, Dictionary<PlayerMovement.MoveAction, SimulationOutcome>>();
    // Mesh Data
    private Dictionary<GameStateSnapshot, int> GameStateToMeshNodeIdx = new Dictionary<GameStateSnapshot, int>();
    private Dictionary<int, Vector2> MeshNodeIdxToPosition = new Dictionary<int, Vector2>();
    private Dictionary<int, Dictionary<int, float>> MeshEdges = new Dictionary<int, Dictionary<int, float>>();
    private Vector2 meshOrigin = Vector2.zero;
    private int nextGameStateIdx = 0;
    // Mesh Visualization Data
    private Dictionary<Vector2, float> rawExploredStatesAndDists = new Dictionary<Vector2, float>();
    private Dictionary<Vector2Int, float> clusteredExploredStates = new Dictionary<Vector2Int, float>();
    private float maxDistanceFound = 1f;

    // Path finding Visualization Data
    private readonly List<Vector3> lastPathPoints = new List<Vector3>();

    // Node used for both search and mesh construction
    public class PathSearchNode : IComparable<PathSearchNode>
    {
        public GameStateSnapshot State;
        public Vector2 Position;
        public PathSearchNode Parent;
        
        public float G;
        public float H;
        public float F => G + H;

        // A* typically picks the lowest F score
        public int CompareTo(PathSearchNode other)
        {
            // Min-heap: We want the smallest F at the top
            int result = F.CompareTo(other.F);
            if (result == 0) 
            {
                // Tie-breaker: Prefer nodes closer to the goal (lower H) 
                // or further from start (higher G) to find path faster
                return H.CompareTo(other.H); 
            }
            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj is PathSearchNode other)
            {
                // Use the Snapshot's built-in equality logic
                return State.Equals(other.State);
            }
            return false;
        }

        public override int GetHashCode()
        {
            // Use the Snapshot's cached hash
            return State.GetHashCode();
        }
    }

    /*
     *
     *   Below: mesh construction
     *
     */

    // Entry point for mesh generation
    [ContextMenu("Run 8-Epoch Mesh Construction")]
    public void RunReachabilityMeshConstruction()
    {
        // Reset Stats and Data
        ResetMeshSearchData();

        PhysicsSimulator.BeginSimulation(stateManager);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            Queue<PathSearchNode> discoveryQueue = new Queue<PathSearchNode>();
            PathSearchNode startNode = new PathSearchNode {
                State = stateManager.CaptureState(),
                Position = meshOrigin
            };
            PathSearchNode newVisitNode;
            RecordDiscoveredMeshNode(startNode);
            discoveryQueue.Enqueue(startNode);

            while (discoveryQueue.Count > 0)
            {
                PathSearchNode currentNode = discoveryQueue.Dequeue();
                int currentIdx = GameStateToMeshNodeIdx[currentNode.State];

                foreach (var dynamicTransformation in GetStateDynamics(currentNode.State))
                {
                    var action = dynamicTransformation.Key;
                    var simOutcome = dynamicTransformation.Value;
                    // Not visited yet; by doing so we only push each state once.
                    if (!GameStateToMeshNodeIdx.ContainsKey(simOutcome.ResultState))
                    {
                        newVisitNode = new PathSearchNode {
                            State = simOutcome.ResultState,
                            Position = simOutcome.ResultPosition
                        };
                        RecordDiscoveredMeshNode(newVisitNode);
                        discoveryQueue.Enqueue(newVisitNode);
                    }
                    AddEdge(currentIdx, GameStateToMeshNodeIdx[simOutcome.ResultState], GetActionCost(action.type));
                }
            }
            // Update distances for final coloring & statistics
            CalculateShortestPathDistances();
            // Print stats
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            PrintStatistics(elapsedMs);
        }
        finally { PhysicsSimulator.EndSimulation(stateManager); }
    }

    private void ResetMeshSearchData()
    {
        GameStateToMeshNodeIdx.Clear();
        MeshNodeIdxToPosition.Clear();
        MeshEdges.Clear();
        rawExploredStatesAndDists.Clear();
        clusteredExploredStates.Clear();
        cacheHits = 0;
        cacheMisses = 0;
        nextGameStateIdx = 0;
        meshOrigin = player.transform.position;
    }

    private Dictionary<PlayerMovement.MoveAction, SimulationOutcome> GetStateDynamics(GameStateSnapshot state)
    {
        if (simulationCache.ContainsKey(state))
        {
            cacheHits++;
        }
        else
        {
            cacheMisses++;
            Dictionary<PlayerMovement.MoveAction, SimulationOutcome> outcomes = new Dictionary<PlayerMovement.MoveAction, SimulationOutcome>();
            var actions = player.GetCandidateActions();

            foreach (var action in actions)
            {
                PhysicsSimulator.SetGameState(stateManager, state);

                PhysicsSimulator.SimulatePlyAction(player, action, StepDt, StepsPerAction);

                outcomes[action] = new SimulationOutcome
                {
                    ResultState = stateManager.CaptureState(),
                    ResultPosition = player.transform.position
                };
            }
            simulationCache[state] = outcomes;
        }

        return simulationCache[state];
    }

    private float GetActionCost(PlayerMovement.MoveActionType type)
    {
        switch (type)
        {
            case PlayerMovement.MoveActionType.Left:
            case PlayerMovement.MoveActionType.Right:
                return moveMultiplier;
            case PlayerMovement.MoveActionType.Jump:
                return jumpMultiplier;
            case PlayerMovement.MoveActionType.Dash:
                return dashMultiplier;
            case PlayerMovement.MoveActionType.None:
            default:
                return idleMultiplier;
        }
    }

    private Vector2Int GetMeshDisplayCoordinate(Vector2 pos)
    {
        return new Vector2Int(
            Mathf.RoundToInt((pos.x - meshOrigin.x) / HeatMapGranularity),
            Mathf.RoundToInt((pos.y - meshOrigin.y) / HeatMapGranularity)
        );
    }

    private void AddEdge(int from, int to, float weight)
    {
        if (!MeshEdges.ContainsKey(from)) MeshEdges[from] = new Dictionary<int, float>();
        MeshEdges[from][to] = weight;
    }

    private void RecordDiscoveredMeshNode(PathSearchNode node)
    {
        if (!GameStateToMeshNodeIdx.ContainsKey(node.State))
        {
            nextGameStateIdx++;
            GameStateToMeshNodeIdx.Add(node.State, nextGameStateIdx);
            MeshNodeIdxToPosition.Add(nextGameStateIdx, node.Position);
        }
    }

    private void PrintStatistics(long elapsedMs)
    {
        double hitRate = (double)cacheHits / (cacheHits + cacheMisses) * 100;

        Debug.Log($"<b>Reachability Mesh Stats:</b>\n" +
                  $"- Time taken (ms): {elapsedMs}\n" +
                  $"- Reached State Nodes: {rawExploredStatesAndDists.Count}\n" +
                  $"- Cubes drawn configured by Mesh Granularity: {clusteredExploredStates.Count}\n" +
                  $"- Cache Hit Rate: {hitRate:F2}% ({cacheHits} hits / {cacheMisses} misses)\n");
    }

    private struct DijkstraNode : IComparable<DijkstraNode>
    {
        public int NodeIdx;
        public float Distance;

        public int CompareTo(DijkstraNode other)
        {
            // Min-priority: smaller distances come first
            return Distance.CompareTo(other.Distance);
        }
    }
    private void CalculateShortestPathDistances()
    {
        if (MeshNodeIdxToPosition.Count == 0) return;

        // Start node is index 1 (the first one recorded)
        int startNodeIdx = 1;
        
        // Initialize distances
        Dictionary<int, float> meshNodeDistances = new Dictionary<int, float>();
        foreach (var idx in GameStateToMeshNodeIdx.Values) 
            meshNodeDistances[idx] = float.MaxValue;
        meshNodeDistances[startNodeIdx] = 0;

        // Use the PriorityQueue from AStarSearch
        var queue = new AStarSearch.PriorityQueue<DijkstraNode>();
        var visited = new HashSet<int>();

        queue.Push(new DijkstraNode { NodeIdx = startNodeIdx, Distance = 0f });

        maxDistanceFound = 0.1f; // Avoid division by zero in Gizmos

        while (queue.Count > 0)
        {
            DijkstraNode current = queue.Pop();
            int u = current.NodeIdx;

            if (visited.Contains(u)) continue;
            visited.Add(u);

            meshNodeDistances[u] = current.Distance;
            if (current.Distance > maxDistanceFound)
            {
                maxDistanceFound = current.Distance;
            }

            if (!MeshEdges.ContainsKey(u)) continue;
            foreach (var edge in MeshEdges[u])
            {
                int v = edge.Key;
                float newDist = meshNodeDistances[u] + edge.Value;

                queue.Push(new DijkstraNode { NodeIdx = v, Distance = newDist });
            }
        }
        
        // Transcribe Int index distances to vector2 distances for rendering
        foreach (var item in meshNodeDistances)
        {
            var dst = item.Value;
            // Individual point
            var posVec = MeshNodeIdxToPosition[item.Key];
            rawExploredStatesAndDists[posVec] = dst;
            // Heatmap
            var gridCoord = GetMeshDisplayCoordinate(posVec);
            if (!clusteredExploredStates.ContainsKey(gridCoord) || clusteredExploredStates[gridCoord] > dst)
            {
                clusteredExploredStates[gridCoord] = dst;
            }
        }

    }


    /*
     *
     *   Below: point path search
     *
     */

    [ContextMenu("Run A* (Reach Goal)")]
    public void RunAStar() => RunInternalSearch(heuristicWeight: 1f);

    [ContextMenu("Run Dijkstra (Reachability Map)")]
    public void RunDijkstra() => RunInternalSearch(heuristicWeight: 0f);

    public void RunInternalSearch(float heuristicWeight)
    {
        if (player == null || stateManager == null || goal == null) {
            return;
        }

        PhysicsSimulator.BeginSimulation(stateManager);
        try {
            Vector2 goalPos = goal.position;
            // Starting with current position
            var StartingNodes = new List<PathSearchNode>
            {
                new PathSearchNode()
                {
                    State = stateManager.CaptureState(),
                    Position = player.transform.position,
                    Parent = null,
                    G = 0f,
                    H = 0f
                }
            };
            // Movements' state transformation provider
            Func<PathSearchNode, List<PathSearchNode>> FutureStatesProvider = (current) =>
            {
                var result = new List<PathSearchNode>();
                var availableActions = player.GetCandidateActions();

                foreach (var action in availableActions)
                {
                    PhysicsSimulator.SetGameState(stateManager, current.State);

                    PhysicsSimulator.SimulatePlyAction(player, action, StepDt, StepsPerAction);

                    // 5. Capture the resulting state
                    GameStateSnapshot nextState = stateManager.CaptureState();
                    Vector2 nextPos = player.transform.position;

                    float newG = current.G + Vector2.Distance(current.Position, nextPos);
                    float heuristicValue = PathSearchHeuristic(nextPos, goalPos, player);
                    
                    result.Add(new PathSearchNode {
                        State = nextState,
                        Position = nextPos,
                        Parent = current,
                        G = newG,
                        H = heuristicWeight * heuristicValue
                    });
                }
                return result;
            };
            // Node visited is close to target? Terminate.
            Func<PathSearchNode, AStarSearch.AStarNodeVisitAction> NodeVisitHook = (node) =>
            {
                if (Vector2.Distance(node.Position, goalPos) < GoalReachRadiusTolerance)
                {
                    GeneratePath(node);
                    return AStarSearch.AStarNodeVisitAction.Terminate;
                }
                return AStarSearch.AStarNodeVisitAction.Allow;
            };
            // The printing statement
            Action<AStarSearch.AStarSearchFinishCause, int> SearchFinishTrigger = (cause, expansion) =>
            {
                switch (cause)
                {
                    case AStarSearch.AStarSearchFinishCause.TerminatedOnVisit:
                        Debug.Log($"Search finished successfully with {expansion} expansions.");
                        break;
                    case AStarSearch.AStarSearchFinishCause.ExpansionLimit:
                        Debug.Log("Search max expansion reached.");
                        break;
                }
            };
            // Construct cfg and execute
            AStarSearch.AStarConfig<PathSearchNode> cfg = new AStarSearch.AStarConfig<PathSearchNode>
            {
                MaxExpansions = MaxExpansions,
                StartingNodes = StartingNodes,
                FutureStatesProvider = FutureStatesProvider,
                NodeVisitHook = NodeVisitHook,
                SearchFinishTrigger = SearchFinishTrigger
            };

            AStarSearch.RunAStar(cfg);
        } 
        finally
        {
            PhysicsSimulator.EndSimulation(stateManager);
        }
    }

    private float PathSearchHeuristic(Vector2 pos, Vector2 goalPos, PlayerMovement ghost)
    {
        // return 0;
        return Vector2.Distance(pos, goalPos);
    }

    private void GeneratePath(PathSearchNode endNode)
    {
        lastPathPoints.Clear();
        PathSearchNode temp = endNode;
        while (temp.Parent != null)
        {
            lastPathPoints.Add(temp.Position);
            temp = temp.Parent;
        }
        lastPathPoints.Reverse();
    }


    /*
     *
     *   Below: display
     *
     */

    private void OnDrawGizmos()
    {
        if (showClusteredHeatmap)
        {
            foreach (var cell in clusteredExploredStates)
            {
                float t = Mathf.Clamp01(cell.Value / maxDistanceFound);
                Gizmos.color = distanceGradient.Evaluate(t).WithAlpha(0.2f);
                Vector3 worldPos = meshOrigin;
                worldPos += new Vector3(cell.Key.x * HeatMapGranularity, cell.Key.y * HeatMapGranularity, 0);
                // Draw a cube representing the "cell"
                Gizmos.DrawCube(worldPos, Vector3.one * HeatMapGranularity * 0.9f);
            }
        }
        else
        {
            foreach (var state in rawExploredStatesAndDists)
            {
                float t = Mathf.Clamp01(state.Value / maxDistanceFound);
                Gizmos.color = distanceGradient.Evaluate(t).WithAlpha(0.2f);
                Gizmos.DrawSphere(state.Key, nodeSize);
            }
        }

        // 4. Draw Goal Path
        if (lastPathPoints.Count >= 2) {
            Gizmos.color = Color.green.WithAlpha(0.2f);
            for (int i = 0; i < lastPathPoints.Count - 1; i++)
                Gizmos.DrawLine(lastPathPoints[i], lastPathPoints[i + 1]);
        }
    }
}