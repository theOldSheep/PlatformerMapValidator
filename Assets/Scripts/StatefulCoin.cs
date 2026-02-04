using System.Collections.Generic;
using UnityEngine;

public class StatefulCoin : MonoBehaviour, IStateComponent
{
    private bool _isCollected = false;
    private static List<IStateFeature> _cachedGameStateFeatures = new List<IStateFeature> {
        new StateFeature<bool> {
            Type = FeatureType.Discrete,
        }
    };

    public void Collect() => _isCollected = true;

    public static List<IStateFeature> GetFeatures() => _cachedGameStateFeatures;
    public List<object> GetFeaturesRawValue() => new List<object> {
        _isCollected,
    };
}