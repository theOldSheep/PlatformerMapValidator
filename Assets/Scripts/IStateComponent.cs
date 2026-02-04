using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum FeatureType { Discrete, Continuous }

// The compatibility interface to allow us stuff the features list with different T value
public interface IStateFeature
{
    int Encode(object rawValue);
    // At an interface level, T can not be specified.
    object Decode(int encodedValue);
}

public struct StateFeature<T> : IStateFeature
{
    public FeatureType Type;
    public List<T> DiscreteLookup; // For Discrete, this will map raw value to index
    public float Granularity; // For continuous, this will categorize raw value to bin

    public int Encode(object rawValue)
    {
        // Discrete Encoding
        if (Type == FeatureType.Discrete)
        {
            // If lookup exists and in lookup, use this value
            if (DiscreteLookup != null)
            {
                for (int i = 0; i < DiscreteLookup.Count; i ++)
                {
                    if (rawValue.Equals(DiscreteLookup[i]))
                    {
                        return i;
                    }
                }
            }
            // If boolean, map to 0/1
            if (rawValue is bool)
            {
                return ((bool)rawValue) ? 1 : 0;
            }
            // If integer, fall back to direct return
            if (rawValue is int)
            {
                return (int)rawValue;
            }
            // otherwise, fall back to return -1
            return -1;
        }
        // Continuous Encoding
        else
        {
            // Continuous binning
            return Mathf.FloorToInt((float)rawValue / Granularity);
        }
    }

    public object Decode(int encodedValue)
    {
        // Discrete Decoding
        if (Type == FeatureType.Discrete)
        {
            // List lookup - highest precedence
            if (DiscreteLookup != null)
            {
                // Safety check to prevent crashing if fall back threw back a bad index
                if (encodedValue >= 0 && encodedValue < DiscreteLookup.Count)
                {
                    return DiscreteLookup[encodedValue];
                }
                Debug.LogWarning($"State Feature Decode: Index {encodedValue} is out of bounds for {typeof(T)} lookup.");
                return default(T);
            }

            // Map boolean
            if (typeof(T) == typeof(bool))
            {
                return encodedValue == 1;
            }

            // Map integer directly back
            if (typeof(T) == typeof(int))
            {
                return encodedValue;
            }

            return default(T);
        }
        // Continuous Decoding
        else
        {
            // Return the center of the bin for the best reconstruction we can do
            float reconstructed = (encodedValue * Granularity) + (Granularity / 2.0f);
            return reconstructed;
        }
    }
}

public interface IStateComponent
{
    static List<IStateFeature> GetFeatures() => throw new NotImplementedException();
    List<object> GetFeaturesRawValue() => throw new NotImplementedException();
}