using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Defines the type of data an object is reporting
public enum FeatureType { Discrete, Continuous }

public struct StateFeature
{
    public FeatureType Type;
    public float Value; // For Discrete, cast to int (0f/1f). For Continuous, use raw value.
    public float Granularity; // Only used if Continuous
}

public interface IStateComponent
{
    // Used to sort the state vector so it's consistent every frame
    int ID { get; } 
    List<StateFeature> GetFeatures();
}