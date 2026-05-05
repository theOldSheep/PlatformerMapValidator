using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting.Dependencies.NCalc;
using UnityEngine;

public class PathCentroidWalker : MonoBehaviour
{
    [Serializable]
    public class GeneratedPath
    {
        public List<Vector3> Positions = new List<Vector3>();
        public List<PathNode> Nodes = new List<PathNode>();
        public Color PathColor = Color.cyan;
        public string Label;
    }

    [Header("References")]
    [SerializeField] private PlayerMovement player;
    [SerializeField] private StateManager stateManager;

    [Header("Search Targets")]
    [SerializeField] private Transform intermediatePoint;
    [SerializeField] private Transform finalTarget;

    [Header("Search Settings")]
    [SerializeField] private int maxExpansions = 15000;
    [SerializeField] private int patience = 100;
    [SerializeField] private float reachTolerance = 0.6f;
    
    [Header("Generation Settings")]
    [SerializeField] private int numRandomPaths = 10;
    [SerializeField] private float IntermPtCollisionRadius = 5f;
    [SerializeField] private int numRandPointRetry = 100;
    [SerializeField] private Vector3 randomRangeMin = new Vector3(-5, -10, 0);
    [SerializeField] private Vector3 randomRangeMax = new Vector3(70, 5, 0);

    [Header("Loop Refinement Settings")]
    [SerializeField] private bool enableRefinement = true;
    [SerializeField] private float loopDetectionRadius = 2.0f;
    [SerializeField] private int maxRefinementPasses = 3;

    [Header("Action Costs")]
    [SerializeField] private float moveCost = 1.0f;
    [SerializeField] private float jumpCost = 3.0f;
    [SerializeField] private float dashCost = 4.0f;

    private List<GeneratedPath> allGeneratedPaths = new List<GeneratedPath>();
    private List<Vector2> allConsideredNodePos = new List<Vector2>();
    // Cache for physics simulations to speed up re-searching and refinement
    private readonly Dictionary<GameStateSnapshot, Dictionary<PlayerMovement.MoveAction, PhysicsSimulator.SimulationOutcome>> simulationCache = 
        new Dictionary<GameStateSnapshot, Dictionary<PlayerMovement.MoveAction, PhysicsSimulator.SimulationOutcome>>();

    public class PathNode : IComparable<PathNode>
    {
        public GameStateSnapshot State;
        public Vector3 Position;
        public float GCost;
        public float HCost;
        public PathNode Parent;
        
        // Patience fields
        public int PatienceLeft;
        public float BestDistInPath;

        public float FCost => GCost + HCost;

        public int CompareTo(PathNode other)
        {
            int res = FCost.CompareTo(other.FCost);
            return res == 0 ? HCost.CompareTo(other.HCost) : res;
        }

        public override bool Equals(object obj) => obj is PathNode other && State.Equals(other.State);
        public override int GetHashCode() => State.GetHashCode();
    }

    [ContextMenu("Generate All Potential Paths")]
    public void GenerateAllPaths()
    {
        if (player == null || stateManager == null || finalTarget == null) return;

        allGeneratedPaths.Clear();
        allConsideredNodePos.Clear();

        // 1. Straight-to-the-goal path
        GenerateSinglePath(finalTarget.position, finalTarget.position, Color.white, "Straight Path");

        // 2. Manual Intermediate Point Path
        if (intermediatePoint != null)
            GenerateSinglePath(intermediatePoint.position, finalTarget.position, Color.yellow, "Manual Midpoint");

        // 3. Randomized Paths
        for (int i = 0; i < numRandomPaths; i++)
        {
            Vector3 randMid = GetRandomMidpoint();
            Color randColor = Color.HSVToRGB((float)i / numRandomPaths, 0.8f, 1.0f);
            GenerateSinglePath(randMid, finalTarget.position, randColor, $"Random Path {i + 1}");
        }
    }

    // Note: does not handle physics simulator. Wrapper needed.
    public void GenerateSinglePath(Vector3 midPoint, Vector3 target, Color color, string label)
    {
        if (player == null || stateManager == null) return;

        // The mid (although might be cut off by the loop cutting) should be marked considered
        allConsideredNodePos.Add(midPoint);

        PhysicsSimulator.BeginSimulation(stateManager);
        try
        {
            // --- Step 1: Initial Raw Search ---
            PathNode startNode = CreateInitialNode(stateManager.CaptureState(), midPoint);
            PathNode bestMid = startNode;

            // Stage 1: Search toward intermediate point with patience
            PathNode reachedMid = RunStage(startNode, midPoint, true, (n) => {
                if (n.HCost < bestMid.HCost) bestMid = n;
            });

            PathNode stage2Start = reachedMid ?? bestMid;
            
            // Stage 2: Search toward final target from the found middlepoint
            PathNode finalNode = RunStage(stage2Start, target, false, null);

            if (finalNode == null)
            {
                Debug.LogWarning("Target unreachable. Showing path to best found state.");
                finalNode = stage2Start;
            }

            List<PathNode> currentPath = ReconstructNodeList(finalNode);

            // --- Step 2: Loop Refinement ---
            if (enableRefinement)
            {
                currentPath = RefinePath(currentPath);
            }

            // --- Step 3: Finalize ---
            var pathNodes = currentPath;
            var pathPos = currentPath.Select(n => n.Position).ToList();
            // Random offset to display different paths
            for (int i = 0; i < pathPos.Count; i ++)
            {
                var currPos = pathPos[i];
                currPos.x += (UnityEngine.Random.value - 0.5f) * 0.35f;
                currPos.y += (UnityEngine.Random.value - 0.5f) * 0.35f;
                pathPos[i] = currPos;
            }

            var newPath = new GeneratedPath {
                Nodes = pathNodes,
                Positions = pathPos,
                PathColor = color,
                Label = label
            };
            allGeneratedPaths.Add(newPath);
            foreach (var pt in newPath.Positions)
            {
                allConsideredNodePos.Add(pt);
            }
            Debug.Log($"Path generated with {pathNodes.Count} nodes.");
        }
        finally
        {
            PhysicsSimulator.EndSimulation(stateManager);
        }
    }

    private List<PathNode> RefinePath(List<PathNode> nodes)
    {
        for (int pass = 0; pass < maxRefinementPasses; pass++)
        {
            bool loopFound = false;

            for (int i = 0; i < nodes.Count - 2; i++)
            {
                // Find first faraway index to get out of loop detection
                int firstFarawayIdx = -1;
                for (int k = i + 1; k < nodes.Count; k++)
                {
                    if (Vector3.Distance(nodes[i].Position, nodes[k].Position) >= loopDetectionRadius)
                    {
                        firstFarawayIdx = k;
                        break;
                    }
                }
                if (firstFarawayIdx == -1) continue;
                // Loop from back to the faraway idx to get loop-back state
                for (int j = nodes.Count - 1; j >= firstFarawayIdx; j--)
                {
                    if (Vector3.Distance(nodes[i].Position, nodes[j].Position) < loopDetectionRadius)
                    {
                        // Attempt to bridge nodes[i] to nodes[j].Position
                        PathNode bridgeEnd = RunStage(nodes[i], nodes[j].Position, false, null, 150); // Small expansion limit for bridge

                        if (bridgeEnd != null)
                        {
                            // Bridge successful. Now re-run Stage 2 from the end of the bridge to final target
                            PathNode suffixEnd = RunStage(bridgeEnd, finalTarget.position, false, null);
                            
                            if (suffixEnd != null)
                            {
                                nodes = ReconstructNodeList(suffixEnd);
                                loopFound = true;
                                break;
                            }
                        }
                    }
                }
                if (loopFound) break;
            }
            if (!loopFound) break;
        }
        return nodes;
    }

    private PathNode RunStage(PathNode start, Vector3 targetPos, bool usePatience, Action<PathNode> onVisit = null, int limit = -1)
    {
        PathNode resultNode = null;
        int expansionLimit = limit > 0 ? limit : maxExpansions;

        var config = new AStarSearch.AStarConfig<PathNode>
        {
            MaxExpansions = expansionLimit,
            StartingNodes = new List<PathNode> { start },
            FutureStatesProvider = (currentNode) => GetNeighbors(currentNode, targetPos, usePatience),
            NodeVisitHook = (node) => 
            {
                onVisit?.Invoke(node);
                if (Vector3.Distance(node.Position, targetPos) <= reachTolerance)
                {
                    resultNode = node;
                    return AStarSearch.AStarNodeVisitAction.Terminate;
                }
                return AStarSearch.AStarNodeVisitAction.Allow;
            }
        };

        AStarSearch.RunAStar(config);
        return resultNode;
    }

    private Vector3 GetRandomMidpoint()
    {
        Vector3 center = Vector2.zero;
        for (int i = 0; i < numRandPointRetry; i ++) {
            Vector3 start = player.transform.position;
            Vector3 end = finalTarget.position;
            center = (start + end) / 2f;
            center += new Vector3(UnityEngine.Random.Range(randomRangeMin.x, randomRangeMax.x), UnityEngine.Random.Range(randomRangeMin.y, randomRangeMax.y), 0);
            // Too close to existing points?
            bool valid = true;
            foreach (var existing in allConsideredNodePos)
            {
                if (Vector2.Distance(existing, center) < IntermPtCollisionRadius)
                {
                    valid = false;
                    break;
                }
            }
            if (valid) break;
        }
        return center;
    }

    private List<PathNode> GetNeighbors(PathNode current, Vector3 targetPos, bool usePatience)
    {
        List<PathNode> neighbors = new List<PathNode>();
        if (usePatience && current.PatienceLeft <= 0) return neighbors;

        if (!simulationCache.TryGetValue(current.State, out var actionMap))
        {
            actionMap = new Dictionary<PlayerMovement.MoveAction, PhysicsSimulator.SimulationOutcome>();
            stateManager.RestoreState(current.State);
            foreach (var action in player.GetCandidateActions())
            {
                var outcomes = PhysicsSimulator.SimulatePlyAction(stateManager, current.State, player, action, true);
                if (outcomes.Count > 0) actionMap[action] = outcomes.Last();
            }
            simulationCache[current.State] = actionMap;
        }

        foreach (var kvp in actionMap)
        {
            var outcome = kvp.Value;
            float dist = Vector3.Distance(outcome.ResultPosition, targetPos);
            
            PathNode newNode = new PathNode
            {
                State = outcome.ResultState,
                Position = outcome.ResultPosition,
                GCost = current.GCost + CalculateActionCost(kvp.Key),
                HCost = dist,
                Parent = current
            };

            if (usePatience)
            {
                if (dist < current.BestDistInPath - 0.01f)
                {
                    newNode.PatienceLeft = patience;
                    newNode.BestDistInPath = dist;
                }
                else
                {
                    newNode.PatienceLeft = current.PatienceLeft - 1;
                    newNode.BestDistInPath = current.BestDistInPath;
                }
            }
            neighbors.Add(newNode);
        }
        return neighbors;
    }

    private List<PathNode> ReconstructNodeList(PathNode endNode)
    {
        List<PathNode> path = new List<PathNode>();
        PathNode temp = endNode;
        while (temp != null)
        {
            path.Add(temp);
            temp = temp.Parent;
        }
        path.Reverse();
        return path;
    }

    private PathNode CreateInitialNode(GameStateSnapshot state, Vector3 target)
    {
        float dist = Vector3.Distance(player.transform.position, target);
        return new PathNode {
            State = state, Position = player.transform.position,
            GCost = 0, HCost = dist, PatienceLeft = patience, BestDistInPath = dist
        };
    }

    private float CalculateActionCost(PlayerMovement.MoveAction action)
    {
        switch (action.type)
        {
            case PlayerMovement.MoveActionType.Jump: return jumpCost;
            case PlayerMovement.MoveActionType.Dash: return dashCost;
            default: return moveCost;
        }
    }

    private void OnDrawGizmos()
    {
        foreach (var path in allGeneratedPaths)
        {
            if (path.Positions == null || path.Positions.Count < 2) continue;
            Gizmos.color = path.PathColor;
            for (int i = 0; i < path.Positions.Count - 1; i++)
            {
                Gizmos.DrawLine(path.Positions[i], path.Positions[i + 1]);
                // Draw small spheres to see the path density
                Gizmos.DrawSphere(path.Positions[i], 0.05f);
            }
            Gizmos.DrawSphere(path.Positions.Last(), 0.25f);
        }
        foreach (var reachpt in allConsideredNodePos)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(reachpt, 0.1f);
        }
    }
}