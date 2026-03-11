using UnityEngine;
using System;

public class A_StarNode : ScriptableObject 
{   
    private GameStateSnapshot _state;
    private Vector2 _position; // Keep for heuristic calculations
    private float g_n;
    private float h_n;
    private float f_n;

    public GameStateSnapshot State => _state;

    public void nodeSetup(GameStateSnapshot state, Vector2 pos, float g, float h)
    {
        _state = state;
        _position = pos;
        g_n = g;
        h_n = h;
        // F is the cost + heuristic
        f_n = (float)Math.Round(g + h, 2); 
    }

    public Vector2 getPosition() => _position;
    public float getG() => g_n;
    public float getH() => h_n;
    public float getF() => f_n;

    public bool isEqual(A_StarNode other)
    {
        // Use the snapshot's built-in equality (based on encoded features)
        return _state.Equals(other.State);
    }
}