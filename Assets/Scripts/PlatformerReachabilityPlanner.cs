using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class PlatformerReachabilityPlanner : MonoBehaviour
{
    [SerializeField] private PlayerMovement player;
    [SerializeField] private StateManager stateManager; // Link to the new StateManager
    [SerializeField] private Transform goal;

    [Header("Search Settings")]
    [SerializeField] private int MaxExpansions = 5000;
    [SerializeField] private float StepDt = 0.02f;
    [SerializeField] private int StepsPerAction = 3;
    [SerializeField] private float GoalReachRadiusTolerance = 0.5f;
    [Header("Reachability & Heurisitical Cache Mesh Settings")]
    [SerializeField] private int MeshExpansionPerNode = 100;
    [SerializeField] private int MeshExpansionPathLen = 10;
    [SerializeField] private float PosMeshGanularity = 2;
    [SerializeField] private float PosMeshAcceptanceRadius = 0.25f;
    [SerializeField] private float MeshSnappingHeuristicWeight = 3.5f;
    private Dictionary<GameStateSnapshot, int> GameStateToMeshNodeIdx = new Dictionary<GameStateSnapshot, int>();
    private Dictionary<int, GameStateSnapshot> MeshNodeIdxToGameState = new Dictionary<int, GameStateSnapshot>();
    private Dictionary<int, Dictionary<int, float>> MeshEdges = new Dictionary<int, Dictionary<int, float>>();
    private int nextGameStateIdx = 0;


    // For drawing the last generated path
    private readonly List<Vector3> lastPathPoints = new List<Vector3>();


    public class PathSearchNode : IComparable<PathSearchNode>
    {
        public GameStateSnapshot State;
        public Vector2 Position;
        public PathSearchNode Parent;

        // For mesh construction only - terminate based on route length
        public int PathLen;
        
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


    [ContextMenu("Run Mesh Construction")]
    public void RunReachabilityMeshConstruction()
    {
        if (player == null || stateManager == null || goal == null) {
            return;
        }

        // Clear mesh info
        GameStateToMeshNodeIdx.Clear();
        MeshNodeIdxToGameState.Clear();
        MeshEdges.Clear();
        nextGameStateIdx = 0;

        PhysicsSimulator.BeginSimulation(stateManager);
        try {
            Stack<PathSearchNode> nextNodes = new Stack<PathSearchNode>();

            GameStateSnapshot startState = stateManager.CaptureState();
            PathSearchNode startNode = new PathSearchNode()
            {
                State = startState,
                Position = player.transform.position,
                Parent = null,
                PathLen = 0,
                G = 0f,
                H = 0f
            };
            nextNodes.Push(startNode);

            RecordMeshNode(startNode);

            while (nextNodes.Count > 0) {
                Debug.Log(nextGameStateIdx);
                // Plan from the next reachable mesh node
                PathSearchNode currNode = nextNodes.Pop();
                GameStateSnapshot currState = currNode.State;
                Vector2 currPos = currNode.Position;
                int currMeshNodeIdx = GameStateToMeshNodeIdx[currState];

                // Starting with current position
                var StartingNodes = new List<PathSearchNode> 
                {
                    currNode
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
                        float heuristicValue = DistToMeshNode(nextPos, currPos, player);
                        
                        result.Add(new PathSearchNode {
                            State = nextState,
                            Position = nextPos,
                            Parent = current,
                            PathLen = current.PathLen + 1,
                            G = newG,
                            H = MeshSnappingHeuristicWeight * heuristicValue
                        });
                    }
                    return result;
                };
                // Node visited is close to target? Terminate.
                Func<PathSearchNode, AStarSearch.AStarNodeVisitAction> NodeVisitHook = (node) =>
                {
                    if (node.PathLen > MeshExpansionPathLen)
                    {
                        return AStarSearch.AStarNodeVisitAction.Skip;
                    }
                    if (DistToMeshNode(node.Position, currPos, player) * PosMeshGanularity < PosMeshAcceptanceRadius)
                    {
                        // Reached a new mesh node
                        if (! GameStateToMeshNodeIdx.ContainsKey(node.State))
                        {
                            RecordMeshNode(node);
                            // Add the found node to the stack
                            nextNodes.Push(node);
                        }
                        // Record connection
                        int reachedMeshNodeIdx = GameStateToMeshNodeIdx[node.State];
                        float minDist = node.G;
                        var currMeshDistDict = MeshEdges[currMeshNodeIdx];
                        if (currMeshDistDict.ContainsKey(reachedMeshNodeIdx))
                        {
                            minDist = Math.Min(minDist, currMeshDistDict[reachedMeshNodeIdx]);
                        }
                        MeshEdges[currMeshNodeIdx][reachedMeshNodeIdx] = minDist;
                    }
                    return AStarSearch.AStarNodeVisitAction.Allow;
                };
                // Construct cfg and execute
                AStarSearch.AStarConfig<PathSearchNode> cfg = new AStarSearch.AStarConfig<PathSearchNode>
                {
                    MaxExpansions = MeshExpansionPerNode,
                    StartingNodes = StartingNodes,
                    FutureStatesProvider = FutureStatesProvider,
                    NodeVisitHook = NodeVisitHook
                };

                AStarSearch.RunAStar(cfg);
            }
        } 
        finally
        {
            PhysicsSimulator.EndSimulation(stateManager);
        }
    }

    private float DistToMeshNode(Vector2 pos, Vector2 startPos, PlayerMovement ghost)
    {
        float xDiff = Math.Abs(startPos.x - pos.x) / PosMeshGanularity;
        float yDiff = Math.Abs(startPos.y - pos.y) / PosMeshGanularity;
        xDiff -= (int)xDiff;
        yDiff -= (int)yDiff;
        return (float)Math.Sqrt(xDiff * xDiff + yDiff * yDiff);
    }

    private void RecordMeshNode(PathSearchNode node)
    {
        if (! GameStateToMeshNodeIdx.ContainsKey(node.State))
        {
            nextGameStateIdx ++;
            GameStateToMeshNodeIdx.Add(node.State, nextGameStateIdx);
            MeshNodeIdxToGameState.Add(nextGameStateIdx, node.State);
            MeshEdges.Add(nextGameStateIdx, new Dictionary<int, float>());
        }
    }


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

    private void OnDrawGizmos()
    {
        if (lastPathPoints.Count < 2) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < lastPathPoints.Count - 1; i++)
        {
            Gizmos.DrawLine(lastPathPoints[i], lastPathPoints[i+1]);
        }
    }
}