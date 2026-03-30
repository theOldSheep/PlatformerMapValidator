using System;
using System.Collections.Generic;
using UnityEngine;

public enum FeatureType { Discrete, Continuous }
public enum PosVelType { PosX, PosY, Vel }

public interface IStateFeature
{
    int Encode(object rawValue, StateManager.StateEncodingSettings settings);
}

public struct StateFeature<T> : IStateFeature
{
    public FeatureType Type;
    public float Granularity;
    public Func<bool> RelevanceProvider;

    public int Encode(object rawValue, StateManager.StateEncodingSettings settings)
    {
        if (RelevanceProvider != null && ! RelevanceProvider.Invoke())
        {
            return 0;
        }
        if (Type == FeatureType.Continuous)
        {
            return Mathf.FloorToInt((float)rawValue / Granularity);
        }
        else
        {
            if (rawValue is bool b) return b ? 1 : 0;
            if (rawValue is int i) return i;
            return -1;
        }
    }
}

public struct StatePosVelFeature<T> : IStateFeature
{
    public PosVelType Type;
    public Func<bool> RelevanceProvider;

    public int Encode(object rawValue, StateManager.StateEncodingSettings settings)
    {
        if (RelevanceProvider != null && ! RelevanceProvider.Invoke())
        {
            return 0;
        }
        float Value = (float)rawValue;
        switch (Type)
        {
            case PosVelType.PosX:
                Value = Math.Clamp(Value, settings.MinX, settings.MaxX);
                break;
            case PosVelType.PosY:
                Value = Math.Clamp(Value, settings.MinY, settings.MaxY);
                break;
        }
        return Mathf.FloorToInt(Value / settings.PosVelGranularity);
    }
}

public interface IStateComponent
{
    static List<IStateFeature> GetFeatures() => throw new NotImplementedException();
    List<object> GetFeaturesRawValue();
    void RestoreFeaturesRawValue(List<object> rawValues); 
}