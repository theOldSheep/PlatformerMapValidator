using System;
using System.Collections.Generic;
using UnityEngine;

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
    
    [Header("Reachability & Heurisitical Cache Mesh Generation Settings")]
    [SerializeField] private float PosMeshGranularity = 2.0f;
    [SerializeField] private float PosMeshRadius = 0.5f;
    [SerializeField] private int MaxExpansionsPerEpoch = 3;
    [SerializeField] private float MeshSnappingHeuristicWeight = 3.5f;
    
    [Header("Reachability & Heurisitical Cache Mesh Display Settings")]
    [SerializeField] private Gradient distanceGradient; // Set this in Inspector (Green to Red)
    [SerializeField] private float nodeSize = 0.15f;
    
    // Mesh Data
    private readonly Vector2Int[] Directions = new Vector2Int[]
    {
        new Vector2Int(-1,  1), new Vector2Int(0,  1), new Vector2Int(1,  1),
        new Vector2Int(-1,  0),                        new Vector2Int(1,  0),
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1)
    };
    private Dictionary<GameStateSnapshot, int> GameStateToMeshNodeIdx = new Dictionary<GameStateSnapshot, int>();
    private Dictionary<int, Vector2> MeshNodeIdxToPosition = new Dictionary<int, Vector2>();
    private Dictionary<int, Dictionary<int, float>> MeshEdges = new Dictionary<int, Dictionary<int, float>>();
    private int nextGameStateIdx = 0;


    // Visualization Data
    private readonly List<Vector2> exploredPositions = new List<Vector2>();
    private Dictionary<int, float> meshNodeDistances = new Dictionary<int, float>();
    private float maxDistanceFound = 1f;
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

    // Simulation cache for mesh generation
    private struct SimulationOutcome
    {
        public GameStateSnapshot ResultState;
        public Vector2 ResultPosition;
        public float WeightedCost;
    }
    private readonly Dictionary<GameStateSnapshot, List<SimulationOutcome>> simulationCache 
        = new Dictionary<GameStateSnapshot, List<SimulationOutcome>>();

    [ContextMenu("Run 8-Epoch Mesh Construction")]
    public void RunReachabilityMeshConstruction()
    {
        // 1. Clear everything at the start of a full run
        GameStateToMeshNodeIdx.Clear();
        MeshNodeIdxToPosition.Clear();
        MeshEdges.Clear();
        exploredPositions.Clear();
        simulationCache.Clear(); // Clear cache for a fresh run
        meshNodeDistances.Clear();
        nextGameStateIdx = 0;

        PhysicsSimulator.BeginSimulation(stateManager);
        try
        {
            Queue<PathSearchNode> discoveryQueue = new Queue<PathSearchNode>();
            
            PathSearchNode startNode = new PathSearchNode {
                State = stateManager.CaptureState(),
                Position = player.transform.position,
                G = 0
            };
            RecordMeshNode(startNode);
            discoveryQueue.Enqueue(startNode);

            while (discoveryQueue.Count > 0)
            {
                PathSearchNode currentNode = discoveryQueue.Dequeue();
                int currentIdx = GameStateToMeshNodeIdx[currentNode.State];

                foreach (Vector2Int dir in Directions)
                {
                    Vector2 targetNeighborPos = currentNode.Position + (new Vector2(dir.x, dir.y) * PosMeshGranularity);
                    
                    AStarSearch.RunAStar(new AStarSearch.AStarConfig<PathSearchNode>
                    {
                        MaxExpansions = MaxExpansionsPerEpoch,
                        StartingNodes = new List<PathSearchNode> { currentNode },
                        // GetNeighbors now uses the cache!
                        FutureStatesProvider = (curr) => GetNeighborsWithCache(curr, targetNeighborPos),
                        NodeVisitHook = (visited) => 
                        {
                            exploredPositions.Add(visited.Position);
                            if (IsInNeighborCell(visited.Position, targetNeighborPos))
                            {
                                if (!GameStateToMeshNodeIdx.ContainsKey(visited.State))
                                {
                                    RecordMeshNode(visited);
                                    discoveryQueue.Enqueue(visited);
                                }
                                AddEdge(currentIdx, GameStateToMeshNodeIdx[visited.State], visited.G);
                                return AStarSearch.AStarNodeVisitAction.Terminate;
                            }
                            return AStarSearch.AStarNodeVisitAction.Allow;
                        }
                    });
                }
            }
            CalculateShortestPathDistances();
        }
        finally { PhysicsSimulator.EndSimulation(stateManager); }
    }

    private List<PathSearchNode> GetNeighborsWithCache(PathSearchNode current, Vector2 targetPos)
    {
        if (!simulationCache.TryGetValue(current.State, out List<SimulationOutcome> outcomes))
        {
            outcomes = new List<SimulationOutcome>();
            var actions = player.GetCandidateActions();

            foreach (var action in actions)
            {
                PhysicsSimulator.SetGameState(stateManager, current.State);
                Vector2 startPos = player.transform.position;

                PhysicsSimulator.SimulatePlyAction(player, action, StepDt, StepsPerAction);

                float distance = Vector2.Distance(startPos, player.transform.position);
                
                // Calculate cost based on action type
                float multiplier = GetActionMultiplier(action.type);
                float stepCost = distance * multiplier;

                // Penalty for idling (optional): if player didn't move, give a flat time penalty
                if (distance < 0.01f) stepCost = StepDt * StepsPerAction * idleMultiplier;

                outcomes.Add(new SimulationOutcome
                {
                    ResultState = stateManager.CaptureState(),
                    ResultPosition = player.transform.position,
                    WeightedCost = stepCost
                });
            }
            simulationCache[current.State] = outcomes;
        }

        var neighborNodes = new List<PathSearchNode>(outcomes.Count);
        foreach (var outcome in outcomes)
        {
            neighborNodes.Add(new PathSearchNode
            {
                State = outcome.ResultState,
                Position = outcome.ResultPosition,
                Parent = current,
                // G now accumulates the weighted cost
                G = current.G + outcome.WeightedCost,
                H = Vector2.Distance(outcome.ResultPosition, targetPos) * MeshSnappingHeuristicWeight
            });
        }
        return neighborNodes;
    }

    private float GetActionMultiplier(PlayerMovement.MoveActionType type)
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

    private bool IsInNeighborCell(Vector2 pos, Vector2 targetCenter)
    {
        return Mathf.Abs(pos.x - targetCenter.x) < PosMeshRadius && 
               Math.Abs(pos.y - targetCenter.y) < PosMeshRadius;
    }

    private void AddEdge(int from, int to, float weight)
    {
        if (!MeshEdges.ContainsKey(from)) MeshEdges[from] = new Dictionary<int, float>();
        if (!MeshEdges[from].ContainsKey(to) || weight < MeshEdges[from][to])
            MeshEdges[from][to] = weight;
    }

    private void RecordMeshNode(PathSearchNode node)
    {
        if (!GameStateToMeshNodeIdx.ContainsKey(node.State))
        {
            nextGameStateIdx++;
            GameStateToMeshNodeIdx.Add(node.State, nextGameStateIdx);
            MeshNodeIdxToPosition.Add(nextGameStateIdx, node.Position);
        }
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
        meshNodeDistances.Clear();
        foreach (var idx in MeshNodeIdxToPosition.Keys) 
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

            if (!MeshEdges.ContainsKey(u)) continue;

            foreach (var edge in MeshEdges[u])
            {
                int v = edge.Key;
                float weight = edge.Value;
                float newDist = meshNodeDistances[u] + weight;

                if (newDist < meshNodeDistances[v])
                {
                    meshNodeDistances[v] = newDist;
                    queue.Push(new DijkstraNode { NodeIdx = v, Distance = newDist });
                    
                    if (newDist > maxDistanceFound) maxDistanceFound = newDist;
                }
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
        if (MeshNodeIdxToPosition.Count > 0)
        {
            foreach (var kvp in MeshNodeIdxToPosition)
            {
                int idx = kvp.Key;
                Vector2 pos = kvp.Value;

                if (meshNodeDistances.TryGetValue(idx, out float dist) && dist != float.MaxValue)
                {
                    // Normalize distance for the gradient (0.0 to 1.0)
                    float t = Mathf.Clamp01(dist / maxDistanceFound);
                    Gizmos.color = distanceGradient.Evaluate(t);
                }
                else
                {
                    // Unreachable nodes stay gray
                    Gizmos.color = Color.gray;
                }

                Gizmos.DrawSphere(pos, nodeSize);
            }
        }

        // 4. Draw Goal Path
        if (lastPathPoints.Count >= 2) {
            Gizmos.color = Color.green;
            for (int i = 0; i < lastPathPoints.Count - 1; i++)
                Gizmos.DrawLine(lastPathPoints[i], lastPathPoints[i + 1]);
        }
    }
}