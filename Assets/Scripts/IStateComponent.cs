using System;
using System.Collections.Generic;
using UnityEngine;

public enum FeatureType { Discrete, Continuous }
public enum PosVelType { PosX, PosY, VelX, VelY }

public interface IStateFeature
{
    int Encode(object rawValue, StateManager.StateEncodingSettings settings);
}

public struct StateFeature<T> : IStateFeature
{
    public FeatureType Type;
    public float Granularity;
    public Func<T, int> CustomEncoding;

    public int Encode(object rawValue, StateManager.StateEncodingSettings settings)
    {
        if (CustomEncoding != null)
        {
            return 0;
        }
        if (Type == FeatureType.Continuous)
        {
            return Mathf.RoundToInt((float) rawValue / Granularity);
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
        float Granularity = 1f;
        switch (Type)
        {
            case PosVelType.PosX:
                Value = Math.Clamp(Value, settings.MinX, settings.MaxX);
                Granularity = settings.PosXGranularity;
                break;
            case PosVelType.PosY:
                Value = Math.Clamp(Value, settings.MinY, settings.MaxY);
                Granularity = settings.PosYGranularity;
                break;
            case PosVelType.VelX:
                Granularity = settings.VelXGranularity;
                break;
            case PosVelType.VelY:
                Granularity = settings.VelYGranularity;
                break;
        }
        return Mathf.RoundToInt(Value / Granularity);
    }
}

public interface IStateComponent
{
    static List<IStateFeature> GetFeatures() => throw new NotImplementedException();
    List<object> GetFeaturesRawValue();
    void RestoreFeaturesRawValue(List<object> rawValues); 
}