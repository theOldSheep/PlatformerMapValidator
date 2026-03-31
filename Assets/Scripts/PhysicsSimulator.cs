using System;
using UnityEngine;

public class PhysicsSimulator : MonoBehaviour
{
    // Game state store & recovery
    private static GameStateSnapshot initialWorldState;
    private static SimulationMode2D previousMode;
    private static bool isInSim = false;

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

    public static void SimulatePlyAction(PlayerMovement player, PlayerMovement.MoveAction action, float StepDt, int StepsPerAction)
    {
        if (! isInSim)
        {
            throw new Exception("Not yet in physics simulation. Can not simulate.");
        }

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
    }

    public static GameStateSnapshot SimulatePlyActionUntilDifferentState(StateManager stateManager, PlayerMovement player, PlayerMovement.MoveAction action, float StepDt, int MaxAttemptSteps)
    {
        if (! isInSim)
        {
            throw new Exception("Not yet in physics simulation. Can not simulate.");
        }

        GameStateSnapshot originalState = stateManager.CaptureState();
        GameStateSnapshot currState;
        player.ApplyAction(action);
        
        // Advance physics for a few steps to see the result of the action
        for (int i = 0; i < MaxAttemptSteps; i++)
        {
            // 1. Ensure Raycasts/Collisions are in sync with current positions
            Physics2D.SyncTransforms();
            
            // 2. Call the logic that updates velocities based on input/state
            player.SimulateStep(StepDt); 
            
            // 3. Move the physical bodies
            Physics2D.Simulate(StepDt);

            // If the state is different, stop early
            currState = stateManager.CaptureState();
            if (! currState.Equals(originalState)) return currState;
        }
        return originalState;
    }
}