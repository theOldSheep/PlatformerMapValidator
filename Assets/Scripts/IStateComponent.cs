using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum FeatureType { Discrete, Continuous }

public interface IStateFeature
{
    int Encode(object rawValue);
}

public struct StateFeature<T> : IStateFeature
{
    public FeatureType Type;
    public float Granularity; 

    public int Encode(object rawValue)
    {
        if (Type == FeatureType.Discrete)
        {
            if (rawValue is bool b) return b ? 1 : 0;
            if (rawValue is int i) return i;
            return -1;
        }
        else
        {
            return Mathf.FloorToInt((float)rawValue / Granularity);
        }
    }
}

public interface IStateComponent
{
    static List<IStateFeature> GetFeatures() => throw new NotImplementedException();
    List<object> GetFeaturesRawValue();
    void RestoreFeaturesRawValue(List<object> rawValues); 
}