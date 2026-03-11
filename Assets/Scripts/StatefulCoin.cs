using System.Collections.Generic;
using UnityEngine;

public class StatefulCoin : MonoBehaviour, IStateComponent
{
    private bool _isCollected = false;
    public static float posStepX = 1.0f;
    public static float posStepY = 1.0f;
    private static List<IStateFeature> _cachedGameStateFeatures = new List<IStateFeature> {
        new StateFeature<float> {
            Type = FeatureType.Continuous,
            Granularity=posStepX
        },
        new StateFeature<float> {
            Type = FeatureType.Continuous,
            Granularity=posStepY 
        },
        new StateFeature<bool> {
            Type = FeatureType.Discrete,
        },
    };

    public void Collect() {
        _isCollected = true;
        gameObject.SetActive(false);
    }

    public static List<IStateFeature> GetFeatures() => _cachedGameStateFeatures;
    public List<object> GetFeaturesRawValue() => new List<object> {
        transform.position.x,
        transform.position.y,
        _isCollected
    };
    public void RestoreFeaturesRawValue(List<object> rawValues)
    {
        transform.position = new Vector3((float)rawValues[0], (float)rawValues[1], transform.position.z);
        bool collected = (bool) rawValues[2];
        _isCollected = collected;
        gameObject.SetActive(!collected);
    }
}