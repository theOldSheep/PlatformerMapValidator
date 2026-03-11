using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

public class PlatformerReachabilityPlanner : MonoBehaviour
{
    [SerializeField] private PlayerMovement player;
    [SerializeField] private StateManager stateManager; // Link to the new StateManager
    [SerializeField] private Transform goal;

    [Header("Search Settings")]
    [SerializeField] private int MaxExpansions = 5000;
    private const float StepDt = 0.02f;
    private const int StepsPerAction = 5;
    private const float GoalRadius = 0.5f;

    // Use GameStateSnapshot as the key for the visited set
    private readonly HashSet<GameStateSnapshot> visitedStates = new HashSet<GameStateSnapshot>();
    private readonly List<Vector3> lastPathPoints = new List<Vector3>();

    public class SearchNode : IComparable<SearchNode>
    {
        public GameStateSnapshot State;
        public Vector2 Position;
        public float G;
        public float H;
        public SearchNode Parent; // No longer causes a cycle
        public PlayerMovement.MoveAction ActionTaken;

        public float F => G + H;

        // A* typically picks the lowest F score
        public int CompareTo(SearchNode other)
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
    }

    [ContextMenu("Run State-Based A*")]
    public void RunAStar()
    {
        if (player == null || stateManager == null || goal == null) {
            return;
        }

        visitedStates.Clear();
        lastPathPoints.Clear();

        // 1. Capture the initial world state
        GameStateSnapshot startState = stateManager.CaptureState();
        Vector2 startPos = player.transform.position;
        Vector2 goalPos = goal.position;
        // Capture current world state and physics mode
        GameStateSnapshot initialWorldState = stateManager.CaptureState();
        SimulationMode2D previousMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;

        try {
            var openList = new PriorityQueue<SearchNode>();
            
            SearchNode root = new SearchNode {
                State = startState,
                Position = startPos,
                G = 0,
                H = Vector2.Distance(startPos, goalPos)
            };

            openList.Push(root);
            int expansions = 0;

            while (openList.Count > 0 && expansions < MaxExpansions)
            {
                SearchNode current = openList.Pop();

                // Check if we already visited this specific encoded state
                if (visitedStates.Contains(current.State)) continue;
                visitedStates.Add(current.State);
                expansions++;

                // Check Goal
                if (Vector2.Distance(current.Position, goalPos) < GoalRadius)
                {
                    Debug.Log($"Goal Found! Expansions: {expansions}");
                    GeneratePath(current);
                    return;
                }

                // 2. Restore the entire world to the current node's state
                // This resets coins, platforms, and player variables
                stateManager.RestoreState(current.State);

                // 3. Explore using the generic Action Abstraction
                var availableActions = player.GetCandidateActions();

                foreach (var action in availableActions)
                {
                    // Always restore back to 'current' before trying a different branch
                    stateManager.RestoreState(current.State);

                    // 4. Simulate the action
                    player.ApplyAction(action);
                    
                    // Advance physics for a few steps to see the result of the action
                    for (int i = 0; i < StepsPerAction; i++)
                    {
                        // 1. Ensure Raycasts/Collisions are in sync with current positions
                        Physics2D.SyncTransforms(); 
                        
                        // 2. Call the logic that updates velocities based on input/state
                        player.SimulateStep(StepDt); 
                        
                        // 3. Move the physical bodies
                        Physics2D.Simulate(StepDt);
                    }

                    // 5. Capture the resulting state
                    GameStateSnapshot nextState = stateManager.CaptureState();
                    Vector2 nextPos = player.transform.position;

                    if (!visitedStates.Contains(nextState))
                    {
                        float cost = Vector2.Distance(current.Position, nextPos);
                        openList.Push(new SearchNode {
                            State = nextState,
                            Position = nextPos,
                            G = current.G + cost,
                            H = Vector2.Distance(nextPos, goalPos),
                            Parent = current,
                            ActionTaken = action
                        });
                    }
                }
            }
            
            Debug.LogWarning("Goal not reached within MaxExpansions.");
        }
        finally 
        {
            // RESTORE: This block runs even if the search crashes
            // 1. Put the player/coins back to where they were before the button was clicked
            stateManager.RestoreState(initialWorldState);
            
            // 2. Give control back to Unity's automatic physics
            Physics2D.simulationMode = previousMode;

            // player.ApplyAction(new PlayerMovement.MoveAction(PlayerMovement.MoveActionType.None));
        }
    }

    private void GeneratePath(SearchNode endNode)
    {
        lastPathPoints.Clear();
        SearchNode temp = endNode;
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
    
    public class PriorityQueue<T> where T : IComparable<T>
    {
        private List<T> heap = new List<T>();

        public int Count => heap.Count;

        public void Push(T item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            // Bubble Up
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[i].CompareTo(heap[parent]) >= 0) break;
                
                T temp = heap[i];
                heap[i] = heap[parent];
                heap[parent] = temp;
                i = parent;
            }
        }

        public T Pop()
        {
            if (heap.Count == 0) return default;

            T result = heap[0];
            int lastIndex = heap.Count - 1;
            heap[0] = heap[lastIndex];
            heap.RemoveAt(lastIndex);

            lastIndex--;
            int i = 0;
            // Bubble Down
            while (true)
            {
                int left = i * 2 + 1;
                int right = i * 2 + 2;
                int smallest = i;

                if (left <= lastIndex && heap[left].CompareTo(heap[smallest]) < 0)
                    smallest = left;
                if (right <= lastIndex && heap[right].CompareTo(heap[smallest]) < 0)
                    smallest = right;

                if (smallest == i) break;

                T temp = heap[i];
                heap[i] = heap[smallest];
                heap[smallest] = temp;
                i = smallest;
            }

            return result;
        }
    }
}