using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class PhysicsSimulator : MonoBehaviour
{
    // Game state store & recovery
    private static GameStateSnapshot initialWorldState;
    private static SimulationMode2D previousMode;
    private static bool isInSim = false;
    
    public struct SimulationOutcome
    {
        public GameStateSnapshot ResultState;
        public Vector2 ResultPosition;
    }

    public static void BeginSimulation(StateManager stateManager)
    {
        if (isInSim)
        {
            throw new Exception("Already in physics simulation. Can not begin simulation.");
        }
        // Capture current world state and physics mode
        initialWorldState = stateManager.CaptureState();
        previousMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;

        isInSim = true;
    }

    public static void EndSimulation(StateManager stateManager)
    {
        if (! isInSim)
        {
            throw new Exception("Not yet in physics simulation. Can not end simulation.");
        }
        
        SetGameState(stateManager, initialWorldState);
        
        Physics2D.simulationMode = previousMode;
        isInSim = false;
    }
    
    public static void SetGameState(StateManager stateManager, GameStateSnapshot gameState)
    {
        if (! isInSim)
        {
            throw new Exception("Not yet in physics simulation. Can not set game state.");
        }

        stateManager.RestoreState(gameState);
    }

    public static List<SimulationOutcome> SimulatePlyAction(StateManager stateManager, PlayerMovement player, PlayerMovement.MoveAction action, bool recordFinalOutcomeOnly)
    {
        if (! isInSim)
        {
            throw new Exception("Not yet in physics simulation. Can not simulate.");
        }

        var initState = stateManager.CaptureState();
        var lastVisitedState = initState;
        player.ApplyAction(action);
        
        List<SimulationOutcome> result = new List<SimulationOutcome>();
        
        // Advance physics for a few steps to see the result of the action
        for (int i = 0; i < action.simIterations; i++)
        {
            // 1. Ensure Raycasts/Collisions are in sync with current positions
            Physics2D.SyncTransforms();
            
            // 2. Call the logic that updates velocities based on input/state
            player.SimulateStep(action.simDeltaTime);
            
            // 3. Move the physical bodies
            Physics2D.Simulate(action.simDeltaTime);

            lastVisitedState = stateManager.CaptureState();
            if (! recordFinalOutcomeOnly) {
                var newOutcome = new SimulationOutcome
                {
                    ResultState = lastVisitedState,
                    ResultPosition = player.transform.position
                };
                result.Add(newOutcome);
            }
            // Actions that can terminate early
            if (action.simEarlyTermination && ! initState.Equals(lastVisitedState)) break;
        }
        // If only record final outcome
        if (recordFinalOutcomeOnly)
        {
            result.Add(new SimulationOutcome
            {
                ResultState = lastVisitedState,
                ResultPosition = player.transform.position
            });
        }
        return result;
    }
}