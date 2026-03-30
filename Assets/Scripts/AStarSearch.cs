using System;
using System.Collections.Generic;
using UnityEngine;

public class AStarSearch : MonoBehaviour
{   
    public enum AStarNodeVisitAction { Allow, Skip, Terminate }
    public enum AStarSearchFinishCause { Finished, TerminatedOnVisit, TerminatedOnPush, ExpansionLimit }
    
    public class AStarConfig<T> where T : IComparable<T>
    {
        public int MaxExpansions = 5000;
        // To customize which nodes to start with
        public List<T> StartingNodes = new List<T>();
        // Provides the edges from a current node to next node & path weights
        public Func<T, List<T>> FutureStatesProvider = (obj) => new List<T>();
        // Called on visiting an item popped from queue, returns the action
        public Func<T, AStarNodeVisitAction> NodeVisitHook = (obj) => AStarNodeVisitAction.Allow;
        // Called on pushing into queue, return false to skip this push
        public Func<T, AStarNodeVisitAction> QueuePushHook = (obj) => AStarNodeVisitAction.Allow;
        // Called on search finishes
        public Action<AStarSearchFinishCause, int> SearchFinishTrigger = (obj, currExpansion) => {};
    }
    
    public static void RunAStar<T>(AStarConfig<T> cfg) where T : IComparable<T>
    {
        HashSet<T> visitedStates = new HashSet<T>();
        // Init the PQ
        var openList = new PriorityQueue<T>();
        foreach (var root in cfg.StartingNodes)
        {
            openList.Push(root);
        }

        int expansions = 0;

        // Expand
        while (openList.Count > 0)
        {
            if (expansions > cfg.MaxExpansions)
            {
                cfg.SearchFinishTrigger.Invoke(AStarSearchFinishCause.ExpansionLimit, expansions);
                return;
            }

            T current = openList.Pop();

            // Check if we already visited this specific encoded state
            if (visitedStates.Contains(current)) continue;
            visitedStates.Add(current);
            expansions++;

            // Call the visit event
            switch (cfg.NodeVisitHook.Invoke(current))
            {
                case AStarNodeVisitAction.Skip:
                    continue;
                case AStarNodeVisitAction.Terminate:
                    cfg.SearchFinishTrigger.Invoke(AStarSearchFinishCause.TerminatedOnVisit, expansions);
                    return;
            }

            var nextStates = cfg.FutureStatesProvider.Invoke(current);
            foreach (var nextState in nextStates)
            {
                if (!visitedStates.Contains(nextState))
                {
                    // Call the visit event
                    switch (cfg.QueuePushHook.Invoke(nextState))
                    {
                        case AStarNodeVisitAction.Skip:
                            continue;
                        case AStarNodeVisitAction.Terminate:
                            cfg.SearchFinishTrigger.Invoke(AStarSearchFinishCause.TerminatedOnPush, expansions);
                            return;
                    }
                    // Allowed, then push into queue.
                    openList.Push(nextState);
                }
            }
        }
        
        cfg.SearchFinishTrigger.Invoke(AStarSearchFinishCause.Finished, expansions);
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