using System.Collections.Generic;

public enum FeatureType { Discrete, Continuous }

public struct StateFeature
{
    public FeatureType Type;
    public float Value;
    public float Granularity;
}

public interface IStateComponent
{
    // ID is removed; we now use the Class Type ID automatically
    List<StateFeature> GetFeatures();
}